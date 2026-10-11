using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cinerama.Api.Controllers;
using Cinerama.Domain.Entities;
using Cinerama.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Cinerama.IntegrationTests.Infrastructure;

/// <summary>Forma de los errores ProblemDetails de la API, tal como la ve un cliente.</summary>
public sealed record ConflictItem(int SeatId, string Label);

public sealed record ApiProblem(
    int Status, string? Code, string? Title, string? Detail, string? TraceId,
    Dictionary<string, string[]>? Errors, List<ConflictItem>? Conflicts = null);

[Collection(ApiCollection.Name)]
public abstract class IntegrationTest(ApiFixture fixture) : IAsyncLifetime
{
    public const string DefaultPassword = "Cine2026x";

    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected ApiFixture Fixture { get; } = fixture;
    protected HttpClient Client { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        await Fixture.ResetAsync();
        Fixture.Notifier.Clear();
        Client = NewClient(Fixture.Factory);
    }

    public virtual Task DisposeAsync()
    {
        Client.Dispose();
        Fixture.Factory.ServerErrors.AssertNone(); // un 500 inesperado hace fallar el test, con su excepción
        return Task.CompletedTask;
    }

    protected static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    // ── HTTP ────────────────────────────────────────────────────────────────

    protected Task<HttpResponseMessage> Send(
        HttpMethod method, string url, object? body = null, string? bearer = null, string? cookie = null, HttpClient? client = null,
        IDictionary<string, string>? headers = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        if (headers is not null)
            foreach (var (name, value) in headers)
                request.Headers.Add(name, value);
        return (client ?? Client).SendAsync(request);
    }

    protected Task<HttpResponseMessage> Post(string url, object? body = null, string? bearer = null, string? cookie = null) =>
        Send(HttpMethod.Post, url, body ?? new { }, bearer, cookie);

    protected Task<HttpResponseMessage> Get(string url, string? bearer = null) => Send(HttpMethod.Get, url, null, bearer);

    protected static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(text, Json)
               ?? throw new InvalidOperationException($"Respuesta vacía ({(int)response.StatusCode}): {text}");
    }

    protected static Task<ApiProblem> ReadProblem(HttpResponseMessage response) => Read<ApiProblem>(response);

    // ── Cuentas ─────────────────────────────────────────────────────────────

    protected async Task<AuthResponse> RegisterAsync(string email, string password = DefaultPassword, string name = "Usuario de Prueba")
    {
        var response = await Post("/api/v1/auth/register", new { fullName = name, email, password });
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await Read<AuthResponse>(response);
    }

    protected async Task<AuthResponse> LoginAsync(string email, string password = DefaultPassword)
    {
        var response = await Post("/api/v1/auth/login", new { email, password });
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Read<AuthResponse>(response);
    }

    /// <summary>Crea un administrador directamente en la base (el registro público nunca crea uno, RN-04) y inicia sesión.</summary>
    protected async Task<AuthResponse> CreateAdminAndLoginAsync(string email = "admin@cinerama.test")
    {
        await Db(async db =>
        {
            var admin = new User { Email = email, FullName = "Admin de Prueba", RoleId = Roles.AdminId, CreatedAt = DateTime.UtcNow };
            admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, DefaultPassword);
            db.Users.Add(admin);
            await db.SaveChangesAsync();
        });
        return await LoginAsync(email);
    }

    // ── Base de datos ───────────────────────────────────────────────────────

    protected async Task<T> Db<T>(Func<CineramaDbContext, Task<T>> action, WebApplicationFactory<Program>? factory = null)
    {
        await using var scope = (factory ?? Fixture.Factory).Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<CineramaDbContext>());
    }

    protected Task Db(Func<CineramaDbContext, Task> action) => Db<int>(async db => { await action(db); return 0; });

    // ── Cookies ─────────────────────────────────────────────────────────────

    protected static string? SetCookieHeader(HttpResponseMessage response, string name = "cinerama_rt") =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(name + "=", StringComparison.Ordinal))
            : null;

    /// <summary>Valor de una cabecera Set-Cookie (lo que está entre "nombre=" y el primer ";").</summary>
    protected static string CookieValue(string setCookie) =>
        setCookie[(setCookie.IndexOf('=') + 1)..].Split(';')[0];
}
