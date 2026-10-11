using FluentValidation;

namespace Cinerama.Application.Bookings;

public sealed class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    public const int MaxItems = 100;
    public const int MaxIdempotencyKeyLength = 64;

    public CreateBookingRequestValidator()
    {
        RuleFor(x => x.ShowtimeId).GreaterThan(0);
        RuleFor(x => x.SeatIds).NotNull().NotEmpty().WithMessage("Indica al menos un asiento.");
        RuleFor(x => x.SeatIds).Must(ids => ids is null || ids.Count <= MaxItems).WithMessage($"No se pueden enviar más de {MaxItems} asientos.");
        RuleForEach(x => x.SeatIds).GreaterThan(0).WithMessage("Los identificadores de asiento deben ser positivos.");
    }
}
