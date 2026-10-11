namespace Cinerama.Domain.Entities;

/// <summary>
/// Posición física de una sala. <see cref="Number"/> es la posición en la fila: un número que falta
/// es un pasillo, y eso permite detectar cortes de contigüidad al recomendar asientos.
/// </summary>
public class Seat
{
    public int Id { get; set; }
    public int CinemaRoomId { get; set; }
    public CinemaRoom CinemaRoom { get; set; } = null!;
    public char RowLabel { get; set; }
    public byte Number { get; set; }
    public bool IsActive { get; set; } = true;

    public string Label => $"{RowLabel}{Number}";
}
