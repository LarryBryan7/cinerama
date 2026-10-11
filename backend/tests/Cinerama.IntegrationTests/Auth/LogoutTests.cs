using System.Net;
using Cinerama.IntegrationTests.Infrastructure;

namespace Cinerama.IntegrationTests.Auth;

public class LogoutTests(ApiFixture fixture) : IntegrationTest(fixture)
{
    private Task<HttpResponseMessage> Refresh(string token) => Post("/api/v1/auth/refresh", new { refreshToken = token });

    [Fact]
    [Trait("Case", "HU-04")]
    public async Task Logout_RevokesTheSessionRefreshToken()
    {
        var session = await RegisterAsync("ana@example.test");

        var logout = await Post("/api/v1/auth/logout", new { refreshToken = session.RefreshToken }, bearer: session.AccessToken);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var refresh = await Refresh(session.RefreshToken!);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblem(refresh)).Code.ShouldBe("invalid_refresh_token"); // cerrar sesión no es "reutilización"
    }

    [Fact]
    public async Task Logout_WithAToken_DoesNotCloseTheOtherSessionsOfTheUser()
    {
        await RegisterAsync("ana@example.test");
        var phone = await LoginAsync("ana@example.test");
        var laptop = await LoginAsync("ana@example.test");

        await Post("/api/v1/auth/logout", new { refreshToken = phone.RefreshToken }, bearer: phone.AccessToken);

        (await Refresh(phone.RefreshToken!)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Refresh(laptop.RefreshToken!)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_WithoutAToken_RevokesAllTheUsersSessions()
    {
        await RegisterAsync("ana@example.test");
        var phone = await LoginAsync("ana@example.test");
        var laptop = await LoginAsync("ana@example.test");

        var logout = await Post("/api/v1/auth/logout", bearer: phone.AccessToken);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Refresh(phone.RefreshToken!)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Refresh(laptop.RefreshToken!)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ViaCookie_RevokesTheTokenAndClearsTheCookie()
    {
        await RegisterAsync("admin@example.test");
        var login = await Post("/api/v1/auth/login", new { email = "admin@example.test", password = DefaultPassword, useCookie = true });
        var access = (await Read<Cinerama.Api.Controllers.AuthResponse>(login)).AccessToken;
        var cookie = CookieValue(SetCookieHeader(login)!);

        var logout = await Post("/api/v1/auth/logout", bearer: access, cookie: $"cinerama_rt={cookie}");

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        SetCookieHeader(logout).ShouldNotBeNull().ToLowerInvariant().ShouldStartWith("cinerama_rt=;");
        (await Post("/api/v1/auth/refresh", cookie: $"cinerama_rt={cookie}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_RequiresAuthentication()
    {
        var response = await Post("/api/v1/auth/logout");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Case", "IDOR")]
    public async Task Logout_PresentingAnotherUsersRefreshToken_DoesNotRevokeIt()
    {
        var victim = await RegisterAsync("victima@example.test");
        var attacker = await RegisterAsync("atacante@example.test");

        var logout = await Post("/api/v1/auth/logout", new { refreshToken = victim.RefreshToken }, bearer: attacker.AccessToken);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent); // no se revela si el token existe
        (await Refresh(victim.RefreshToken!)).StatusCode.ShouldBe(HttpStatusCode.OK); // la víctima sigue con su sesión
    }
}
