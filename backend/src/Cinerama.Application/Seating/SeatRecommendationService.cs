namespace Cinerama.Application.Seating;

/// <summary>Un asiento físico de la sala, con su disponibilidad en una función concreta.</summary>
public sealed record SeatSlot(int SeatId, char Row, int Number, bool IsAvailable)
{
    public string Label => $"{Row}{Number}";
}

public sealed record SeatSuggestion(decimal Score, IReadOnlyList<SeatSlot> Seats);

/// <summary>
/// Recomienda grupos de asientos contiguos y céntricos. Es lógica de negocio pura (sin base de datos ni reloj),
/// así que se prueba con tests unitarios; por eso no es un stored procedure (ADR-009).
/// </summary>
/// <remarks>
/// Algoritmo: agrupa por fila; detecta tramos contiguos de asientos disponibles (un pasillo o un asiento ocupado
/// cortan el tramo); recorre las ventanas de <c>count</c> asientos de cada tramo; puntúa cada ventana por su
/// cercanía al centro de la fila (60 %) y a la fila ideal, a 2/3 de la profundidad de la sala (40 %); y devuelve
/// las mejores opciones que no compartan asientos entre sí, para que sean alternativas reales y no la misma
/// pareja desplazada un asiento.
/// </remarks>
public sealed class SeatRecommendationService
{
    public const int MaxSuggestions = 3;
    private const double HorizontalWeight = 0.6;
    private const double VerticalWeight = 0.4;
    private const double IdealDepth = 2.0 / 3.0;

    public IReadOnlyList<SeatSuggestion> Recommend(IEnumerable<SeatSlot> room, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var rows = room.GroupBy(s => s.Row).OrderBy(g => g.Key).Select(g => g.OrderBy(s => s.Number).ToList()).ToList();
        if (rows.Count == 0)
            return [];

        var idealRow = (rows.Count - 1) * IdealDepth;
        var candidates = new List<Candidate>();

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            double center = (row[0].Number + row[^1].Number) / 2.0;
            double halfWidth = (row[^1].Number - row[0].Number) / 2.0;

            foreach (var run in ContiguousAvailableRuns(row).Where(r => r.Count >= count))
            {
                for (var start = 0; start + count <= run.Count; start++)
                {
                    var window = run.GetRange(start, count);
                    var horizontal = halfWidth == 0 ? 0 : Math.Min(1, Math.Abs(window.Average(s => s.Number) - center) / halfWidth);
                    var vertical = rows.Count == 1 ? 0 : Math.Abs(rowIndex - idealRow) / (rows.Count - 1);
                    var score = 100 * (1 - (HorizontalWeight * horizontal + VerticalWeight * vertical));

                    candidates.Add(new Candidate((decimal)Math.Round(score, 2), horizontal, rowIndex, window));
                }
            }
        }

        // Desempate determinista: mejor puntaje, más centrada, fila más cercana a la pantalla, número menor.
        var ordered = candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Horizontal)
            .ThenBy(c => c.RowIndex)
            .ThenBy(c => c.Seats[0].Number);

        var chosen = new List<Candidate>();
        foreach (var candidate in ordered)
        {
            if (chosen.Any(c => c.Seats.Select(s => s.SeatId).Intersect(candidate.Seats.Select(s => s.SeatId)).Any()))
                continue;

            chosen.Add(candidate);
            if (chosen.Count == MaxSuggestions)
                break;
        }

        return chosen.Select(c => new SeatSuggestion(c.Score, c.Seats)).ToList();
    }

    /// <summary>Tramos de asientos disponibles y consecutivos (<c>Number</c> +1). Un hueco en la numeración es un pasillo.</summary>
    private static IEnumerable<List<SeatSlot>> ContiguousAvailableRuns(List<SeatSlot> row)
    {
        var run = new List<SeatSlot>();
        foreach (var seat in row)
        {
            var continues = run.Count > 0 && run[^1].Number + 1 == seat.Number;

            if (!seat.IsAvailable || (run.Count > 0 && !continues))
            {
                if (run.Count > 0) yield return run;
                run = [];
            }

            if (seat.IsAvailable)
                run.Add(seat);
        }

        if (run.Count > 0)
            yield return run;
    }

    private sealed record Candidate(decimal Score, double Horizontal, int RowIndex, List<SeatSlot> Seats);
}
