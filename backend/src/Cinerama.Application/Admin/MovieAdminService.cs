using Cinerama.Application.Catalog;
using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Application.Common.Options;
using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cinerama.Application.Admin;

/// <summary>
/// Catálogo de películas para el administrador (RF-CAT-03). Eliminar es una baja lógica (RN-08): se rechaza si hay funciones futuras con
/// reservas confirmadas, y si procede se cancelan las funciones futuras que quedaban sin ventas. Una película dada de baja no sale
/// en la cartelera ni admite funciones nuevas (RN-07), y se puede reactivar.
/// </summary>
public sealed class MovieAdminService(
    IAppDbContext db, IValidator<UpsertMovieRequest> validator, IMediaUrls media, ShowtimeCanceller canceller,
    IOptions<CinemaOptions> cinema, TimeProvider time)
{
    public async Task<PagedResult<AdminMovieListItemDto>> ListAsync(string? search, bool? isActive, string? status, int page, int pageSize, CancellationToken ct)
    {
        Paging.Validate(page, pageSize);
        var now = time.UtcNowMs();
        var today = time.LocalToday(cinema.Value.ResolveTimeZone());

        var query = db.Movies.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(m => m.Title.Contains(term));
        }

        if (isActive is not null)
            query = query.Where(m => m.IsActive == isActive);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<MovieStatus>(status.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                throw new ValidationException([new ValidationFailure("status", "El estado debe ser nowShowing, presale o comingSoon.")]);

            query = parsed switch
            {
                MovieStatus.NowShowing => query.Where(m => m.ReleaseDate <= today),
                MovieStatus.PreSale => query.Where(m => m.ReleaseDate > today && m.PreSaleStartsOn != null && m.PreSaleStartsOn <= today),
                _ => query.Where(m => m.ReleaseDate > today && (m.PreSaleStartsOn == null || m.PreSaleStartsOn > today)),
            };
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(m => m.Title).ThenBy(m => m.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new
            {
                m.Id, m.Title, m.DurationMinutes, m.Classification, m.PosterUrl, m.ReleaseDate, m.PreSaleStartsOn, m.IsActive,
                Genres = m.Genres.OrderBy(g => g.Name).Select(g => g.Name).ToList(),
                Upcoming = m.Showtimes.Count(s => s.Status == ShowtimeStatus.Scheduled && s.StartsAt > now),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new AdminMovieListItemDto(
            r.Id, r.Title, r.DurationMinutes, r.Classification, r.Genres, media.Absolute(r.PosterUrl),
            r.ReleaseDate, r.PreSaleStartsOn, Movie.StatusOf(r.ReleaseDate, r.PreSaleStartsOn, today).ToString(), r.IsActive, r.Upcoming)).ToList();

        return new PagedResult<AdminMovieListItemDto>(items, page, pageSize, total);
    }

    public async Task<AdminMovieDto> GetAsync(int id, CancellationToken ct)
    {
        var now = time.UtcNowMs();
        var today = time.LocalToday(cinema.Value.ResolveTimeZone());
        var row = await db.Movies.AsNoTracking().Where(m => m.Id == id)
            .Select(m => new
            {
                m.Id, m.Title, m.Synopsis, m.DurationMinutes, m.Classification, m.PosterUrl, m.TrailerUrl, m.PreviewVideoUrl,
                m.ReleaseDate, m.PreSaleStartsOn, m.IsActive,
                Genres = m.Genres.OrderBy(g => g.Name).Select(g => new { g.Id, g.Name }).ToList(),
                Upcoming = m.Showtimes.Count(s => s.Status == ShowtimeStatus.Scheduled && s.StartsAt > now),
            })
            .FirstOrDefaultAsync(ct) ?? throw NotFound();

        return new AdminMovieDto(
            row.Id, row.Title, row.Synopsis, row.DurationMinutes, row.Classification,
            row.Genres.Select(g => g.Id).ToList(), row.Genres.Select(g => g.Name).ToList(),
            row.PosterUrl, media.Absolute(row.PosterUrl), row.TrailerUrl, row.PreviewVideoUrl,
            row.ReleaseDate, row.PreSaleStartsOn, Movie.StatusOf(row.ReleaseDate, row.PreSaleStartsOn, today).ToString(), row.IsActive, row.Upcoming);
    }

    public async Task<AdminMovieDto> CreateAsync(UpsertMovieRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var genres = await LoadGenresAsync(request.GenreIds, ct);
        var now = time.UtcNowMs();

        var movie = new Movie { CreatedAt = now };
        Apply(movie, request, genres, now);
        db.Movies.Add(movie);
        await db.SaveChangesAsync(ct);
        return await GetAsync(movie.Id, ct);
    }

    public async Task<AdminMovieDto> UpdateAsync(int id, UpsertMovieRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var movie = await db.Movies.Include(m => m.Genres).FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw NotFound();
        var genres = await LoadGenresAsync(request.GenreIds, ct);

        // Desactivarla por la edición pasa por las mismas reglas que darla de baja (RN-08).
        if (movie.IsActive && !request.IsActive)
            await DeactivateAsync(movie, ct);

        Apply(movie, request, genres, time.UtcNowMs());
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Baja lógica (RN-08). Si ya estaba de baja no hace nada.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw NotFound();
        if (!movie.IsActive)
            return;

        await DeactivateAsync(movie, ct);
        movie.UpdatedAt = time.UtcNowMs();
        await db.SaveChangesAsync(ct);
    }

    private async Task DeactivateAsync(Movie movie, CancellationToken ct)
    {
        if (await canceller.HasConfirmedBookingsAsync(s => s.MovieId == movie.Id, ct))
            throw new ConflictException("movie_has_active_bookings", "La película tiene funciones futuras con reservas confirmadas: cancélalas antes de darla de baja.");

        // Sin reservas, las funciones futuras ya no tienen sentido: se cancelan y se liberan sus bloqueos.
        await canceller.CancelAsync(await canceller.FutureIdsAsync(s => s.MovieId == movie.Id, ct), ct);
        movie.IsActive = false;
    }

    private async Task<List<Genre>> LoadGenresAsync(IReadOnlyList<int> ids, CancellationToken ct)
    {
        var genres = await db.Genres.Where(g => ids.Contains(g.Id)).ToListAsync(ct);
        if (genres.Count != ids.Count)
            throw new ValidationException([new ValidationFailure("genreIds", "Algún género no existe.")]);

        return genres;
    }

    private static void Apply(Movie movie, UpsertMovieRequest r, List<Genre> genres, DateTime now)
    {
        movie.Title = r.Title.Trim();
        movie.Synopsis = MediaValues.Clean(r.Synopsis);
        movie.DurationMinutes = r.DurationMinutes;
        movie.Classification = r.Classification.Trim();
        movie.PosterUrl = MediaValues.Clean(r.PosterUrl);
        movie.TrailerUrl = YouTubeVideoId.TryParse(r.TrailerUrl) is { } code ? $"https://www.youtube.com/watch?v={code}" : null;
        movie.PreviewVideoUrl = MediaValues.Clean(r.PreviewVideoUrl);
        movie.ReleaseDate = r.ReleaseDate;
        movie.PreSaleStartsOn = r.PreSaleStartsOn;
        movie.IsActive = r.IsActive;
        movie.UpdatedAt = now;

        movie.Genres.Clear();
        movie.Genres.AddRange(genres);
    }

    private static NotFoundException NotFound() => new("movie_not_found", "La película no existe.");
}
