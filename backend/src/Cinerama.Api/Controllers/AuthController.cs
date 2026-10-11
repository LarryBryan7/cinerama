using Cinerama.Api.Auth;
using Cinerama.Api.Configuration;
using Cinerama.Api.Errors;
using Cinerama.Application.Auth;
using Cinerama.Application.Common.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Cinerama.Api.Controllers;

/// <param name="RefreshToken">Nulo cuando el refresh token viaja en la cookie HttpOnly (panel web, ADR-006).</param>
public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string? RefreshToken,
    DateTime? RefreshTokenExpiresAt,
    UserDto User);

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting(RateLimitSettings.AuthPolicy)]
public sealed class AuthController(AuthService auth, IOptions<WebSessionSettings> session) : ControllerBase
{
    private const string CookiePath = "/api/v1/auth";

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(request, ct);
        return Created("/api/v1/me", ToResponse(result, includeRefreshToken: true));
    }

    /// <summary>Con <c>useCookie: true</c> el refresh token llega solo en <c>Set-Cookie</c> (HttpOnly, Secure, SameSite=Strict).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);

        if (request.UseCookie)
            SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAt);

        return Ok(ToResponse(result, includeRefreshToken: !request.UseCookie));
    }

    /// <summary>
    /// Renueva la sesión y rota el refresh token. Lo lee del cuerpo (app móvil) o, si no viene, de la cookie
    /// (panel web). La respuesta usa el mismo canal por el que llegó.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Refresh(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshRequest? request, CancellationToken ct)
    {
        var fromBody = request?.RefreshToken;
        var usesCookie = string.IsNullOrWhiteSpace(fromBody);
        var presented = usesCookie ? Request.Cookies[session.Value.RefreshCookieName] : fromBody;

        try
        {
            var result = await auth.RefreshAsync(presented, ct);

            if (usesCookie)
                SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAt);

            return Ok(ToResponse(result, includeRefreshToken: !usesCookie));
        }
        catch (AuthenticationFailedException ex) when (usesCookie)
        {
            // La cookie ya no sirve (revocada, vencida o reutilizada): se borra para que el navegador no la reenvíe.
            // Se responde aquí y no con "throw": el middleware de excepciones limpia las cabeceras de la respuesta
            // y el Set-Cookie que borra la cookie se perdería.
            ClearRefreshCookie();

            var problem = ProblemResponses.Create(StatusCodes.Status401Unauthorized, ex.Code, "No autenticado", ex.Message);
            problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            return new ObjectResult(problem) { StatusCode = StatusCodes.Status401Unauthorized };
        }
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LogoutRequest? request, CancellationToken ct)
    {
        var presented = string.IsNullOrWhiteSpace(request?.RefreshToken)
            ? Request.Cookies[session.Value.RefreshCookieName]
            : request.RefreshToken;

        await auth.LogoutAsync(User.GetUserId(), presented, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    private static AuthResponse ToResponse(AuthResult r, bool includeRefreshToken) => new(
        r.AccessToken,
        r.AccessTokenExpiresAt,
        includeRefreshToken ? r.RefreshToken : null,
        includeRefreshToken ? r.RefreshTokenExpiresAt : null,
        r.User);

    // Path restringido: el navegador solo envía la cookie a los endpoints de autenticación, no a toda la API.
    private CookieOptions CookieOptions(DateTime? expiresUtc) => new()
    {
        HttpOnly = true,
        Secure = session.Value.RefreshCookieSecure,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        IsEssential = true,
        Expires = expiresUtc is null ? null : new DateTimeOffset(DateTime.SpecifyKind(expiresUtc.Value, DateTimeKind.Utc)),
    };

    private void SetRefreshCookie(string token, DateTime expiresUtc) =>
        Response.Cookies.Append(session.Value.RefreshCookieName, token, CookieOptions(expiresUtc));

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(session.Value.RefreshCookieName, CookieOptions(null));
}
