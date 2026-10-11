using System.Net;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Options;

namespace Cinerama.IntegrationTests.Platform;

public class Phase2PlatformTests(ApiFixture fixture) : ReservationTest(fixture)
{
    // ── RN-40: límite por usuario al bloquear y liberar ─────────────────────

    [Fact]
    [Trait("Case", "RN-40")]
    public async Task SeatEndpoints_AreRateLimitedPerUser_NotPerIp()
    {
        await using var factory = Fixture.CreateFactory(new() { ["RateLimiting:SeatsPermitPerMinute"] = "3" });
        using var client = NewClient(factory);
        var showtime = await NewShowtimeAsync(rows: 2, perRow: 5);
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());

        // Ana agota su cupo mezclando bloquear y liberar: ambos cuentan en la misma política.
        (await LockSeats(ana, showtime, ["A1"], client)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReleaseSeats(ana, showtime, ["A1"], client)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LockSeats(ana, showtime, ["A2"], client)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var limited = await LockSeats(ana, showtime, ["A3"], client);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.Contains("Retry-After").ShouldBeTrue();
        (await ReadProblem(limited)).Code.ShouldBe("rate_limited");

        // Luis comparte la misma IP, pero tiene su propio cupo.
        (await LockSeats(luis, showtime, ["B1"], client)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Y consultar el mapa no cuenta contra el límite.
        (await MapOf(ana, showtime, client)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ── La configuración inválida impide arrancar ───────────────────────────

    [Fact]
    public void Startup_WithAShortTicketKey_Fails()
    {
        using var factory = Fixture.CreateFactory(new() { ["Ticket:SigningKey"] = "corta" });

        Should.Throw<OptionsValidationException>(() => factory.CreateClient()).Message.ShouldContain("SigningKey");
    }

    [Theory]
    [InlineData("Seating:LockMinutes", "0")]
    [InlineData("Seating:MaxSeatsPerUser", "0")]
    [InlineData("Seating:CleanupSeconds", "0")]
    [InlineData("RateLimiting:SeatsPermitPerMinute", "0")]
    [InlineData("Cinema:CancellationHours", "-1")]
    public void Startup_WithOutOfRangeBusinessSettings_Fails(string key, string value)
    {
        using var factory = Fixture.CreateFactory(new() { [key] = value });

        Should.Throw<OptionsValidationException>(() => factory.CreateClient());
    }

    // ── Los valores inseguros de desarrollo no llegan a un entorno real (ADR-014) ──

    [Fact]
    [Trait("Case", "ADR-014")]
    public void NonDevelopment_ConnectedAsSa_WithoutExplicitPermission_DoesNotStart()
    {
        // La base del contenedor de pruebas usa 'sa'. El entorno de la factory es "Testing", no Development.
        using var factory = Fixture.CreateFactory(new() { ["Local:AllowInsecureSettings"] = "false" });

        var ex = Should.Throw<InvalidOperationException>(() => factory.CreateClient());

        ex.Message.ShouldContain("'sa'");
        ex.Message.ShouldContain("Local:AllowInsecureSettings");
    }

    [Fact]
    [Trait("Case", "ADR-014")]
    public void NonDevelopment_WithAnInsecureSessionCookie_WithoutExplicitPermission_DoesNotStart()
    {
        using var factory = Fixture.CreateFactory(new()
        {
            ["Local:AllowInsecureSettings"] = "false",
            ["WebSession:RefreshCookieSecure"] = "false",
        });

        Should.Throw<InvalidOperationException>(() => factory.CreateClient()).Message.ShouldContain("RefreshCookieSecure");
    }

    [Fact]
    [Trait("Case", "ADR-014")]
    public async Task TheSessionCookie_IsSecureByDefault()
    {
        var response = await Post("/api/v1/auth/login", new { email = "nadie@example.test", password = "Incorrecta1", useCookie = true });
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await RegisterAsync("web@example.test");
        var ok = await Post("/api/v1/auth/login", new { email = "web@example.test", password = DefaultPassword, useCookie = true });

        SetCookieHeader(ok).ShouldNotBeNull().ToLowerInvariant().ShouldContain("secure");
    }
}
