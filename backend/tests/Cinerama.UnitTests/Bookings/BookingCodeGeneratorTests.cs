using Cinerama.Application.Bookings;

namespace Cinerama.UnitTests.Bookings;

public class BookingCodeGeneratorTests
{
    [Fact]
    [Trait("Case", "RN-28")]
    public void Code_Has8CharactersFromTheUnambiguousAlphabet()
    {
        for (var i = 0; i < 500; i++)
        {
            var code = BookingCodeGenerator.Generate();
            code.Length.ShouldBe(8);
            code.ShouldAllBe(c => BookingCodeGenerator.Alphabet.Contains(c));
        }
    }

    [Theory]
    [Trait("Case", "RN-28")]
    [InlineData('0')]
    [InlineData('O')]
    [InlineData('1')]
    [InlineData('I')]
    public void Alphabet_ExcludesAmbiguousCharacters(char ambiguous) =>
        BookingCodeGenerator.Alphabet.ShouldNotContain(ambiguous);

    [Fact]
    public void Codes_AreNotSequential_AndDoNotRepeatInPractice()
    {
        var codes = Enumerable.Range(0, 20_000).Select(_ => BookingCodeGenerator.Generate()).ToList();

        codes.Distinct().Count().ShouldBe(codes.Count);
        codes.Zip(codes.Skip(1), (a, b) => string.CompareOrdinal(a, b)).Distinct().Count().ShouldBeGreaterThan(1); // sin orden creciente
    }
}
