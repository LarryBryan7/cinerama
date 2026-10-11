namespace Cinerama.Application.Admin;

// ── Salas y asientos ────────────────────────────────────────────────────────

/// <summary>
/// La distribución de una sala: una cuadrícula de <c>Rows</c> filas (A, B, …) por <c>Positions</c> posiciones, de la que se quitan las
/// celdas de <c>EmptyCells</c> (por ejemplo <c>["A7","B7"]</c>): una posición sin asiento es un pasillo (RN-10).
/// </summary>
public sealed record RoomLayoutRequest(int Rows, int Positions, IReadOnlyList<string>? EmptyCells);

public sealed record CreateRoomRequest(int CinemaId, string Name, RoomLayoutRequest? Layout);

public sealed record UpdateRoomRequest(string Name, bool IsActive = true);

public sealed record AdminRoomDto(
    int Id, int CinemaId, string CinemaName, string Name, bool IsActive, int SeatCount, int Rows, int Positions, int UpcomingShowtimes);

public sealed record RoomSeatDto(int Id, string Row, int Number);

/// <summary>Los asientos activos de una sala y el tamaño de su cuadrícula; lo que no está en <c>Seats</c> es un pasillo.</summary>
public sealed record RoomSeatsDto(int RoomId, string RoomName, int Rows, int Positions, IReadOnlyList<RoomSeatDto> Seats, IReadOnlyList<string> EmptyCells);

// ── Funciones ───────────────────────────────────────────────────────────────

/// <summary><c>StartsAt</c> es un instante UTC (ISO 8601 con <c>Z</c>). El fin se calcula: inicio + duración + limpieza (RN-12).</summary>
public sealed record UpsertShowtimeRequest(int MovieId, int CinemaRoomId, DateTime StartsAt, decimal Price);

public sealed record AdminShowtimeDto(
    int Id, int MovieId, string MovieTitle, int CinemaId, string CinemaName, int CinemaRoomId, string RoomName,
    DateTime StartsAt, DateTime EndsAt, decimal Price, string Status, int Capacity, int SeatsSold, int ConfirmedBookings, bool HasBookings, bool Started);

// ── Reservas (vista del administrador) ──────────────────────────────────────

public sealed record AdminBookingListItemDto(
    int Id, string Code, string Status, int UserId, string CustomerName, string CustomerEmail,
    int ShowtimeId, int MovieId, string MovieTitle, string CinemaName, string RoomName, DateTime StartsAt,
    IReadOnlyList<string> Seats, decimal TotalAmount, DateTime CreatedAt, DateTime? CancelledAt);
