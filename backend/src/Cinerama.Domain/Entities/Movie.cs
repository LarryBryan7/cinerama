using Cinerama.Domain.Enums;

namespace Cinerama.Domain.Entities;

public class Movie
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Synopsis { get; set; }
    public int DurationMinutes { get; set; }
    public string Classification { get; set; } = null!;

    /// <summary>
    /// Imagen del póster. Una ruta relativa a la carpeta de medios de la API (por ejemplo <c>posters/marea-alta.jpg</c>) o
    /// una URL absoluta. La API siempre la entrega como URL absoluta.
    /// </summary>
    public string? PosterUrl { get; set; }

    /// <summary>Tráiler (URL absoluta). Opcional: el botón de reproducir solo actúa si existe.</summary>
    public string? TrailerUrl { get; set; }

    /// <summary>
    /// Video corto de la película (sin sonido, en bucle) para el detalle. Una ruta relativa a la carpeta de medios (por ejemplo
    /// <c>posters/vertigo-2.mp4</c>) o una URL absoluta. Opcional: sin video, el detalle no muestra el recuadro.
    /// </summary>
    public string? PreviewVideoUrl { get; set; }

    /// <summary>Fecha de estreno en el calendario local del cine (RN-38). Por defecto, una película ya estrenada.</summary>
    public DateOnly ReleaseDate { get; set; }

    /// <summary>Desde cuándo se pueden reservar asientos antes del estreno (preventa). <c>null</c>: no hay preventa.</summary>
    public DateOnly? PreSaleStartsOn { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<Genre> Genres { get; set; } = [];
    public List<Showtime> Showtimes { get; set; } = [];
    public List<Review> Reviews { get; set; } = [];

    /// <summary>RN-41: estado de la película en la fecha local <paramref name="today"/>.</summary>
    public MovieStatus StatusOn(DateOnly today) => StatusOf(ReleaseDate, PreSaleStartsOn, today);

    /// <summary>RN-42: se pueden reservar asientos desde que abre la preventa, o desde el estreno si no hay preventa.</summary>
    public bool IsSalesOpenOn(DateOnly today) => IsSalesOpen(ReleaseDate, PreSaleStartsOn, today);

    public static MovieStatus StatusOf(DateOnly releaseDate, DateOnly? preSaleStartsOn, DateOnly today)
    {
        if (today >= releaseDate)
            return MovieStatus.NowShowing;

        return preSaleStartsOn is { } start && today >= start ? MovieStatus.PreSale : MovieStatus.ComingSoon;
    }

    public static bool IsSalesOpen(DateOnly releaseDate, DateOnly? preSaleStartsOn, DateOnly today) =>
        today >= (preSaleStartsOn ?? releaseDate);
}
