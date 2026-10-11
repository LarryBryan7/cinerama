using System.ComponentModel.DataAnnotations;

namespace Cinerama.Application.Common.Options;

/// <summary>
/// Fuente única de la configuración de tokens. Se valida al arrancar para que login y refresh emitan siempre
/// la misma duración y para que una clave débil o ausente impida iniciar la API.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Clave HMAC-SHA256. Mínimo 32 caracteres. Nunca se versiona: variable de entorno o user-secrets.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = "";

    [Required]
    public string Issuer { get; set; } = "cinerama-api";

    [Required]
    public string Audience { get; set; } = "cinerama-clients";

    [Range(1, 120)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 7;
}
