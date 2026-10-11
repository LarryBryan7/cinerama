using Cinerama.Application.Seating;

namespace Cinerama.UnitTests.Seating;

public class SeatRecommendationServiceTests
{
    private readonly SeatRecommendationService _service = new();

    /// <summary>Sala de <paramref name="rows"/> filas (A, B, …) con asientos 1..<paramref name="perRow"/>; <paramref name="aisles"/> son posiciones sin asiento y <paramref name="taken"/> asientos ocupados ("F5").</summary>
    private static List<SeatSlot> Room(int rows, int perRow, int[]? aisles = null, params string[] taken)
    {
        var slots = new List<SeatSlot>();
        var id = 1;
        for (var r = 0; r < rows; r++)
            for (var n = 1; n <= perRow; n++)
            {
                if (aisles?.Contains(n) == true) continue;
                var row = (char)('A' + r);
                slots.Add(new SeatSlot(id++, row, n, IsAvailable: !taken.Contains($"{row}{n}")));
            }
        return slots;
    }

    private static string[] Labels(SeatSuggestion s) => s.Seats.Select(x => x.Label).ToArray();

    [Fact]
    public void EmptyRoom_ReturnsNoSuggestions() =>
        _service.Recommend([], 2).ShouldBeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void CountBelowOne_IsAProgrammingError(int count) =>
        Should.Throw<ArgumentOutOfRangeException>(() => _service.Recommend(Room(2, 5), count));

    [Fact]
    public void FreeRoom_SuggestsAContiguousPairInTheCentreOfTheIdealRow()
    {
        // 10 filas: la fila ideal (2/3 de la profundidad) es la G. En una fila de 11, el centro es el asiento 6.
        var best = _service.Recommend(Room(10, 11), 2)[0];

        Labels(best).ShouldBe(["G5", "G6"]);   // el empate entre G5-G6 y G6-G7 lo gana el número menor
        best.Score.ShouldBe(94.00m);           // 100 × (1 − 0,6 × 0,1)
    }

    [Fact]
    public void SingleSeat_IsTheExactCentreOfTheIdealRow_WithAPerfectScore()
    {
        var best = _service.Recommend(Room(10, 11), 1)[0];

        Labels(best).ShouldBe(["G6"]);
        best.Score.ShouldBe(100m);
    }

    [Fact]
    public void SuggestedSeatsAreAlwaysContiguousAndInTheSameRow()
    {
        foreach (var suggestion in _service.Recommend(Room(8, 12, aisles: [6]), 3))
        {
            suggestion.Seats.Select(s => s.Row).Distinct().Count().ShouldBe(1);
            suggestion.Seats.Select(s => (int)s.Number).Order().ToArray()
                .Zip(suggestion.Seats.Select(s => (int)s.Number).Order().Skip(1), (a, b) => b - a)
                .ShouldAllBe(gap => gap == 1);
        }
    }

    [Fact]
    public void AnAisleBreaksContiguity_SoAPairCannotStraddleIt()
    {
        // Fila A de 1..5 con pasillo en la posición 3: solo existen A1-A2 y A4-A5.
        var room = Room(1, 5, aisles: [3]);

        var result = _service.Recommend(room, 2);

        result.Select(Labels).ShouldAllBe(l => l.SequenceEqual(new[] { "A1", "A2" }) || l.SequenceEqual(new[] { "A4", "A5" }));
        result.Count.ShouldBe(2);
        _service.Recommend(room, 3).ShouldBeEmpty(); // ningún tramo contiguo alcanza para 3
    }

    [Fact]
    public void ATakenSeat_BreaksContiguityToo()
    {
        var room = Room(1, 5, taken: "A3");

        _service.Recommend(room, 3).ShouldBeEmpty();
        _service.Recommend(room, 2).Select(Labels).ShouldAllBe(l => !l.Contains("A3"));
    }

    [Fact]
    public void WhenNoGroupFits_ReturnsAnEmptyListNotAnError()
    {
        var room = Room(3, 6, taken: ["A3", "A6", "B3", "B6", "C3", "C6"]);

        _service.Recommend(room, 4).ShouldBeEmpty();
    }

    [Fact]
    public void CountLargerThanEveryRow_ReturnsNothing() =>
        _service.Recommend(Room(5, 6), 7).ShouldBeEmpty();

    [Fact]
    public void PrefersTheGroupNearestTheCentreOfTheRow()
    {
        // Una sola fila de 11: dejan libres 1-2 (borde) y 5-6 (centro).
        var room = Room(1, 11, taken: ["A3", "A4", "A7", "A8", "A9", "A10", "A11"]);

        var result = _service.Recommend(room, 2);

        Labels(result[0]).ShouldBe(["A5", "A6"]);
        Labels(result[1]).ShouldBe(["A1", "A2"]);
        result[0].Score.ShouldBeGreaterThan(result[1].Score);
    }

    [Fact]
    public void PrefersTheIdealRowOverOneNearTheScreen()
    {
        // Solo la fila A (pegada a la pantalla) y la fila G (la ideal de 10) tienen lugar.
        var taken = Room(10, 5).Where(s => s.Row is not ('A' or 'G')).Select(s => s.Label).ToArray();

        var result = _service.Recommend(Room(10, 5, taken: taken), 2);

        Labels(result[0]).ShouldBe(["G2", "G3"]);
        result.Select(r => r.Seats[0].Row).ShouldContain('A');
    }

    [Fact]
    public void ReturnsAtMostThreeSuggestions_WithNoSeatInCommon()
    {
        var result = _service.Recommend(Room(10, 12, aisles: [6]), 2);

        result.Count.ShouldBe(SeatRecommendationService.MaxSuggestions);
        result.SelectMany(r => r.Seats).Select(s => s.SeatId).Distinct().Count().ShouldBe(6); // 3 opciones × 2 asientos, todos distintos
    }

    [Fact]
    public void SuggestionsAreOrderedFromBestToWorst()
    {
        var scores = _service.Recommend(Room(10, 12), 3).Select(r => r.Score).ToList();

        scores.ShouldBe(scores.OrderByDescending(s => s).ToList());
    }

    [Fact]
    public void UnavailableSeatsAreNeverSuggested()
    {
        var room = Room(4, 8, taken: ["C4", "C5", "D4", "D5"]);

        var seats = _service.Recommend(room, 2).SelectMany(r => r.Seats);

        seats.ShouldAllBe(s => s.IsAvailable);
    }

    [Fact]
    public void TheResultDoesNotDependOnTheOrderOfTheInput()
    {
        var room = Room(8, 10, aisles: [5], taken: ["D3", "D4", "E6"]);
        var shuffled = room.OrderBy(s => (s.SeatId * 7919) % 101).ToList();

        var a = _service.Recommend(room, 2).Select(Labels).ToList();
        var b = _service.Recommend(shuffled, 2).Select(Labels).ToList();

        b.ShouldBe(a);
        _service.Recommend(room, 2).Select(Labels).ToList().ShouldBe(a); // y es repetible
    }
}
