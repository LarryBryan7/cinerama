using Cinerama.Application.Admin;
using Cinerama.Application.Auth;
using Cinerama.Application.Bookings;
using Cinerama.Application.Catalog;
using Cinerama.Application.Seating;
using Cinerama.Application.Users;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Cinerama.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<GenreService>();
        services.AddScoped<MovieService>();
        services.AddScoped<ShowtimeService>();
        services.AddScoped<CinemaService>();
        services.AddScoped<ReviewService>();
        services.AddScoped<ShowtimeCanceller>();
        services.AddScoped<GenreAdminService>();
        services.AddScoped<MovieAdminService>();
        services.AddScoped<CinemaAdminService>();
        services.AddScoped<RoomAdminService>();
        services.AddScoped<ShowtimeAdminService>();
        services.AddScoped<BookingAdminService>();
        services.AddScoped<ReportService>();
        services.AddSingleton<TrailerCommentsCache>();
        services.AddScoped<TrailerCommentsService>();
        services.AddSingleton<SeatRecommendationService>();
        services.AddScoped<SeatMapService>();
        services.AddScoped<SeatLockService>();
        services.AddScoped<BookingService>();

        return services;
    }
}
