using System.ComponentModel.DataAnnotations;

namespace Cinerama.Api.Configuration;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";
    public const string PolicyName = "web";

    /// <summary>Orígenes permitidos, separados por coma (se lee bien desde una variable de entorno). Nunca "*": se usan credenciales (ADR-006).</summary>
    public string AllowedOrigins { get; set; } = "";

    public string[] ParseOrigins() =>
        AllowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimiting";
    public const string AuthPolicy = "auth";
    public const string SeatsPolicy = "seats";
    public const string ReviewsPolicy = "reviews";

    /// <summary>RN-06: solicitudes por minuto y por IP a los endpoints de autenticación.</summary>
    [Range(1, 10_000)]
    public int AuthPermitPerMinute { get; set; } = 10;

    /// <summary>RN-40: solicitudes por minuto y por usuario a bloquear y liberar asientos.</summary>
    [Range(1, 10_000)]
    public int SeatsPermitPerMinute { get; set; } = 60;

    /// <summary>RN-47: opiniones que una persona puede publicar, cambiar o borrar por minuto.</summary>
    [Range(1, 10_000)]
    public int ReviewsPermitPerMinute { get; set; } = 10;
}

/// <summary>Imágenes del catálogo (pósters, logos, avatares). Se sirven como archivos estáticos desde <c>/media</c>.</summary>
public sealed class MediaSettings
{
    public const string SectionName = "Media";
    public const string RequestPath = "/media";

    /// <summary>Carpeta con las imágenes. Relativa a la raíz de contenido de la API, o absoluta.</summary>
    public string RootPath { get; set; } = "media";

    /// <summary>
    /// Dirección pública del servidor (por ejemplo <c>https://api.cinerama.example</c>). Si se deja vacía, las URL se arman con la
    /// dirección por la que llegó cada solicitud, que es lo que necesita el emulador (10.0.2.2) y el uso local.
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}

public sealed class WebSessionSettings
{
    public const string SectionName = "WebSession";

    [Required]
    public string RefreshCookieName { get; set; } = "cinerama_rt";

    /// <summary>La cookie del refresh token solo viaja por HTTPS. Se puede desactivar únicamente en desarrollo local.</summary>
    public bool RefreshCookieSecure { get; set; } = true;
}

public sealed class DatabaseSettings
{
    public const string SectionName = "Database";

    /// <summary>Aplica migraciones y seed al arrancar. Pensado para desarrollo y Docker Compose (ADR-014).</summary>
    public bool MigrateOnStartup { get; set; }
}
