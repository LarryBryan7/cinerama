using System.Net;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Options;

namespace Cinerama.IntegrationTests.Platform;

public class PlatformTests(ApiFixture fixture) : IntegrationTest(fixture)
{
    private const string AllowedOrigin = "https://admin.cinerama.example";

    // ── Salud y formato de errores ──────────────────────────────────────────

    [Fact]
    public async Task Health_IsPublicAndReportsSqlServer()
    {
        var response = await Get("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task UnknownRoute_WhenAuthenticated_Returns404AsProblemDetailsWithATraceId()
    {
        var session = await RegisterAsync("ana@example.test");

        var response = await Get("/api/v1/no-existe", session.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await ReadProblem(response);
        problem.Code.ShouldBe("not_found");
        problem.TraceId.ShouldNotBeNullOrWhiteSpace();
    }

    // La política por defecto (RNF-03) también cubre las rutas que no existen: un anónimo no puede averiguar
    // qué rutas hay más allá de las públicas.
    [Fact]
    public async Task UnknownRoute_WithoutAToken_Returns401_SoRoutesCannotBeEnumerated()
    {
        var response = await Get("/api/v1/no-existe");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblem(response)).Code.ShouldBe("unauthorized");
    }

    // ── CORS con credenciales (ADR-006, TC-SEC-10) ──────────────────────────

    private static HttpRequestMessage Preflight(string origin) => new(HttpMethod.Options, "/api/v1/auth/refresh")
    {
        Headers =
        {
            { "Origin", origin },
            { "Access-Control-Request-Method", "POST" },
            { "Access-Control-Request-Headers", "content-type" },
        },
    };

    [Fact]
    [Trait("Case", "TC-SEC-10")]
    public async Task Cors_AllowsOnlyTheConfiguredOrigin_WithCredentials()
    {
        await using var factory = Fixture.CreateFactory(new() { ["Cors:AllowedOrigins"] = AllowedOrigin });
        using var client = NewClient(factory);

        var allowed = await client.SendAsync(Preflight(AllowedOrigin));
        allowed.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe(AllowedOrigin);
        allowed.Headers.GetValues("Access-Control-Allow-Credentials").Single().ShouldBe("true");

        var evil = await client.SendAsync(Preflight("https://evil.example"));
        evil.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        evil.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
    }

    [Fact]
    [Trait("Case", "TC-SEC-10")]
    public async Task Cors_WithNoConfiguredOrigins_AllowsNoOrigin()
    {
        var response = await Client.SendAsync(Preflight(AllowedOrigin));

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    // ── Rate limiting en autenticación (RN-06) ──────────────────────────────

    [Fact]
    [Trait("Case", "RN-06")]
    public async Task AuthEndpoints_AreRateLimitedPerIp()
    {
        await using var factory = Fixture.CreateFactory(new() { ["RateLimiting:AuthPermitPerMinute"] = "3" });
        using var client = NewClient(factory);

        for (var i = 0; i < 3; i++)
        {
            var allowed = await Send(HttpMethod.Post, "/api/v1/auth/login", new { email = "nadie@example.test", password = "Incorrecta1" }, client: client);
            allowed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized); // pasa el límite y falla por credenciales
        }

        var limited = await Send(HttpMethod.Post, "/api/v1/auth/login", new { email = "nadie@example.test", password = "Incorrecta1" }, client: client);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.Contains("Retry-After").ShouldBeTrue();
        (await ReadProblem(limited)).Code.ShouldBe("rate_limited");

        // El límite aplica a autenticación: el catálogo no se ve afectado.
        (await Send(HttpMethod.Get, "/api/v1/genres", client: client)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ── La configuración inválida impide arrancar ───────────────────────────

    [Fact]
    public void Startup_WithAWeakSigningKey_Fails()
    {
        using var factory = Fixture.CreateFactory(new() { ["Jwt:SigningKey"] = "corta" });

        var ex = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        ex.Message.ShouldContain("SigningKey");
    }

    [Fact]
    public void Startup_WithAWildcardCorsOrigin_Fails()
    {
        using var factory = Fixture.CreateFactory(new() { ["Cors:AllowedOrigins"] = "*" });

        Should.Throw<OptionsValidationException>(() => factory.CreateClient()).Message.ShouldContain("'*'");
    }

    [Fact]
    public void Startup_WithAnInvalidTimeZone_Fails()
    {
        using var factory = Fixture.CreateFactory(new() { ["Cinema:TimeZone"] = "Mars/Olympus_Mons" });

        Should.Throw<OptionsValidationException>(() => factory.CreateClient()).Message.ShouldContain("zona horaria");
    }
}
