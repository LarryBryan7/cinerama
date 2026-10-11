using System.Net;
using Cinerama.Application.Admin;
using Cinerama.Application.Bookings;
using Cinerama.Application.Common;
using Cinerama.Domain.Enums;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Admin;

/// <summary>Salas con su distribución (HU-18, RN-10, RN-11), programación de funciones (HU-19, CU-03, RN-12 a RN-17) y listado general de reservas (RF-BOOK-05).</summary>
public class RoomShowtimeAdminTests(ApiFixture fixture) : ReservationTest(fixture)
{
    private async Task<string> AdminAsync() => (await NewUserAsync(admin: true)).Token;

    private static DateTime InDays(int days, int hour = 19, int minute = 0) =>
        DateTime.UtcNow.Date.AddDays(days).AddHours(hour).AddMinutes(minute);

    private async Task<int> CinemaIdAsync(string name = "Cine de Prueba") =>
        await Db(async db => (await db.Cinemas.FirstOrDefaultAsync(c => c.Name == name) ?? await Seeds.CinemaAsync(db, name)).Id);

    private static object Layout(int rows, int positions, params string[] empty) => new { rows, positions, emptyCells = empty };

    private async Task<int> RoomWithSeatsAsync(string name = "Sala 1", int rows = 2, int positions = 5, int? cinemaId = null)
    {
        var token = await AdminAsync();
        var cinema = cinemaId ?? await CinemaIdAsync();
        var response = await Send(HttpMethod.Post, "/api/v1/cinema-rooms", new { cinemaId = cinema, name, layout = Layout(rows, positions) }, token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await Read<AdminRoomDto>(response)).Id;
    }

    private async Task<int> MovieIdAsync(string title = "Película", int minutes = 100, bool active = true) =>
        await Db(async db => (await Seeds.MovieAsync(db, title, active, minutes)).Id);

    private static object ShowtimeBody(int movieId, int roomId, DateTime startsAt, decimal price = 12.5m) =>
        new { movieId, cinemaRoomId = roomId, startsAt = DateTime.SpecifyKind(startsAt, DateTimeKind.Utc), price };

