using System.Threading.RateLimiting;
using Cinerama.Api.Auth;
using Cinerama.Api.Configuration;
using Cinerama.Api.Errors;
using Cinerama.Api.Storage;
using Cinerama.Api.Realtime;
using Cinerama.Application.Common;
using Cinerama.Application;
using Cinerama.Application.Common.Options;
using Cinerama.Domain.Entities;
using Cinerama.Infrastructure;
using Cinerama.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Logs estructurados (JSON) fuera de desarrollo. Los scopes incluyen el TraceId de cada request (RNF-15).
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
}

// ── Configuración tipada y validada al arrancar ─────────────────────────────
// Una clave JWT débil o una zona horaria inválida impiden iniciar la API en lugar de fallar a mitad de una solicitud.
builder.Services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<CinemaOptions>().Bind(config.GetSection(CinemaOptions.SectionName)).ValidateDataAnnotations()
    .Validate(o =>
    {
        try { o.ResolveTimeZone(); return true; }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return false; }
    }, "Cinema:TimeZone no es una zona horaria IANA válida.")
    .ValidateOnStart();
builder.Services.AddOptions<SeatingOptions>().Bind(config.GetSection(SeatingOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<TicketOptions>().Bind(config.GetSection(TicketOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<CorsSettings>().Bind(config.GetSection(CorsSettings.SectionName))
    .Validate(o => !o.ParseOrigins().Contains("*"), "Cors:AllowedOrigins no admite '*' porque se usan credenciales.")
    .ValidateOnStart();
builder.Services.AddOptions<RateLimitSettings>().Bind(config.GetSection(RateLimitSettings.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<WebSessionSettings>().Bind(config.GetSection(WebSessionSettings.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<DatabaseSettings>().Bind(config.GetSection(DatabaseSettings.SectionName));
builder.Services.AddOptions<MediaSettings>().Bind(config.GetSection(MediaSettings.SectionName))
    .Validate(o => string.IsNullOrWhiteSpace(o.PublicBaseUrl) || Uri.TryCreate(o.PublicBaseUrl, UriKind.Absolute, out _), "Media:PublicBaseUrl debe ser una URL absoluta.")
    .ValidateOnStart();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IMediaUrls, HttpMediaUrls>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);

// ── Tiempo real (ADR-016): el hub solo avisa; las escrituras pasan por REST ──
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 8 * 1024;   // el cliente solo envía "me suscribo a la función N"
    o.EnableDetailedErrors = false;           // un error inesperado no expone detalles internos al cliente
});
// Reemplaza al notificador nulo de Infrastructure.
builder.Services.Replace(ServiceDescriptor.Singleton<ISeatNotifier, SignalRSeatNotifier>());

// ── Errores: siempre application/problem+json con un "code" estable ─────────
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    var problem = ctx.ProblemDetails;
    problem.Extensions.TryAdd("code", ProblemResponses.DefaultCode(problem.Status ?? 500));
    problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddControllers().ConfigureApiBehaviorOptions(o =>
{
    // Cuerpo malformado o tipos inválidos: mismo formato que el resto de los errores de validación.
    o.InvalidModelStateResponseFactory = ctx =>
    {
        var problem = new ValidationProblemDetails(ctx.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Hay errores de validación",
            Detail = "Revisa los campos indicados.",
            Type = "https://cinerama.example/errors/validation-failed",
        };
        problem.Extensions["code"] = "validation_failed";
        problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest, ContentTypes = { "application/problem+json" } };
    };
});

// ── Autenticación y autorización: seguro por defecto ────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, JwtBearerSetup>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationHandler>();
builder.Services.AddAuthorizationBuilder()
    // Todo endpoint exige un usuario autenticado salvo que lo marque [AllowAnonymous] (RNF-03).
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin));

// ── CORS con credenciales: solo los orígenes configurados (ADR-006) ─────────
// Sin orígenes configurados la política no permite ninguno (comportamiento seguro por defecto).
builder.Services.AddCors();
builder.Services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
    .Configure<IOptions<CorsSettings>>((cors, settings) => cors.AddPolicy(CorsSettings.PolicyName, p => p
        .WithOrigins(settings.Value.ParseOrigins())
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod()));

// ── Rate limiting en autenticación (RN-06) ──────────────────────────────────
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(RateLimitSettings.AuthPolicy, ctx =>
    {
        var permit = ctx.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value.AuthPermitPerMinute;
        return RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });

    // RN-40: bloquear y liberar asientos se limita por usuario (no por IP: tras un NAT comparten dirección).
    o.AddPolicy(RateLimitSettings.SeatsPolicy, ctx =>
    {
        var permit = ctx.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value.SeatsPermitPerMinute;
        var partition = ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"seats:{partition}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });

    // RN-47: publicar, cambiar o borrar opiniones se limita por usuario.
    o.AddPolicy(RateLimitSettings.ReviewsPolicy, ctx =>
    {
        var permit = ctx.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value.ReviewsPermitPerMinute;
        var partition = ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"reviews:{partition}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });

    o.OnRejected = async (ctx, _) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

        var problems = ctx.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(ctx.HttpContext, ProblemResponses.Create(
            StatusCodes.Status429TooManyRequests, "rate_limited", "Demasiadas solicitudes",
            "Alcanzaste el límite de solicitudes. Espera un momento antes de volver a intentarlo."));
    };
});

// ── Swagger / OpenAPI ───────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Cinerama API",
        Version = "v1",
        Description = "Reserva de entradas de cine. Proyecto personal de portafolio: todos los datos son ficticios.",
    });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Access token obtenido en /api/v1/auth/login.",
    });
    o.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
    });
});

