using System.Net;
using Cinerama.Application.Seating;
using Cinerama.Domain.Enums;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Seating;

public class SeatLockTests(ApiFixture fixture) : ReservationTest(fixture)
{
    [Fact]
    [Trait("Case", "HU-09")]
    public async Task Lock_AvailableSeats_BlocksThemForFiveMinutes()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();

        var response = await LockSeats(ana, showtime, ["A1", "A2"]);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await Read<LockResultDto>(response);
        (result.LockedUntil - DateTime.UtcNow).TotalMinutes.ShouldBeInRange(4.8, 5.1); // RN-19
        result.Seats.Select(s => s.Label).ShouldBe(["A1", "A2"]);
        result.Seats.ShouldAllBe(s => s.Status == "Locked" && s.IsMine);

        var seat = await SeatInDb(showtime, "A1");
        (seat.Status, seat.LockedByUserId).ShouldBe((ShowSeatStatus.Locked, ana.Id));
    }

    [Fact]
    [Trait("Case", "TC-SEAT-01")]
    public async Task Lock_ASoldSeat_Returns409_AndStaysSold()
    {
        var showtime = await NewShowtimeAsync();
        await BookedAsync(await NewUserAsync(), showtime, "A1");

        var response = await LockSeats(await NewUserAsync(), showtime, ["A1"]);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await ReadProblem(response);
        problem.Code.ShouldBe("seat_unavailable");
        problem.Conflicts!.Select(c => c.Label).ShouldBe(["A1"]);
        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Booked);
    }

    [Fact]
    [Trait("Case", "TC-SEAT-02")]
    public async Task Lock_ASeatHeldByAnotherUser_Returns409_AndTheirLockIsUntouched()
    {
        var showtime = await NewShowtimeAsync();
        var luis = await NewUserAsync();
        var ana = await NewUserAsync();
        await LockSeats(luis, showtime, ["A1"]);
        var before = await SeatInDb(showtime, "A1");

        var response = await LockSeats(ana, showtime, ["A1"]);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(response)).Code.ShouldBe("seat_unavailable");
        (await SeatInDb(showtime, "A1")).ShouldBe(before); // mismo dueño y mismo vencimiento
    }

    [Fact]
    [Trait("Case", "TC-SEAT-03")]
    public async Task Lock_IsAllOrNothing_AFailingSeatLeavesTheOthersFree()
    {
        var showtime = await NewShowtimeAsync();
        var luis = await NewUserAsync();
        var ana = await NewUserAsync();
        await LockSeats(luis, showtime, ["A1"]);
        Fixture.Notifier.Clear();

        var response = await LockSeats(ana, showtime, ["A1", "A2"]);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(response)).Conflicts!.Select(c => c.Label).ShouldBe(["A1"]); // solo informa el que falla
        (await SeatInDb(showtime, "A2")).Status.ShouldBe(ShowSeatStatus.Available);      // A2 no quedó bloqueado
        Fixture.Notifier.Events.ShouldBeEmpty();                                          // y no se avisó nada
    }

    [Fact]
    [Trait("Case", "TC-SEAT-06")]
    public async Task Lock_AUserCanHoldAtMostTenSeatsPerShowtime()
    {
        var showtime = await NewShowtimeAsync(rows: 3, perRow: 5);
        var ana = await NewUserAsync();
        string[] ten = ["A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3", "B4", "B5"];
        (await LockSeats(ana, showtime, ten)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var eleventh = await LockSeats(ana, showtime, ["C1"]);

        eleventh.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(eleventh)).Code.ShouldBe("lock_limit_exceeded");
        (await CountInDb(db => db.ShowSeats.CountAsync(s => s.Status == ShowSeatStatus.Locked))).ShouldBe(10);

        // Volver a bloquear uno que ya es mío no cuenta como uno más.
        (await LockSeats(ana, showtime, ["A1"])).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Case", "RN-20")]
    public async Task Lock_ARequestForMoreThanTenSeatsIsRejected()
    {
        var showtime = await NewShowtimeAsync(rows: 3, perRow: 5);
        var labels = showtime.Seats.Take(11).Select(s => s.Label).ToArray();

        var response = await LockSeats(await NewUserAsync(), showtime, labels);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(response)).Code.ShouldBe("lock_limit_exceeded");
        (await CountInDb(db => db.ShowSeats.CountAsync(s => s.Status == ShowSeatStatus.Locked))).ShouldBe(0);
    }

    [Fact]
    [Trait("Case", "TC-SEAT-07")]
    public async Task Lock_LockingYourOwnSeatAgain_DoesNotExtendTheDeadline()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        await using var api = new TimedApi(Fixture);
        var first = await Read<LockResultDto>(await LockSeats(api.As(ana), showtime, ["A1"], api.Client));

        api.Time.Advance(TimeSpan.FromMinutes(3));
        var again = await LockSeats(api.As(ana), showtime, ["A1"], api.Client);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Read<LockResultDto>(again)).LockedUntil.ShouldBe(first.LockedUntil); // RN-19
        (await SeatInDb(showtime, "A1")).LockedUntil.ShouldBe(first.LockedUntil);
        api.Notifier.Events.Count.ShouldBe(1); // el segundo bloqueo no cambió nada: no hay nada que avisar
    }

    [Fact]
    [Trait("Case", "RN-23")]
    public async Task Lock_AnExpiredLockOfAnotherUserCanBeTaken_WithoutWaitingForCleanup()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        var luis = await NewUserAsync();
        await using var api = new TimedApi(Fixture);
        await LockSeats(api.As(ana), showtime, ["A1"], api.Client);

        api.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        var response = await LockSeats(api.As(luis), showtime, ["A1"], api.Client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SeatInDb(showtime, "A1")).LockedByUserId.ShouldBe(luis.Id);
    }

    [Fact]
    [Trait("Case", "TC-SEAT-09")]
    public async Task Lock_AfterTheShowtimeStarted_Returns422SalesClosed()
    {
        var showtime = await NewShowtimeAsync(startsAtUtc: DateTime.UtcNow.AddMinutes(-1));

        var response = await LockSeats(await NewUserAsync(), showtime, ["A1"]);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(response)).Code.ShouldBe("sales_closed");
    }

    [Fact]
    public async Task Lock_ACancelledShowtime_Returns422()
    {
        var showtime = await NewShowtimeAsync(status: ShowtimeStatus.Cancelled);

        var response = await LockSeats(await NewUserAsync(), showtime, ["A1"]);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(response)).Code.ShouldBe("showtime_cancelled");
    }

    [Fact]
    [Trait("Case", "TC-SEAT-10")]
    public async Task Lock_OnAnUnknownShowtime_Returns404()
    {
        var response = await LockIds(await NewUserAsync(), 999999, [1]);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadProblem(response)).Code.ShouldBe("showtime_not_found");
    }

    [Fact]
    public async Task Lock_ASeatThatDoesNotBelongToTheShowtime_Returns404_AndLocksNothing()
    {
        var showtime = await NewShowtimeAsync();
        var otherRoom = await NewShowtimeAsync(roomName: "Otra sala");

        var response = await LockIds(await NewUserAsync(), showtime.Id, [showtime.Seat("A1").SeatId, otherRoom.Seat("A1").SeatId]);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadProblem(response)).Code.ShouldBe("seat_not_found");
        (await CountInDb(db => db.ShowSeats.CountAsync(s => s.Status == ShowSeatStatus.Locked))).ShouldBe(0);
    }

    [Theory]
    [InlineData("""{ "seatIds": [] }""")]
    [InlineData("""{ "seatIds": [0] }""")]
    [InlineData("""{ "seatIds": [-3] }""")]
    [InlineData("""{ }""")]
    public async Task Lock_InvalidRequest_Returns400(string body)
    {
        var showtime = await NewShowtimeAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/showtimes/{showtime.Id}/seats/lock")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new("Bearer", (await NewUserAsync()).Token);

        var response = await Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Code.ShouldBe("validation_failed");
    }

    [Fact]
    public async Task Lock_RepeatedIdsInOneRequest_CountOnce()
    {
        var showtime = await NewShowtimeAsync();
        var a1 = showtime.Seat("A1").SeatId;
        var a2 = showtime.Seat("A2").SeatId;

        var response = await LockIds(await NewUserAsync(), showtime.Id, [a1, a1, a2]);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Read<LockResultDto>(response)).Seats.Count.ShouldBe(2);
    }

    // ── Avisos: solo después del commit y sin romper la operación ───────────

    [Fact]
    [Trait("Case", "ADR-005")]
    public async Task Lock_NotifiesTheChange_AfterTheCommit()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        ShowSeatStatus? statusSeenByTheNotification = null;

        // El aviso lee la base desde otra conexión. Si se enviara ANTES del commit, la fila seguiría bloqueada
        // por la transacción de la API y esta lectura fallaría por tiempo de espera.
        Fixture.Notifier.OnNotify = _ => Db(async db =>
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 3000");
            statusSeenByTheNotification = await db.ShowSeats.AsNoTracking().Where(s => s.SeatId == showtime.Seat("A1").SeatId)
                .Select(s => s.Status).SingleAsync();
        });

        (await LockSeats(ana, showtime, ["A1"])).StatusCode.ShouldBe(HttpStatusCode.OK);

        statusSeenByTheNotification.ShouldBe(ShowSeatStatus.Locked);
        var notified = Fixture.Notifier.Events.Single();
        notified.ShowtimeId.ShouldBe(showtime.Id);
        notified.Changes.Select(c => (c.SeatId, c.Status)).ShouldBe([(showtime.Seat("A1").SeatId, ShowSeatStatus.Locked)]);
    }

    [Fact]
    [Trait("Case", "ADR-005")]
    public async Task Lock_StillSucceeds_WhenTheNotificationServiceIsDown()
    {
        var showtime = await NewShowtimeAsync();
        Fixture.Notifier.ThrowOnNotify = true;

        var response = await LockSeats(await NewUserAsync(), showtime, ["A1"]);

        response.StatusCode.ShouldBe(HttpStatusCode.OK); // los datos ya están confirmados: el cliente se resincroniza por REST
        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Locked);
    }
}
