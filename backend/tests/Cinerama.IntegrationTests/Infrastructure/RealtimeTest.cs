using Cinerama.Api.Realtime;
using Cinerama.Application.Common;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cinerama.IntegrationTests.Infrastructure;

/// <summary>
/// Pruebas de tiempo real: cada test levanta su propia API con el notificador REAL (SignalR), a diferencia de la API
/// compartida, que usa un notificador de pruebas.
/// </summary>
public abstract class RealtimeTest(ApiFixture fixture) : ReservationTest(fixture)
{
    protected CineramaFactory Api { get; private set; } = null!;
    protected HttpClient Http { get; private set; } = null!;
    private readonly List<HubClient> _hubs = [];

    protected virtual Action<IServiceCollection>? ConfigureApi => null;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Api = Fixture.CreateFactory(configureServices: ConfigureApi);
        Http = NewClient(Api);
    }

    public override async Task DisposeAsync()
    {
        foreach (var hub in _hubs)
            await hub.DisposeAsync();

        Http.Dispose();
        var errors = Api.ServerErrors;
        await Api.DisposeAsync();
        errors.AssertNone();
        await base.DisposeAsync();
    }

    protected async Task<HubClient> ConnectedAsync(TestUser user, params int[] showtimes)
    {
        var hub = HubClient.Create(Api, user.Token);
        _hubs.Add(hub);
        await hub.StartAsync();
        foreach (var showtime in showtimes)
            await hub.Join(showtime);
        return hub;
    }

    protected HubClient Unconnected(string? token)
    {
        var hub = HubClient.Create(Api, token);
        _hubs.Add(hub);
        return hub;
    }

    protected Task<HttpResponseMessage> Lock(TestUser u, TestShowtime s, params string[] labels) => LockSeats(u, s, labels, Http);
}

/// <summary>Un SignalR "caído": cualquier intento de enviar falla.</summary>
internal sealed class BrokenHubContext : IHubContext<SeatHub>
{
    public IHubClients Clients { get; } = new BrokenClients();
    public IGroupManager Groups => throw new InvalidOperationException("SignalR no está disponible.");

    private sealed class BrokenClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new BrokenProxy();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class BrokenProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SignalR no está disponible.");
    }
}
