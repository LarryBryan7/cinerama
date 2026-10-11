using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using Cinerama.Domain.Exceptions;

namespace Cinerama.UnitTests.Domain;

public class BookingCancellationTests
{
    private static readonly DateTime StartsAt = new(2026, 10, 3, 20, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan TwoHours = TimeSpan.FromHours(2);

    private static Booking Confirmed() => new()
    {
        Status = BookingStatus.Confirmed,
        Seats = [new BookingSeat { Price = 10m }, new BookingSeat { Price = 10m }],
    };

    [Fact]
    [Trait("Case", "RN-30")]
    public void Customer_CanCancelMoreThanTwoHoursBefore_AndTheSeatsAreReleased()
    {
        var booking = Confirmed();
        var now = StartsAt.AddHours(-5);

        booking.Cancel(now, StartsAt, byAdmin: false, TwoHours);

        booking.Status.ShouldBe(BookingStatus.Cancelled);
        booking.CancelledAt.ShouldBe(now);
        booking.Seats.ShouldAllBe(s => s.ReleasedAt == now);
    }

    [Fact]
    [Trait("Case", "RN-30")]
    public void Customer_CanCancelExactlyTwoHoursBefore() =>
        Should.NotThrow(() => Confirmed().Cancel(StartsAt - TwoHours, StartsAt, byAdmin: false, TwoHours));

    [Theory]
    [Trait("Case", "RN-30")]
    [InlineData(119)]   // 1 h 59 min antes
    [InlineData(90)]
    [InlineData(1)]
    [InlineData(-10)]   // la función ya empezó
    public void Customer_CannotCancelInsideTheTwoHourWindow(int minutesBefore)
    {
        var booking = Confirmed();

        var ex = Should.Throw<DomainRuleException>(() =>
            booking.Cancel(StartsAt.AddMinutes(-minutesBefore), StartsAt, byAdmin: false, TwoHours));

        ex.Code.ShouldBe("cancellation_window_closed");
        booking.Status.ShouldBe(BookingStatus.Confirmed); // no cambia nada
        booking.Seats.ShouldAllBe(s => s.ReleasedAt == null);
    }

    [Fact]
    [Trait("Case", "RN-30")]
    public void Admin_CanCancelUntilTheShowtimeStarts_EvenInsideTheCustomerWindow() =>
        Should.NotThrow(() => Confirmed().Cancel(StartsAt.AddMinutes(-1), StartsAt, byAdmin: true, TwoHours));

    [Theory]
    [Trait("Case", "RN-17")]
    [InlineData(0)]
    [InlineData(30)]
    public void Admin_CannotCancelOnceTheShowtimeHasStarted(int minutesAfter)
    {
        var ex = Should.Throw<DomainRuleException>(() =>
            Confirmed().Cancel(StartsAt.AddMinutes(minutesAfter), StartsAt, byAdmin: true, TwoHours));

        ex.Code.ShouldBe("showtime_already_started");
    }

    [Fact]
    public void ACancelledBooking_CannotBeCancelledAgain()
    {
        var booking = Confirmed();
        booking.Cancel(StartsAt.AddHours(-5), StartsAt, byAdmin: false, TwoHours);

        Should.Throw<InvalidOperationException>(() => booking.Cancel(StartsAt.AddHours(-4), StartsAt, byAdmin: false, TwoHours));
    }
}
