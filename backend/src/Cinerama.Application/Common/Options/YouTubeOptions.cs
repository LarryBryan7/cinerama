namespace Cinerama.Application.Common.Options;

/// <summary>
/// Clave de la API de datos de YouTube (v3), solo para leer los comentarios del tráiler. Vive únicamente en el servidor
/// (variable <c>YouTube__ApiKey</c>): la app móvil nunca la ve. Sin clave, la función se apaga sin errores.
/// </summary>
public sealed class YouTubeOptions
{
    public const string SectionName = "YouTube";

    public string? ApiKey { get; set; }

    /// <summary>Cuánto se guarda una respuesta correcta. La cuota diaria es limitada, y los términos de YouTube piden no guardar comentarios por mucho tiempo.</summary>
    public int CacheMinutes { get; set; } = 360;

    /// <summary>Cuánto se recuerda un fallo antes de volver a intentar (evita gastar cuota insistiendo).</summary>
    public int FailureCacheMinutes { get; set; } = 5;

    public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);
}
