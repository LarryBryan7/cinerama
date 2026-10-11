using Cinerama.Application.Common.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cinerama.Infrastructure.Seating;

/// <summary>
/// Ejecuta <see cref="SeatLockCleanup"/> cada <see cref="SeatingOptions.CleanupSeconds"/> (30 s por defecto).
/// </summary>
/// <remarks>
/// Un <c>BackgroundService</c> es singleton y el <c>DbContext</c> es <i>scoped</i>: por eso cada pasada crea su propio
/// scope con <see cref="IServiceScopeFactory"/>. Un fallo en una pasada se registra y no detiene el proceso. Con varias
/// instancias de la API todas lo ejecutan: no hace falta coordinarlas porque el procedimiento es atómico e idempotente.
/// </remarks>
public sealed class SeatLockCleanupService(
    IServiceScopeFactory scopes,
    IOptions<SeatingOptions> options,
    TimeProvider time,
    ILogger<SeatLockCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.CleanupEnabled)
        {
            logger.LogInformation("La limpieza periódica de bloqueos está deshabilitada (Seating:CleanupEnabled=false).");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.CleanupSeconds), time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var released = await scope.ServiceProvider.GetRequiredService<SeatLockCleanup>().RunOnceAsync(stoppingToken);
                    if (released > 0)
                        logger.LogInformation("Se liberaron {Count} asientos con el bloqueo vencido.", released);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Falló la limpieza de bloqueos vencidos; se reintenta en la próxima pasada.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado ordenado de la API.
        }
    }
}
