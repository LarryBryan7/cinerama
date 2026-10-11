using Microsoft.Data.SqlClient;
using Cinerama.Application.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Respawn;
using Respawn.Graph;
using Testcontainers.MsSql;

namespace Cinerama.IntegrationTests.Infrastructure;

/// <summary>
/// Una base SQL Server REAL y efímera por ejecución de la suite (ADR-012). La cadena de conexión la genera
/// Testcontainers: no existe forma de que estos tests escriban en otra base. Respawn limpia los datos entre tests.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string JwtKey = "integration-tests-signing-key-with-at-least-32-chars";
    public const string TicketKey = "integration-tests-ticket-key-with-at-least-32-chars!";

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private Respawner? _respawner;

    public string ConnectionString { get; private set; } = "";
    public CineramaFactory Factory { get; private set; } = null!;

    /// <summary>Registra los avisos de cambios de asientos de la API compartida (la fase 3 los enviará por SignalR).</summary>
    public RecordingSeatNotifier Notifier { get; } = new();

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        ConnectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "CineramaTests" }.ConnectionString;

        // Arrancar el host aplica las migraciones (Database:MigrateOnStartup) sobre la base nueva.
        Factory = CreateFactory(configureServices: services =>
        {
            services.RemoveAll<ISeatNotifier>();
            services.AddSingleton<ISeatNotifier>(Notifier);
        });
        _ = Factory.Server;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            // Los roles son datos de referencia de la migración, no datos de cada test.
            TablesToIgnore = [new Table("__EFMigrationsHistory"), new Table("Roles")],
        });
    }

    public CineramaFactory CreateFactory(Dictionary<string, string?>? overrides = null, Action<IServiceCollection>? configureServices = null) =>
        new(ConnectionString, overrides, configureServices);

    public async Task ResetAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _sql.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
