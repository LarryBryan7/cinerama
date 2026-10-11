using Cinerama.Application.Common;
using Cinerama.Domain.Enums;
using Cinerama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cinerama.Infrastructure.Seating;

/// <summary>Un asiento que el procedimiento liberó.</summary>
public sealed record ReleasedSeatRow(int ShowtimeId, long ShowSeatId, int SeatId);

/// <summary>
/// Una pasada de limpieza: libera los bloqueos vencidos con <c>sp_ReleaseExpiredSeatLocks</c> y avisa un evento por
/// función afectada. La corrección del sistema no depende de esto (RN-23): un bloqueo vencido ya se trata como
/// disponible al consultar y al bloquear. Esto solo hace que los clientes conectados lo vean sin recargar.
/// </summary>
public sealed class SeatLockCleanup(CineramaDbContext db, ISeatNotifier notifier, TimeProvider time, ILogger<SeatLockCleanup> logger)
{
    public const int BatchSize = 500;

    /// <returns>Cantidad de asientos liberados.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var released = new List<ReleasedSeatRow>();

        while (true)
        {
            var now = time.UtcNowMs();
            var batch = await db.Database
                .SqlQuery<ReleasedSeatRow>($"EXEC dbo.sp_ReleaseExpiredSeatLocks @NowUtc = {now}, @BatchSize = {BatchSize}")
                .ToListAsync(ct);

            released.AddRange(batch);
            if (batch.Count < BatchSize)
                break; // el lote no vino lleno: no quedan más vencidos
        }

        // Las filas ya están confirmadas (cada UPDATE es su propia transacción): recién ahora se avisa.
        foreach (var showtime in released.GroupBy(r => r.ShowtimeId))
        {
            var changes = showtime.Select(r => new SeatChange(r.SeatId, ShowSeatStatus.Available)).ToList();
            try
            {
                await notifier.NotifyAsync(showtime.Key, changes, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "No se pudo notificar la liberación de asientos de la función {ShowtimeId}.", showtime.Key);
            }
        }

        return released.Count;
    }
}
