using System.Net;
using System.Text.RegularExpressions;
using Cinerama.Application.Bookings;
using Cinerama.Application.Common;
using Cinerama.Domain.Enums;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cinerama.IntegrationTests.Bookings;

public class BookingFlowTests(ApiFixture fixture) : ReservationTest(fixture)
{
    // ── Reserva exitosa ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "TC-BOOK-01")]
    public async Task Confirm_LockedSeats_CreatesTheBookingAndMarksTheSeatsAsSold()
    {
        var showtime = await NewShowtimeAsync(price: 12.5m);
        var ana = await NewUserAsync();
        (await LockSeats(ana, showtime, ["A1", "A2"])).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await Book(ana, showtime, ["A1", "A2"], key: "k-1");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var booking = await Read<BookingDto>(response);
        response.Headers.Location!.ToString().ShouldEndWith($"/api/v1/bookings/{booking.Id}");
        booking.Status.ShouldBe("Confirmed");
        booking.TotalAmount.ShouldBe(25.00m);
        booking.Seats.Select(s => (s.Label, s.Price)).ShouldBe([("A1", 12.5m), ("A2", 12.5m)]);
        booking.Code.ShouldMatch("^[A-HJ-NP-Z2-9]{8}$"); // RN-28: 8 caracteres, sin 0/O/1/I
        booking.Showtime.Id.ShouldBe(showtime.Id);

        (await SeatInDb(showtime, "A1")).ShouldBe(new ShowSeatSnapshot(ShowSeatStatus.Booked, null, null));
        (await CountInDb(db => db.BookingSeats.CountAsync(bs => bs.ReleasedAt == null))).ShouldBe(2);
        (await Fixture.Notifier.Events.ShouldHaveSingleItem(e => e.Changes.All(c => c.Status == ShowSeatStatus.Booked))).Changes.Count.ShouldBe(2);
        await AssertNoDoubleBookingAsync();
    }

    [Fact]
    [Trait("Case", "RN-35")]
    public async Task Confirm_ReturnsATicketPayloadSignedWithTheBookingCode()
    {
        var showtime = await NewShowtimeAsync();
        var booking = await BookedAsync(await NewUserAsync(), showtime, "A1");

        var signer = Fixture.Factory.Services.GetRequiredService<ITicketSigner>();

        signer.TryVerify(booking.TicketPayload, out var code).ShouldBeTrue();
        code.ShouldBe(booking.Code);
        signer.TryVerify(booking.TicketPayload.Replace(booking.Code, "AAAAAAAA"), out _).ShouldBeFalse();
    }

    [Fact]
    [Trait("Case", "RN-25")]
    public async Task Confirm_CopiesThePriceToTheBooking_SoLaterPriceChangesDoNotAffectIt()
    {
        var showtime = await NewShowtimeAsync(price: 10m);
        var ana = await NewUserAsync();
        var booking = await BookedAsync(ana, showtime, "A1");

        await Db(db => db.Showtimes.ExecuteUpdateAsync(s => s.SetProperty(x => x.Price, 99m)));

        var again = await Read<BookingDto>(await Get($"/api/v1/bookings/{booking.Id}", ana.Token));
        again.Seats.Single().Price.ShouldBe(10m);
        again.TotalAmount.ShouldBe(10m);
    }

    // ── Reglas de la confirmación ───────────────────────────────────────────

    [Fact]
    [Trait("Case", "RN-24")]
    public async Task Confirm_WithoutHavingLockedTheSeats_Returns409LockExpired_AndCreatesNothing()
    {
        var showtime = await NewShowtimeAsync();

        var response = await Book(await NewUserAsync(), showtime, ["A1", "A2"]);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await ReadProblem(response);
        problem.Code.ShouldBe("lock_expired");
        problem.Conflicts!.Select(c => c.Label).ShouldBe(["A1", "A2"]);
        (await CountInDb(db => db.Bookings.CountAsync())).ShouldBe(0);
    }

