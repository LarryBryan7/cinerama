namespace Cinerama.Application.Catalog;

public sealed record GenreDto(int Id, string Name);

/// <summary>Un cine donde la película tiene funciones próximas. La distancia no está: la calcula la app con la ubicación del dispositivo.</summary>
public sealed record CinemaRefDto(
    int Id, string Name, string Address, string City, string Region, decimal Latitude, decimal Longitude, string? LogoUrl);

/// <summary>Opinión de una persona. El nombre se abrevia ("David F.") para no exponer el nombre completo de nadie (RN-44).</summary>
public sealed record ReviewDto(
    int Id, string AuthorName, string? AuthorAvatarUrl, int Rating, string? Comment, DateTime CreatedAt, bool IsMine);

/// <summary>Datos de cada tarjeta de la cartelera. <c>Status</c>: <c>NowShowing</c>, <c>PreSale</c> o <c>ComingSoon</c> (RN-41).</summary>
public sealed record MovieListItemDto(
    int Id, string Title, string? Synopsis, int DurationMinutes, string Classification, string? PosterUrl, string? TrailerUrl,
    IReadOnlyList<string> Genres, DateOnly ReleaseDate, DateOnly? PreSaleStartsOn, string Status,
    double? AverageRating, int ReviewCount, ReviewDto? FeaturedReview, IReadOnlyList<CinemaRefDto> Cinemas, string? PreviewVideoUrl = null);

public sealed record ShowtimeSummaryDto(
    int Id, DateTime StartsAt, string RoomName, decimal Price, int AvailableSeats, int CinemaId, string CinemaName);

public sealed record MovieDetailDto(
    int Id, string Title, string? Synopsis, int DurationMinutes, string Classification, string? PosterUrl, string? TrailerUrl,
    IReadOnlyList<string> Genres, DateOnly ReleaseDate, DateOnly? PreSaleStartsOn, string Status,
    double? AverageRating, int ReviewCount, ReviewDto? FeaturedReview, IReadOnlyList<CinemaRefDto> Cinemas,
    IReadOnlyList<ShowtimeSummaryDto> UpcomingShowtimes, string? PreviewVideoUrl = null);

public sealed record ShowtimeDto(
    int Id, int MovieId, string MovieTitle, int CinemaRoomId, string RoomName,
    DateTime StartsAt, DateTime EndsAt, decimal Price, string Status, int Capacity, int AvailableSeats,
    int CinemaId, string CinemaName);

public sealed record CityDto(string City, string Region, int CinemaCount);

public sealed record CinemaDto(
    int Id, string Name, string Address, string City, string Region, decimal Latitude, decimal Longitude, string? LogoUrl);

public sealed record UpsertReviewRequest(int Rating, string? Comment);

public sealed record UpsertReviewResult(ReviewDto Review, bool Created);
