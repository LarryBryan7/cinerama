using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Cinerama.Api.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace Cinerama.IntegrationTests.Infrastructure;

/// <summary>
/// Un cliente SignalR real contra la API en memoria. Usa WebSockets y el token en la URL (<c>?access_token=</c>), igual
/// que la app móvil, así que ejercita el mismo camino de autenticación que producción.
/// </summary>
public sealed class HubClient : IAsyncDisposable
{
    private readonly Channel<JsonElement> _events = Channel.CreateUnbounded<JsonElement>();
    private readonly TaskCompletionSource<Exception?> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public HubConnection Connection { get; }

    /// <summary>Se completa cuando el servidor (o la red) cierra la conexión.</summary>
    public Task<Exception?> Closed => _closed.Task;

    private HubClient(HubConnection connection)
    {
        Connection = connection;
        // Se recibe el JSON crudo: así los tests ven exactamente lo que viaja, no una clase que lo oculte.
        connection.On<JsonElement>(SeatHub.EventName, e => _events.Writer.TryWrite(e.Clone()));
        connection.Closed += error =>
        {
            _closed.TrySetResult(error);
            return Task.CompletedTask;
        };
    }

    public static HubClient Create(WebApplicationFactory<Program> factory, string? token, Func<Task<string?>>? tokenFactory = null)
    {
        var server = factory.Server;
        var provider = tokenFactory ?? (() => Task.FromResult(token));
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, SeatHub.Path), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.AccessTokenProvider = provider;

                // El cliente móvil (Dart) manda el token en la URL (?access_token=) al abrir el WebSocket, porque ese
                // transporte no puede enviar encabezados en todas las plataformas. Se reproduce exactamente eso para
                // probar el mismo camino de autenticación que usa la app.
                options.WebSocketFactory = async (context, ct) =>
                {
                    var uri = new UriBuilder(context.Uri);
                    var accessToken = await provider();
                    if (!string.IsNullOrEmpty(accessToken))
                        uri.Query = (uri.Query.TrimStart('?') + "&access_token=" + Uri.EscapeDataString(accessToken)).TrimStart('&');
                    return await server.CreateWebSocketClient().ConnectAsync(uri.Uri, ct);
                };
            })
            .Build();

        return new HubClient(connection);
    }

    public Task StartAsync() => Connection.StartAsync();

    public Task Join(int showtimeId) => Connection.InvokeAsync("JoinShowtime", showtimeId);

    public Task Leave(int showtimeId) => Connection.InvokeAsync("LeaveShowtime", showtimeId);

    public async Task<JsonElement> NextEventAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        try
        {
            return await _events.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new Shouldly.ShouldAssertException("Se esperaba un aviso de SignalR y no llegó a tiempo.");
        }
    }

    /// <summary>Verifica que durante <paramref name="quiet"/> no llegue ningún aviso.</summary>
    public async Task AssertNoEventAsync(TimeSpan? quiet = null)
    {
        using var cts = new CancellationTokenSource(quiet ?? TimeSpan.FromSeconds(1));
        try
        {
            var unexpected = await _events.Reader.ReadAsync(cts.Token);
            throw new Shouldly.ShouldAssertException($"No se esperaba ningún aviso, pero llegó: {unexpected}");
        }
        catch (OperationCanceledException)
        {
            // Silencio: es lo que se espera.
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await Connection.DisposeAsync(); } catch { /* ya cerrada */ }
    }
}
