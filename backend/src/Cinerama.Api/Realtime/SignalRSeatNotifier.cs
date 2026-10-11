using Cinerama.Application.Common;
using Microsoft.AspNetCore.SignalR;

namespace Cinerama.Api.Realtime;

/// <summary>
/// Entrega a los clientes suscritos los cambios de asientos. Los servicios de aplicación lo llaman <b>después del
/// commit</b> y absorben cualquier fallo: si SignalR está caído, la operación ya se confirmó y los clientes se
/// resincronizan consultando el mapa por REST.
/// </summary>
public sealed class SignalRSeatNotifier(IHubContext<SeatHub> hub) : ISeatNotifier
{
    public Task NotifyAsync(int showtimeId, IReadOnlyList<SeatChange> changes, CancellationToken ct = default)
    {
        var message = new SeatStatusChangedEvent(
            showtimeId,
            changes.Select(c => new SeatChangeEvent(c.SeatId, c.Status.ToString())).ToList());

        return hub.Clients.Group(SeatHub.GroupName(showtimeId)).SendAsync(SeatHub.EventName, message, ct);
    }
}
