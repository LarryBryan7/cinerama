using System.Net;
using Cinerama.Api.Controllers;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Auth;

public class RefreshTokenTests(ApiFixture fixture) : IntegrationTest(fixture)
{
    private Task<HttpResponseMessage> Refresh(string token) => Post("/api/v1/auth/refresh", new { refreshToken = token });

    [Fact]
    [Trait("Case", "HU-03")]
    public async Task Refresh_RotatesTheToken_AndTheOldOneStopsWorking()
    {
        var session = await RegisterAsync("ana@example.test");
        var first = session.RefreshToken!;

        var response = await Refresh(first);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = await Read<AuthResponse>(response);
        rotated.RefreshToken.ShouldNotBe(first);
        rotated.AccessToken.ShouldNotBeNullOrWhiteSpace();
        rotated.User.Email.ShouldBe("ana@example.test");
    }

    [Fact]
    [Trait("Case", "TC-SEC-07")]
    public async Task Refresh_ReusingARotatedToken_Returns401AndRevokesTheWholeSession()
    {
        var first = (await RegisterAsync("ana@example.test")).RefreshToken!;
        var second = (await Read<AuthResponse>(await Refresh(first))).RefreshToken!;

        var reuse = await Refresh(first); // alguien presenta una copia vieja

        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblem(reuse)).Code.ShouldBe("refresh_token_reused");

        // El token vigente de esa sesión también quedó revocado.
        var current = await Refresh(second);
        current.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblem(current)).Code.ShouldBe("invalid_refresh_token");
    }

    [Fact]
    public async Task Refresh_ReuseOfOneSession_DoesNotAffectAnotherSessionOfTheSameUser()
    {
        await RegisterAsync("ana@example.test");
        var phone = (await LoginAsync("ana@example.test")).RefreshToken!;
        var laptop = (await LoginAsync("ana@example.test")).RefreshToken!;

        var rotated = (await Read<AuthResponse>(await Refresh(phone))).RefreshToken!;
        (await Refresh(phone)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized); // reutilización en el teléfono

        (await Refresh(rotated)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized); // su sesión se cerró
        (await Refresh(laptop)).StatusCode.ShouldBe(HttpStatusCode.OK);            // la otra sesión sigue
    }

    [Fact]
    [Trait("Case", "TC-SEC-09")]
    public async Task Refresh_ViaCookie_RotatesTheCookieAndKeepsTheTokenOutOfTheBody()
    {
        await RegisterAsync("admin@example.test");
        var login = await Post("/api/v1/auth/login", new { email = "admin@example.test", password = DefaultPassword, useCookie = true });
        var cookie = CookieValue(SetCookieHeader(login)!);

        var response = await Post("/api/v1/auth/refresh", cookie: $"cinerama_rt={cookie}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Read<AuthResponse>(response)).RefreshToken.ShouldBeNull();
        var rotated = CookieValue(SetCookieHeader(response).ShouldNotBeNull());
        rotated.ShouldNotBe(cookie);
    }

    [Fact]
    public async Task Refresh_WithARejectedCookie_ClearsIt()
    {
        var response = await Post("/api/v1/auth/refresh", cookie: "cinerama_rt=valor-que-no-existe");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var cleared = SetCookieHeader(response).ShouldNotBeNull().ToLowerInvariant();
        cleared.ShouldStartWith("cinerama_rt=;"); // valor vacío y fecha de vencimiento en el pasado
        cleared.ShouldContain("expires=");
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401()
    {
        var response = await Refresh("token-que-nunca-se-emitio");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblem(response)).Code.ShouldBe("invalid_refresh_token");
    }

    [Fact]
    public async Task Refresh_WithoutAnyToken_Returns401()
    {
        var response = await Post("/api/v1/auth/refresh");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblem(response)).Code.ShouldBe("invalid_refresh_token");
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Returns401()
    {
        var token = (await RegisterAsync("ana@example.test")).RefreshToken!;
        await Db(db => db.RefreshTokens.ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));

        var response = await Refresh(token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblem(response)).Code.ShouldBe("invalid_refresh_token");
    }

    [Fact]
    [Trait("Case", "TC-SEC-08")]
    public async Task Refresh_DeactivatedUser_Returns401()
    {
        var token = (await RegisterAsync("ana@example.test")).RefreshToken!;
        await Db(db => db.Users.ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));

        var response = await Refresh(token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblem(response)).Code.ShouldBe("account_disabled");
    }

    [Fact]
    [Trait("Case", "ADR-007")]
    public async Task RefreshToken_IsStoredOnlyAsAHash_NeverInPlainText()
    {
        var token = (await RegisterAsync("ana@example.test")).RefreshToken!;

        var stored = await Db(db => db.RefreshTokens.SingleAsync());

        stored.TokenHash.Length.ShouldBe(32);
        stored.TokenHash.ShouldNotBe(System.Text.Encoding.UTF8.GetBytes(token));
        stored.FamilyId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    [Trait("Case", "ADR-007")]
    public async Task Refresh_TwoSimultaneousRequestsWithTheSameToken_OnlyOneSucceeds()
    {
        var token = (await RegisterAsync("ana@example.test")).RefreshToken!;

        var responses = await Task.WhenAll(Refresh(token), Refresh(token));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        var loser = responses.Single(r => r.StatusCode != HttpStatusCode.OK);
        loser.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblem(loser)).Code.ShouldBe("refresh_token_reused");

        // La reutilización revoca la familia: nunca quedan dos tokens activos de una misma sesión.
        (await Db(db => db.RefreshTokens.CountAsync(t => t.RevokedAt == null))).ShouldBe(0);
    }
}
