using System.ComponentModel.DataAnnotations;
using Cinerama.Application.Bookings;
using Cinerama.Application.Common.Options;
using Cinerama.Application.Seating;

namespace Cinerama.UnitTests.Seating;

public class SeatingOptionsAndValidatorsTests
{
    private static IList<ValidationResult> Validate(object options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    // Los valores por defecto son las reglas de negocio confirmadas (docs/business-rules.md).
    [Fact]
    public void Defaults_AreTheConfirmedBusinessRules()
    {
        var seating = new SeatingOptions();
        seating.LockMinutes.ShouldBe(5);          // RN-19
        seating.MaxSeatsPerUser.ShouldBe(10);     // RN-20, RN-24
        seating.CleanupSeconds.ShouldBe(30);      // CU-06
        seating.CleanupEnabled.ShouldBeTrue();
        seating.LockDuration.ShouldBe(TimeSpan.FromMinutes(5));

        new CinemaOptions().CancellationHours.ShouldBe(2); // RN-30
        Validate(seating).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void LockMinutes_OutOfRange_IsRejected(int minutes) =>
        Validate(new SeatingOptions { LockMinutes = minutes }).ShouldNotBeEmpty();

    [Fact]
    public void TicketKey_ShorterThan32Chars_IsRejected()
    {
        Validate(new TicketOptions()).ShouldNotBeEmpty();
        Validate(new TicketOptions { SigningKey = new string('k', 31) }).ShouldNotBeEmpty();
        Validate(new TicketOptions { SigningKey = new string('k', 32) }).ShouldBeEmpty();
    }

    // ── Validadores de solicitudes ──────────────────────────────────────────

    [Fact]
    public void SeatSelection_RequiresAtLeastOnePositiveId()
    {
        var validator = new SeatSelectionRequestValidator();

        validator.Validate(new SeatSelectionRequest([1, 2])).IsValid.ShouldBeTrue();
        validator.Validate(new SeatSelectionRequest([])).IsValid.ShouldBeFalse();
        validator.Validate(new SeatSelectionRequest(null!)).IsValid.ShouldBeFalse();
        validator.Validate(new SeatSelectionRequest([1, 0])).IsValid.ShouldBeFalse();
        validator.Validate(new SeatSelectionRequest([-5])).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void SeatSelection_CapsTheRequestSize()
    {
        var validator = new SeatSelectionRequestValidator();

        validator.Validate(new SeatSelectionRequest(Enumerable.Range(1, 100).ToList())).IsValid.ShouldBeTrue();
        validator.Validate(new SeatSelectionRequest(Enumerable.Range(1, 101).ToList())).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void CreateBooking_RequiresAShowtimeAndSeats()
    {
        var validator = new CreateBookingRequestValidator();

        validator.Validate(new CreateBookingRequest(5, [1, 2])).IsValid.ShouldBeTrue();
        validator.Validate(new CreateBookingRequest(0, [1])).IsValid.ShouldBeFalse();
        validator.Validate(new CreateBookingRequest(5, [])).IsValid.ShouldBeFalse();
        validator.Validate(new CreateBookingRequest(5, [0])).IsValid.ShouldBeFalse();
    }
}
