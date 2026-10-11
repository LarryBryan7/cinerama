using System.Net;
using Cinerama.Application.Bookings;
using Cinerama.Domain.Enums;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Concurrency;

/// <summary>
/// CRITERIO PRINCIPAL DE LA FASE 2: dos solicitudes concurrentes no pueden terminar con el mismo asiento. Cada test
/// lanza las solicitudes realmente a la vez (compuerta común), contra un SQL Server real, y verifica el resultado HTTP
/// <b>y</b> el estado final de la base de datos. SQL Server es la única fuente de verdad: no hay ningún lock en Redis.
/// </summary>
public class SeatConcurrencyTests(ApiFixture fixture) : ReservationTest(fixture)
{
    private static string Summary(IEnumerable<HttpResponseMessage> responses) =>
        string.Join(", ", responses.GroupBy(r => (int)r.StatusCode).OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Count()}"));

    private static int Count(IEnumerable<HttpResponseMessage> responses, HttpStatusCode code) => responses.Count(r => r.StatusCode == code);

    // ── Bloqueo ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "TC-CONC-01")]
    public async Task TwentyUsersLockTheSameSeat_ExactlyOneWins_AndNineteenGet409()
    {
        var users = await NewUsersAsync(20);
        var showtime = await NewShowtimeAsync(rows: 2, perRow: 5);

        // Diez repeticiones, cada una sobre un asiento distinto, para no depender de un golpe de suerte.
        foreach (var seat in showtime.Seats)
        {
            var responses = await AtTheSameTime(users.Select(u => (Func<Task<HttpResponseMessage>>)(() => LockSeats(u, showtime, [seat.Label]))));

            Count(responses, HttpStatusCode.OK).ShouldBe(1, $"{seat.Label}: {Summary(responses)}");
            Count(responses, HttpStatusCode.Conflict).ShouldBe(19, $"{seat.Label}: {Summary(responses)}");

            foreach (var loser in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
                (await ReadProblem(loser)).Code.ShouldBe("seat_unavailable");

            // La base coincide con la respuesta: el asiento es de quien recibió 200, y de nadie más.
            var winner = users[Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.OK)];
            var stored = await SeatInDb(showtime, seat.Label);
            (stored.Status, stored.LockedByUserId).ShouldBe((ShowSeatStatus.Locked, winner.Id));

            // Y se avisó una sola vez de ese cambio.
            Fixture.Notifier.Events.Count(e => e.Changes.Any(c => c.SeatId == seat.SeatId && c.Status == ShowSeatStatus.Locked)).ShouldBe(1);
        }

        (await CountInDb(db => db.ShowSeats.CountAsync(s => s.Status == ShowSeatStatus.Locked))).ShouldBe(10); // un dueño por asiento
    }

    [Fact]
    [Trait("Case", "TC-CONC-06")]
    public async Task TwoUsersLockTheSameThreeSeats_InOppositeOrder_NoDeadlock_ExactlyOneWinsAllThree()
    {
        var showtime = await NewShowtimeAsync(rows: 10, perRow: 3);

        for (var row = 0; row < 10; row++)
        {
            // Usuarios nuevos en cada repetición: el ganador de la anterior ya acumula asientos y toparía con el límite de 10 (RN-20).
            var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
            var forward = new[] { $"{(char)('A' + row)}1", $"{(char)('A' + row)}2", $"{(char)('A' + row)}3" };
            var backward = forward.Reverse().ToArray();

            var responses = await AtTheSameTime<HttpResponseMessage>(
            [
                () => LockSeats(ana, showtime, forward),
                () => LockSeats(luis, showtime, backward),
            ]);

            Count(responses, HttpStatusCode.OK).ShouldBe(1, Summary(responses));
            Count(responses, HttpStatusCode.Conflict).ShouldBe(1, Summary(responses)); // sin 500: no hubo interbloqueo

            // Todo o nada: los tres asientos son del mismo usuario.
            var owners = new List<int?>();
            foreach (var label in forward) owners.Add((await SeatInDb(showtime, label)).LockedByUserId);
            owners.Distinct().Count().ShouldBe(1);
            owners[0].ShouldNotBeNull();
        }
    }

