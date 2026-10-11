using System.Text;
using Cinerama.Api.Errors;
using Cinerama.Application.Common.Options;
using Cinerama.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Cinerama.Api.Auth;

/// <summary>Configura la validación del access token a partir de <see cref="JwtOptions"/> (fuente única).</summary>
public sealed class JwtBearerSetup(IOptions<JwtOptions> jwt, TimeProvider time) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        var o = jwt.Value;

        // Sin esto el handler renombra "sub" y "role" a los nombres largos de WS-Federation.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = o.Issuer,
            ValidateAudience = true,
            ValidAudience = o.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey)),

            // Solo HS256: rechaza tokens con "alg": "none" o firmados con otro algoritmo.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireSignedTokens = true,

            // La vigencia se valida con el mismo reloj que emite los tokens (TimeProvider), no con el del sistema:
            // así las pruebas pueden adelantar el tiempo. Se lanzan las mismas excepciones que el validador estándar,
            // porque OnChallenge distingue "token_expired" por el tipo de la excepción.
            ValidateLifetime = true,
            LifetimeValidator = ValidateLifetime,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(5),

            NameClaimType = "sub",
            RoleClaimType = TokenService.RoleClaim,
        };

        options.Events = new JwtBearerEvents
        {
            // WebSockets no permite enviar encabezados desde el navegador ni desde algunos clientes: SignalR manda el token
            // como ?access_token=. Se acepta SOLO en las rutas del hub; en cualquier otra ruta se ignora, para que un token
            // pegado en una URL de la API REST no sirva y no termine en historiales ni en registros de proxies.
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = token;
                return Task.CompletedTask;
            },

            OnChallenge = async context =>
            {
                // Un único punto para TODOS los 401 de sesión. El cliente decide por el "code": token_expired
                // → intenta un refresh; cualquier otro → no tiene sesión.
                context.HandleResponse();

                var code = IsExpired(context.AuthenticateFailure) ? "token_expired"
                    : context.AuthenticateFailure is null ? "unauthorized"
                    : "invalid_token";

                var title = code == "token_expired" ? "El token expiró" : "No autenticado";
                var detail = code switch
                {
                    "token_expired" => "El access token expiró. Renueva la sesión con el refresh token.",
                    "invalid_token" => "El token no es válido.",
                    _ => "Debes iniciar sesión para acceder a este recurso.",
                };

                context.Response.Headers.WWWAuthenticate = "Bearer";
                var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.WriteAsync(context.HttpContext, ProblemResponses.Create(StatusCodes.Status401Unauthorized, code, title, detail));
            },
        };
    }

    private bool ValidateLifetime(DateTime? notBefore, DateTime? expires, SecurityToken token, TokenValidationParameters parameters)
    {
        var now = time.GetUtcNow().UtcDateTime;

        if (expires is null)
            throw new SecurityTokenNoExpirationException("El token no tiene vencimiento.");
        if (notBefore is not null && notBefore.Value > now + parameters.ClockSkew)
            throw new SecurityTokenNotYetValidException("El token todavía no es válido.");
        if (expires.Value < now - parameters.ClockSkew)
            throw new SecurityTokenExpiredException("El token expiró.");

        return true;
    }

    public void Configure(JwtBearerOptions options) => Configure(JwtBearerDefaults.AuthenticationScheme, options);

    private static bool IsExpired(Exception? failure) => failure switch
    {
        SecurityTokenExpiredException => true,
        AggregateException agg => agg.InnerExceptions.Any(e => e is SecurityTokenExpiredException),
        _ => false,
    };
}
