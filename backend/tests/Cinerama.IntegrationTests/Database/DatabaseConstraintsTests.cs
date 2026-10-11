using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using Cinerama.Infrastructure.Persistence;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cinerama.IntegrationTests.Database;

/// <summary>
/// Lo que garantiza SQL Server aunque la aplicación tuviera un error (ADR-004). Ninguno de estos comportamientos
/// existe en el proveedor EF InMemory ni en SQLite, por eso la suite corre contra SQL Server real (ADR-012).
/// </summary>
public class DatabaseConstraintsTests(ApiFixture fixture) : IntegrationTest(fixture)
{
    private static async Task<T> Violation<T>(Func<Task> action, string constraintOrIndex) where T : Exception
    {
        var ex = await Should.ThrowAsync<T>(action);
        var sql = (ex.InnerException as SqlException).ShouldNotBeNull();
        sql.Message.ShouldContain(constraintOrIndex);
        return ex;
    }

    private static DateTime Tomorrow() => DateTime.UtcNow.Date.AddDays(1).AddHours(20);

    /// <summary>Función con 4 asientos y un usuario, listos para forzar estados.</summary>
    private async Task<(int ShowtimeId, int UserId)> ShowtimeWithSeatsAsync() => await Db(async db =>
    {
        var user = await Seeds.UserAsync(db, "ana@example.test");
        var showtime = await Seeds.ShowtimeAsync(db, await Seeds.MovieAsync(db, "Película"), await Seeds.RoomAsync(db, rows: 1, perRow: 4), Tomorrow());
        return (showtime.Id, user.Id);
    });

    // ── Esquema ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Migration_SeedsTheTwoRoles() =>
        (await Db(db => db.Roles.OrderBy(r => r.Id).Select(r => r.Name).ToListAsync())).ShouldBe([Roles.Admin, Roles.Customer]);

    [Fact]
    public async Task ShowSeatStatus_IsStoredAsReadableText()
    {
        await ShowtimeWithSeatsAsync();

        var statuses = await Db(db => db.Database.SqlQueryRaw<string>("SELECT DISTINCT Status AS Value FROM ShowSeats").ToListAsync());

        statuses.ShouldBe(["Available"]);
    }

    // ── Unicidad ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "RN-01")]
    public async Task Users_EmailIsUnique() =>
        await Violation<DbUpdateException>(() => Db(async db =>
        {
            await Seeds.UserAsync(db, "ana@example.test");
            await Seeds.UserAsync(db, "ana@example.test");
        }), "UX_Users_Email");

    [Fact]
    public async Task Seats_AreUniquePerRoomRowAndNumber() =>
        await Violation<DbUpdateException>(() => Db(async db =>
        {
            var room = await Seeds.RoomAsync(db, rows: 1, perRow: 2);
            db.Seats.Add(new Seat { CinemaRoomId = room.Id, RowLabel = 'A', Number = 1 });
            await db.SaveChangesAsync();
        }), "UX_Seats_Room_Row_Number");

    [Fact]
    public async Task ShowSeats_AreUniquePerShowtimeAndSeat()
    {
        await ShowtimeWithSeatsAsync();

        await Violation<DbUpdateException>(() => Db(async db =>
        {
            var existing = await db.ShowSeats.AsNoTracking().FirstAsync();
            db.ShowSeats.Add(new ShowSeat { ShowtimeId = existing.ShowtimeId, SeatId = existing.SeatId });
            await db.SaveChangesAsync();
        }), "UX_ShowSeats_Showtime_Seat");
    }

    // ── CHECK constraints ───────────────────────────────────────────────────

    [Theory]
    [Trait("Case", "ADR-004")]
    [InlineData("LockedWithoutOwner")]
    [InlineData("LockedWithoutExpiry")]
    [InlineData("AvailableWithExpiry")]
    [InlineData("BookedWithOwner")]
    public async Task ShowSeats_RejectIncoherentLockState(string scenario)
    {
        var (_, userId) = await ShowtimeWithSeatsAsync();

        await Violation<DbUpdateException>(() => Db(async db =>
        {
            var seat = await db.ShowSeats.FirstAsync();
            var later = DateTime.UtcNow.AddMinutes(5);
            switch (scenario)
            {
                case "LockedWithoutOwner": (seat.Status, seat.LockedUntil) = (ShowSeatStatus.Locked, later); break;
                case "LockedWithoutExpiry": (seat.Status, seat.LockedByUserId) = (ShowSeatStatus.Locked, userId); break;
                case "AvailableWithExpiry": seat.LockedUntil = later; break;
                case "BookedWithOwner": (seat.Status, seat.LockedByUserId) = (ShowSeatStatus.Booked, userId); break;
            }
            await db.SaveChangesAsync();
        }), "CK_ShowSeats_LockConsistency");
    }

