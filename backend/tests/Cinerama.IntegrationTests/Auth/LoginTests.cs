using System.Net;
using Cinerama.Api.Controllers;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Auth;

public class LoginTests(ApiFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    [Trait("Case", "HU-02")]
    public async Task Login_ValidCredentials_ReturnsAnAccessTokenValidFor15MinutesAndARefreshToken()
    {
        await RegisterAsync("ana@example.test");

        var body = await LoginAsync("ana@example.test");

        body.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        var minutes = (body.AccessTokenExpiresAt - DateTime.UtcNow).TotalMinutes;
        minutes.ShouldBeInRange(14, 15.1); // RN-05
    }

    [Fact]
    public async Task Login_EmailIsCaseInsensitive()
    {
        await RegisterAsync("ana@example.test");

        var body = await LoginAsync(" ANA@Example.TEST ");

        body.User.Email.ShouldBe("ana@example.test");
    }

    [Fact]
    [Trait("Case", "HU-02")]
    public async Task Login_UnknownEmailAndWrongPassword_ReturnTheSameError()
    {
        await RegisterAsync("ana@example.test");

        var wrongPassword = await Post("/api/v1/auth/login", new { email = "ana@example.test", password = "Incorrecta1" });
        var unknownEmail = await Post("/api/v1/auth/login", new { email = "nadie@example.test", password = "Incorrecta1" });

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var a = await ReadProblem(wrongPassword);
        var b = await ReadProblem(unknownEmail);
        a.Code.ShouldBe("invalid_credentials");
        (a.Code, a.Title, a.Detail).ShouldBe((b.Code, b.Title, b.Detail)); // no se revela qué emails existen
    }

    [Fact]
    [Trait("Case", "TC-SEC-08")]
    public async Task Login_InactiveUser_Returns401()
    {
        await RegisterAsync("ana@example.test");
        await Db(async db =>
        {
            (await db.Users.SingleAsync()).IsActive = false;
            await db.SaveChangesAsync();
        });

        var response = await Post("/api/v1/auth/login", new { email = "ana@example.test", password = DefaultPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblem(response)).Code.ShouldBe("invalid_credentials");
    }

    [Fact]
    [Trait("Case", "TC-SEC-09")]
    public async Task Login_WithUseCookie_SendsTheRefreshTokenOnlyInAnHttpOnlyCookie()
    {
        await RegisterAsync("admin@example.test");

        var response = await Post("/api/v1/auth/login", new { email = "admin@example.test", password = DefaultPassword, useCookie = true });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await Read<AuthResponse>(response);
        body.RefreshToken.ShouldBeNull();
        body.RefreshTokenExpiresAt.ShouldBeNull();
        body.AccessToken.ShouldNotBeNullOrWhiteSpace();

        var cookie = SetCookieHeader(response).ShouldNotBeNull().ToLowerInvariant();
        cookie.ShouldContain("httponly");
        cookie.ShouldContain("secure");
        cookie.ShouldContain("samesite=strict");
        cookie.ShouldContain("path=/api/v1/auth"); // el navegador no la envía al resto de la API
    }

    [Fact]
    public async Task Login_WithoutUseCookie_ReturnsTheRefreshTokenInTheBodyAndNoCookie()
    {
        await RegisterAsync("ana@example.test");

        var response = await Post("/api/v1/auth/login", new { email = "ana@example.test", password = DefaultPassword });

        (await Read<AuthResponse>(response)).RefreshToken.ShouldNotBeNullOrWhiteSpace();
        SetCookieHeader(response).ShouldBeNull();
    }

    [Fact]
    public async Task Login_EmptyBody_Returns400()
    {
        var response = await Post("/api/v1/auth/login", new { email = "", password = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Errors.ShouldNotBeNull().Keys.ShouldBe(["email", "password"], ignoreOrder: true);
    }
}
