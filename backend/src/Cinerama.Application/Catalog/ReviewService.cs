using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Application.Common.Options;
using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using Cinerama.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cinerama.Application.Catalog;

/// <summary>
/// Opiniones de las películas. Cualquier persona con cuenta puede opinar (una opinión por película, RN-43) una vez
/// que la película se estrenó (RN-46). La identidad sale siempre del token: no existe forma de opinar "como" otra persona.
/// </summary>
public sealed class ReviewService(
    IAppDbContext db, IValidator<UpsertReviewRequest> validator, IMediaUrls media, IOptions<CinemaOptions> cinema, TimeProvider time)
{
    public async Task<PagedResult<ReviewDto>> ListAsync(int movieId, int? userId, int page, int pageSize, CancellationToken ct)
    {
        Paging.Validate(page, pageSize);
        await EnsureMovieExistsAsync(movieId, ct);

        var query = db.Reviews.AsNoTracking().Where(r => r.MovieId == movieId);
        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r.Id, r.UserId, r.User.FullName, r.User.AvatarUrl, r.Rating, r.Comment, r.CreatedAt })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new ReviewDto(r.Id, ReviewNames.Public(r.FullName), media.Absolute(r.AvatarUrl), r.Rating, r.Comment, r.CreatedAt, r.UserId == userId))
            .ToList();

        return new PagedResult<ReviewDto>(items, page, pageSize, total);
    }

    /// <summary>Crea la opinión de la persona o reemplaza la que ya tenía (idempotente: repetir la misma solicitud deja el mismo resultado).</summary>
    public async Task<UpsertReviewResult> UpsertMineAsync(int movieId, int userId, UpsertReviewRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var movie = await db.Movies.AsNoTracking().Where(m => m.Id == movieId && m.IsActive)
            .Select(m => new { m.ReleaseDate, m.PreSaleStartsOn })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("movie_not_found", "La película no existe.");

        var today = time.LocalToday(cinema.Value.ResolveTimeZone());
        if (Movie.StatusOf(movie.ReleaseDate, movie.PreSaleStartsOn, today) != MovieStatus.NowShowing)
            throw new DomainRuleException("movie_not_released", "Solo se puede opinar sobre una película que ya se estrenó.");

        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        var now = time.UtcNowMs();

        var review = await db.Reviews.FirstOrDefaultAsync(r => r.MovieId == movieId && r.UserId == userId, ct);
        var created = review is null;
        if (review is null)
        {
            review = new Review { MovieId = movieId, UserId = userId, Rating = (byte)request.Rating, Comment = comment, CreatedAt = now, UpdatedAt = now };
            db.Reviews.Add(review);
        }
        else
        {
            review.Rating = (byte)request.Rating;
            review.Comment = comment;
            review.UpdatedAt = now;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            // Dos solicitudes de la misma persona compitieron por crear su primera opinión: gana una y la otra actualiza.
            db.ChangeTracker.Clear();
            review = await db.Reviews.FirstAsync(r => r.MovieId == movieId && r.UserId == userId, ct);
            review.Rating = (byte)request.Rating;
            review.Comment = comment;
            review.UpdatedAt = now;
            created = false;
            await db.SaveChangesAsync(ct);
        }

        var author = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.FullName, u.AvatarUrl }).SingleAsync(ct);
        var dto = new ReviewDto(review.Id, ReviewNames.Public(author.FullName), media.Absolute(author.AvatarUrl), review.Rating, review.Comment, review.CreatedAt, IsMine: true);
        return new UpsertReviewResult(dto, created);
    }

    /// <summary>Borra la opinión propia. Si no existe, 404: no se distingue "no tienes" de "no existe".</summary>
    public async Task DeleteMineAsync(int movieId, int userId, CancellationToken ct)
    {
        var deleted = await db.Reviews.Where(r => r.MovieId == movieId && r.UserId == userId).ExecuteDeleteAsync(ct);
        if (deleted == 0)
            throw new NotFoundException("review_not_found", "No tienes una opinión sobre esta película.");
    }

    private async Task EnsureMovieExistsAsync(int movieId, CancellationToken ct)
    {
        if (!await db.Movies.AsNoTracking().AnyAsync(m => m.Id == movieId && m.IsActive, ct))
            throw new NotFoundException("movie_not_found", "La película no existe.");
    }
}
