namespace Cinerama.Domain.Entities;

/// <summary>Un cine (sede) con sus salas. Antes de la fase 3.5 el proyecto tenía un único cine implícito.</summary>
public class Cinema
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;
    public string City { get; set; } = null!;
    public string Region { get; set; } = null!;

    /// <summary>Coordenadas del cine. El cálculo de distancia a la persona lo hace la app en el dispositivo: la ubicación de nadie viaja al servidor.</summary>
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }

    /// <summary>Logo (ruta relativa a la carpeta de medios o URL absoluta). Opcional: sin logo la app muestra la inicial.</summary>
    public string? LogoUrl { get; set; }

    public bool IsActive { get; set; } = true;

    public List<CinemaRoom> Rooms { get; set; } = [];
}