    // ── Seguridad ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/api/v1/cinema-rooms")]
    [InlineData("POST", "/api/v1/cinema-rooms")]
    [InlineData("PUT", "/api/v1/cinema-rooms/1")]
    [InlineData("GET", "/api/v1/cinema-rooms/1/seats")]
    [InlineData("PUT", "/api/v1/cinema-rooms/1/layout")]
    [InlineData("GET", "/api/v1/admin/showtimes")]
    [InlineData("GET", "/api/v1/admin/showtimes/1")]
    [InlineData("POST", "/api/v1/showtimes")]
    [InlineData("PUT", "/api/v1/showtimes/1")]
    [InlineData("POST", "/api/v1/showtimes/1/cancel")]
    [InlineData("GET", "/api/v1/admin/bookings")]
    [Trait("Case", "TC-ADM-03")]
    public async Task EveryAdminEndpoint_Rejects401WithoutAToken_And403ToACustomer(string method, string url)
    {
        var body = method is "POST" or "PUT" ? new { } : null;
        (await Send(new HttpMethod(method), url, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var customer = await NewUserAsync();
        (await Send(new HttpMethod(method), url, body, customer.Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ── Salas y layout ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "TC-ADM-05")]
    public async Task ALayoutWithACentralAisle_Has130Seats_AndTheAisleIsReportedAsEmptyCells()
    {
        var token = await AdminAsync();
        var cinema = await CinemaIdAsync();
        var aisle = Enumerable.Range(0, 10).Select(r => $"{(char)('A' + r)}7").ToArray();

        var created = await Send(HttpMethod.Post, "/api/v1/cinema-rooms", new { cinemaId = cinema, name = "Sala central", layout = Layout(10, 14, aisle) }, token);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var room = await Read<AdminRoomDto>(created);
        (room.SeatCount, room.Rows, room.Positions).ShouldBe((130, 10, 14));
        var seats = await Read<RoomSeatsDto>(await Get($"/api/v1/cinema-rooms/{room.Id}/seats", token));
        seats.Seats.Count.ShouldBe(130);
        seats.EmptyCells.ShouldBe(aisle.OrderBy(c => c[0]).ToList(), "la posición 7 de cada fila es el pasillo");
        seats.Seats.ShouldNotContain(s => s.Number == 7);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(27, 5)]
    [InlineData(5, 0)]
    [InlineData(5, 41)]
    [InlineData(-1, 5)]
    public async Task TheGridMustHave1To26Rows_And1To40Positions(int rows, int positions)
    {
        var response = await Send(HttpMethod.Post, "/api/v1/cinema-rooms", new { cinemaId = await CinemaIdAsync(), name = "Mala", layout = Layout(rows, positions) }, await AdminAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Db(db => db.CinemaRooms.CountAsync())).ShouldBe(0);
    }

    [Theory]
    [InlineData(26, 40)]
    [InlineData(1, 1)]
    public async Task TheGridLimitsThemselvesAreAccepted(int rows, int positions)
    {
        var response = await Send(HttpMethod.Post, "/api/v1/cinema-rooms", new { cinemaId = await CinemaIdAsync(), name = "Límite", layout = Layout(rows, positions) }, await AdminAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Read<AdminRoomDto>(response)).SeatCount.ShouldBe(rows * positions);
    }

    [Theory]
    [InlineData("Z9")]      // fuera de la cuadrícula de 2 × 5
    [InlineData("A6")]
    [InlineData("a1")]
    [InlineData("A0")]
    [InlineData("AA")]
    [InlineData("A41")]
    [InlineData("")]
    public async Task EmptyCellsMustBeInsideTheGrid(string cell)
    {
        var response = await Send(HttpMethod.Post, "/api/v1/cinema-rooms", new { cinemaId = await CinemaIdAsync(), name = "Mala", layout = Layout(2, 5, cell) }, await AdminAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Errors!.Keys.ShouldContain(k => k.Contains("emptyCells", StringComparison.OrdinalIgnoreCase) || k.Contains("EmptyCells"));
    }

    [Fact]
    public async Task ARoomCannotHaveRepeatedEmptyCells_NorBeEntirelyEmpty()
    {
        var token = await AdminAsync();
        var cinema = await CinemaIdAsync();

        (await Send(HttpMethod.Post, "/api/v1/cinema-rooms", new { cinemaId = cinema, name = "R", layout = Layout(2, 5, "A1", "A1") }, token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, "/api/v1/cinema-rooms", new { cinemaId = cinema, name = "V", layout = Layout(1, 2, "A1", "A2") }, token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, "/api/v1/cinema-rooms", new { cinemaId = cinema, name = "S", layout = (object?)null }, token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ARoomNameIsUniqueInsideItsCinema_ButTheSameNameIsFineInAnotherCinema()
    {
        var token = await AdminAsync();
        var a = await CinemaIdAsync("Cine A");
        var b = await CinemaIdAsync("Cine B");
        await RoomWithSeatsAsync("Sala 1", cinemaId: a);

        var repeated = await Send(HttpMethod.Post, "/api/v1/cinema-rooms", new { cinemaId = a, name = "Sala 1", layout = Layout(1, 3) }, token);
        repeated.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(repeated)).Code.ShouldBe("room_name_taken");

        (await Send(HttpMethod.Post, "/api/v1/cinema-rooms", new { cinemaId = b, name = "Sala 1", layout = Layout(1, 3) }, token)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ARoomInAnUnknownCinemaIs404()
    {
        var response = await Send(HttpMethod.Post, "/api/v1/cinema-rooms", new { cinemaId = 9999, name = "X", layout = Layout(1, 3) }, await AdminAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadProblem(response)).Code.ShouldBe("cinema_not_found");
    }

    [Fact]
    public async Task ARoomIsRenamed_AndTheListFiltersByCinema()
    {
        var token = await AdminAsync();
        var a = await CinemaIdAsync("Cine A");
        var b = await CinemaIdAsync("Cine B");
        var roomA = await RoomWithSeatsAsync("Sala A1", cinemaId: a);
        await RoomWithSeatsAsync("Sala B1", cinemaId: b);

        var renamed = await Send(HttpMethod.Put, $"/api/v1/cinema-rooms/{roomA}", new { name = "Sala Premium", isActive = true }, token);
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Read<List<AdminRoomDto>>(await Get($"/api/v1/cinema-rooms?cinemaId={a}", token))).Select(r => r.Name).ShouldBe(["Sala Premium"]);
        (await Read<List<AdminRoomDto>>(await Get("/api/v1/cinema-rooms", token))).Count.ShouldBe(2);
        (await Send(HttpMethod.Put, "/api/v1/cinema-rooms/9999", new { name = "X", isActive = true }, token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Get("/api/v1/cinema-rooms/9999/seats", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheLayoutIsRedefined_NewSeatsAppear_RemovedOnesDisappear_AndTheOthersKeepTheirIds()
    {
        var token = await AdminAsync();
        var room = await RoomWithSeatsAsync(rows: 2, positions: 4);
        var before = await Read<RoomSeatsDto>(await Get($"/api/v1/cinema-rooms/{room}/seats", token));
        var a1 = before.Seats.Single(s => s is { Row: "A", Number: 1 }).Id;

        var response = await Send(HttpMethod.Put, $"/api/v1/cinema-rooms/{room}/layout", Layout(3, 4, "A2", "B2", "C2"), token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var after = await Read<RoomSeatsDto>(response);
        (after.Rows, after.Positions, after.Seats.Count).ShouldBe((3, 4, 9));
        after.Seats.Single(s => s is { Row: "A", Number: 1 }).Id.ShouldBe(a1, "un asiento que sigue existiendo conserva su id");
        after.Seats.ShouldNotContain(s => s.Number == 2, "la posición 2 pasó a ser pasillo");
        (await Db(db => db.Seats.CountAsync(s => s.CinemaRoomId == room))).ShouldBe(9, "los que sobraban y no tenían historial se borraron");
    }

    [Fact]
    public async Task ASeatThatAppearsInPastShowtimesIsKeptAsInactive_NotDeleted_AndComesBackWhenReused()
    {
        var token = await AdminAsync();
        var room = await RoomWithSeatsAsync(rows: 1, positions: 3);
        var movie = await MovieIdAsync();
        await Db(async db => await Seeds.ShowtimeAsync(db, await db.Movies.SingleAsync(m => m.Id == movie), await db.CinemaRooms.SingleAsync(r => r.Id == room), DateTime.UtcNow.AddDays(-5)));

        (await Send(HttpMethod.Put, $"/api/v1/cinema-rooms/{room}/layout", Layout(1, 3, "A3"), token)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var a3 = await Db(db => db.Seats.SingleAsync(s => s.CinemaRoomId == room && s.Number == 3));
        a3.IsActive.ShouldBeFalse("la función pasada sigue apuntando a ese asiento");
        (await Read<RoomSeatsDto>(await Get($"/api/v1/cinema-rooms/{room}/seats", token))).Seats.Count.ShouldBe(2);

        (await Send(HttpMethod.Put, $"/api/v1/cinema-rooms/{room}/layout", Layout(1, 3), token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Db(db => db.Seats.SingleAsync(s => s.CinemaRoomId == room && s.Number == 3))).IsActive.ShouldBeTrue();
        (await Db(db => db.Seats.CountAsync(s => s.CinemaRoomId == room))).ShouldBe(3, "no se duplicó el asiento");
    }

    [Fact]
    [Trait("Case", "TC-ADM-05")]
    public async Task ARoomWithFutureShowtimes_CannotChangeItsLayoutOrBeDeactivated_AndNothingChanges()
    {
        var token = await AdminAsync();
        var room = await RoomWithSeatsAsync(rows: 2, positions: 5);
        await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(await MovieIdAsync(), room, InDays(2)), token);

        var layout = await Send(HttpMethod.Put, $"/api/v1/cinema-rooms/{room}/layout", Layout(5, 5), token);
        layout.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(layout)).Code.ShouldBe("room_has_future_showtimes");

        var deactivate = await Send(HttpMethod.Put, $"/api/v1/cinema-rooms/{room}", new { name = "Sala 1", isActive = false }, token);
        deactivate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(deactivate)).Code.ShouldBe("room_has_future_showtimes");

        (await Db(db => db.Seats.CountAsync(s => s.CinemaRoomId == room))).ShouldBe(10);
        (await Db(db => db.CinemaRooms.Where(r => r.Id == room).Select(r => r.IsActive).SingleAsync())).ShouldBeTrue();
    }

    [Fact]
    public async Task OnlyCancelledOrPastShowtimes_DoNotBlockTheLayout()
    {
        var token = await AdminAsync();
        var room = await RoomWithSeatsAsync();
        var movie = await MovieIdAsync();
        var created = await Read<AdminShowtimeDto>(await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room, InDays(2)), token));
        (await Send(HttpMethod.Post, $"/api/v1/showtimes/{created.Id}/cancel", null, token)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Send(HttpMethod.Put, $"/api/v1/cinema-rooms/{room}/layout", Layout(3, 3), token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ── Funciones ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "TC-ADM-02")]
    public async Task AShowtimeIsCreated_WithItsEndComputed_AndOneAvailableSeatPerActiveSeatOfTheRoom()
    {
        var token = await AdminAsync();
        var room = await RoomWithSeatsAsync(rows: 10, positions: 14);
        var movie = await MovieIdAsync(minutes: 100);
        var starts = InDays(3, 19);

        var response = await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room, starts, 18.5m), token);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var showtime = await Read<AdminShowtimeDto>(response);
        response.Headers.Location!.ToString().ShouldBe($"/api/v1/showtimes/{showtime.Id}");
        showtime.EndsAt.ShouldBe(starts.AddMinutes(100 + 15), "inicio + duración + 15 minutos de limpieza (RN-12)");
        (showtime.Capacity, showtime.SeatsSold, showtime.Status, showtime.Price).ShouldBe((140, 0, "Scheduled", 18.5m));
        (await Db(db => db.ShowSeats.CountAsync(s => s.ShowtimeId == showtime.Id && s.Status == ShowSeatStatus.Available))).ShouldBe(140);

        // Y ya se ve en la cartelera pública.
        (await Read<PagedResult<Cinerama.Application.Catalog.ShowtimeDto>>(await Get($"/api/v1/showtimes?movieId={movie}"))).Items.Select(s => s.Id).ShouldContain(showtime.Id);
    }

    [Fact]
    public async Task OnlyTheActiveSeatsOfTheRoomAreGenerated()
    {
        var token = await AdminAsync();
        var room = await RoomWithSeatsAsync(rows: 1, positions: 4);
        await Db(async db =>
        {
            (await db.Seats.FirstAsync(s => s.CinemaRoomId == room && s.Number == 2)).IsActive = false;
            await db.SaveChangesAsync();
        });

        var response = await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(await MovieIdAsync(), room, InDays(2)), token);

        (await Read<AdminShowtimeDto>(response)).Capacity.ShouldBe(3);
    }

    [Fact]
    [Trait("Case", "TC-ADM-01")]
    public async Task OverlappingShowtimesInTheSameRoom_AreRejected_IncludingTheCleaningTime()
    {
        var token = await AdminAsync();
        var room = await RoomWithSeatsAsync();
        var movie = await MovieIdAsync(minutes: 120); // 18:00 → 20:00, más 15 de limpieza = 20:15
        await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room, InDays(2, 18)), token);

        var overlap = await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room, InDays(2, 20)), token);
        overlap.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(overlap)).Code.ShouldBe("showtime_overlap");

        (await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room, InDays(2, 20, 10)), token)).StatusCode.ShouldBe(HttpStatusCode.Conflict); // dentro de la limpieza
        (await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room, InDays(2, 16)), token)).StatusCode.ShouldBe(HttpStatusCode.Conflict); // termina cuando la otra ya empezó
        (await Db(db => db.Showtimes.CountAsync())).ShouldBe(1, "ninguna de las rechazadas quedó a medias");
    }

    [Fact]
    public async Task ShowtimesTouchingEndToEnd_OrInDifferentRooms_AreFine()
    {
        var token = await AdminAsync();
        var cinema = await CinemaIdAsync();
        var room1 = await RoomWithSeatsAsync("Sala 1", cinemaId: cinema);
        var room2 = await RoomWithSeatsAsync("Sala 2", cinemaId: cinema);
        var movie = await MovieIdAsync(minutes: 105); // 105 + 15 = 120 minutos exactos

        (await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room1, InDays(2, 18)), token)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room1, InDays(2, 20)), token)).StatusCode.ShouldBe(HttpStatusCode.Created); // empieza justo cuando la otra termina
        (await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room2, InDays(2, 18)), token)).StatusCode.ShouldBe(HttpStatusCode.Created); // misma hora, otra sala
    }

    [Fact]
    public async Task ACancelledShowtimeNoLongerBlocksItsSlot()
    {
        var token = await AdminAsync();
        var room = await RoomWithSeatsAsync();
        var movie = await MovieIdAsync();
        var first = await Read<AdminShowtimeDto>(await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room, InDays(2)), token));
        await Send(HttpMethod.Post, $"/api/v1/showtimes/{first.Id}/cancel", null, token);

        (await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room, InDays(2)), token)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    [Trait("Case", "TC-ADM-01")]
    public async Task TwoAdminsSchedulingOverlappingShowtimesAtTheSameInstant_OnlyOneIsConfirmed()
    {
        var room = await RoomWithSeatsAsync();
        var movie = await MovieIdAsync();
        var admins = new[] { await AdminAsync(), await AdminAsync() };

        var responses = await AtTheSameTime(admins.Select(token => (Func<Task<HttpResponseMessage>>)(() =>
            Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room, InDays(2, 19)), token))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
        (await Db(db => db.Showtimes.CountAsync())).ShouldBe(1);
        (await Db(db => db.ShowSeats.CountAsync())).ShouldBe(10, "una sola función, con sus asientos una sola vez");
    }

    [Fact]
    public async Task EightAdminsSchedulingInTheSameRoomAtTheSameTime_LeaveExactlyOneShowtime()
    {
        var room = await RoomWithSeatsAsync();
        var movie = await MovieIdAsync();
        var tokens = new List<string>();
        for (var i = 0; i < 8; i++) tokens.Add(await AdminAsync());

        var responses = await AtTheSameTime(tokens.Select(token => (Func<Task<HttpResponseMessage>>)(() =>
            Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room, InDays(2, 21)), token))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict);
        (await Db(db => db.Showtimes.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task ShowtimesRequireAFutureStart_AnActiveMovie_AnActiveRoom_AndNotBeforeTheRelease()
    {
        var token = await AdminAsync();
        var room = await RoomWithSeatsAsync();
        var movie = await MovieIdAsync();

        var past = await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room, DateTime.UtcNow.AddMinutes(-5)), token);
        past.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(past)).Code.ShouldBe("showtime_in_past");

        var inactive = await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(await MovieIdAsync("De baja", active: false), room, InDays(2)), token);
        inactive.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(inactive)).Code.ShouldBe("movie_inactive");

        await Send(HttpMethod.Put, $"/api/v1/cinema-rooms/{await RoomWithSeatsAsync("Cerrada")}", new { name = "Cerrada", isActive = false }, token);
        var closedRoom = await Db(db => db.CinemaRooms.Where(r => r.Name == "Cerrada").Select(r => r.Id).SingleAsync());
        var closed = await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, closedRoom, InDays(2)), token);
        closed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(closed)).Code.ShouldBe("room_inactive");

        var premiere = await Db(async db =>
        {
            var m = await Seeds.MovieWithReleaseAsync(db, "Estreno lejano", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));
            return m.Id;
        });
        var early = await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(premiere, room, InDays(5)), token);
        early.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(early)).Code.ShouldBe("showtime_before_release");
        (await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(premiere, room, InDays(31)), token)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task UnknownMovieOrRoom_Is404()
    {
        var token = await AdminAsync();
        var room = await RoomWithSeatsAsync();
        var movie = await MovieIdAsync();

        (await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(9999, room, InDays(2)), token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, 9999, InDays(2)), token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(12.345)]
    [InlineData(100000000)]
    public async Task ThePriceMustBeZeroOrMore_WithTwoDecimalsAtMost(double price)
    {
        var response = await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(await MovieIdAsync(), await RoomWithSeatsAsync(), InDays(2), (decimal)price), await AdminAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Errors!.ShouldContainKey("price");
    }

    [Fact]
    public async Task APriceOfZeroIsAccepted_AndAStartWithoutAZoneIsRejectedAsAmbiguous()
    {
        var token = await AdminAsync();
        var room = await RoomWithSeatsAsync();
        var movie = await MovieIdAsync();

        (await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room, InDays(2), 0m), token)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var ambiguous = await Send(HttpMethod.Post, "/api/v1/showtimes", new { movieId = movie, cinemaRoomId = room, startsAt = "2099-01-01T19:00:00", price = 10 }, token);
        ambiguous.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(ambiguous)).Errors!.ShouldContainKey("startsAt");

        // Con una zona distinta de UTC, el instante es el mismo: 14:00 en Lima (UTC-5) es 19:00 UTC.
        var lima = await Send(HttpMethod.Post, "/api/v1/showtimes", new { movieId = movie, cinemaRoomId = room, startsAt = "2099-01-01T14:00:00-05:00", price = 10 }, token);
        lima.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Read<AdminShowtimeDto>(lima)).StartsAt.ShouldBe(new DateTime(2099, 1, 1, 19, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task AShowtimeInARoomWithoutSeatsIsRejected()
    {
        var token = await AdminAsync();
        var room = await RoomWithSeatsAsync(rows: 1, positions: 2);
        await Db(async db => { foreach (var s in await db.Seats.Where(s => s.CinemaRoomId == room).ToListAsync()) s.IsActive = false; await db.SaveChangesAsync(); });

        var response = await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(await MovieIdAsync(), room, InDays(2)), token);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(response)).Code.ShouldBe("room_has_no_seats");
    }

    [Fact]
    public async Task AShowtimeWithoutBookingsIsEdited_AndChangingRoomRegeneratesItsSeats()
    {
        var token = await AdminAsync();
        var cinema = await CinemaIdAsync();
        var small = await RoomWithSeatsAsync("Chica", 1, 4, cinema);
        var big = await RoomWithSeatsAsync("Grande", 3, 5, cinema);
        var movie = await MovieIdAsync(minutes: 90);
        var created = await Read<AdminShowtimeDto>(await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, small, InDays(2, 18), 10m), token));

        var response = await Send(HttpMethod.Put, $"/api/v1/showtimes/{created.Id}", ShowtimeBody(movie, big, InDays(2, 20), 22m), token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var edited = await Read<AdminShowtimeDto>(response);
        (edited.Id, edited.CinemaRoomId, edited.Price, edited.Capacity).ShouldBe((created.Id, big, 22m, 15));
        edited.EndsAt.ShouldBe(InDays(2, 20).AddMinutes(105));
        (await Db(db => db.ShowSeats.CountAsync(s => s.ShowtimeId == created.Id))).ShouldBe(15, "los asientos de la sala anterior se reemplazaron");
    }

    [Fact]
    public async Task EditingAShowtime_CannotOverlapAnotherOne_ButMayOverlapItself()
    {
        var token = await AdminAsync();
        var room = await RoomWithSeatsAsync();
        var movie = await MovieIdAsync(minutes: 105);
        var first = await Read<AdminShowtimeDto>(await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room, InDays(2, 18)), token));
        var second = await Read<AdminShowtimeDto>(await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movie, room, InDays(2, 22)), token));

        (await Send(HttpMethod.Put, $"/api/v1/showtimes/{first.Id}", ShowtimeBody(movie, room, InDays(2, 18, 30), 11m), token)).StatusCode.ShouldBe(HttpStatusCode.OK); // se solapa consigo misma
        var clash = await Send(HttpMethod.Put, $"/api/v1/showtimes/{second.Id}", ShowtimeBody(movie, room, InDays(2, 19)), token);
        clash.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(clash)).Code.ShouldBe("showtime_overlap");
    }

    [Fact]
    public async Task AShowtimeWithBookings_CannotBeEdited_NotEvenIfTheyWereCancelled()
    {
        var token = await AdminAsync();
        var showtime = await NewShowtimeAsync();
        var customer = await NewUserAsync();
        var booking = await BookedAsync(customer, showtime, "A1");
        var movieId = await Db(db => db.Showtimes.Where(s => s.Id == showtime.Id).Select(s => s.MovieId).SingleAsync());

        var withBooking = await Send(HttpMethod.Put, $"/api/v1/showtimes/{showtime.Id}", ShowtimeBody(movieId, showtime.RoomId, InDays(5)), token);
        withBooking.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(withBooking)).Code.ShouldBe("showtime_has_bookings");

        (await CancelBooking(customer, booking.Id)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Send(HttpMethod.Put, $"/api/v1/showtimes/{showtime.Id}", ShowtimeBody(movieId, showtime.RoomId, InDays(5)), token)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task APastOrCancelledShowtimeCannotBeEdited_AndAnUnknownOneIs404()
    {
        var token = await AdminAsync();
        var pastShowtime = await NewShowtimeAsync(startsAtUtc: DateTime.UtcNow.AddHours(-3));
        var movieId = await Db(db => db.Showtimes.Where(s => s.Id == pastShowtime.Id).Select(s => s.MovieId).SingleAsync());
        var past = await Send(HttpMethod.Put, $"/api/v1/showtimes/{pastShowtime.Id}", ShowtimeBody(movieId, pastShowtime.RoomId, InDays(3)), token);
        past.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(past)).Code.ShouldBe("showtime_already_started");

        var cancelled = await NewShowtimeAsync(status: ShowtimeStatus.Cancelled, roomName: "Otra");
        var movie2 = await Db(db => db.Showtimes.Where(s => s.Id == cancelled.Id).Select(s => s.MovieId).SingleAsync());
        var response = await Send(HttpMethod.Put, $"/api/v1/showtimes/{cancelled.Id}", ShowtimeBody(movie2, cancelled.RoomId, InDays(3)), token);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(response)).Code.ShouldBe("showtime_cancelled");

        (await Send(HttpMethod.Put, "/api/v1/showtimes/9999", ShowtimeBody(movieId, pastShowtime.RoomId, InDays(3)), token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Case", "TC-ADM-06")]
    public async Task CancellingAShowtime_CancelsItsConfirmedBookings_FreesTheLocks_AndNotifiesTheClients()
    {
        var token = await AdminAsync();
        var showtime = await NewShowtimeAsync();
        var buyer = await NewUserAsync();
        var holder = await NewUserAsync();
        var booking = await BookedAsync(buyer, showtime, "A1", "A2");
        (await LockSeats(holder, showtime, ["B1"])).StatusCode.ShouldBe(HttpStatusCode.OK);
        Fixture.Notifier.Clear();

        var response = await Send(HttpMethod.Post, $"/api/v1/showtimes/{showtime.Id}/cancel", null, token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Read<AdminShowtimeDto>(response)).Status.ShouldBe("Cancelled");
        (await Db(db => db.Bookings.Where(b => b.Id == booking.Id).Select(b => b.Status).SingleAsync())).ShouldBe(BookingStatus.Cancelled);
        (await Db(db => db.Bookings.Where(b => b.Id == booking.Id).Select(b => b.CancelledAt).SingleAsync())).ShouldNotBeNull();
        (await Db(db => db.BookingSeats.CountAsync(bs => bs.BookingId == booking.Id && bs.ReleasedAt == null))).ShouldBe(0);
        (await Db(db => db.ShowSeats.CountAsync(s => s.ShowtimeId == showtime.Id && s.Status != ShowSeatStatus.Available))).ShouldBe(0, "ni vendidos ni bloqueados");
        Fixture.Notifier.Events.ShouldContain(e => e.ShowtimeId == showtime.Id && e.Changes.Count == 3);

        // El cliente lo ve cancelado en "mis reservas", y la función ya no vende.
        (await Read<BookingDto>(await Get($"/api/v1/bookings/{booking.Id}", buyer.Token))).Status.ShouldBe("Cancelled");
        var late = await LockSeats(holder, showtime, ["B2"]);
        late.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(late)).Code.ShouldBe("showtime_cancelled");
        (await Read<PagedResult<Cinerama.Application.Catalog.ShowtimeDto>>(await Get("/api/v1/showtimes"))).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ACancelledOrStartedShowtimeCannotBeCancelledAgain_AndAnUnknownOneIs404()
    {
        var token = await AdminAsync();
        var future = await NewShowtimeAsync();
        (await Send(HttpMethod.Post, $"/api/v1/showtimes/{future.Id}/cancel", null, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = await Send(HttpMethod.Post, $"/api/v1/showtimes/{future.Id}/cancel", null, token);
        again.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(again)).Code.ShouldBe("showtime_cancelled");

        var started = await NewShowtimeAsync(startsAtUtc: DateTime.UtcNow.AddMinutes(-10), roomName: "Empezada");
        var late = await Send(HttpMethod.Post, $"/api/v1/showtimes/{started.Id}/cancel", null, token);
        late.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(late)).Code.ShouldBe("showtime_already_started");

        (await Send(HttpMethod.Post, "/api/v1/showtimes/9999/cancel", null, token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheAdminShowtimeListShowsPastAndCancelledOnes_WithSalesAndFilters()
    {
        var token = await AdminAsync();
        var cinema = await CinemaIdAsync();
        var room = await RoomWithSeatsAsync("Sala 1", 2, 5, cinema);
        var movieA = await MovieIdAsync("A");
        var movieB = await MovieIdAsync("B");
        var future = await Read<AdminShowtimeDto>(await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movieA, room, InDays(2)), token));
        var toCancel = await Read<AdminShowtimeDto>(await Send(HttpMethod.Post, "/api/v1/showtimes", ShowtimeBody(movieB, room, InDays(3)), token));
        await Send(HttpMethod.Post, $"/api/v1/showtimes/{toCancel.Id}/cancel", null, token);
        await Db(async db => await Seeds.ShowtimeAsync(db, await db.Movies.SingleAsync(m => m.Id == movieA), await db.CinemaRooms.SingleAsync(r => r.Id == room), DateTime.UtcNow.AddDays(-4)));

        async Task<List<AdminShowtimeDto>> List(string query) => (await Read<PagedResult<AdminShowtimeDto>>(await Get($"/api/v1/admin/showtimes{query}", token))).Items.ToList();

        (await List("")).Count.ShouldBe(3, "incluye la pasada y la cancelada");
        (await List("?upcomingOnly=true")).Select(s => s.Id).ShouldBe([toCancel.Id, future.Id]);
        (await List("?status=cancelled")).Single().Id.ShouldBe(toCancel.Id);
        (await List($"?movieId={movieB}")).Single().Id.ShouldBe(toCancel.Id);
        (await List($"?roomId={room}")).Count.ShouldBe(3);
        (await List($"?cinemaId={cinema}")).Count.ShouldBe(3);
        var limaDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(future.StartsAt, TimeZoneInfo.FindSystemTimeZoneById("America/Lima")));
        (await List($"?from={limaDay:yyyy-MM-dd}&to={limaDay:yyyy-MM-dd}")).Single().Id.ShouldBe(future.Id);
        (await Get("/api/v1/admin/showtimes?status=nada", token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ── Reservas ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheAdminSeesTheBookingsOfEveryCustomer_WithFilters_WhileACustomerOnlySeesHisOwn()
    {
        var token = await AdminAsync();
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync(fullName: "Ana Pérez");
        var beto = await NewUserAsync(fullName: "Beto Ruiz");
        var anaBooking = await BookedAsync(ana, showtime, "A1", "A2");
        var betoBooking = await BookedAsync(beto, showtime, "B1");
        (await CancelBooking(beto, betoBooking.Id)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var movieId = await Db(db => db.Showtimes.Where(s => s.Id == showtime.Id).Select(s => s.MovieId).SingleAsync());

        async Task<List<AdminBookingListItemDto>> List(string query) => (await Read<PagedResult<AdminBookingListItemDto>>(await Get($"/api/v1/admin/bookings{query}", token))).Items.ToList();

        var all = await List("");
        all.Select(b => b.Code).ShouldBe([betoBooking.Code, anaBooking.Code], "las más recientes primero");
        all.Single(b => b.Code == anaBooking.Code).Seats.ShouldBe(["A1", "A2"]);
        all.Single(b => b.Code == anaBooking.Code).CustomerName.ShouldBe("Ana Pérez");
        (await List("?status=confirmed")).Single().Code.ShouldBe(anaBooking.Code);
        (await List("?status=cancelled")).Single().Code.ShouldBe(betoBooking.Code);
        (await List($"?movieId={movieId}")).Count.ShouldBe(2);
        (await List("?movieId=9999")).ShouldBeEmpty();
        (await List($"?search={anaBooking.Code}")).Single().Code.ShouldBe(anaBooking.Code);
        (await List("?search=Ruiz")).Single().Code.ShouldBe(betoBooking.Code);
        (await List($"?search={ana.Email}")).Single().Code.ShouldBe(anaBooking.Code);
        (await Get("/api/v1/admin/bookings?status=nada", token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Get("/api/v1/admin/bookings?page=0", token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var limaDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(showtime.StartsAt, TimeZoneInfo.FindSystemTimeZoneById("America/Lima")));
        (await List($"?from={limaDay:yyyy-MM-dd}&to={limaDay:yyyy-MM-dd}")).Count.ShouldBe(2);
        (await List($"?from={limaDay.AddDays(1):yyyy-MM-dd}")).ShouldBeEmpty();
        (await List($"?to={limaDay.AddDays(-1):yyyy-MM-dd}")).ShouldBeEmpty();

        // El cliente sigue viendo solo las suyas (RN-32).
        (await Read<PagedResult<BookingListItemDto>>(await Get("/api/v1/bookings", ana.Token))).Items.Select(b => b.Code).ShouldBe([anaBooking.Code]);
    }

    [Fact]
    public async Task TheAdminCancelsACustomersBooking_AndTheSeatsComeBack()
    {
        var token = await AdminAsync();
        var showtime = await NewShowtimeAsync();
        var customer = await NewUserAsync();
        var booking = await BookedAsync(customer, showtime, "A1");

        var response = await Send(HttpMethod.Post, $"/api/v1/bookings/{booking.Id}/cancel", null, token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Available);
    }
}
