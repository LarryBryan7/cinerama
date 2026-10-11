using Cinerama.Application.Bookings;
using Cinerama.Application.Seating;
using Cinerama.Domain.Enums;
using Cinerama.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Infrastructure;

public sealed record TestSeat(int SeatId, string Label);

public sealed record TestShowtime(int Id, int RoomId, decimal Price, DateTime StartsAt, IReadOnlyList<TestSeat> Seats)
{
    public TestSeat Seat(string label) => Seats.Single(s => s.Label == label);
    public int[] Ids(params string[] labels) => labels.Select(l => Seat(l).SeatId).ToArray();
}

public sealed record TestUser(int Id, string Email, string Token);

/// <summary>Ayudantes comunes a las pruebas de asientos, reservas y concurrencia.</summary>
public abstract class ReservationTest(ApiFixture fixture) : IntegrationTest(fixture)
{
    protected const string Locks = "/api/v1/showtimes/{0}/seats/lock";

    // ── Datos ───────────────────────────────────────────────────────────────

    protected async Task<TestShowtime> NewShowtimeAsync(
        int rows = 2, int perRow = 5, decimal price = 10m, DateTime? startsAtUtc = null,
        ShowtimeStatus status = ShowtimeStatus.Scheduled, string roomName = "Sala T", int? aisle = null)
    {
        var starts = startsAtUtc ?? DateTime.UtcNow.AddDays(1);
        return await Db(async db =>
        {
            var room = await Seeds.RoomAsync(db, roomName, rows, perRow, aisle);
            var movie = await Seeds.MovieAsync(db, "Película de prueba " + Guid.NewGuid().ToString("N")[..6]);
            var showtime = await Seeds.ShowtimeAsync(db, movie, room, starts, price, status);

            var seats = await db.Seats.Where(s => s.CinemaRoomId == room.Id)
                .OrderBy(s => s.RowLabel).ThenBy(s => s.Number)
                .Select(s => new { s.Id, s.RowLabel, s.Number }).ToListAsync();

            return new TestShowtime(showtime.Id, room.Id, price, starts,
                seats.Select(s => new TestSeat(s.Id, $"{s.RowLabel}{s.Number}")).ToList());
        });
    }

    /// <summary>Otra función en la misma sala (los asientos son los mismos; los ShowSeats son propios).</summary>
    protected async Task<TestShowtime> NewShowtimeInSameRoomAsync(TestShowtime existing, DateTime startsAtUtc) =>
        await Db(async db =>
        {
            var movie = await Seeds.MovieAsync(db, "Otra película " + Guid.NewGuid().ToString("N")[..6]);
            var room = await db.CinemaRooms.SingleAsync(r => r.Id == existing.RoomId);
            var showtime = await Seeds.ShowtimeAsync(db, movie, room, startsAtUtc, existing.Price);
            return existing with { Id = showtime.Id, StartsAt = startsAtUtc };
        });

    protected async Task<TestUser> NewUserAsync(string? email = null, string role = "Customer", string? tokenOverride = null, bool admin = false, string? fullName = null)
    {
        email ??= $"u{Guid.NewGuid():N}@example.test";
        var roleId = admin || role == "Admin" ? Cinerama.Domain.Entities.Roles.AdminId : Cinerama.Domain.Entities.Roles.CustomerId;
        var id = await Db(async db => (await Seeds.UserAsync(db, email, roleId, fullName: fullName)).Id);
        return new TestUser(id, email, tokenOverride ?? TestTokens.IssuedAt(DateTimeOffset.UtcNow, id, admin ? "Admin" : role));
    }

    protected async Task<List<TestUser>> NewUsersAsync(int count) =>
        (await Task.WhenAll(Enumerable.Range(0, count).Select(_ => NewUserAsync()))).ToList();

    // ── Solicitudes ─────────────────────────────────────────────────────────

    protected Task<HttpResponseMessage> MapOf(TestUser user, TestShowtime showtime, HttpClient? client = null) =>
        Send(HttpMethod.Get, $"/api/v1/showtimes/{showtime.Id}/seats", bearer: user.Token, client: client);

    protected Task<HttpResponseMessage> LockSeats(TestUser user, TestShowtime showtime, string[] labels, HttpClient? client = null) =>
        LockIds(user, showtime.Id, showtime.Ids(labels), client);

    protected Task<HttpResponseMessage> LockIds(TestUser user, int showtimeId, int[] seatIds, HttpClient? client = null) =>
        Send(HttpMethod.Post, $"/api/v1/showtimes/{showtimeId}/seats/lock", new { seatIds }, user.Token, client: client);