    [Fact]
    [Trait("Case", "TC-CONC-06")]
    public async Task TwoUsersLockPartiallyOverlappingSets_OnlyOneWins_AndTheLosersOtherSeatStaysFree()
    {
        var showtime = await NewShowtimeAsync(rows: 10, perRow: 3);

        for (var row = 0; row < 10; row++)
        {
            var (ana, luis) = (await NewUserAsync(), await NewUserAsync()); // nuevos en cada repetición (límite RN-20)
            var r = (char)('A' + row);
            // Ana pide {1, 2}; Luis pide {2, 3}: comparten el asiento 2.
            var responses = await AtTheSameTime<HttpResponseMessage>(
            [
                () => LockSeats(ana, showtime, [$"{r}1", $"{r}2"]),
                () => LockSeats(luis, showtime, [$"{r}2", $"{r}3"]),
            ]);

            Count(responses, HttpStatusCode.OK).ShouldBe(1, Summary(responses));
            Count(responses, HttpStatusCode.Conflict).ShouldBe(1, Summary(responses));

            var anaWon = responses[0].StatusCode == HttpStatusCode.OK;
            var (winner, loserExclusive) = anaWon ? (ana, $"{r}3") : (luis, $"{r}1");

            (await SeatInDb(showtime, $"{r}2")).LockedByUserId.ShouldBe(winner.Id);
            (await SeatInDb(showtime, loserExclusive)).Status.ShouldBe(ShowSeatStatus.Available, "el perdedor no debe quedar con un asiento a medias");
        }
    }

    [Fact]
    [Trait("Case", "TC-CONC-05")]
    public async Task OneUserSendingManyLockRequestsAtOnce_CannotExceedTheTenSeatLimit()
    {
        var showtime = await NewShowtimeAsync(rows: 5, perRow: 4); // 20 asientos
        var ana = await NewUserAsync();

        // Cinco solicitudes simultáneas de 4 asientos distintos (20 en total). Es una comprobación de extremo a extremo; la
        // prueba determinista de que el lock por usuario es lo que lo garantiza está en RaceInjectionTests.
        var responses = await AtTheSameTime(Enumerable.Range(0, 5).Select(row =>
            (Func<Task<HttpResponseMessage>>)(() => LockSeats(ana, showtime, Enumerable.Range(1, 4).Select(n => $"{(char)('A' + row)}{n}").ToArray()))));

        Count(responses, HttpStatusCode.OK).ShouldBe(2, Summary(responses));                    // 4 + 4 = 8 ≤ 10
        Count(responses, HttpStatusCode.UnprocessableEntity).ShouldBe(3, Summary(responses));   // la tercera haría 12 > 10
        foreach (var rejected in responses.Where(r => r.StatusCode == HttpStatusCode.UnprocessableEntity))
            (await ReadProblem(rejected)).Code.ShouldBe("lock_limit_exceeded");

        (await CountInDb(db => db.ShowSeats.CountAsync(s => s.LockedByUserId == ana.Id))).ShouldBe(8);
    }

