using System.Net;
using Cinerama.Application.Seating;
using Cinerama.Domain.Enums;
using Cinerama.Infrastructure.Persistence;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Cinerama.IntegrationTests.Concurrency;

/// <summary>
/// Una carrera que no se puede provocar de forma fiable con hilos se provoca de forma DETERMINISTA: un interceptor de
/// EF Core ejecuta, desde otra conexión, un cambio sobre un asiento justo antes de que la API guarde el suyo. Es
/// exactamente lo que pasa cuando otra solicitud (o la limpieza) modifica la fila entre la lectura y la escritura.
/// </summary>
public class RaceInjectionTests(ApiFixture fixture) : ReservationTest(fixture)
{
    private sealed class RaceInjector : SaveChangesInterceptor
    {
        private Func<Task>? _pending;

        public void BeforeNextSave(Func<Task> action) => _pending = action;
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var action = Interlocked.Exchange(ref _pending, null);
            if (action is not null)
            {
                Fired = true;
                await action();
            }
            return result;
        }
    }

    private async Task Sql(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private TimedApi ApiWith(RaceInjector injector) =>
        new(Fixture, extraServices: services => services.ConfigureDbContext<CineramaDbContext>(o => o.AddInterceptors(injector)));

    /// <summary>
    /// Prueba DETERMINISTA del lock por usuario (RN-20). Con hilos reales el límite parecía cumplirse aun sin el lock,
    /// porque las lecturas de la segunda solicitud suelen esperar a las escrituras de la primera: el resultado dependía
    /// de los tiempos. Aquí se detiene a la primera solicitud DENTRO de su transacción, ya con el lock tomado y sin
    /// confirmar, y se comprueba que la segunda del mismo usuario espera en lugar de leer "0 bloqueados".
    /// </summary>
    [Fact]
    [Trait("Case", "TC-CONC-05")]
    public async Task TheSameUsersSecondLockRequest_WaitsForTheFirstToFinish_SoTheTenSeatLimitCannotBeBypassed()
    {
        var injector = new RaceInjector();
        await using var api = ApiWith(injector);
        var showtime = await NewShowtimeAsync(rows: 3, perRow: 4); // 12 asientos
        var ana = await NewUserAsync();

        var firstIsInsideItsTransaction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var letTheFirstFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        injector.BeforeNextSave(async () =>
        {
            firstIsInsideItsTransaction.SetResult();
            await letTheFirstFinish.Task;
        });

        var first = LockSeats(api.As(ana), showtime, ["A1", "A2", "A3", "A4", "B1", "B2"], api.Client);          // 6 asientos
        await firstIsInsideItsTransaction.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var second = LockSeats(api.As(ana), showtime, ["B3", "B4", "C1", "C2", "C3", "C4"], api.Client);          // otros 6: 6 + 6 > 10
        await Task.Delay(1500);
        second.IsCompleted.ShouldBeFalse("la segunda solicitud del mismo usuario debe esperar el lock de la primera");

        letTheFirstFinish.SetResult();
        var (firstResponse, secondResponse) = (await first, await second);

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // ya ve los 6 de la primera
        (await ReadProblem(secondResponse)).Code.ShouldBe("lock_limit_exceeded");
        (await CountInDb(db => db.ShowSeats.CountAsync(x => x.LockedByUserId == ana.Id))).ShouldBe(6);
    }

    [Fact]
    public async Task TheLockIsPerUser_AnotherUsersRequestIsNotBlockedByIt()
    {
        var injector = new RaceInjector();
        await using var api = ApiWith(injector);
        var showtime = await NewShowtimeAsync(rows: 2, perRow: 4);
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());

        var firstIsInsideItsTransaction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var letTheFirstFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        injector.BeforeNextSave(async () =>
        {
            firstIsInsideItsTransaction.SetResult();
            await letTheFirstFinish.Task;
        });

        var anas = LockSeats(api.As(ana), showtime, ["A1"], api.Client);
        await firstIsInsideItsTransaction.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Luis no comparte lock con Ana: su solicitud avanza aunque la de Ana siga detenida.
        var luiss = await LockSeats(api.As(luis), showtime, ["B1"], api.Client).WaitAsync(TimeSpan.FromSeconds(15));
        luiss.StatusCode.ShouldBe(HttpStatusCode.OK);

        letTheFirstFinish.SetResult();
        (await anas).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Case", "ADR-005")]
    public async Task Lock_WhenTheCleanupReleasesTheExpiredSeatJustBeforeTheWrite_ItIsRetriedAndSucceeds()
    {
        var injector = new RaceInjector();
        await using var api = ApiWith(injector);
        var showtime = await NewShowtimeAsync();
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
        await LockSeats(api.As(ana), showtime, ["A1"], api.Client);
        api.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        // Entre que Luis lee el bloqueo vencido de Ana y lo escribe, la limpieza lo libera (cambia su RowVersion).
        injector.BeforeNextSave(() => Sql(
            "UPDATE ShowSeats SET Status='Available', LockedByUserId=NULL, LockedUntil=NULL WHERE SeatId=@s",
            new SqlParameter("@s", showtime.Seat("A1").SeatId)));

        var response = await LockSeats(api.As(luis), showtime, ["A1"], api.Client);

        injector.Fired.ShouldBeTrue("la carrera debía provocarse");
        response.StatusCode.ShouldBe(HttpStatusCode.OK); // un 409 aquí sería engañoso: el asiento quedó libre, no lo ganó nadie
        (await SeatInDb(showtime, "A1")).LockedByUserId.ShouldBe(luis.Id);
    }

    [Fact]
    [Trait("Case", "ADR-005")]
    public async Task Lock_WhenAnotherUserReallyTakesTheSeatJustBeforeTheWrite_ItIs409_WithTheRightConflict()
    {
        var injector = new RaceInjector();
        await using var api = ApiWith(injector);
        var showtime = await NewShowtimeAsync();
        var (ana, luis, marta) = (await NewUserAsync(), await NewUserAsync(), await NewUserAsync());
        await LockSeats(api.As(ana), showtime, ["A1"], api.Client);
        api.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        // Esta vez, entre la lectura y la escritura de Luis, Marta SÍ se queda con el asiento. El vencimiento se calcula con
        // el reloj de la API (adelantado en este test), no con el de SQL Server: de lo contrario ya habría vencido.
        injector.BeforeNextSave(() => Sql(
            "UPDATE ShowSeats SET Status='Locked', LockedByUserId=@u, LockedUntil=@until WHERE SeatId=@s",
            new SqlParameter("@u", marta.Id), new SqlParameter("@until", api.Now.AddMinutes(5)), new SqlParameter("@s", showtime.Seat("A1").SeatId)));

        var response = await LockSeats(api.As(luis), showtime, ["A1"], api.Client);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await ReadProblem(response);
        problem.Code.ShouldBe("seat_unavailable");
        problem.Conflicts!.Select(c => c.Label).ShouldBe(["A1"]);
        (await SeatInDb(showtime, "A1")).LockedByUserId.ShouldBe(marta.Id); // Luis no pisó a Marta
    }

    [Fact]
    public async Task Release_WhenTheCleanupAlreadyReleasedMyExpiredLockJustBeforeTheWrite_StillSucceeds()
    {
        var injector = new RaceInjector();
        await using var api = ApiWith(injector);
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        await LockSeats(api.As(ana), showtime, ["A1"], api.Client);
        api.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        injector.BeforeNextSave(() => Sql(
            "UPDATE ShowSeats SET Status='Available', LockedByUserId=NULL, LockedUntil=NULL WHERE SeatId=@s",
            new SqlParameter("@s", showtime.Seat("A1").SeatId)));

        var response = await ReleaseSeats(api.As(ana), showtime, ["A1"], api.Client);

        injector.Fired.ShouldBeTrue();
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent); // el objetivo (que quede libre) ya se cumplió
        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Available);
    }

    [Fact]
    public async Task Release_WhenAnotherUserTookTheExpiredSeatJustBeforeTheWrite_NeverReleasesTheirLock()
    {
        var injector = new RaceInjector();
        await using var api = ApiWith(injector);
        var showtime = await NewShowtimeAsync();
        var (ana, marta) = (await NewUserAsync(), await NewUserAsync());
        await LockSeats(api.As(ana), showtime, ["A1"], api.Client);
        api.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        injector.BeforeNextSave(() => Sql(
            "UPDATE ShowSeats SET LockedByUserId=@u, LockedUntil=@until WHERE SeatId=@s",
            new SqlParameter("@u", marta.Id), new SqlParameter("@until", api.Now.AddMinutes(5)), new SqlParameter("@s", showtime.Seat("A1").SeatId)));

        var response = await ReleaseSeats(api.As(ana), showtime, ["A1"], api.Client);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SeatInDb(showtime, "A1")).LockedByUserId.ShouldBe(marta.Id); // el bloqueo de Marta sobrevivió
    }

    [Fact]
    [Trait("Case", "TC-CONC-02")]
    public async Task Confirm_WhenTheSeatChangesJustBeforeTheWrite_Returns409_AndCreatesNoBooking()
    {
        var injector = new RaceInjector();
        await using var api = ApiWith(injector);
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        await LockSeats(api.As(ana), showtime, ["A1"], api.Client);

        // Entre que Ana lee su bloqueo vigente y confirma, otro proceso modifica la fila (cualquier cambio cambia la RowVersion).
        injector.BeforeNextSave(() => Sql(
            "UPDATE ShowSeats SET LockedUntil = DATEADD(SECOND, 1, LockedUntil) WHERE SeatId=@s",
            new SqlParameter("@s", showtime.Seat("A1").SeatId)));

        var response = await Book(api.As(ana), showtime, ["A1"], client: api.Client);

        injector.Fired.ShouldBeTrue();
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict); // primera barrera: la RowVersion detuvo la escritura
        (await ReadProblem(response)).Code.ShouldBe("seat_unavailable");
        (await CountInDb(db => db.Bookings.CountAsync())).ShouldBe(0);
        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Locked); // la selección de Ana sigue intacta
    }

    [Fact]
    [Trait("Case", "RN-31")]
    public async Task Confirm_TheFilteredUniqueIndexIsTheLastLineOfDefense_EvenIfTheRowVersionCheckIsBypassed()
    {
        // Se simula un error de programación: otro proceso deja una reserva activa sobre el asiento SIN pasar por la
        // lógica de la aplicación (el asiento sigue "Locked" para Ana). La RowVersion del asiento no cambia, así que la
        // primera barrera no se activa; la tercera, el índice único filtrado, sí.
        var injector = new RaceInjector();
        await using var api = ApiWith(injector);
        var showtime = await NewShowtimeAsync();
        var (ana, intruder) = (await NewUserAsync(), await NewUserAsync());
        await LockSeats(api.As(ana), showtime, ["A1"], api.Client);
        var showSeatId = await Db(db => db.ShowSeats.Where(s => s.SeatId == showtime.Seat("A1").SeatId).Select(s => s.Id).SingleAsync());

        injector.BeforeNextSave(() => Sql($"""
            INSERT INTO Bookings (Code, UserId, ShowtimeId, Status, TotalAmount, CreatedAt) VALUES ('ZZZZZZZZ', {intruder.Id}, {showtime.Id}, 'Confirmed', 10, SYSUTCDATETIME());
            INSERT INTO BookingSeats (BookingId, ShowSeatId, Price) VALUES (SCOPE_IDENTITY(), {showSeatId}, 10);
            """));

        var response = await Book(api.As(ana), showtime, ["A1"], client: api.Client);

        injector.Fired.ShouldBeTrue();
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(response)).Code.ShouldBe("seat_unavailable");
        (await CountInDb(db => db.Bookings.CountAsync(b => b.UserId == ana.Id))).ShouldBe(0); // Ana no creó una segunda reserva activa
        (await CountInDb(db => db.BookingSeats.CountAsync(bs => bs.ReleasedAt == null))).ShouldBe(1);
    }
}
