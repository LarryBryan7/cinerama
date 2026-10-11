namespace Cinerama.Application.Admin;

// ── Géneros ─────────────────────────────────────────────────────────────────

public sealed record GenreRequest(string Name);

public sealed record AdminGenreDto(int Id, string Name, int MovieCount);

// ── Películas ───────────────────────────────────────────────────────────────

/// <summary>
/// Datos de una película tal como los edita el panel. Las direcciones del póster, el tráiler y el video corto se guardan como se
/// escriben (ruta relativa a la carpeta de medios o URL absoluta, RN-48); el tráiler se normaliza a la URL de YouTube (RN-51).
/// </summary>
public sealed record UpsertMovieRequest(
    string Title, string? Synopsis, int DurationMinutes, string Classification, IReadOnlyList<int> GenreIds,
    string? PosterUrl, string? TrailerUrl, string? PreviewVideoUrl, DateOnly ReleaseDate, DateOnly? PreSaleStartsOn, bool IsActive = true);

public sealed record AdminMovieDto(
    int Id, string Title, string? Synopsis, int DurationMinutes, string Classification,
    IReadOnlyList<int> GenreIds, IReadOnlyList<string> Genres,
    string? PosterUrl, string? PosterPreviewUrl, string? TrailerUrl, string? PreviewVideoUrl,
    DateOnly ReleaseDate, DateOnly? PreSaleStartsOn, string Status, bool IsActive, int UpcomingShowtimes);

public sealed record AdminMovieListItemDto(
    int Id, string Title, int DurationMinutes, string Classification, IReadOnlyList<string> Genres, string? PosterPreviewUrl,
    DateOnly ReleaseDate, DateOnly? PreSaleStartsOn, string Status, bool IsActive, int UpcomingShowtimes);

// ── Cines ───────────────────────────────────────────────────────────────────

public sealed record UpsertCinemaRequest(
    string Name, string Address, string City, string Region, decimal Latitude, decimal Longitude, string? LogoUrl, bool IsActive = true);

public sealed record AdminCinemaDto(
    int Id, string Name, string Address, string City, string Region, decimal Latitude, decimal Longitude,
    string? LogoUrl, string? LogoPreviewUrl, bool IsActive, int RoomCount, int UpcomingShowtimes);
