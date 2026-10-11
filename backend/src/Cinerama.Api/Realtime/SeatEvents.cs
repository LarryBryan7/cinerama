namespace Cinerama.Api.Realtime;

/// <summary>
/// Un cambio de estado de un asiento. <b>No lleva ningún dato de personas</b>: todos los clientes de la función reciben
/// el mismo mensaje, así que enviar quién tiene el asiento filtraría información de otros usuarios. Cada cliente ya
/// conoce sus propios bloqueos por la respuesta REST de <c>/seats/lock</c>.
/// </summary>
public sealed record SeatChangeEvent(int SeatId, string Status);

/// <summary>Mensaje <c>SeatStatusChanged</c>. Es un aviso para actualizar la pantalla; la fuente de verdad sigue siendo REST.</summary>
public sealed record SeatStatusChangedEvent(int ShowtimeId, IReadOnlyList<SeatChangeEvent> Changes);
