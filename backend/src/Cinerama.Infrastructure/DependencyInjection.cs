using Cinerama.Application.Catalog;
using Cinerama.Application.Common;
using Cinerama.Application.Common.Options;
using Cinerama.Infrastructure.YouTube;
using Cinerama.Domain.Entities;
using Cinerama.Infrastructure.Auth;
using Cinerama.Infrastructure.Persistence;
using Cinerama.Infrastructure.Persistence.Seed;
using Cinerama.Infrastructure.Seating;
using Cinerama.Infrastructure.Tickets;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cinerama.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // La cadena se lee al crear el DbContext (no al registrar el servicio): así se valida con la configuración final.
        services.AddDbContext<CineramaDbContext>((sp, o) => o.UseSqlServer(
            sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta la cadena de conexión 'ConnectionStrings:Default'.")));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<CineramaDbContext>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ITicketSigner, HmacTicketSigner>();

        // Hasta la fase 3 (SignalR) nadie escucha los cambios de asientos. TryAdd: la API puede registrar otra implementación antes.
        services.TryAddSingleton<ISeatNotifier, NullSeatNotifier>();
        services.AddScoped<SeatLockCleanup>();
        services.AddHostedService<SeatLockCleanupService>();

        // Comentarios del tráiler (YouTube Data API v3). Sin clave en YouTube:ApiKey la función queda apagada sin errores.
        services.AddOptions<YouTubeOptions>().Bind(configuration.GetSection(YouTubeOptions.SectionName));
        services.AddHttpClient<ITrailerCommentsSource, YouTubeCommentsClient>(c =>
        {
            c.BaseAddress = new Uri(YouTubeCommentsClient.BaseAddress);
            c.Timeout = TimeSpan.FromSeconds(8);
        });

        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
