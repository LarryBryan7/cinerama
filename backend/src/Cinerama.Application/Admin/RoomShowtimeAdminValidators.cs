using System.Text.RegularExpressions;
using FluentValidation;

namespace Cinerama.Application.Admin;

public sealed partial class RoomLayoutRequestValidator : AbstractValidator<RoomLayoutRequest>
{
    public const int MaxRows = 26;
    public const int MaxPositions = 40;

    [GeneratedRegex(@"^[A-Z](0?[1-9]|[1-3][0-9]|40)$")]
    private static partial Regex Cell();

    public RoomLayoutRequestValidator()
    {
        // RN-10: de 1 a 26 filas (A–Z) y de 1 a 40 posiciones por fila.
        RuleFor(x => x.Rows).InclusiveBetween(1, MaxRows);
        RuleFor(x => x.Positions).InclusiveBetween(1, MaxPositions);
        RuleForEach(x => x.EmptyCells).Must(c => c is not null && Cell().IsMatch(c)).WithMessage("'{PropertyValue}' no es una celda válida (por ejemplo A7).");
        RuleFor(x => x).Custom((x, ctx) =>
        {
            if (x.Rows is < 1 or > MaxRows || x.Positions is < 1 or > MaxPositions)
                return;

            var empty = (x.EmptyCells ?? []).Where(c => c is not null && Cell().IsMatch(c)).Select(c => (c[0], int.Parse(c[1..]))).ToList();
            if (empty.Any(c => c.Item1 - 'A' >= x.Rows || c.Item2 > x.Positions))
                ctx.AddFailure(nameof(RoomLayoutRequest.EmptyCells), "Hay celdas fuera de la cuadrícula.");

            if (empty.Distinct().Count() != empty.Count)
                ctx.AddFailure(nameof(RoomLayoutRequest.EmptyCells), "Hay celdas repetidas.");

            if (empty.Distinct().Count(c => c.Item1 - 'A' < x.Rows && c.Item2 <= x.Positions) >= x.Rows * x.Positions)
                ctx.AddFailure(nameof(RoomLayoutRequest.EmptyCells), "La sala necesita al menos un asiento.");
        });
    }
}

public sealed class CreateRoomRequestValidator : AbstractValidator<CreateRoomRequest>
{
    public CreateRoomRequestValidator()
    {
        RuleFor(x => x.CinemaId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Layout).NotNull().WithMessage("La sala necesita su distribución de asientos.");
        RuleFor(x => x.Layout!).SetValidator(new RoomLayoutRequestValidator()).When(x => x.Layout is not null);
    }
}

public sealed class UpdateRoomRequestValidator : AbstractValidator<UpdateRoomRequest>
{
    public UpdateRoomRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
    }
}

public sealed class UpsertShowtimeRequestValidator : AbstractValidator<UpsertShowtimeRequest>
{
    public UpsertShowtimeRequestValidator()
    {
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.CinemaRoomId).GreaterThan(0);
        // La hora debe llegar como instante UTC (con "Z"). Una hora sin zona es ambigua: el administrador cree que es la de Lima y el servidor la leería como UTC.
        RuleFor(x => x.StartsAt).Must(d => d != default && d.Kind != DateTimeKind.Unspecified)
            .WithMessage("La hora de inicio debe ser un instante UTC (por ejemplo 2026-10-10T19:00:00Z).");
        // RN-14: precio mayor o igual a 0, con dos decimales (decimal(10,2)).
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).LessThan(100_000_000m);
        RuleFor(x => x.Price).Must(p => p == decimal.Round(p, 2)).WithMessage("El precio admite hasta dos decimales.");
    }
}
