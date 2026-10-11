using System.Net;
using Cinerama.Application.Seating;
using Cinerama.Domain.Enums;
using Cinerama.IntegrationTests.Infrastructure;

namespace Cinerama.IntegrationTests.Seating;

public class SeatMapTests(ApiFixture fixture) : ReservationTest(fixture)
{
    [Fact]
    [Trait("Case", "HU-08")]
    public async Task Map_ListsEverySeat_OrderedByRowAndNumber_AllAvailableAtFirst()
    {
        var showtime = await NewShowtimeAsync(rows: 2, perRow: 5, price: 12.5m);
        var ana = await NewUserAsync();

        var map = await MapAsync(ana, showtime);

        map.ShowtimeId.ShouldBe(showtime.Id);
        map.Price.ShouldBe(12.5m);
        map.ShowtimeStatus.ShouldBe("Scheduled");
        map.Seats.Select(s => s.Label).ShouldBe(["A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3", "B4", "B5"]);
        map.Seats.ShouldAllBe(s => s.Status == "Available" && !s.IsMine && s.LockedUntil == null);
    }

    [Fact]
    public async Task Map_AnAisleIsAGapInTheNumbering()
    {
        var showtime = await NewShowtimeAsync(rows: 1, perRow: 5, aisle: 3);

        var map = await MapAsync(await NewUserAsync(), showtime);

        map.Seats.Select(s => s.Number).ShouldBe([1, 2, 4, 5]);
    }

    [Fact]
    [Trait("Case", "HU-08")]
    public async Task Map_DistinguishesMySelectionFromOthersAndSoldSeats()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        var luis = await NewUserAsync();
        (await LockSeats(ana, showtime, ["A1"])).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LockSeats(luis, showtime, ["A2"])).StatusCode.ShouldBe(HttpStatusCode.OK);
        await BookedAsync(luis, showtime, "B1");

        var anasMap = (await MapAsync(ana, showtime)).Seats.ToDictionary(s => s.Label);
        var luissMap = (await MapAsync(luis, showtime)).Seats.ToDictionary(s => s.Label);

        anasMap["A1"].ShouldSatisfyAllConditions(
            s => s.Status.ShouldBe("Locked"), s => s.IsMine.ShouldBeTrue(), s => s.LockedUntil.ShouldNotBeNull());
        anasMap["A2"].ShouldSatisfyAllConditions(
            s => s.Status.ShouldBe("Locked"), s => s.IsMine.ShouldBeFalse(), s => s.LockedUntil.ShouldBeNull());
        anasMap["B1"].ShouldSatisfyAllConditions(s => s.Status.ShouldBe("Booked"), s => s.IsMine.ShouldBeFalse());
        anasMap["A3"].Status.ShouldBe("Available");

        luissMap["A2"].IsMine.ShouldBeTrue();
        luissMap["A1"].IsMine.ShouldBeFalse();
    }

    [Fact]
    public async Task Map_NeverExposesWhoHoldsASeat()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync("ana-privada@example.test");
        var luis = await NewUserAsync();
        await LockSeats(ana, showtime, ["A1"]);

        var json = await (await MapOf(luis, showtime)).Content.ReadAsStringAsync();

        json.ShouldNotContain("ana-privada");
        json.ToLowerInvariant().ShouldNotContain("lockedby");
        json.ToLowerInvariant().ShouldNotContain("userid");
    }

    // RN-23 + ADR-005: la corrección no depende del proceso de limpieza (que en estos tests está apagado).
    [Fact]
    [Trait("Case", "TC-SEAT-04")]
    public async Task Map_ShowsAnExpiredLockAsAvailable_WithoutAnyCleanup()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        var luis = await NewUserAsync();
        await using var api = new TimedApi(Fixture);
        (await LockSeats(api.As(ana), showtime, ["A1"], api.Client)).StatusCode.ShouldBe(HttpStatusCode.OK);

        api.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        var map = await MapAsync(api.As(luis), showtime, api.Client);
        map.Seats.Single(s => s.Label == "A1").Status.ShouldBe("Available");
        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Locked); // en la base sigue "Locked": nadie lo limpió
    }

    [Fact]
    [Trait("Case", "TC-SEAT-10")]
    public async Task Map_OfAnUnknownShowtime_Returns404()
    {
        var response = await Get("/api/v1/showtimes/999999/seats", (await NewUserAsync()).Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadProblem(response)).Code.ShouldBe("showtime_not_found");
    }

    [Fact]
    public async Task Map_RequiresAuthentication()
    {
        var showtime = await NewShowtimeAsync();

        (await Get($"/api/v1/showtimes/{showtime.Id}/seats")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Map_OfACancelledShowtime_StillShowsItsStatus()
    {
        var showtime = await NewShowtimeAsync(status: ShowtimeStatus.Cancelled);

        (await MapAsync(await NewUserAsync(), showtime)).ShowtimeStatus.ShouldBe("Cancelled");
    }
}
