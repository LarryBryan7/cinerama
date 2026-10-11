using Cinerama.Domain.Entities;

namespace Cinerama.UnitTests.Domain;

public class DomainTests
{
    // RN-01: el email se normaliza antes de guardar y antes de comparar.
    [Theory]
    [InlineData("ana@example.test", "ana@example.test")]
    [InlineData("  ANA@Example.TEST ", "ana@example.test")]
    [InlineData("Ana.Perez@Example.test", "ana.perez@example.test")]
    public void NormalizeEmail_TrimsAndLowercases_RN01(string input, string expected) =>
        User.NormalizeEmail(input).ShouldBe(expected);

    [Fact]
    public void RefreshToken_IsActive_OnlyWhenNotRevokedAndNotExpired()
    {
        var now = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

        new RefreshToken { ExpiresAt = now.AddMinutes(1) }.IsActive(now).ShouldBeTrue();
        new RefreshToken { ExpiresAt = now }.IsActive(now).ShouldBeFalse();
        new RefreshToken { ExpiresAt = now.AddDays(1), RevokedAt = now.AddMinutes(-1) }.IsActive(now).ShouldBeFalse();
    }

    [Fact]
    public void Seat_Label_CombinesRowAndNumber() =>
        new Seat { RowLabel = 'F', Number = 7 }.Label.ShouldBe("F7");
}