    [Fact]
    [Trait("Case", "RN-24")]
    public async Task Confirm_SeatsHeldByAnotherUser_Returns409_AndTheirLockSurvives()
    {
        var showtime = await NewShowtimeAsync();
        var luis = await NewUserAsync();
        var ana = await NewUserAsync();
        await LockSeats(luis, showtime, ["A1"]);

        var response = await Book(ana, showtime, ["A1"]);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await SeatInDb(showtime, "A1")).LockedByUserId.ShouldBe(luis.Id);
    }

    [Fact]
    [Trait("Case", "RN-24")]
    public async Task Confirm_IsAllOrNothing_OneSeatNotHeldKeepsTheOtherLocked()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        await LockSeats(ana, showtime, ["A1"]);

        var response = await Book(ana, showtime, ["A1", "A2"]);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(response)).Conflicts!.Select(c => c.Label).ShouldBe(["A2"]);
        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Locked); // sigue siendo mío y bloqueado
        (await CountInDb(db => db.Bookings.CountAsync())).ShouldBe(0);
    }

    [Fact]
    [Trait("Case", "TC-BOOK-02")]
    public async Task Confirm_AfterMyLockExpired_Returns409_EvenIfNobodyTookTheSeat()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        await using var api = new TimedApi(Fixture);
        await LockSeats(api.As(ana), showtime, ["A1"], api.Client);

        api.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        var response = await Book(api.As(ana), showtime, ["A1"], client: api.Client);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(response)).Code.ShouldBe("lock_expired");
        (await CountInDb(db => db.Bookings.CountAsync())).ShouldBe(0);
    }

    [Fact]
    [Trait("Case", "TC-BOOK-02")]
    public async Task Confirm_AfterMyLockExpiredAndAnotherUserTookTheSeat_Returns409_AndTheirLockIsKept()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        var luis = await NewUserAsync();
        await using var api = new TimedApi(Fixture);
        await LockSeats(api.As(ana), showtime, ["A1"], api.Client);
        api.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        (await LockSeats(api.As(luis), showtime, ["A1"], api.Client)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var anas = await Book(api.As(ana), showtime, ["A1"], client: api.Client);

        anas.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(anas)).Code.ShouldBe("lock_expired");
        (await SeatInDb(showtime, "A1")).LockedByUserId.ShouldBe(luis.Id);
        (await Book(api.As(luis), showtime, ["A1"], client: api.Client)).StatusCode.ShouldBe(HttpStatusCode.Created); // y la de Luis sí prospera
        await AssertNoDoubleBookingAsync();
    }

    [Fact]
    [Trait("Case", "RN-16")]
    public async Task Confirm_AfterTheShowtimeStarted_Returns422SalesClosed()
    {
        await using var api = new TimedApi(Fixture);
        var showtime = await NewShowtimeAsync(startsAtUtc: api.Now.AddMinutes(10));
        var ana = await NewUserAsync();
        await LockSeats(api.As(ana), showtime, ["A1"], api.Client);

        api.Time.Advance(TimeSpan.FromMinutes(11));
        var response = await Book(api.As(ana), showtime, ["A1"], client: api.Client);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(response)).Code.ShouldBe("sales_closed");
    }

    [Fact]
    public async Task Confirm_OnAnUnknownShowtime_Returns404()
    {
        var response = await BookIds(await NewUserAsync(), 999999, [1]);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadProblem(response)).Code.ShouldBe("showtime_not_found");
    }

    [Fact]
    [Trait("Case", "RN-24")]
    public async Task Confirm_MoreThanTenSeats_Returns422()
    {
        var showtime = await NewShowtimeAsync(rows: 3, perRow: 5);

        var response = await Book(await NewUserAsync(), showtime, showtime.Seats.Take(11).Select(s => s.Label).ToArray());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(response)).Code.ShouldBe("seat_limit_exceeded");
    }

    [Fact]
    public async Task Confirm_WithNoSeats_Returns400()
    {
        var showtime = await NewShowtimeAsync();

        var response = await Send(HttpMethod.Post, "/api/v1/bookings", new { showtimeId = showtime.Id, seatIds = Array.Empty<int>() }, (await NewUserAsync()).Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Code.ShouldBe("validation_failed");
    }

    [Fact]
    public async Task Confirm_WithAnOverlongIdempotencyKey_Returns400()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        await LockSeats(ana, showtime, ["A1"]);

        var response = await Book(ana, showtime, ["A1"], key: new string('k', 65));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CountInDb(db => db.Bookings.CountAsync())).ShouldBe(0);
    }

    // ── Idempotencia (RN-27) ────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "TC-BOOK-03")]
    public async Task Confirm_RepeatingTheSameKey_ReturnsTheOriginalBooking_AndCreatesNoSecondOne()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        await LockSeats(ana, showtime, ["A1", "A2"]);

        var first = await Book(ana, showtime, ["A1", "A2"], key: "mi-clave");
        var second = await Book(ana, showtime, ["A1", "A2"], key: "mi-clave");

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var a = await Read<BookingDto>(first);
        var b = await Read<BookingDto>(second);
        (b.Id, b.Code).ShouldBe((a.Id, a.Code));
        (await CountInDb(db => db.Bookings.CountAsync())).ShouldBe(1);
        await AssertNoDoubleBookingAsync();
    }

    [Fact]
    [Trait("Case", "RN-27")]
    public async Task Confirm_TheSameKeyWithADifferentRequest_Returns422()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        await LockSeats(ana, showtime, ["A1", "A2"]);
        await Book(ana, showtime, ["A1"], key: "clave");

        var response = await Book(ana, showtime, ["A2"], key: "clave");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(response)).Code.ShouldBe("idempotency_key_reused");
        (await SeatInDb(showtime, "A2")).Status.ShouldBe(ShowSeatStatus.Locked); // no se vendió
    }

    [Fact]
    [Trait("Case", "RN-27")]
    public async Task Confirm_TheSameKeyFromDifferentUsers_AreIndependentBookings()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        var luis = await NewUserAsync();
        await LockSeats(ana, showtime, ["A1"]);
        await LockSeats(luis, showtime, ["A2"]);

        var anas = await Read<BookingDto>(await Book(ana, showtime, ["A1"], key: "compartida"));
        var luiss = await Read<BookingDto>(await Book(luis, showtime, ["A2"], key: "compartida"));

        anas.Id.ShouldNotBe(luiss.Id); // la clave de una persona nunca devuelve la reserva de otra
        luiss.Seats.Single().Label.ShouldBe("A2");
    }

    [Fact]
    [Trait("Case", "RN-27")]
    public async Task Confirm_ReusingAKeyAfter24Hours_Returns409KeyExpired()
    {
        await using var api = new TimedApi(Fixture);
        var showtime = await NewShowtimeAsync(startsAtUtc: api.Now.AddDays(5));
        var ana = await NewUserAsync();
        await LockSeats(api.As(ana), showtime, ["A1"], api.Client);
        (await Book(api.As(ana), showtime, ["A1"], key: "vieja", client: api.Client)).StatusCode.ShouldBe(HttpStatusCode.Created);

        api.Time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));
        var response = await Book(api.As(ana), showtime, ["A1"], key: "vieja", client: api.Client);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(response)).Code.ShouldBe("idempotency_key_expired");
    }

    [Fact]
    public async Task Confirm_WithoutAKey_ASecondConfirmationOfTheSameSeatsFails()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync();
        await LockSeats(ana, showtime, ["A1"]);
        (await Book(ana, showtime, ["A1"])).StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await Book(ana, showtime, ["A1"]);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict); // sin clave no hay forma de saber que es un reintento
        (await CountInDb(db => db.Bookings.CountAsync())).ShouldBe(1);
    }

    // ── Consultas ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "HU-13")]
    public async Task List_ReturnsMyBookings_NewestFirst_WithSeatLabels()
    {
        var showtime = await NewShowtimeAsync(rows: 2, perRow: 5);
        var ana = await NewUserAsync();
        var first = await BookedAsync(ana, showtime, "B2", "B1");
        var second = await BookedAsync(ana, showtime, "A1");

        var list = await Read<PagedResult<BookingListItemDto>>(await Get("/api/v1/bookings", ana.Token));

        list.TotalCount.ShouldBe(2);
        list.Items.Select(b => b.Id).ShouldBe([second.Id, first.Id]);
        list.Items[1].SeatLabels.ShouldBe(["B1", "B2"]); // ordenados por fila y número
        list.Items[0].ShouldSatisfyAllConditions(b => b.RoomName.ShouldBe("Sala T"), b => b.TotalAmount.ShouldBe(10m));
    }

    [Fact]
    public async Task List_FiltersByStatus_AndPaginates()
    {
        var showtime = await NewShowtimeAsync(rows: 2, perRow: 5);
        var ana = await NewUserAsync();
        var one = await BookedAsync(ana, showtime, "A1");
        await BookedAsync(ana, showtime, "A2");
        await BookedAsync(ana, showtime, "A3");
        (await CancelBooking(ana, one.Id)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var cancelled = await Read<PagedResult<BookingListItemDto>>(await Get("/api/v1/bookings?status=Cancelled", ana.Token));
        var page = await Read<PagedResult<BookingListItemDto>>(await Get("/api/v1/bookings?pageSize=2", ana.Token));

        cancelled.Items.Select(b => b.Id).ShouldBe([one.Id]);
        (page.Items.Count, page.TotalCount).ShouldBe((2, 3));
    }

    [Fact]
    public async Task List_InvalidStatusOrPaging_Returns400()
    {
        var ana = await NewUserAsync();

        (await Get("/api/v1/bookings?status=Quizas", ana.Token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Get("/api/v1/bookings?pageSize=500", ana.Token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_ReturnsTheBookingWithShowtimeAndTicket()
    {
        var showtime = await NewShowtimeAsync(roomName: "Sala 7");
        var ana = await NewUserAsync();
        var created = await BookedAsync(ana, showtime, "A1");

        var booking = await Read<BookingDto>(await Get($"/api/v1/bookings/{created.Id}", ana.Token));

        booking.Showtime.RoomName.ShouldBe("Sala 7");
        booking.Showtime.MovieTitle.ShouldStartWith("Película de prueba");
        booking.TicketPayload.ShouldStartWith(booking.Code + ".");
        booking.CancelledAt.ShouldBeNull();
    }
}

internal static class AssertionExtensions
{
    /// <summary>Verifica que haya exactamente un elemento que cumpla la condición y lo devuelve.</summary>
    public static Task<T> ShouldHaveSingleItem<T>(this IReadOnlyList<T> items, Func<T, bool> predicate)
    {
        items.Count(predicate).ShouldBe(1);
        return Task.FromResult(items.Single(predicate));
    }
}
