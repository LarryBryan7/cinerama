using FluentValidation;

namespace Cinerama.Application.Seating;

public sealed class SeatSelectionRequestValidator : AbstractValidator<SeatSelectionRequest>
{
    /// <summary>Tope de seguridad del tamaño del request. El límite de negocio (RN-20) es menor y se aplica como regla (422).</summary>
    public const int MaxItems = 100;

    public SeatSelectionRequestValidator()
    {
        RuleFor(x => x.SeatIds).NotNull().NotEmpty().WithMessage("Indica al menos un asiento.");
        RuleFor(x => x.SeatIds).Must(ids => ids is null || ids.Count <= MaxItems).WithMessage($"No se pueden enviar más de {MaxItems} asientos.");
        RuleForEach(x => x.SeatIds).GreaterThan(0).WithMessage("Los identificadores de asiento deben ser positivos.");
    }
}
