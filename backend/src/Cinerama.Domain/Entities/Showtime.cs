using Cinerama.Domain.Enums;

namespace Cinerama.Domain.Entities;

public class Showtime
{
    public int Id { get; set; }
    public int MovieId { get; set; }
    public Movie Movie { get; set; } = null!;
    public int CinemaRoomId { get; set; }
    public CinemaRoom CinemaRoom { get; set; } = null!;

    /// <summary>UTC (RN-38).</summary>
    public DateTime StartsAt { get; set; }

    /// <summary>Inicio + duración de la película + limpieza (RN-12).</summary>
    public DateTime EndsAt { get; set; }

    public decimal Price { get; set; }
    public ShowtimeStatus Status { get; set; } = ShowtimeStatus.Scheduled;
    public DateTime CreatedAt { get; set; }

    public List<ShowSeat> ShowSeats { get; set; } = [];
}
