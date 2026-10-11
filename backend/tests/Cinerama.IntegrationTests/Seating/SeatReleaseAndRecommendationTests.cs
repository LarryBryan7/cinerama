using System.Net;
using Cinerama.Application.Seating;
using Cinerama.Domain.Enums;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Seating;

public class SeatReleaseTests(ApiFixture fixture) : ReservationTest(fixture)
{
    [Fact]
    [Trait("Case", "HU-10")]
    public async Task Release_YourOwnLock_MakesTheSeatAvailableAndNotifies()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        await LockSeats(ana, showtime, ["A1", "A2"]);
        Fixture.Notifier.Clear();

        var response = await ReleaseSeats(ana, showtime, ["A1"]);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SeatInDb(showtime, "A1")).ShouldBe(new ShowSeatSnapshot(ShowSeatStatus.Available, null, null));
        (await SeatInDb(showtime, "A2")).Status.ShouldBe(ShowSeatStatus.Locked); // el resto de mi selección sigue
        Fixture.Notifier.Events.Single().Changes.Select(c => c.Status).ShouldBe([ShowSeatStatus.Available]);
    }

    [Fact]
    [Trait("Case", "TC-SEAT-08")]
    public async Task Release_ASeatHeldByAnotherUser_Returns404_AndTheirLockSurvives()
    {
        var showtime = await NewShowtimeAsync();
        var luis = await NewUserAsync();
        var ana = await NewUserAsync();
        await LockSeats(luis, showtime, ["A1"]);
        var before = await SeatInDb(showtime, "A1");

        var response = await ReleaseSeats(ana, showtime, ["A1"]);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadProblem(response)).Code.ShouldBe("lock_not_found");
        (await SeatInDb(showtime, "A1")).ShouldBe(before);
    }

    [Fact]
    public async Task Release_IsAllOrNothing_AnotherUsersSeatBlocksTheWholeRequest()
    {
        var showtime = await NewShowtimeAsync();
        var luis = await NewUserAsync();
        var ana = await NewUserAsync();
        await LockSeats(ana, showtime, ["A1"]);
        await LockSeats(luis, showtime, ["A2"]);

        var response = await ReleaseSeats(ana, showtime, ["A1", "A2"]);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SeatInDb(showtime, "A1")).LockedByUserId.ShouldBe(ana.Id); // el mío tampoco se liberó
        (await SeatInDb(showtime, "A2")).LockedByUserId.ShouldBe(luis.Id);
    }

    [Fact]
    public async Task Release_ASoldSeat_Returns404()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        await BookedAsync(ana, showtime, "A1");

        var response = await ReleaseSeats(ana, showtime, ["A1"]);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Booked);
    }

    [Fact]
    public async Task Release_ASeatThatIsAlreadyFree_IsAHarmlessNoOp_WithoutNotification()
    {
        var showtime = await NewShowtimeAsync();

        var response = await ReleaseSeats(await NewUserAsync(), showtime, ["A1"]);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Fixture.Notifier.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Release_AfterReleasing_AnotherUserCanTakeTheSeatImmediately()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        var luis = await NewUserAsync();
        await LockSeats(ana, showtime, ["A1"]);
        await ReleaseSeats(ana, showtime, ["A1"]);

        (await LockSeats(luis, showtime, ["A1"])).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Release_OnAnUnknownShowtime_Returns404()
    {
        var response = await Send(HttpMethod.Post, "/api/v1/showtimes/999999/seats/release", new { seatIds = new[] { 1 } }, (await NewUserAsync()).Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadProblem(response)).Code.ShouldBe("showtime_not_found");
    }
}

public class SeatRecommendationEndpointTests(ApiFixture fixture) : ReservationTest(fixture)
{
    private async Task<List<SeatRecommendationDto>> Recommend(TestUser user, TestShowtime showtime, string query = "") =>
        await Read<List<SeatRecommendationDto>>(await Get($"/api/v1/showtimes/{showtime.Id}/seats/recommendations{query}", user.Token));

    [Fact]
    [Trait("Case", "HU-11")]
    public async Task Recommendations_DefaultToAPairOfContiguousSeats_UpToThreeOptions()
    {
        var showtime = await NewShowtimeAsync(rows: 6, perRow: 9);

        var options = await Recommend(await NewUserAsync(), showtime);

        options.Count.ShouldBe(3);
        options.ShouldAllBe(o => o.Seats.Count == 2);
        options.Select(o => o.Score).ShouldBe(options.Select(o => o.Score).OrderByDescending(s => s).ToList());
        options.SelectMany(o => o.Seats).Select(s => s.SeatId).Distinct().Count().ShouldBe(6); // sin asientos en común
    }

    [Fact]
    public async Task Recommendations_AreAdjacentSeatsOfTheSameRow()
    {
        var showtime = await NewShowtimeAsync(rows: 6, perRow: 9);

        var options = await Recommend(await NewUserAsync(), showtime, "?count=4");

        options.ShouldNotBeEmpty();
        foreach (var option in options)
        {
            option.Seats.Select(s => s.Label[0]).Distinct().Count().ShouldBe(1);
            var numbers = option.Seats.Select(s => int.Parse(s.Label[1..])).Order().ToList();
            numbers.Zip(numbers.Skip(1), (a, b) => b - a).ShouldAllBe(gap => gap == 1);
        }
    }

    [Fact]
    public async Task Recommendations_SkipSeatsThatAreSoldOrHeldByAnyone_IncludingMe()
    {
        var showtime = await NewShowtimeAsync(rows: 1, perRow: 6);
        var ana = await NewUserAsync();
        var luis = await NewUserAsync();
        await BookedAsync(luis, showtime, "A2");
        await LockSeats(ana, showtime, ["A5"]);

        var labels = (await Recommend(ana, showtime)).SelectMany(o => o.Seats).Select(s => s.Label).ToList();

        labels.ShouldNotContain("A2");
        labels.ShouldNotContain("A5");
    }

    [Fact]
    public async Task Recommendations_WhenNoGroupFits_ReturnAnEmptyList_NotAnError()
    {
        var showtime = await NewShowtimeAsync(rows: 2, perRow: 3);

        var response = await Get($"/api/v1/showtimes/{showtime.Id}/seats/recommendations?count=4", (await NewUserAsync()).Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Read<List<SeatRecommendationDto>>(response)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("count=0")]
    [InlineData("count=11")]
    [InlineData("count=-2")]
    public async Task Recommendations_CountOutOfRange_Returns400(string query)
    {
        var showtime = await NewShowtimeAsync();

        var response = await Get($"/api/v1/showtimes/{showtime.Id}/seats/recommendations?{query}", (await NewUserAsync()).Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Errors.ShouldNotBeNull().ShouldContainKey("count");
    }

    [Fact]
    public async Task Recommendations_OfAnUnknownShowtime_Returns404()
    {
        var response = await Get("/api/v1/showtimes/999999/seats/recommendations", (await NewUserAsync()).Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
