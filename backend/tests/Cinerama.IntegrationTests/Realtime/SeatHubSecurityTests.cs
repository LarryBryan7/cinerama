using System.Net;
using Cinerama.Domain.Enums;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;

namespace Cinerama.IntegrationTests.Realtime;

/// <summary>Quién puede conectarse al hub, y qué pasa cuando el token deja de ser válido.</summary>
public class SeatHubSecurityTests(ApiFixture fixture) : RealtimeTest(fixture)
{
    private static async Task ShouldBeRejected(HubClient hub)
    {
        var ex = await Should.ThrowAsync<Exception>(() => hub.StartAsync());
        ex.ToString().ShouldContain("401"); // el servidor rechaza con 401, no deja la conexión a medias
    }

    // ── Autorización al conectar ────────────────────────────────────────────

    [Fact]
    [Trait("Case", "TC-RT-04")]
    public async Task ConnectingWithoutAToken_IsRejected() =>
        await ShouldBeRejected(Unconnected(null));

    [Fact]
    [Trait("Case", "TC-RT-04")]
    public async Task ConnectingWithAGarbageToken_IsRejected() =>
        await ShouldBeRejected(Unconnected("esto-no-es-un-jwt"));

    [Fact]
    [Trait("Case", "TC-RT-04")]
    public async Task ConnectingWithATamperedToken_IsRejected()
    {
        var user = await NewUserAsync();
        await ShouldBeRejected(Unconnected(TestTokens.WithTamperedSignature(user.Token)));
    }

    [Fact]
    [Trait("Case", "TC-RT-04")]
    public async Task ConnectingWithATokenSignedByAnotherKey_OrWithoutSignature_IsRejected()
    {
        await ShouldBeRejected(Unconnected(TestTokens.SignedWithAnotherKey()));
        await ShouldBeRejected(Unconnected(TestTokens.AlgNone()));
        await ShouldBeRejected(Unconnected(TestTokens.WithAudience("otro-sistema")));
    }

    [Fact]
    [Trait("Case", "TC-RT-04")]
    public async Task ConnectingWithAnExpiredToken_IsRejected_AndNegotiationExplainsWhy()
    {
        var user = await NewUserAsync();
        var expired = TestTokens.IssuedAt(DateTimeOffset.UtcNow.AddHours(-1), user.Id);

        await ShouldBeRejected(Unconnected(expired));

        // La negociación responde con el mismo ProblemDetails que el resto de la API: el cliente sabe que debe renovar el token.
        var negotiate = await Send(HttpMethod.Post, "/hubs/seats/negotiate?negotiateVersion=1", new { }, expired, client: Http);
        negotiate.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblem(negotiate)).Code.ShouldBe("token_expired");
    }

    [Fact]
    public async Task CustomersAndAdministratorsCanBothConnect()
    {
        var customer = await NewUserAsync();
        var admin = await NewUserAsync(admin: true);

        await Should.NotThrowAsync(async () => await ConnectedAsync(customer));
        await Should.NotThrowAsync(async () => await ConnectedAsync(admin));
    }

    [Fact]
    [Trait("Case", "ADR-016")]
    public async Task ATokenInTheQueryString_IsOnlyAcceptedByTheHub_NeverByTheRestApi()
    {
        var user = await NewUserAsync();
        var showtime = await NewShowtimeAsync();

        // Un token pegado en la URL de la API REST no sirve: iría a historiales, registros y capturas.
        (await Http.GetAsync($"/api/v1/me?access_token={user.Token}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Http.GetAsync($"/api/v1/showtimes/{showtime.Id}/seats?access_token={user.Token}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Http.PostAsync($"/api/v1/auth/logout?access_token={user.Token}", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // En el hub, en cambio, es el camino normal (el WebSocket lo usa): el test de conexión ya lo ejercita.
        await Should.NotThrowAsync(async () => await ConnectedAsync(user));
    }

    // ── El token vence con la conexión abierta ──────────────────────────────

    [Fact]
    [Trait("Case", "TC-RT-05")]
    public async Task WhenTheTokenExpiresWhileConnected_TheServerClosesTheConnection_AndNoMoreEventsArrive()
    {
        var showtime = await NewShowtimeAsync();
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
        var shortLived = luis with { Token = TestTokens.ExpiringIn(seconds: 3, luis.Id) };
        var watcher = await ConnectedAsync(shortLived, showtime.Id);

        // Mientras el token es válido, recibe.
        await Lock(ana, showtime, "A1");
        await watcher.NextEventAsync();

        // El token vence y el servidor cierra la conexión: sin esto seguiría escuchando con una credencial vencida.
        var closed = await Task.WhenAny(watcher.Closed, Task.Delay(TimeSpan.FromSeconds(20)));
        closed.ShouldBe(watcher.Closed, "el servidor debía cerrar la conexión al vencer el token");

        await Lock(ana, showtime, "A2");
        await watcher.AssertNoEventAsync();

        // Al reconectar con un token nuevo vuelve a funcionar (la app lo renueva y se suscribe de nuevo).
        var renewed = await ConnectedAsync(luis, showtime.Id);
        await Lock(ana, showtime, "A3");
        (await renewed.NextEventAsync()).GetProperty("changes")[0].GetProperty("seatId").GetInt32().ShouldBe(showtime.Seat("A3").SeatId);
    }

    [Fact]
    public async Task AValidTokenKeepsTheConnectionOpen()
    {
        var showtime = await NewShowtimeAsync();
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
        var watcher = await ConnectedAsync(luis, showtime.Id);

        await Task.Delay(TimeSpan.FromSeconds(4)); // más que el token corto de la prueba anterior
        await Lock(ana, showtime, "A1");

        await watcher.NextEventAsync();
        watcher.Connection.State.ShouldBe(HubConnectionState.Connected);
    }
}
