using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Application.Common.Options;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cinerama.Application.Catalog;

/// <summary>Un comentario público del tráiler en YouTube. Se muestra con su nombre público tal como lo publica YouTube.</summary>
public sealed record TrailerCommentDto(string AuthorName, string? AuthorAvatarUrl, string Text, int LikeCount, DateTime PublishedAt);

/// <summary>
/// <c>Status</c>: <c>ok</c> (hay comentarios), <c>empty</c> (el video no tiene comentarios), <c>disabled</c> (el dueño los desactivó o el
/// video ya no existe), <c>noTrailer</c> (la película no tiene tráiler) o <c>unavailable</c> (sin clave de YouTube o YouTube no respondió).
/// </summary>
public sealed record TrailerCommentsDto(string Status, string? VideoId, IReadOnlyList<TrailerCommentDto> Items);

public enum TrailerCommentsOutcome { Ok, Disabled, Unavailable }

public sealed record TrailerCommentsFetch(TrailerCommentsOutcome Outcome, IReadOnlyList<TrailerCommentDto> Items);

/// <summary>De dónde salen los comentarios. La implementación real llama a YouTube (Infrastructure); las pruebas usan una falsa.</summary>
public interface ITrailerCommentsSource
{
    Task<TrailerCommentsFetch> FetchAsync(string videoId, int limit, CancellationToken ct);
}

/// <summary>Código de un video de YouTube: 11 caracteres. Se extrae de una URL (watch, youtu.be, embed, shorts) o se acepta tal cual.</summary>
public static partial class YouTubeVideoId
{
    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$")]
    private static partial Regex Id();

    // En youtube.com el código va en ?v=, o tras /embed/, /shorts/ o /live/. En youtu.be es el primer segmento de la ruta.
    [GeneratedRegex(@"(?:[?&]v=|/embed/|/shorts/|/live/)([A-Za-z0-9_-]{11})(?![A-Za-z0-9_-])")]
    private static partial Regex InUrl();

    public static string? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        if (Id().IsMatch(value))
            return value;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;

        var host = uri.Host.ToLowerInvariant();
        if (host is not ("youtu.be" or "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtube-nocookie.com" or "www.youtube-nocookie.com"))
            return null;

        if (host == "youtu.be")
        {
            var first = uri.AbsolutePath.Trim('/').Split('/')[0];
            return Id().IsMatch(first) ? first : null;
        }

        var match = InUrl().Match(uri.PathAndQuery);
        return match.Success ? match.Groups[1].Value : null;
    }
}

/// <summary>Memoria de las respuestas de YouTube para no gastar la cuota diaria ni hacer esperar a nadie. Vive en el proceso.</summary>
public sealed class TrailerCommentsCache(TimeProvider time)
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, TrailerCommentsFetch Value)> _entries = new();

    public bool TryGet(string videoId, out TrailerCommentsFetch value)
    {
        if (_entries.TryGetValue(videoId, out var entry) && entry.Expires > time.GetUtcNow())
        {
            value = entry.Value;
            return true;
        }

        value = null!;
        return false;
    }

    public void Set(string videoId, TrailerCommentsFetch value, TimeSpan ttl) =>
        _entries[videoId] = (time.GetUtcNow() + ttl, value);
}

/// <summary>
/// Comentarios del tráiler de una película, traídos de YouTube (máximo 20). Si el video tiene menos, se devuelven los que haya.
/// Cualquier problema con YouTube (sin clave, cuota agotada, comentarios cerrados) se informa en <c>Status</c>: nunca es un error de la API.
/// </summary>
public sealed class TrailerCommentsService(
    IAppDbContext db, ITrailerCommentsSource source, TrailerCommentsCache cache, IOptions<YouTubeOptions> options)
{
    public const int MaxLimit = 20;

    public async Task<TrailerCommentsDto> ListAsync(int movieId, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > MaxLimit)
            throw new ValidationException([new ValidationFailure("limit", $"El límite debe estar entre 1 y {MaxLimit}.")]);

        var trailerUrl = await db.Movies.AsNoTracking().Where(m => m.Id == movieId && m.IsActive).Select(m => m.TrailerUrl).FirstOrDefaultAsync(ct)
            ?? (await db.Movies.AsNoTracking().AnyAsync(m => m.Id == movieId && m.IsActive, ct)
                ? null
                : throw new NotFoundException("movie_not_found", "La película no existe."));

        var videoId = YouTubeVideoId.TryParse(trailerUrl);
        if (videoId is null)
            return new TrailerCommentsDto("noTrailer", null, []);

        var opts = options.Value;
        if (!opts.HasKey)
            return new TrailerCommentsDto("unavailable", videoId, []);

        // Siempre se pide el máximo y se guarda: así 5, 10 o 20 comentarios cuestan una sola consulta a YouTube.
        if (!cache.TryGet(videoId, out var fetch))
        {
            fetch = await source.FetchAsync(videoId, MaxLimit, ct);
            cache.Set(videoId, fetch, TimeSpan.FromMinutes(fetch.Outcome == TrailerCommentsOutcome.Unavailable ? opts.FailureCacheMinutes : opts.CacheMinutes));
        }

        return fetch.Outcome switch
        {
            TrailerCommentsOutcome.Disabled => new TrailerCommentsDto("disabled", videoId, []),
            TrailerCommentsOutcome.Unavailable => new TrailerCommentsDto("unavailable", videoId, []),
            _ when fetch.Items.Count == 0 => new TrailerCommentsDto("empty", videoId, []),
            _ => new TrailerCommentsDto("ok", videoId, fetch.Items.Take(limit).ToList()),
        };
    }
}
