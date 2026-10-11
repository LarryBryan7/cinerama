using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Application.Common.Options;
using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cinerama.Application.Catalog;

public sealed class MovieService(IAppDbContext db, IOptions<CinemaOptions> cinema, IMediaUrls media, TimeProvider time)
{
    /// <summary>
    /// Cartelera: solo películas activas (RN-07), paginada. Cada página usa un número fijo de consultas (no hay N+1):
    /// las películas, y después un lote para promedios, opinión destacada y cines de todas las películas de la página.
    /// </summary>
    /// <param name="status"><c>nowShowing</c>, <c>presale</c> o <c>comingSoon</c> (RN-41). Sin valor, todas.</param>
    /// <param name="city">Deja las películas con funciones próximas en un cine de esa ciudad. Las que aún no tienen venta (<c>comingSoon</c>) no se filtran.</param>
    public async Task<PagedResult<MovieListItemDto>> ListAsync(
        string? search, int? genreId, string? status, string? city, int? userId, int page, int pageSize, CancellationToken ct)
    {
        Paging.Validate(page, pageSize);
        var statusFilter = ParseStatus(status);

        var now = time.GetUtcNow().UtcDateTime;
        var today = time.LocalToday(cinema.Value.ResolveTimeZone());

        var query = db.Movies.AsNoTracking().Where(m => m.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(m => m.Title.Contains(term));
        }

        if (genreId is not null)
            query = query.Where(m => m.Genres.Any(g => g.Id == genreId));

        query = statusFilter switch
        {
            MovieStatus.NowShowing => query.Where(m => m.ReleaseDate <= today),
            MovieStatus.PreSale => query.Where(m => m.ReleaseDate > today && m.PreSaleStartsOn != null && m.PreSaleStartsOn <= today),
            MovieStatus.ComingSoon => query.Where(m => m.ReleaseDate > today && (m.PreSaleStartsOn == null || m.PreSaleStartsOn > today)),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(city))
        {
            var cityName = city.Trim();
            query = query.Where(m =>
                m.Showtimes.Any(s => s.Status == ShowtimeStatus.Scheduled && s.StartsAt > now
                    && s.CinemaRoom.Cinema.IsActive && s.CinemaRoom.Cinema.City == cityName)
                || (m.ReleaseDate > today && (m.PreSaleStartsOn == null || m.PreSaleStartsOn > today)));
        }

        var total = await query.CountAsync(ct);

        // Lo que se estrena primero va primero en preventa y próximamente; la cartelera va por título.
        var ordered = statusFilter is MovieStatus.PreSale or MovieStatus.ComingSoon
            ? query.OrderBy(m => m.ReleaseDate).ThenBy(m => m.Title).ThenBy(m => m.Id)
            : query.OrderBy(m => m.Title).ThenBy(m => m.Id);

        var rows = await ordered
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new MovieRow(
                m.Id, m.Title, m.Synopsis, m.DurationMinutes, m.Classification, m.PosterUrl, m.TrailerUrl,
                m.Genres.OrderBy(g => g.Name).Select(g => g.Name).ToList(), m.ReleaseDate, m.PreSaleStartsOn, m.PreviewVideoUrl))
            .ToListAsync(ct);

        var extras = await LoadExtrasAsync(rows.Select(r => r.Id).ToList(), now, userId, ct);

        var items = rows.Select(r =>
        {
            var e = extras.For(r.Id);
            return new MovieListItemDto(
                r.Id, r.Title, r.Synopsis, r.DurationMinutes, r.Classification, media.Absolute(r.PosterUrl), media.Absolute(r.TrailerUrl),
                r.Genres, r.ReleaseDate, r.PreSaleStartsOn, Movie.StatusOf(r.ReleaseDate, r.PreSaleStartsOn, today).ToString(),
                e.AverageRating, e.ReviewCount, e.Featured, e.Cinemas, media.Absolute(r.PreviewVideoUrl));
        }).ToList();

