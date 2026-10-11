using Cinerama.Application.Auth;

namespace Cinerama.UnitTests.Auth;

public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    private static RegisterRequest Valid(string? name = null, string? email = null, string? password = null) =>
        new(name ?? "Ana Pérez", email ?? "ana@example.test", password ?? "Cine2026");

    [Fact]
    public void ValidRequest_Passes() =>
        _validator.Validate(Valid()).IsValid.ShouldBeTrue();

    // RN-02: al menos 8 caracteres, con una letra y un número.
    [Theory]
    [InlineData("Ab1")]                // demasiado corta
    [InlineData("12345678")]           // sin letras
    [InlineData("abcdefgh")]           // sin números
    [InlineData("")]
    public void Password_BreakingRn02_Fails(string password) =>
        _validator.Validate(Valid(password: password)).Errors.ShouldContain(e => e.PropertyName == "Password");

    [Fact]
    public void Password_TooLong_Fails() =>
        _validator.Validate(Valid(password: new string('a', 128) + "1")).IsValid.ShouldBeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("sin-arroba")]
    [InlineData("a@")]
    public void Email_Invalid_Fails(string email) =>
        _validator.Validate(Valid(email: email)).Errors.ShouldContain(e => e.PropertyName == "Email");

    [Fact]
    public void Name_Empty_Fails() =>
        _validator.Validate(Valid(name: "")).Errors.ShouldContain(e => e.PropertyName == "FullName");

    [Fact]
    public void Name_Over100Chars_Fails() =>
        _validator.Validate(Valid(name: new string('x', 101))).Errors.ShouldContain(e => e.PropertyName == "FullName");
}
