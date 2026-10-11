namespace Cinerama.Domain.Entities;

/// <summary>
/// Una fila de la vista <c>vw_ShowtimeSales</c>: capacidad, entradas vendidas, ingresos y ocupación de una función. Es de solo lectura
/// (no tiene clave ni se guarda): la definición de "vendida", "ingreso" y "ocupación" vive en la vista (RN-36, RN-37), en un solo lugar.
/// </summary>
public class ShowtimeSales
{
    public int ShowtimeId { get; set; }
    public int MovieId { get; set; }
    public string MovieTitle { get; set; } = null!;
    public int CinemaId { get; set; }
    public string CinemaName { get; set; } = null!;
    public int CinemaRoomId { get; set; }
    public string RoomName { get; set; } = null!;
    public DateTime StartsAt { get; set; }
    public string Status { get; set; } = null!;
    public int Capacity { get; set; }
    public int SeatsSold { get; set; }
    public decimal Revenue { get; set; }
    public int Bookings { get; set; }

    /// <summary>Entre 0 y 1.</summary>
    public decimal Occupancy { get; set; }
}
