using Cinerama.Domain.Enums;

namespace Cinerama.Application.Common;

/// <summary>Cambio de estado de un asiento que los clientes conectados deben ver.</summary>
public sealed record SeatChange(int SeatId, ShowSeatStatus Status);

/// <summary>
/// Avisa a los clientes conectados que cambió el estado de asientos de una función. SQL Server es la fuente de
/// verdad: <b>solo se notifica después del commit</b>, y si la notificación falla la operación no se revierte
/// (los clientes se resincronizan consultando el mapa por REST). La implementación con SignalR llega en la fase 3.
/// </summary>
public interface ISeatNotifier
{
    Task NotifyAsync(int showtimeId, IReadOnlyList<SeatChange> changes, CancellationToken ct = default);
}
