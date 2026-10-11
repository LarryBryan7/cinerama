using System.Collections.Concurrent;
using Cinerama.Application.Common;

namespace Cinerama.IntegrationTests.Infrastructure;

public sealed record NotifiedEvent(int ShowtimeId, IReadOnlyList<SeatChange> Changes);

/// <summary>
/// Sustituye al notificador real y anota qué avisos se enviaron. Permite comprobar que se avisa solo después del
/// commit (<see cref="OnNotify"/> se ejecuta en el momento del aviso) y que un aviso que falla no rompe la operación.
/// </summary>
public sealed class RecordingSeatNotifier : ISeatNotifier
{
    private readonly ConcurrentQueue<NotifiedEvent> _events = new();

    public IReadOnlyList<NotifiedEvent> Events => _events.ToArray();

    /// <summary>Simula una caída del servicio de avisos.</summary>
    public bool ThrowOnNotify { get; set; }

    /// <summary>Se invoca al recibir cada aviso, antes de devolver el control a la API.</summary>
    public Func<NotifiedEvent, Task>? OnNotify { get; set; }

    public void Clear()
    {
        _events.Clear();
        ThrowOnNotify = false;
        OnNotify = null;
    }

    public async Task NotifyAsync(int showtimeId, IReadOnlyList<SeatChange> changes, CancellationToken ct = default)
    {
        if (ThrowOnNotify)
            throw new InvalidOperationException("El servicio de avisos no responde.");

        var notified = new NotifiedEvent(showtimeId, changes);
        _events.Enqueue(notified);

        if (OnNotify is not null)
            await OnNotify(notified);
    }
}
