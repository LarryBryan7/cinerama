using System.Globalization;
using System.Security.Claims;

namespace Cinerama.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Id del usuario autenticado, tomado del claim "sub" del token firmado. Es la única fuente de identidad
    /// (RN-34): ningún endpoint la recibe por parámetro, cabecera personalizada ni cuerpo.
    /// </summary>
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst("sub")?.Value;
        return int.TryParse(sub, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : throw new InvalidOperationException("El token no contiene un claim 'sub' válido.");
    }

    /// <summary>
    /// El id del usuario si la solicitud trae un token válido; <c>null</c> si es anónima. Sirve a los endpoints públicos que
    /// muestran algo extra a quien inició sesión (por ejemplo, cuál de las opiniones es la suya).
    /// </summary>
    public static int? GetUserIdOrNull(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirst("sub")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
}