    // ── Confirmación ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "TC-CONC-02")]
    public async Task OneUserConfirmsTheSameSeatsManyTimesAtOnce_WithDifferentKeys_OnlyOneBookingExists()
    {
        var showtime = await NewShowtimeAsync(rows: 5, perRow: 4);
        var ana = await NewUserAsync();

        for (var trial = 0; trial < 5; trial++)
        {
            var labels = new[] { $"{(char)('A' + trial)}1", $"{(char)('A' + trial)}2" };
            (await LockSeats(ana, showtime, labels)).StatusCode.ShouldBe(HttpStatusCode.OK);

            // Ocho confirmaciones simultáneas con claves distintas (un doble clic desde dos pestañas). Sin una clave común
            // no hay forma de saber que son la misma: tiene que decidir la base de datos (RowVersion e índice único).
            var responses = await AtTheSameTime(Enumerable.Range(0, 8).Select(i =>
                (Func<Task<HttpResponseMessage>>)(() => Book(ana, showtime, labels, key: $"trial{trial}-{i}"))));

            Count(responses, HttpStatusCode.Created).ShouldBe(1, Summary(responses));
            Count(responses, HttpStatusCode.Conflict).ShouldBe(7, Summary(responses));
            foreach (var loser in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
                (await ReadProblem(loser)).Code.ShouldBeOneOf("lock_expired", "seat_unavailable");

            await AssertNoDoubleBookingAsync();
        }

        (await CountInDb(db => db.Bookings.CountAsync())).ShouldBe(5);
        (await CountInDb(db => db.BookingSeats.CountAsync(bs => bs.ReleasedAt == null))).ShouldBe(10);
    }

    [Fact]
    [Trait("Case", "TC-CONC-02")]
    public async Task ThirtyUsersRaceTheWholeFlowForOneSeat_OnlyOneEndsWithABooking()
    {
        var users = await NewUsersAsync(30);
        var showtime = await NewShowtimeAsync(rows: 1, perRow: 5);

        foreach (var seat in showtime.Seats)
        {
            // Cada usuario hace el flujo completo (bloquear y confirmar) sobre el mismo asiento.
            var outcomes = await AtTheSameTime(users.Select(u => (Func<Task<(HttpStatusCode Lock, HttpStatusCode? Book)>>)(async () =>
            {
                var locked = await LockSeats(u, showtime, [seat.Label]);
                if (locked.StatusCode != HttpStatusCode.OK)
                    return (locked.StatusCode, null);
                return (locked.StatusCode, (await Book(u, showtime, [seat.Label], key: Guid.NewGuid().ToString())).StatusCode);
            })));

            outcomes.Count(o => o.Lock == HttpStatusCode.OK).ShouldBe(1, seat.Label);
            outcomes.Count(o => o.Lock == HttpStatusCode.Conflict).ShouldBe(29, seat.Label);
            outcomes.Count(o => o.Book == HttpStatusCode.Created).ShouldBe(1, seat.Label); // quien bloqueó siempre logra confirmar
            outcomes.Count(o => o.Lock is not (HttpStatusCode.OK or HttpStatusCode.Conflict)).ShouldBe(0);

            (await SeatInDb(showtime, seat.Label)).Status.ShouldBe(ShowSeatStatus.Booked);
            Fixture.Notifier.Events.Count(e => e.Changes.Any(c => c.SeatId == seat.SeatId && c.Status == ShowSeatStatus.Booked)).ShouldBe(1);
        }

        (await CountInDb(db => db.Bookings.CountAsync())).ShouldBe(5); // una reserva por asiento, ninguna repetida
        await AssertNoDoubleBookingAsync();
    }

    [Fact]
    [Trait("Case", "TC-CONC-02")]
    public async Task FortyUsersGrabRandomPairsOfSeats_NoSeatIsEverSoldTwice()
    {
        var users = await NewUsersAsync(40);
        var showtime = await NewShowtimeAsync(rows: 3, perRow: 4); // 12 asientos muy disputados
        var random = new Random(2026);
        var pairs = users.Select(_ => showtime.Seats.OrderBy(_ => random.Next()).Take(2).Select(s => s.Label).ToArray()).ToList();

        var outcomes = await AtTheSameTime(users.Select((u, i) => (Func<Task<(HttpStatusCode Lock, HttpStatusCode? Book)>>)(async () =>
        {
            var locked = await LockSeats(u, showtime, pairs[i]);
            if (locked.StatusCode != HttpStatusCode.OK)
                return (locked.StatusCode, null);
            return (locked.StatusCode, (await Book(u, showtime, pairs[i], key: Guid.NewGuid().ToString())).StatusCode);
        })));

        // Cada solicitud tuvo un desenlace definido: ningún 5xx ni estados intermedios.
        outcomes.ShouldAllBe(o => o.Lock == HttpStatusCode.OK || o.Lock == HttpStatusCode.Conflict);
        // Quien consiguió bloquear sus dos asientos los confirma siempre: nadie puede quitárselos mientras los tiene.
        outcomes.Where(o => o.Lock == HttpStatusCode.OK).ShouldAllBe(o => o.Book == HttpStatusCode.Created);

        var winners = outcomes.Count(o => o.Lock == HttpStatusCode.OK);
        winners.ShouldBeInRange(1, 6); // con 12 asientos y parejas, a lo sumo 6 reservas distintas
        (await CountInDb(db => db.Bookings.CountAsync())).ShouldBe(winners);
        (await CountInDb(db => db.BookingSeats.CountAsync(bs => bs.ReleasedAt == null))).ShouldBe(winners * 2);
        (await CountInDb(db => db.ShowSeats.CountAsync(s => s.Status == ShowSeatStatus.Booked))).ShouldBe(winners * 2);
        await AssertNoDoubleBookingAsync();
    }

    [Fact]
    [Trait("Case", "TC-CONC-02")]
    public async Task WhenALockExpiresAndAnotherUserTakesTheSeat_OnlyTheNewHolderCanBookIt()
    {
        var showtime = await NewShowtimeAsync();
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
        await using var api = new TimedApi(Fixture);
        await LockSeats(api.As(ana), showtime, ["A1"], api.Client);
        api.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        (await LockSeats(api.As(luis), showtime, ["A1"], api.Client)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Ana (con su bloqueo ya vencido) y Luis confirman a la vez.
        var responses = await AtTheSameTime<HttpResponseMessage>(
        [
            () => Book(api.As(ana), showtime, ["A1"], key: "ana", client: api.Client),
            () => Book(api.As(luis), showtime, ["A1"], key: "luis", client: api.Client),
        ]);

        responses[0].StatusCode.ShouldBe(HttpStatusCode.Conflict);
        responses[1].StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CountInDb(db => db.Bookings.CountAsync(b => b.UserId == luis.Id))).ShouldBe(1);
        (await CountInDb(db => db.Bookings.CountAsync(b => b.UserId == ana.Id))).ShouldBe(0);
        await AssertNoDoubleBookingAsync();
    }

    // ── Idempotencia ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "TC-CONC-03")]
    public async Task TheSameKeySentManyTimesAtOnce_ReturnsOneBooking_ToEveryRequest()
    {
        var showtime = await NewShowtimeAsync(rows: 5, perRow: 4);
        var ana = await NewUserAsync();

        for (var trial = 0; trial < 5; trial++)
        {
            var labels = new[] { $"{(char)('A' + trial)}1", $"{(char)('A' + trial)}2" };
            await LockSeats(ana, showtime, labels);

            // Cinco solicitudes idénticas a la vez (doble toque + reintentos de red): todas deben recibir LA MISMA reserva.
            var responses = await AtTheSameTime(Enumerable.Range(0, 5).Select(_ =>
                (Func<Task<HttpResponseMessage>>)(() => Book(ana, showtime, labels, key: $"doble-toque-{trial}"))));

            Count(responses, HttpStatusCode.Created).ShouldBe(1, Summary(responses));
            Count(responses, HttpStatusCode.OK).ShouldBe(4, Summary(responses));

            var bookings = await Task.WhenAll(responses.Select(Read<BookingDto>));
            bookings.Select(b => b.Id).Distinct().Count().ShouldBe(1);
            bookings.Select(b => b.Code).Distinct().Count().ShouldBe(1);
        }

        (await CountInDb(db => db.Bookings.CountAsync())).ShouldBe(5);
        await AssertNoDoubleBookingAsync();
    }

    // ── Cancelación ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "TC-CONC-04")]
    public async Task TheSameBookingCancelledManyTimesAtOnce_IsCancelledOnce_AndItsSeatsReleasedOnce()
    {
        var showtime = await NewShowtimeAsync(rows: 5, perRow: 4, startsAtUtc: DateTime.UtcNow.AddHours(24));
        var ana = await NewUserAsync();

        for (var trial = 0; trial < 5; trial++)
        {
            var labels = new[] { $"{(char)('A' + trial)}1", $"{(char)('A' + trial)}2" };
            var booking = await BookedAsync(ana, showtime, labels);
            Fixture.Notifier.Clear();

            var responses = await AtTheSameTime(Enumerable.Range(0, 4).Select(_ =>
                (Func<Task<HttpResponseMessage>>)(() => CancelBooking(ana, booking.Id))));

            Count(responses, HttpStatusCode.OK).ShouldBe(1, Summary(responses));
            Count(responses, HttpStatusCode.Conflict).ShouldBe(3, Summary(responses));
            foreach (var loser in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
                (await ReadProblem(loser)).Code.ShouldBe("booking_already_cancelled");

            Fixture.Notifier.Events.Count(e => e.Changes.All(c => c.Status == ShowSeatStatus.Available)).ShouldBe(1); // se avisó una sola vez
            (await SeatInDb(showtime, labels[0])).Status.ShouldBe(ShowSeatStatus.Available);
        }

        await AssertNoDoubleBookingAsync();
    }

    [Fact]
    [Trait("Case", "TC-CONC-04")]
    public async Task ACancelledSeatCanBeRebookedByManyUsersAtOnce_OnlyOneSucceeds()
    {
        var showtime = await NewShowtimeAsync(rows: 1, perRow: 3, startsAtUtc: DateTime.UtcNow.AddHours(24));
        var owner = await NewUserAsync();
        var users = await NewUsersAsync(15);
        var booking = await BookedAsync(owner, showtime, "A1");
        (await CancelBooking(owner, booking.Id)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var outcomes = await AtTheSameTime(users.Select(u => (Func<Task<(HttpStatusCode Lock, HttpStatusCode? Book)>>)(async () =>
        {
            var locked = await LockSeats(u, showtime, ["A1"]);
            if (locked.StatusCode != HttpStatusCode.OK)
                return (locked.StatusCode, null);
            return (locked.StatusCode, (await Book(u, showtime, ["A1"], key: Guid.NewGuid().ToString())).StatusCode);
        })));

        outcomes.Count(o => o.Book == HttpStatusCode.Created).ShouldBe(1);
        (await CountInDb(db => db.Bookings.CountAsync(b => b.Status == BookingStatus.Confirmed))).ShouldBe(1);
        (await CountInDb(db => db.BookingSeats.CountAsync())).ShouldBe(2); // la cancelada (historial) y la nueva
        await AssertNoDoubleBookingAsync();
    }
}
