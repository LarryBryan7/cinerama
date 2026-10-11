namespace Cinerama.Domain.Entities;

/// <summary>
/// Asiento de una reserva. El precio se copia al confirmar (RN-25). <see cref="ReleasedAt"/> se informa al
/// cancelar la reserva; el índice único filtrado sobre <c>ShowSeatId WHERE ReleasedAt IS NULL</c> garantiza
/// en la base que un asiento solo pertenece a una reserva activa (RN-31).
/// </summary>
public class BookingSeat
{
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public long ShowSeatId { get; set; }
    public ShowSeat ShowSeat { get; set; } = null!;
    public decimal Price { get; set; }
    public DateTime? ReleasedAt { get; set; }
}
