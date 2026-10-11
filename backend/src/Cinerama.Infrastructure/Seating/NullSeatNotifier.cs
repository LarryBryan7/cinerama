using Cinerama.Application.Common;

namespace Cinerama.Infrastructure.Seating;

/// <summary>No avisa a nadie. Es el valor por defecto hasta que la fase 3 registre la implementación con SignalR.</summary>
public sealed class NullSeatNotifier : ISeatNotifier
{
    public Task NotifyAsync(int showtimeId, IReadOnlyList<SeatChange> changes, CancellationToken ct = default) => Task.CompletedTask;
}
