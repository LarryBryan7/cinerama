using System.Text.Json;
using System.Text.RegularExpressions;
using Cinerama.Application.Catalog;
using Cinerama.Application.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cinerama.Infrastructure.YouTube;

/// <summary>
/// Lee los comentarios de un video con la API de datos de YouTube v3 (<c>commentThreads.list</c>, 1 unidad de cuota por llamada).
/// La clave viaja en la cabecera <c>x-goog-api-key</c> y no en la URL: así no aparece en los registros de HttpClient ni en los de ningún proxy.
/// Nunca lanza: cualquier fallo se devuelve como <see cref="TrailerCommentsOutcome.Unavailable"/> o <see cref="TrailerCommentsOutcome.Disabled"/>.
/// </summary>
public sealed partial class YouTubeCommentsClient(HttpClient http, IOptions<YouTubeOptions> options, ILogger<YouTubeCommentsClient> logger) : ITrailerCommentsSource
{
    public const string BaseAddress = "https://www.googleapis.com/";
    public const int MaxTextLength = 500;

    public async Task<TrailerCommentsFetch> FetchAsync(string videoId, int limit, CancellationToken ct)
    {
        var key = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
            return new(TrailerCommentsOutcome.Unavailable, []);

        // Los comentarios más relevantes primero (los que YouTube muestra arriba), en texto plano.
        var url = $"youtube/v3/commentThreads?part=snippet&videoId={Uri.EscapeDataString(videoId)}&maxResults={Math.Clamp(limit, 1, 100)}&order=relevance&textFormat=plainText";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-goog-api-key", key);

        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
                return new(TrailerCommentsOutcome.Ok, Parse(body, limit));

            var reason = ErrorReason(body);
            // Los comentarios cerrados o un video que ya no existe no se arreglan reintentando: se recuerdan como "desactivados".
            if (reason is "commentsDisabled" or "videoNotFound" || (int)response.StatusCode == 404)
                return new(TrailerCommentsOutcome.Disabled, []);

            // Sin la URL (llevaría datos de la solicitud) y sin la clave: solo el motivo.
            logger.LogWarning("YouTube respondió {Status} ({Reason}) al pedir los comentarios del video {VideoId}.", (int)response.StatusCode, reason ?? "sin motivo", videoId);
            return new(TrailerCommentsOutcome.Unavailable, []);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested)
                throw;

            logger.LogWarning("No se pudo consultar YouTube para el video {VideoId}: {Error}", videoId, e.GetType().Name);
            return new(TrailerCommentsOutcome.Unavailable, []);
        }
    }

    public static IReadOnlyList<TrailerCommentDto> Parse(string json, int limit)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<TrailerCommentDto>();
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("snippet", out var thread)
                || !thread.TryGetProperty("topLevelComment", out var top)
                || !top.TryGetProperty("snippet", out var s))
                continue;

            var text = Clean(Str(s, "textDisplay"));
            var author = Clean(Str(s, "authorDisplayName"));
            if (text.Length == 0 || author.Length == 0)
                continue; // un comentario sin texto (por ejemplo, solo una imagen) no aporta

            if (!DateTime.TryParse(Str(s, "publishedAt"), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var published))
                published = DateTime.UnixEpoch;

            result.Add(new TrailerCommentDto(
                Truncate(author, 100), SafeHttps(Str(s, "authorProfileImageUrl")), Truncate(text, MaxTextLength),
                s.TryGetProperty("likeCount", out var likes) && likes.TryGetInt32(out var n) && n >= 0 ? n : 0, published));

            if (result.Count == limit)
                break;
        }

        return result;
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? ErrorReason(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("error").GetProperty("errors")[0].GetProperty("reason").GetString();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Sin caracteres de control (salvo el salto de línea), con los espacios repetidos reducidos y sin extremos en blanco.</summary>
    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var noControl = new string(value.Where(c => !char.IsControl(c) || c is '\n').ToArray());
        return Spaces().Replace(Newlines().Replace(noControl, "\n\n"), " ").Trim();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)].TrimEnd() + "…";

    /// <summary>Solo se acepta una foto servida por https (nunca una dirección http, data: ni javascript:).</summary>
    private static string? SafeHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.ToString() : null;

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex Newlines();
}
