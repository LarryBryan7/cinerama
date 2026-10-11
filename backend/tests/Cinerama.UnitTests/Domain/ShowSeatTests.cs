using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;

namespace Cinerama.UnitTests.Domain;

/// <summary>Reglas de bloqueo, liberación y confirmación. El reloj entra como parámetro: nada espera 5 minutos.</summary>
public class ShowSeatTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 20, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
    private const int Ana = 1, Luis = 2;

    private static ShowSeat Available() => new() { Status = ShowSeatStatus.Available };
    private static ShowSeat Booked() => new() { Status = ShowSeatStatus.Booked };
    private static ShowSeat LockedBy(int user, DateTime until) =>
        new() { Status = ShowSeatStatus.Locked, LockedByUserId = user, LockedUntil = until };

    // ── TryLock ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "RN-18")]
    public void Lock_AnAvailableSeat_LocksItForFiveMinutes()
    {
        var seat = Available();

        seat.TryLock(Ana, Now, FiveMinutes).ShouldBe(LockOutcome.Locked);

        seat.Status.ShouldBe(ShowSeatStatus.Locked);
        seat.LockedByUserId.ShouldBe(Ana);
        seat.LockedUntil.ShouldBe(Now.AddMinutes(5)); // RN-19
    }

    [Fact]
    [Trait("Case", "RN-18")]
    public void Lock_ASeatBookedByAnyone_IsUnavailable()
    {
        var seat = Booked();

        seat.TryLock(Ana, Now, FiveMinutes).ShouldBe(LockOutcome.Unavailable);

        seat.Status.ShouldBe(ShowSeatStatus.Booked);
    }

    [Fact]
    [Trait("Case", "RN-18")]
    public void Lock_ASeatLockedByAnotherUser_IsUnavailable_AndNothingChanges()
    {
        var seat = LockedBy(Luis, Now.AddMinutes(3));

        seat.TryLock(Ana, Now, FiveMinutes).ShouldBe(LockOutcome.Unavailable);

        seat.LockedByUserId.ShouldBe(Luis);
        seat.LockedUntil.ShouldBe(Now.AddMinutes(3));
    }

    [Fact]
    [Trait("Case", "RN-19")]
    public void Lock_YourOwnValidLock_DoesNotExtendTheDeadline()
    {
        var original = Now.AddMinutes(2);
        var seat = LockedBy(Ana, original);

        seat.TryLock(Ana, Now, FiveMinutes).ShouldBe(LockOutcome.AlreadyMine);

        seat.LockedUntil.ShouldBe(original); // no se renueva volviendo a bloquear
    }

    [Fact]
    [Trait("Case", "RN-23")]
    public void Lock_AnExpiredLockOfAnotherUser_CanBeTakenEvenIfCleanupHasNotRun()
    {
        var seat = LockedBy(Luis, Now.AddSeconds(-1));

        seat.TryLock(Ana, Now, FiveMinutes).ShouldBe(LockOutcome.Locked);

        seat.LockedByUserId.ShouldBe(Ana);
        seat.LockedUntil.ShouldBe(Now.AddMinutes(5));
    }

    [Fact]
    public void Lock_AnExpiredLockOfYourOwn_IsRenewedAsAFreshLock()
    {
        var seat = LockedBy(Ana, Now.AddMinutes(-1));

        seat.TryLock(Ana, Now, FiveMinutes).ShouldBe(LockOutcome.Locked);

        seat.LockedUntil.ShouldBe(Now.AddMinutes(5));
    }

    // ── Vencimiento ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "RN-23")]
    public void ALock_ExpiresExactlyWhenItsDeadlineIsReached()
    {
        var until = Now.AddMinutes(5);

        ShowSeat.IsExpired(until, until.AddTicks(-1)).ShouldBeFalse();
        ShowSeat.IsExpired(until, until).ShouldBeTrue();
        ShowSeat.IsExpired(until, until.AddSeconds(1)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(ShowSeatStatus.Available, null, ShowSeatStatus.Available)]
    [InlineData(ShowSeatStatus.Booked, null, ShowSeatStatus.Booked)]
    public void EffectiveStatus_KeepsStatesWithoutALock(ShowSeatStatus status, DateTime? until, ShowSeatStatus expected) =>
        ShowSeat.EffectiveStatus(status, until, Now).ShouldBe(expected);

    [Fact]
    [Trait("Case", "RN-23")]
    public void EffectiveStatus_ShowsAnExpiredLockAsAvailable_AndAValidOneAsLocked()
    {
        ShowSeat.EffectiveStatus(ShowSeatStatus.Locked, Now.AddSeconds(-1), Now).ShouldBe(ShowSeatStatus.Available);
        ShowSeat.EffectiveStatus(ShowSeatStatus.Locked, Now.AddSeconds(1), Now).ShouldBe(ShowSeatStatus.Locked);
    }

    // ── TryRelease ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "RN-22")]
    public void Release_YourOwnLock_MakesTheSeatAvailable()
    {
        var seat = LockedBy(Ana, Now.AddMinutes(3));

        seat.TryRelease(Ana, Now).ShouldBe(ReleaseOutcome.Released);

        (seat.Status, seat.LockedByUserId, seat.LockedUntil).ShouldBe((ShowSeatStatus.Available, null, null));
    }

    [Fact]
    [Trait("Case", "RN-22")]
    public void Release_ALockOfAnotherUser_IsRefused_AndTheirLockSurvives()
    {
        var seat = LockedBy(Luis, Now.AddMinutes(3));

        seat.TryRelease(Ana, Now).ShouldBe(ReleaseOutcome.NotYours);

        seat.LockedByUserId.ShouldBe(Luis);
    }

    [Fact]
    public void Release_ABookedSeat_IsRefused() =>
        Booked().TryRelease(Ana, Now).ShouldBe(ReleaseOutcome.NotYours);

    [Fact]
    public void Release_AnAvailableSeat_IsAHarmlessNoOp() =>
        Available().TryRelease(Ana, Now).ShouldBe(ReleaseOutcome.NothingToRelease);

    [Fact]
    public void Release_AnExpiredLockOfAnotherUser_IsTreatedAsFree() =>
        LockedBy(Luis, Now.AddSeconds(-1)).TryRelease(Ana, Now).ShouldBe(ReleaseOutcome.NothingToRelease);

    // ── TryConfirm ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "RN-24")]
    public void Confirm_YourValidLock_BooksTheSeat()
    {
        var seat = LockedBy(Ana, Now.AddMinutes(1));

        seat.TryConfirm(Ana, Now).ShouldBeTrue();

        (seat.Status, seat.LockedByUserId, seat.LockedUntil).ShouldBe((ShowSeatStatus.Booked, null, null));
    }

    [Fact]
    [Trait("Case", "RN-24")]
    public void Confirm_AnExpiredLock_IsRefused_EvenIfItWasYours()
    {
        var seat = LockedBy(Ana, Now.AddSeconds(-1));

        seat.TryConfirm(Ana, Now).ShouldBeFalse();

        seat.Status.ShouldBe(ShowSeatStatus.Locked); // no cambia nada
    }

    [Fact]
    [Trait("Case", "RN-24")]
    public void Confirm_ALockOfAnotherUser_IsRefused() =>
        LockedBy(Luis, Now.AddMinutes(1)).TryConfirm(Ana, Now).ShouldBeFalse();

    [Fact]
    [Trait("Case", "RN-24")]
    public void Confirm_WithoutHavingLockedFirst_IsRefused()
    {
        Available().TryConfirm(Ana, Now).ShouldBeFalse();
        Booked().TryConfirm(Ana, Now).ShouldBeFalse();
    }

    [Fact]
    public void ReleaseFromBooking_MakesABookedSeatAvailableAgain()
    {
        var seat = Booked();

        seat.ReleaseFromBooking();

        (seat.Status, seat.LockedByUserId, seat.LockedUntil).ShouldBe((ShowSeatStatus.Available, null, null));
    }
}