        return new PagedResult<MovieListItemDto>(items, page, pageSize, total);
    }

    public async Task<MovieDetailDto> GetAsync(int id, int? userId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var today = time.LocalToday(cinema.Value.ResolveTimeZone());

        var movie = await db.Movies.AsNoTracking()
            .Where(m => m.Id == id && m.IsActive)
            .Select(m => new
            {
                m.Id, m.Title, m.Synopsis, m.DurationMinutes, m.Classification, m.PosterUrl, m.TrailerUrl, m.PreviewVideoUrl,
                m.ReleaseDate, m.PreSaleStartsOn,
                Genres = m.Genres.OrderBy(g => g.Name).Select(g => g.Name).ToList(),
                Showtimes = m.Showtimes
                    .Where(s => s.Status == ShowtimeStatus.Scheduled && s.StartsAt > now && s.CinemaRoom.Cinema.IsActive)
                    .OrderBy(s => s.StartsAt).ThenBy(s => s.Id)
                    .Select(s => new ShowtimeSummaryDto(
                        s.Id, s.StartsAt, s.CinemaRoom.Name, s.Price,
                        s.ShowSeats.Count(ss => ss.Status == ShowSeatStatus.Available
                            || (ss.Status == ShowSeatStatus.Locked && ss.LockedUntil < now)),
                        s.CinemaRoom.CinemaId, s.CinemaRoom.Cinema.Name))
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (movie is null)
            throw new NotFoundException("movie_not_found", "La película no existe.");

        var e = (await LoadExtrasAsync([movie.Id], now, userId, ct)).For(movie.Id);

        return new MovieDetailDto(
            movie.Id, movie.Title, movie.Synopsis, movie.DurationMinutes, movie.Classification,
            media.Absolute(movie.PosterUrl), media.Absolute(movie.TrailerUrl), movie.Genres,
            movie.ReleaseDate, movie.PreSaleStartsOn, Movie.StatusOf(movie.ReleaseDate, movie.PreSaleStartsOn, today).ToString(),
            e.AverageRating, e.ReviewCount, e.Featured, e.Cinemas, movie.Showtimes, media.Absolute(movie.PreviewVideoUrl));
    }

    private static MovieStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        if (Enum.TryParse<MovieStatus>(status.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            return parsed;

        throw new ValidationException([new ValidationFailure("status", "El estado debe ser nowShowing, presale o comingSoon.")]);
    }

    private sealed record MovieRow(
        int Id, string Title, string? Synopsis, int DurationMinutes, string Classification, string? PosterUrl, string? TrailerUrl,
        List<string> Genres, DateOnly ReleaseDate, DateOnly? PreSaleStartsOn, string? PreviewVideoUrl);

    private sealed record Extras(double? AverageRating, int ReviewCount, ReviewDto? Featured, IReadOnlyList<CinemaRefDto> Cinemas);

    private sealed class ExtrasByMovie(Dictionary<int, Extras> map)
    {
        public Extras For(int movieId) => map.GetValueOrDefault(movieId) ?? new Extras(null, 0, null, []);
    }

    /// <summary>
    /// Promedio y cantidad de opiniones, opinión destacada y cines con funciones próximas de TODAS las películas pedidas,
    /// en tres consultas (una por dato), sin importar cuántas películas sean.
    /// </summary>
    private async Task<ExtrasByMovie> LoadExtrasAsync(IReadOnlyList<int> movieIds, DateTime now, int? userId, CancellationToken ct)
    {
        if (movieIds.Count == 0)
            return new ExtrasByMovie([]);

        var stats = await db.Reviews.AsNoTracking()
            .Where(r => movieIds.Contains(r.MovieId))
            .GroupBy(r => r.MovieId)
            .Select(g => new { MovieId = g.Key, Count = g.Count(), Average = g.Average(r => (double)r.Rating) })
            .ToDictionaryAsync(x => x.MovieId, ct);

        // RN-45: la opinión destacada es la más reciente que tiene comentario (no la mejor puntuada: no es publicidad).
        var featured = await db.Reviews.AsNoTracking()
            .Where(r => movieIds.Contains(r.MovieId) && r.Comment != null
                && r.Id == db.Reviews.Where(x => x.MovieId == r.MovieId && x.Comment != null)
                    .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => x.Id).First())
            .Select(r => new { r.MovieId, r.Id, r.UserId, r.User.FullName, r.User.AvatarUrl, r.Rating, r.Comment, r.CreatedAt })
            .ToDictionaryAsync(x => x.MovieId, ct);

        var cinemaRows = await db.Showtimes.AsNoTracking()
            .Where(s => movieIds.Contains(s.MovieId) && s.Status == ShowtimeStatus.Scheduled && s.StartsAt > now
                && s.CinemaRoom.Cinema.IsActive)
            .Select(s => new
            {
                s.MovieId, s.CinemaRoom.CinemaId, s.CinemaRoom.Cinema.Name, s.CinemaRoom.Cinema.Address, s.CinemaRoom.Cinema.City,
                s.CinemaRoom.Cinema.Region, s.CinemaRoom.Cinema.Latitude, s.CinemaRoom.Cinema.Longitude, s.CinemaRoom.Cinema.LogoUrl,
            })
            .Distinct()
            .ToListAsync(ct);
        var cinemasByMovie = cinemaRows.GroupBy(c => c.MovieId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<CinemaRefDto>)g.OrderBy(c => c.Name)
                .Select(c => new CinemaRefDto(c.CinemaId, c.Name, c.Address, c.City, c.Region, c.Latitude, c.Longitude, media.Absolute(c.LogoUrl)))
                .ToList());

        var map = new Dictionary<int, Extras>();
        foreach (var id in movieIds)
        {
            stats.TryGetValue(id, out var s);
            featured.TryGetValue(id, out var f);
            cinemasByMovie.TryGetValue(id, out var cinemas);
            map[id] = new Extras(
                s is null ? null : Math.Round(s.Average, 1, MidpointRounding.AwayFromZero),
                s?.Count ?? 0,
                f is null ? null : new ReviewDto(f.Id, ReviewNames.Public(f.FullName), media.Absolute(f.AvatarUrl), f.Rating, f.Comment, f.CreatedAt, f.UserId == userId),
                cinemas ?? []);
        }

        return new ExtrasByMovie(map);
    }
}