    [Fact]
    public async Task ShowSeats_AcceptTheValidStateTransitions()
    {
        var (_, userId) = await ShowtimeWithSeatsAsync();

        await Db(async db =>
        {
            var seat = await db.ShowSeats.FirstAsync();

            (seat.Status, seat.LockedByUserId, seat.LockedUntil) = (ShowSeatStatus.Locked, userId, DateTime.UtcNow.AddMinutes(5));
            await db.SaveChangesAsync();

            (seat.Status, seat.LockedByUserId, seat.LockedUntil) = (ShowSeatStatus.Booked, null, null);
            await db.SaveChangesAsync();

            (seat.Status, seat.LockedByUserId, seat.LockedUntil) = (ShowSeatStatus.Available, null, null);
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Showtimes_MustEndAfterTheyStart()
    {
        await Violation<DbUpdateException>(() => Db(async db =>
        {
            var showtime = await Seeds.ShowtimeAsync(db, await Seeds.MovieAsync(db, "P"), await Seeds.RoomAsync(db), Tomorrow(), generateSeats: false);
            showtime.EndsAt = showtime.StartsAt;
            await db.SaveChangesAsync();
        }), "CK_Showtimes_EndsAfterStart");
    }

    [Fact]
    public async Task Showtimes_PriceCannotBeNegative()
    {
        await Violation<DbUpdateException>(() => Db(async db =>
            await Seeds.ShowtimeAsync(db, await Seeds.MovieAsync(db, "P"), await Seeds.RoomAsync(db), Tomorrow(), price: -1m, generateSeats: false)),
            "CK_Showtimes_Price");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(401)]
    public async Task Movies_DurationMustBeBetween1And400Minutes(int minutes)
    {
        await Violation<DbUpdateException>(() => Db(db => Seeds.MovieAsync(db, "Película", minutes: minutes)), "CK_Movies_Duration");
    }

    // ── RowVersion: primera barrera contra la doble venta ───────────────────

    [Fact]
    [Trait("Case", "ADR-004")]
    public async Task RowVersion_ChangesOnEveryUpdate_AndAStaleWriteIsRejected()
    {
        var (_, userId) = await ShowtimeWithSeatsAsync();

        // Dos "solicitudes" cargan el mismo asiento con la misma versión.
        await using var scopeA = Fixture.Factory.Services.CreateAsyncScope();
        await using var scopeB = Fixture.Factory.Services.CreateAsyncScope();
        var a = scopeA.ServiceProvider.GetRequiredService<CineramaDbContext>();
        var b = scopeB.ServiceProvider.GetRequiredService<CineramaDbContext>();

        var seatA = await a.ShowSeats.OrderBy(s => s.Id).FirstAsync();
        var seatB = await b.ShowSeats.FirstAsync(s => s.Id == seatA.Id);
        var versionBefore = seatA.RowVersion.ToArray();
        seatB.RowVersion.ShouldBe(versionBefore);

        // La primera gana: bloquea el asiento.
        (seatA.Status, seatA.LockedByUserId, seatA.LockedUntil) = (ShowSeatStatus.Locked, userId, DateTime.UtcNow.AddMinutes(5));
        await a.SaveChangesAsync();
        seatA.RowVersion.ShouldNotBe(versionBefore);

        // La segunda intenta lo mismo con la versión vieja: SQL Server no actualiza ninguna fila.
        (seatB.Status, seatB.LockedByUserId, seatB.LockedUntil) = (ShowSeatStatus.Locked, userId, DateTime.UtcNow.AddMinutes(5));
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    // ── Tercera barrera: un asiento, una reserva activa (RN-31) ─────────────

    private async Task<(long ShowSeatId, int UserId, int ShowtimeId)> BookableSeatAsync() => await Db(async db =>
    {
        var (showtimeId, userId) = await ShowtimeWithSeatsAsync();
        return (await db.ShowSeats.Select(s => s.Id).FirstAsync(), userId, showtimeId);
    });

    private static Booking NewBooking(int userId, int showtimeId, string code, long showSeatId, string? key = null) => new()
    {
        Code = code, UserId = userId, ShowtimeId = showtimeId, TotalAmount = 10m, CreatedAt = DateTime.UtcNow, IdempotencyKey = key,
        Seats = [new BookingSeat { ShowSeatId = showSeatId, Price = 10m }],
    };

    [Fact]
    [Trait("Case", "RN-31")]
    public async Task BookingSeats_AShowSeatCannotBelongToTwoActiveBookings()
    {
        var (showSeatId, userId, showtimeId) = await BookableSeatAsync();
        await Db(async db => { db.Bookings.Add(NewBooking(userId, showtimeId, "AAAAAAAA", showSeatId)); await db.SaveChangesAsync(); });

        await Violation<DbUpdateException>(() => Db(async db =>
        {
            db.Bookings.Add(NewBooking(userId, showtimeId, "BBBBBBBB", showSeatId));
            await db.SaveChangesAsync();
        }), "UX_BookingSeats_ShowSeat_Active");
    }

    [Fact]
    [Trait("Case", "TC-BOOK-04")]
    public async Task BookingSeats_AfterReleasingTheFirstBooking_TheSeatCanBeBookedAgain()
    {
        var (showSeatId, userId, showtimeId) = await BookableSeatAsync();
        await Db(async db => { db.Bookings.Add(NewBooking(userId, showtimeId, "AAAAAAAA", showSeatId)); await db.SaveChangesAsync(); });

        // Cancelar una reserva libera sus asientos marcando ReleasedAt: el índice filtrado deja de contarla.
        await Db(db => db.BookingSeats.ExecuteUpdateAsync(s => s.SetProperty(x => x.ReleasedAt, DateTime.UtcNow)));

        await Db(async db => { db.Bookings.Add(NewBooking(userId, showtimeId, "BBBBBBBB", showSeatId)); await db.SaveChangesAsync(); });

        (await Db(db => db.BookingSeats.CountAsync())).ShouldBe(2);
    }

    [Fact]
    [Trait("Case", "RN-27")]
    public async Task Bookings_IdempotencyKey_IsUniquePerUser_ButNullsAndOtherUsersAreFine()
    {
        var (showSeatId, userId, showtimeId) = await BookableSeatAsync();
        var otherUserId = await Db(async db => (await Seeds.UserAsync(db, "luis@example.test")).Id);
        var seatIds = await Db(db => db.ShowSeats.OrderBy(s => s.Id).Select(s => s.Id).Take(4).ToListAsync());

        await Db(async db =>
        {
            db.Bookings.Add(NewBooking(userId, showtimeId, "AAAAAAAA", seatIds[0], key: "clave-1"));
            db.Bookings.Add(NewBooking(otherUserId, showtimeId, "BBBBBBBB", seatIds[1], key: "clave-1")); // otra persona: permitido
            db.Bookings.Add(NewBooking(userId, showtimeId, "CCCCCCCC", seatIds[2]));                       // sin clave: permitido
            db.Bookings.Add(NewBooking(userId, showtimeId, "DDDDDDDD", seatIds[3]));                       // sin clave otra vez: permitido
            await db.SaveChangesAsync();
        });

        await Violation<DbUpdateException>(() => Db(async db =>
        {
            db.Bookings.Add(NewBooking(userId, showtimeId, "EEEEEEEE", showSeatId, key: "clave-1")); // misma persona y misma clave
            await db.SaveChangesAsync();
        }), "UX_Bookings_User_IdempotencyKey");
    }

    [Fact]
    public async Task Bookings_CodeIsUnique()
    {
        var (_, userId, showtimeId) = await BookableSeatAsync();
        var seatIds = await Db(db => db.ShowSeats.OrderBy(s => s.Id).Select(s => s.Id).Take(2).ToListAsync());
        await Db(async db => { db.Bookings.Add(NewBooking(userId, showtimeId, "ZZZZZZZZ", seatIds[0])); await db.SaveChangesAsync(); });

        await Violation<DbUpdateException>(() => Db(async db =>
        {
            db.Bookings.Add(NewBooking(userId, showtimeId, "ZZZZZZZZ", seatIds[1]));
            await db.SaveChangesAsync();
        }), "UX_Bookings_Code");
    }
}
