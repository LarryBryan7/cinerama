using Cinerama.Application.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Cinerama.IntegrationTests.Infrastructure;

/// <summary>
/// Una instancia de la API con el reloj bajo control del test: sirve para vencer bloqueos y llegar a la ventana de
/// cancelación sin esperar. El reloj arranca en la hora real, así que los datos creados con <c>DateTime.UtcNow</c> son coherentes.
/// El token de acceso se emite con este mismo reloj, que también valida su vigencia.
/// </summary>
public sealed class TimedApi : IAsyncDisposable
{
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);
    public RecordingSeatNotifier Notifier { get; } = new();
    public CineramaFactory Factory { get; }
    public HttpClient Client { get; }

    public DateTime Now => Time.GetUtcNow().UtcDateTime;

    public TimedApi(ApiFixture fixture, Dictionary<string, string?>? overrides = null, Action<IServiceCollection>? extraServices = null)
    {
        Factory = fixture.CreateFactory(overrides, services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.RemoveAll<ISeatNotifier>();
            services.AddSingleton<ISeatNotifier>(Notifier);
            extraServices?.Invoke(services);
        });
        Client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false, AllowAutoRedirect = false,
        });
    }

    /// <summary>Access token emitido con la hora actual de este reloj.</summary>
    public string TokenFor(int userId, string role = "Customer") => TestTokens.IssuedAt(Time.GetUtcNow(), userId, role);

    /// <summary>El mismo usuario con un token emitido en el instante actual del reloj (útil después de adelantarlo).</summary>
    public TestUser As(TestUser user, string role = "Customer") => user with { Token = TokenFor(user.Id, role) };

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        var errors = Factory.ServerErrors;
        await Factory.DisposeAsync();
        errors.AssertNone();
    }
}