    protected Task<HttpResponseMessage> ReleaseSeats(TestUser user, TestShowtime showtime, string[] labels, HttpClient? client = null) =>
        Send(HttpMethod.Post, $"/api/v1/showtimes/{showtime.Id}/seats/release", new { seatIds = showtime.Ids(labels) }, user.Token, client: client);

    protected Task<HttpResponseMessage> Book(
        TestUser user, TestShowtime showtime, string[] labels, string? key = null, HttpClient? client = null) =>
        BookIds(user, showtime.Id, showtime.Ids(labels), key, client);

    protected Task<HttpResponseMessage> BookIds(TestUser user, int showtimeId, int[] seatIds, string? key = null, HttpClient? client = null) =>
        Send(HttpMethod.Post, "/api/v1/bookings", new { showtimeId, seatIds }, user.Token, client: client,
            headers: key is null ? null : new Dictionary<string, string> { ["Idempotency-Key"] = key });

    protected Task<HttpResponseMessage> CancelBooking(TestUser user, int bookingId, HttpClient? client = null) =>
        Send(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", new { }, user.Token, client: client);

    /// <summary>Bloquea y confirma: devuelve la reserva creada.</summary>
    protected async Task<BookingDto> BookedAsync(TestUser user, TestShowtime showtime, params string[] labels)
    {
        (await LockSeats(user, showtime, labels)).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var response = await Book(user, showtime, labels, key: Guid.NewGuid().ToString());
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await Read<BookingDto>(response);
    }

    protected async Task<SeatMapDto> MapAsync(TestUser user, TestShowtime showtime, HttpClient? client = null) =>
        await Read<SeatMapDto>(await MapOf(user, showtime, client));

    // ── Concurrencia ────────────────────────────────────────────────────────

    /// <summary>
    /// Lanza todas las tareas a la vez: cada una espera en una compuerta común y se libera de golpe. Es lo que
    /// convierte "N solicitudes" en N solicitudes realmente simultáneas.
    /// </summary>
    protected static async Task<T[]> AtTheSameTime<T>(IEnumerable<Func<Task<T>>> work)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = work.Select(w => Task.Run(async () =>
        {
            await gate.Task;
            return await w();
        })).ToList();

        gate.SetResult();
        return await Task.WhenAll(tasks);
    }

    // ── Estado en la base ───────────────────────────────────────────────────

    protected Task<ShowSeatSnapshot> SeatInDb(TestShowtime showtime, string label) =>
        Db(async db =>
        {
            var seatId = showtime.Seat(label).SeatId;
            var ss = await db.ShowSeats.AsNoTracking().SingleAsync(x => x.ShowtimeId == showtime.Id && x.SeatId == seatId);
            return new ShowSeatSnapshot(ss.Status, ss.LockedByUserId, ss.LockedUntil);
        });

    protected Task<int> CountInDb(Func<CineramaDbContext, Task<int>> query) => Db(query);

    /// <summary>
    /// Invariantes que NUNCA pueden romperse, pase lo que pase con la concurrencia: ningún asiento en dos reservas
    /// activas, todo asiento vendido tiene exactamente una reserva activa, y toda reserva activa apunta a un asiento vendido.
    /// </summary>
    protected async Task AssertNoDoubleBookingAsync()
    {
        await Db(async db =>
        {
            var duplicated = await db.BookingSeats.Where(bs => bs.ReleasedAt == null)
                .GroupBy(bs => bs.ShowSeatId).Where(g => g.Count() > 1).Select(g => g.Key).ToListAsync();
            duplicated.ShouldBeEmpty("un asiento quedó en más de una reserva activa");

            var soldWithoutBooking = await db.ShowSeats.CountAsync(ss => ss.Status == ShowSeatStatus.Booked
                && !db.BookingSeats.Any(bs => bs.ShowSeatId == ss.Id && bs.ReleasedAt == null));
            soldWithoutBooking.ShouldBe(0, "hay asientos vendidos sin una reserva activa");

            var activeButNotSold = await db.BookingSeats.CountAsync(bs => bs.ReleasedAt == null && bs.ShowSeat.Status != ShowSeatStatus.Booked);
            activeButNotSold.ShouldBe(0, "hay reservas activas sobre asientos que no figuran como vendidos");
        });
    }
}

public sealed record ShowSeatSnapshot(ShowSeatStatus Status, int? LockedByUserId, DateTime? LockedUntil);
