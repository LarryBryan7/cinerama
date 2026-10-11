using System.ComponentModel.DataAnnotations;
using Cinerama.Application.Common.Options;

namespace Cinerama.UnitTests.Auth;

public class JwtOptionsTests
{
    private static IList<ValidationResult> Validate(JwtOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void SigningKey_Missing_IsRejected() =>
        Validate(new JwtOptions()).ShouldNotBeEmpty();

    [Fact]
    public void SigningKey_Shorter_Than32Chars_IsRejected() =>
        Validate(new JwtOptions { SigningKey = new string('k', 31) }).ShouldNotBeEmpty();

    [Fact]
    public void Defaults_Match_Rn05() =>
        Validate(new JwtOptions { SigningKey = new string('k', 32) }).ShouldBeEmpty();

    [Fact]
    public void DefaultLifetimes_AreTheDocumentedBusinessValues()
    {
        var options = new JwtOptions();
        options.AccessTokenMinutes.ShouldBe(15);
        options.RefreshTokenDays.ShouldBe(7);
    }
}
