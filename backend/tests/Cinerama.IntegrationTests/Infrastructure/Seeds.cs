using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using Cinerama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Infrastructure;

/// <summary>Constructores de datos mínimos y válidos para los tests. Todo es ficticio.</summary>
public static class Seeds
{
    public static async Task<User> UserAsync(CineramaDbContext db, string email, byte roleId = Roles.CustomerId, bool active = true, string? fullName = null)
    {
        var user = new User
        {
            Email = email, FullName = fullName ?? "Usuario " + email, RoleId = roleId, IsActive = active,
            PasswordHash = "hash-no-usable-para-login", CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task<Genre> GenreAsync(CineramaDbContext db, string name)
    {
        var genre = new Genre { Name = name };
        db.Genres.Add(genre);
        await db.SaveChangesAsync();
        return genre;
    }

    public static async Task<Movie> MovieAsync(
        CineramaDbContext db, string title, bool active = true, int minutes = 100, params Genre[] genres)
    {
        var now = DateTime.UtcNow;
        var movie = new Movie
        {
            Title = title, Synopsis = "Sinopsis ficticia.", DurationMinutes = minutes, Classification = "+14",
            IsActive = active, CreatedAt = now, UpdatedAt = now, Genres = genres.ToList(),
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie;
    }

    /// <summary>Película con fechas de estreno y preventa explícitas (calendario local del cine).</summary>
    public static async Task<Movie> MovieWithReleaseAsync(
        CineramaDbContext db, string title, DateOnly releaseDate, DateOnly? preSaleStartsOn = null, bool active = true)
    {
        var movie = await MovieAsync(db, title, active);
        movie.ReleaseDate = releaseDate;
        movie.PreSaleStartsOn = preSaleStartsOn;
        await db.SaveChangesAsync();
        return movie;
    }

    public static async Task<Cinema> CinemaAsync(
        CineramaDbContext db, string name = "Cine de Prueba", string city = "Lima", decimal latitude = -12.046m, decimal longitude = -77.043m, bool active = true)
    {
        var cinema = new Cinema
        {
            Name = name, Address = "Calle Ficticia 123", City = city, Region = "Lima", Latitude = latitude, Longitude = longitude, IsActive = active,
        };
        db.Cinemas.Add(cinema);
        await db.SaveChangesAsync();
        return cinema;
    }

    public static async Task<Review> ReviewAsync(CineramaDbContext db, Movie movie, User user, int rating, string? comment = null, DateTime? at = null)
    {
        var when = at ?? DateTime.UtcNow;
        var review = new Review { MovieId = movie.Id, UserId = user.Id, Rating = (byte)rating, Comment = comment, CreatedAt = when, UpdatedAt = when };
        db.Reviews.Add(review);
        await db.SaveChangesAsync();
        return review;
    }

    /// <summary>Sala con <paramref name="rows"/> filas (A, B, …) de <paramref name="perRow"/> asientos contiguos.</summary>
    public static async Task<CinemaRoom> RoomAsync(
        CineramaDbContext db, string name = "Sala T", int rows = 2, int perRow = 5, int? aisle = null, Cinema? cinema = null)
    {
        // Toda sala pertenece a un cine: si el test no indica uno, se usa (o se crea) un cine de prueba.
        cinema ??= await db.Cinemas.FirstOrDefaultAsync(c => c.Name == "Cine de Prueba") ?? await CinemaAsync(db);
        var room = new CinemaRoom { Name = name, CinemaId = cinema.Id };
        for (var r = 0; r < rows; r++)
            for (var n = 1; n <= perRow; n++)
                if (n != aisle) // una posición sin asiento es un pasillo
                    room.Seats.Add(new Seat { RowLabel = (char)('A' + r), Number = (byte)n });
        db.CinemaRooms.Add(room);
        await db.SaveChangesAsync();
        return room;
    }

    public static async Task<Showtime> ShowtimeAsync(
        CineramaDbContext db, Movie movie, CinemaRoom room, DateTime startsAtUtc, decimal price = 10m,
        ShowtimeStatus status = ShowtimeStatus.Scheduled, bool generateSeats = true)
    {
        var showtime = new Showtime
        {
            MovieId = movie.Id, CinemaRoomId = room.Id, StartsAt = startsAtUtc,
            EndsAt = startsAtUtc.AddMinutes(movie.DurationMinutes + 15),
            Price = price, Status = status, CreatedAt = DateTime.UtcNow,
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        if (generateSeats)
            await ShowSeatGenerator.GenerateAsync(db, showtime.Id, room.Id);

        return showtime;
    }
}
