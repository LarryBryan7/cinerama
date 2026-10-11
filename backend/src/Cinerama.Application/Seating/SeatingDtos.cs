namespace Cinerama.Application.Seating;

/// <param name="Status">Available, Locked o Booked. Un bloqueo vencido se informa como Available (RN-23).</param>
/// <param name="IsMine">Bloqueado, y vigente, a nombre del usuario que consulta. La app lo muestra como "Seleccionado".</param>
/// <param name="LockedUntil">Solo se informa para los bloqueos propios: no se exponen datos de otras personas.</param>
public sealed record SeatStateDto(int SeatId, string Row, int Number, string Label, string Status, bool IsMine, DateTime? LockedUntil);

public sealed record SeatMapDto(int ShowtimeId, string ShowtimeStatus, DateTime StartsAt, decimal Price, IReadOnlyList<SeatStateDto> Seats);

public sealed record SeatSelectionRequest(IReadOnlyList<int> SeatIds);

/// <param name="LockedUntil">El vencimiento más próximo entre los asientos pedidos (volver a bloquear un asiento propio no extiende su plazo).</param>
public sealed record LockResultDto(int ShowtimeId, DateTime LockedUntil, IReadOnlyList<SeatStateDto> Seats);

public sealed record RecommendedSeatDto(int SeatId, string Label);

public sealed record SeatRecommendationDto(decimal Score, IReadOnlyList<RecommendedSeatDto> Seats);
