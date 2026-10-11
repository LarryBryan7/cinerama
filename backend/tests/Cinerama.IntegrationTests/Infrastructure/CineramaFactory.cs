using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cinerama.IntegrationTests.Infrastructure;

public sealed class CineramaFactory(
    string connectionString,
    Dictionary<string, string?>? overrides = null,
    Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    public ServerErrorLog ServerErrors { get; } = new();
    public LogCapture Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["Jwt:SigningKey"] = ApiFixture.JwtKey,
                ["Ticket:SigningKey"] = ApiFixture.TicketKey,
                // La base del contenedor de pruebas es local y se conecta con 'sa': permitido a propósito (ADR-014).
                ["Local:AllowInsecureSettings"] = "true",
                ["Database:MigrateOnStartup"] = "true",
                ["Seed:Enabled"] = "false",
                ["Cors:AllowedOrigins"] = "",
                // Los tests hacen cientos de registros y logins desde una misma "IP": el límite real se prueba aparte.
                ["RateLimiting:AuthPermitPerMinute"] = "10000",
                ["RateLimiting:SeatsPermitPerMinute"] = "10000",
                // El proceso de limpieza queda apagado por defecto: la corrección no depende de él (RN-23) y los
                // tests que lo ejercitan lo activan de forma explícita.
                ["Seating:CleanupEnabled"] = "false",
            };

            if (overrides is not null)
                foreach (var (key, value) in overrides)
                    settings[key] = value;

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(ServerErrors);
            logging.AddProvider(Logs);
        });

        if (configureServices is not null)
            builder.ConfigureServices(configureServices);
    }
}
