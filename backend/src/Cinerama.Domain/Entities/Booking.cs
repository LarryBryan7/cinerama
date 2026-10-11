using Cinerama.Domain.Enums;
using Cinerama.Domain.Exceptions;

namespace Cinerama.Domain.Entities;

public class Booking
{
    public int Id { get; set; }

    /// <summary>Código público de 8 caracteres, único y no secuencial (RN-28).</summary>
    public string Code { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int ShowtimeId { get; set; }
    public Showtime Showtime { get; set; } = null!;
    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;
    public decimal TotalAmount { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public List<BookingSeat> Seats { get; set; } = [];

    /// <summary>
    /// RN-30: el cliente cancela hasta <paramref name="customerWindow"/> antes de la función; el administrador,
    /// hasta que empieza. Marca la reserva como cancelada y libera la relación con sus asientos
    /// (<see cref="BookingSeat.ReleasedAt"/>); devolver cada asiento a <c>Available</c> es responsabilidad de quien llama.
    /// </summary>
    public void Cancel(DateTime nowUtc, DateTime showtimeStartsAtUtc, bool byAdmin, TimeSpan customerWindow)
    {
        if (Status != BookingStatus.Confirmed)
            throw new InvalidOperationException("Solo se puede cancelar una reserva confirmada.");

        if (byAdmin)
        {
            if (nowUtc >= showtimeStartsAtUtc)
                throw new DomainRuleException("showtime_already_started", "La función ya empezó: no se puede cancelar la reserva.");
        }
        else if (nowUtc > showtimeStartsAtUtc - customerWindow)
        {
            throw new DomainRuleException("cancellation_window_closed",
                $"Solo puedes cancelar hasta {customerWindow.TotalHours:0.#} horas antes de la función.");
        }

        Status = BookingStatus.Cancelled;
        CancelledAt = nowUtc;
        foreach (var seat in Seats)
            seat.ReleasedAt = nowUtc;
    }
}
