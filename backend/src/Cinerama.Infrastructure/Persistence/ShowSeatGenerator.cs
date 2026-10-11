using Microsoft.EntityFrameworkCore;

namespace Cinerama.Infrastructure.Persistence;

public static class ShowSeatGenerator
{
    /// <summary>
    /// RN-15: crea un <c>ShowSeat</c> en estado Available por cada asiento activo de la sala, en una sola sentencia
    /// (<c>INSERT ... SELECT</c>) en lugar de una inserción por asiento. Debe ejecutarse en la misma transacción
    /// que crea la función.
    /// </summary>
    public static Task<int> GenerateAsync(CineramaDbContext db, int showtimeId, int cinemaRoomId, CancellationToken ct = default) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ShowSeats (ShowtimeId, SeatId, Status)
            SELECT {showtimeId}, s.Id, 'Available'
            FROM Seats s
            WHERE s.CinemaRoomId = {cinemaRoomId} AND s.IsActive = 1
            """, ct);
}
