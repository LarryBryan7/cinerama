namespace Cinerama.Domain.Entities;

public class CinemaRoom
{
    public int Id { get; set; }
    public int CinemaId { get; set; }
    public Cinema Cinema { get; set; } = null!;
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    public List<Seat> Seats { get; set; } = [];
    public List<Showtime> Showtimes { get; set; } = [];
}
