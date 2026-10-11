using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Cinerama.Api.Errors;

/// <summary>
/// Un 403 (autenticado pero sin permiso) responde con ProblemDetails. El 401 lo resuelve el JwtBearer
/// (ver <see cref="Auth.JwtBearerSetup"/>), así que aquí se delega en el comportamiento por defecto.
/// </summary>
public sealed class ProblemDetailsAuthorizationHandler(IProblemDetailsService problems) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            await problems.WriteAsync(context, ProblemResponses.Create(
                StatusCodes.Status403Forbidden, "forbidden", "Acceso denegado", "No tienes permisos para realizar esta acción."));
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