builder.Services.AddHealthChecks().AddDbContextCheck<CineramaDbContext>("sqlserver");

var app = builder.Build();

// Los compromisos de desarrollo local no pueden llegar a un entorno real (ADR-014).
var insecure = InsecureSettingsGuard.Enforce(app.Configuration, app.Environment);
if (insecure.Count > 0)
    app.Logger.LogWarning("Configuración insegura PERMITIDA (solo para uso local): {Findings}", string.Join("; ", insecure));

if (app.Services.GetRequiredService<IOptions<DatabaseSettings>>().Value.MigrateOnStartup)
    await DatabaseInitializer.InitializeAsync(app.Services);

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "Cinerama API v1"));
}

// Medios del catálogo (pósters, logos y el video corto del detalle): archivos estáticos públicos, solo de tipo imagen o .mp4. Van antes de la autenticación,
// así que no pasan por la política de seguridad por defecto, y nunca exponen nada fuera de la carpeta de medios.
var mediaRoot = Path.GetFullPath(app.Services.GetRequiredService<IOptions<MediaSettings>>().Value.RootPath, app.Environment.ContentRootPath);
if (Directory.Exists(mediaRoot))
{
    var imageTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider(new Dictionary<string, string>
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp",
        [".mp4"] = "video/mp4", // el servidor de archivos atiende solicitudes por rangos: el reproductor puede empezar sin bajar todo
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(mediaRoot),
        RequestPath = MediaSettings.RequestPath,
        ContentTypeProvider = imageTypes,
        ServeUnknownFileTypes = false,
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers.CacheControl = "public, max-age=3600";
            ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
        },
    });
}
else
{
    app.Logger.LogWarning("La carpeta de medios {Path} no existe: no se sirven imágenes del catálogo.", mediaRoot);
}

app.UseCors(CorsSettings.PolicyName);

// El limitador va después de la autenticación para poder particionar por usuario (RN-40).
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHub<SeatHub>(SeatHub.Path, o =>
{
    // El token se valida al conectar; sin esto una conexión seguiría abierta después de que el token expire.
    // Al cerrarse, el cliente se reconecta con un token renovado.
    o.CloseOnAuthenticationExpiration = true;
});
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

// Necesario para que WebApplicationFactory<Program> pueda referenciar el punto de entrada en los tests.
public partial class Program;
