namespace Cinerama.Application.Bookings;

public sealed record CreateBookingRequest(int ShowtimeId, IReadOnlyList<int> SeatIds);

public sealed record BookingShowtimeDto(int Id, int MovieId, string MovieTitle, string RoomName, DateTime StartsAt);

public sealed record BookingSeatDto(int SeatId, string Label, decimal Price);

/// <param name="TicketPayload">Contenido firmado del QR (RN-35): <c>{código}.{firma}</c>. La app lo guarda para mostrarlo sin conexión.</param>
public sealed record BookingDto(
    int Id,
    string Code,
    string Status,
    BookingShowtimeDto Showtime,
    IReadOnlyList<BookingSeatDto> Seats,
    decimal TotalAmount,
    string TicketPayload,
    DateTime CreatedAt,
    DateTime? CancelledAt);

public sealed record BookingListItemDto(
    int Id,
    string Code,
    string Status,
    string MovieTitle,
    string RoomName,
    DateTime StartsAt,
    IReadOnlyList<string> SeatLabels,
    decimal TotalAmount,
    DateTime CreatedAt);

/// <param name="Created"><c>false</c> cuando es la reserva original devuelta por una repetición con la misma clave de idempotencia (RN-27).</param>
public sealed record BookingResult(BookingDto Booking, bool Created);
