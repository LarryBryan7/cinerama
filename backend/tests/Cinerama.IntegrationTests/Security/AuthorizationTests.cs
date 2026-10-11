using System.Net;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cinerama.IntegrationTests.Security;

public class AuthorizationTests(ApiFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    [Trait("Case", "TC-SEC-01")]
    public async Task ProtectedEndpoint_WithoutToken_Returns401WithProblemDetails()
    {
        var response = await Get("/api/v1/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await ReadProblem(response);
        problem.Code.ShouldBe("unauthorized");
        problem.TraceId.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [Trait("Case", "TC-SEC-01")]
    [InlineData("basura-que-no-es-un-jwt")]
    public async Task ProtectedEndpoint_WithGarbageToken_Returns401(string token)
    {
        var response = await Get("/api/v1/me", token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblem(response)).Code.ShouldBe("invalid_token");
    }

    [Fact]
    [Trait("Case", "TC-SEC-01")]
    public async Task ProtectedEndpoint_WithTamperedSignature_Returns401()
    {
        var session = await RegisterAsync("ana@example.test");

        var response = await Get("/api/v1/me", TestTokens.WithTamperedSignature(session.AccessToken));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblem(response)).Code.ShouldBe("invalid_token");
    }

    [Fact]
    [Trait("Case", "TC-SEC-01")]
    public async Task ProtectedEndpoint_WithTokenSignedByAnotherKey_Returns401_EvenIfItClaimsAdmin()
    {
        var response = await Get("/api/v1/users", TestTokens.SignedWithAnotherKey(role: "Admin"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Case", "TC-SEC-01")]
    public async Task ProtectedEndpoint_WithAlgNoneToken_Returns401()
    {
        var response = await Get("/api/v1/users", TestTokens.AlgNone());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Case", "TC-SEC-01")]
    public async Task ProtectedEndpoint_WithWrongAudience_Returns401()
    {
        var response = await Get("/api/v1/users", TestTokens.WithAudience("otro-sistema"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Case", "TC-SEC-02")]
    public async Task ProtectedEndpoint_WithExpiredToken_Returns401WithTokenExpiredCode()
    {
        await RegisterAsync("ana@example.test");
        var expired = TestTokens.IssuedAt(DateTimeOffset.UtcNow.AddHours(-1), userId: 1);

        var response = await Get("/api/v1/me", expired);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblem(response)).Code.ShouldBe("token_expired"); // el cliente usa este código para intentar un refresh
    }

    [Fact]
    [Trait("Case", "TC-SEC-03")]
    public async Task AdminEndpoints_WithACustomerToken_Return403WithProblemDetails()
    {
        var customer = await RegisterAsync("cliente@example.test");

        foreach (var (method, url) in new[] { (HttpMethod.Get, "/api/v1/users"), (HttpMethod.Put, "/api/v1/users/1/status") })
        {
            var response = await Send(method, url, method == HttpMethod.Put ? new { isActive = false } : null, customer.AccessToken);

            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {url}");
            var problem = await ReadProblem(response);
            problem.Code.ShouldBe("forbidden");
            problem.TraceId.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task AdminEndpoints_WithAnAdminToken_Return200()
    {
        var admin = await CreateAdminAndLoginAsync();

        var response = await Get("/api/v1/users", admin.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        admin.User.Role.ShouldBe("Admin");
    }

    // RNF-03: seguro por defecto. Si alguien agrega un endpoint y olvida protegerlo, o marca uno como público
    // sin querer, este test falla.
    private static readonly string[] ExpectedPublicEndpoints =
    [
        "GET /api/v1/cinemas",
        "GET /api/v1/cities",
        "GET /api/v1/genres",
        "GET /api/v1/movies",
        "GET /api/v1/movies/{id:int}",
        "GET /api/v1/movies/{id:int}/trailer-comments",
        "GET /api/v1/movies/{movieId:int}/reviews",
        "GET /api/v1/showtimes",
        "GET /api/v1/showtimes/{id:int}",
        "ANY /health",
        "POST /api/v1/auth/login",
        "POST /api/v1/auth/refresh",
        "POST /api/v1/auth/register",
    ];

    private static IEnumerable<(string Method, string Route, bool Anonymous)> Endpoints(IServiceProvider services) =>
        services.GetServices<EndpointDataSource>()
            .SelectMany(ds => ds.Endpoints.OfType<RouteEndpoint>())
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"])
                .Select(m => (m, "/" + e.RoutePattern.RawText!.TrimStart('/'), e.Metadata.GetMetadata<IAllowAnonymous>() is not null)));

    [Fact]
    [Trait("Case", "TC-SEC-11")]
    public void OnlyTheExpectedEndpointsArePublic()
    {
        var anonymous = Endpoints(Fixture.Factory.Services).Where(e => e.Anonymous).Select(e => $"{e.Method} {e.Route}");

        anonymous.OrderBy(x => x).ShouldBe(ExpectedPublicEndpoints.OrderBy(x => x));
    }

    [Fact]
    [Trait("Case", "TC-SEC-11")]
    public async Task EveryNonPublicEndpoint_Returns401WithoutAToken()
    {
        var protectedEndpoints = Endpoints(Fixture.Factory.Services).Where(e => !e.Anonymous).ToList();
        protectedEndpoints.ShouldNotBeEmpty();

        foreach (var (method, route, _) in protectedEndpoints)
        {
            var url = System.Text.RegularExpressions.Regex.Replace(route, @"\{[^}]+\}", "1");
            var response = await Send(method == "ANY" ? HttpMethod.Get : new HttpMethod(method), url, method is "GET" or "ANY" ? null : new { });

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {route} debería exigir autenticación");
        }
    }
}
