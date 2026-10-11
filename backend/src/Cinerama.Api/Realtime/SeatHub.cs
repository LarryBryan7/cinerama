using Cinerama.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.Api.Realtime;

/// <summary>
/// Hub de solo escucha. El cliente se suscribe a las funciones que está mirando y el servidor le avisa los cambios
/// (<see cref="EventName"/>). <b>Todas las escrituras pasan por REST</b>, que valida, persiste y recién después avisa:
/// el hub no tiene ningún método que bloquee, libere o reserve, así que no se puede saltar las reglas de negocio.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Requiere autenticación (<c>[Authorize]</c>). El token viaja en el encabezado o, en WebSockets, en <c>?access_token=</c>.</item>
/// <item>Cada función es un grupo (<see cref="GroupName"/>): quien mira la función 1 no recibe nada de la función 2.</item>
/// <item>SignalR no reenvía lo emitido mientras el cliente estaba desconectado, y al reconectar se pierden los grupos:
/// el cliente debe volver a suscribirse y pedir el mapa completo por REST (ADR-016).</item>
/// </list>
/// </remarks>
[Authorize]
public sealed class SeatHub(IAppDbContext db) : Hub
{
    public const string Path = "/hubs/seats";
    public const string EventName = "SeatStatusChanged";

    /// <summary>Tope de funciones suscritas por conexión: evita que una sola conexión acumule grupos sin límite.</summary>
    public const int MaxShowtimesPerConnection = 5;

    private const string JoinedKey = "joined-showtimes";

    public static string GroupName(int showtimeId) => $"showtime-{showtimeId}";

    public async Task JoinShowtime(int showtimeId)
    {
        var joined = Joined();
        if (joined.Contains(showtimeId))
            return; // suscribirse dos veces no es un error

        if (joined.Count >= MaxShowtimesPerConnection)
            throw new HubException("too_many_subscriptions");

        if (!await db.Showtimes.AnyAsync(s => s.Id == showtimeId, Context.ConnectionAborted))
            throw new HubException("showtime_not_found");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(showtimeId), Context.ConnectionAborted);
        joined.Add(showtimeId);
    }

    public async Task LeaveShowtime(int showtimeId)
    {
        if (Joined().Remove(showtimeId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(showtimeId), Context.ConnectionAborted);
    }

    // Un hub atiende de a una llamada por conexión (MaximumParallelInvocationsPerClient = 1), así que no hace falta sincronizar.
    private HashSet<int> Joined() =>
        (HashSet<int>)(Context.Items[JoinedKey] ??= new HashSet<int>());
}
