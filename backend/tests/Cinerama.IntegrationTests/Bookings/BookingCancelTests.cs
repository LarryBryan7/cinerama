using System.Net;
using Cinerama.Application.Bookings;
using Cinerama.Domain.Enums;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Bookings;

public class BookingCancelTests(ApiFixture fixture) : ReservationTest(fixture)
{
    private static DateTime AtMs(DateTime value) => new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

    [Fact]
    [Trait("Case", "TC-BOOK-04")]
    public async Task Cancel_OnTime_CancelsTheBooking_ReleasesTheSeats_AndLetsAnotherUserBookThem()
    {
        var showtime = await NewShowtimeAsync(startsAtUtc: DateTime.UtcNow.AddHours(24));
        var ana = await NewUserAsync();
        var luis = await NewUserAsync();
        var booking = await BookedAsync(ana, showtime, "A1", "A2");
        Fixture.Notifier.Clear();

        var response = await CancelBooking(ana, booking.Id);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cancelled = await Read<BookingDto>(response);
        cancelled.Status.ShouldBe("Cancelled");
        cancelled.CancelledAt.ShouldNotBeNull();

        (await SeatInDb(showtime, "A1")).ShouldBe(new ShowSeatSnapshot(ShowSeatStatus.Available, null, null));
        (await CountInDb(db => db.BookingSeats.CountAsync(bs => bs.ReleasedAt != null))).ShouldBe(2);
        Fixture.Notifier.Events.Single().Changes.Select(c => c.Status).ShouldAllBe(s => s == ShowSeatStatus.Available);

        // El índice único filtrado deja de contar la reserva cancelada: otra persona puede reservar el mismo asiento.
        var again = await BookedAsync(luis, showtime, "A1");
        again.Id.ShouldNotBe(booking.Id);
        await AssertNoDoubleBookingAsync();
    }

    [Fact]
    [Trait("Case", "TC-BOOK-05")]
    public async Task Cancel_InsideTheTwoHourWindow_Returns422_AndNothingChanges()
    {
        await using var api = new TimedApi(Fixture);
        var showtime = await NewShowtimeAsync(startsAtUtc: api.Now.AddMinutes(90));
        var ana = await NewUserAsync();
        await LockSeats(api.As(ana), showtime, ["A1"], api.Client);
        var booking = await Read<BookingDto>(await Book(api.As(ana), showtime, ["A1"], client: api.Client));

        var response = await CancelBooking(api.As(ana), booking.Id, api.Client);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(response)).Code.ShouldBe("cancellation_window_closed");
        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Booked);
        (await Read<BookingDto>(await Get($"/api/v1/bookings/{booking.Id}", api.As(ana).Token))).Status.ShouldBe("Confirmed");
    }

    [Fact]
    [Trait("Case", "RN-30")]
    public async Task Cancel_TheTwoHourLimit_IsInclusive_AndOneMillisecondInsideTheWindowIsRefused()
    {
        await using var api = new TimedApi(Fixture);
        var start = AtMs(api.Now.AddHours(5));
        var showtime = await NewShowtimeAsync(rows: 1, perRow: 4, startsAtUtc: start);
        var ana = await NewUserAsync();
        await LockSeats(api.As(ana), showtime, ["A1", "A2", "A3"], api.Client);
        var beforeTheLimit = await Read<BookingDto>(await Book(api.As(ana), showtime, ["A1"], client: api.Client));
        var atTheLimit = await Read<BookingDto>(await Book(api.As(ana), showtime, ["A2"], client: api.Client));
        var insideTheWindow = await Read<BookingDto>(await Book(api.As(ana), showtime, ["A3"], client: api.Client));

        var limit = start - TimeSpan.FromHours(2);

        api.Time.SetUtcNow(new DateTimeOffset(limit - TimeSpan.FromMilliseconds(1)));
        (await CancelBooking(api.As(ana), beforeTheLimit.Id, api.Client)).StatusCode.ShouldBe(HttpStatusCode.OK);

        api.Time.SetUtcNow(new DateTimeOffset(limit));
        (await CancelBooking(api.As(ana), atTheLimit.Id, api.Client)).StatusCode.ShouldBe(HttpStatusCode.OK); // "hasta 2 horas antes" incluye la hora exacta

        api.Time.SetUtcNow(new DateTimeOffset(limit + TimeSpan.FromMilliseconds(1)));
        var refused = await CancelBooking(api.As(ana), insideTheWindow.Id, api.Client);
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(refused)).Code.ShouldBe("cancellation_window_closed");
    }

    [Fact]
    [Trait("Case", "TC-BOOK-06")]
    public async Task Cancel_TwiceReturns409_TheSecondTime()
    {
        var showtime = await NewShowtimeAsync(startsAtUtc: DateTime.UtcNow.AddHours(24));
        var ana = await NewUserAsync();
        var booking = await BookedAsync(ana, showtime, "A1");
        (await CancelBooking(ana, booking.Id)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await CancelBooking(ana, booking.Id);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(second)).Code.ShouldBe("booking_already_cancelled");
    }

    [Fact]
    [Trait("Case", "RN-30")]
    public async Task Cancel_AnAdminCanCancelInsideTheCustomerWindow_ButNotAfterTheShowtimeStarted()
    {
        await using var api = new TimedApi(Fixture);
        var showtime = await NewShowtimeAsync(rows: 1, perRow: 4, startsAtUtc: api.Now.AddMinutes(60));
        var ana = await NewUserAsync();
        var admin = await NewUserAsync(admin: true);
        await LockSeats(api.As(ana), showtime, ["A1", "A2"], api.Client);
        var first = await Read<BookingDto>(await Book(api.As(ana), showtime, ["A1"], client: api.Client));
        var second = await Read<BookingDto>(await Book(api.As(ana), showtime, ["A2"], client: api.Client));

        // Dentro de las 2 h el cliente ya no puede, pero el administrador sí (hasta el inicio).
        (await CancelBooking(api.As(ana), first.Id, api.Client)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CancelBooking(api.As(admin, "Admin"), first.Id, api.Client)).StatusCode.ShouldBe(HttpStatusCode.OK);

        api.Time.Advance(TimeSpan.FromMinutes(61));
        var afterStart = await CancelBooking(api.As(admin, "Admin"), second.Id, api.Client);

        afterStart.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(afterStart)).Code.ShouldBe("showtime_already_started");
    }

    [Fact]
    public async Task Cancel_AnUnknownBooking_Returns404()
    {
        var response = await CancelBooking(await NewUserAsync(), 999999);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadProblem(response)).Code.ShouldBe("booking_not_found");
    }
}
