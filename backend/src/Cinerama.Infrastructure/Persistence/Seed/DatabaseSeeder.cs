using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cinerama.Application.Common;
using Cinerama.Application.Common.Options;
using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cinerama.Infrastructure.Persistence.Seed;

/// <summary>
/// Datos de demostración reproducibles (semilla fija). Solo cargan si la base está vacía.
/// El catálogo (películas, cine, horarios) es la cartelera real de Cinerama Chimbote; las cuentas, las opiniones y los pósters
/// son ficticios. Los usuarios usan el dominio reservado <c>.test</c>, que nunca recibe correos reales.
/// </summary>
public sealed partial class DatabaseSeeder(
    CineramaDbContext db,
    IPasswordHasher<User> hasher,
    IOptions<SeedOptions> options,
    IOptions<CinemaOptions> cinemaOptions,
    TimeProvider time,
    ILogger<DatabaseSeeder> logger)
{
    public const string AdminEmail = "admin@cinerama.test";
    public const int CustomerCount = 20;

    // Nombres genéricos e inventados para las cuentas demo (los emails siguen siendo clienteN@cinerama.test).
    private static readonly string[] CustomerNames =
    [
        "David Flores", "Lucía Vargas", "Mateo Rojas", "Valeria Quispe", "Diego Salazar", "Camila Paredes", "Andrés Cárdenas",
        "Sofía Mendoza", "Joaquín Ramos", "Renata Torres", "Sebastián Núñez", "Isabella Cruz", "Gabriel Herrera", "Mariana Castillo",
        "Emilio Vega", "Daniela Ríos", "Nicolás Aguilar", "Fernanda Silva", "Bruno Medina", "Alejandra Soto",
    ];

    private const string City = "Chimbote";

    /// <summary>
    /// La cartelera real de Cinerama Chimbote, importada con <c>backend/tools/import_cartelera.py</c> (solo texto, en minúsculas,
    /// sin imágenes). Se carga una vez, de un archivo incrustado, para que el seed no dependa de internet.
    /// </summary>
    private static readonly Lazy<CarteleraData> Cartelera = new(() =>
    {
        using var stream = typeof(DatabaseSeeder).Assembly.GetManifestResourceStream("cartelera.json")
            ?? throw new InvalidOperationException("Falta el recurso incrustado cartelera.json.");
        return JsonSerializer.Deserialize<CarteleraData>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("cartelera.json está vacío.");
    });

    private sealed record CarteleraData(string Source, string SnapshotDate, List<MovieData> Movies, List<CinemaData> Cinemas);

    // Director, reparto y distribuidora se conservan en el archivo, pero el modelo de datos todavía no tiene dónde guardarlos.
    private sealed record MovieData(
        string Title, string Synopsis, int Minutes, string Classification, string Genre, DateOnly ReleaseDate, bool PreSale, string? TrailerUrl, string? PreviewVideo = null);

    private sealed record ScreeningData(string Movie, List<string> Times);

    // Sala: (nombre, filas, posiciones por fila, posición del pasillo, precio). Las posiciones de pasillo no tienen asiento.
    private sealed record RoomData(string Name, int Rows, int Positions, int Aisle, decimal Price);

    private sealed record CinemaData(
        string Name, string Address, string Region, decimal Latitude, decimal Longitude, string? LogoUrl, List<RoomData> Rooms, List<ScreeningData> Screenings);

    private static readonly (int Rating, string Text)[] ReviewComments =
    [
        (5, "Excelente película, me encantó"), (5, "Me emocionó de principio a fin"), (5, "Una joya, la volvería a ver"),
        (5, "La música y la fotografía son espectaculares"), (5, "Totalmente recomendada"),
        (4, "Muy buena, con un final que sorprende"), (4, "Buenas actuaciones y un ritmo constante"),
        (4, "Me gustó mucho, aunque el comienzo es lento"), (4, "Vale la pena verla en pantalla grande"),
        (3, "Entretenida, pero esperaba un poco más"), (3, "Cumple sin sorprender"), (3, "Tiene buenos momentos y otros que se alargan"),
        (2, "La historia se pierde a la mitad"), (2, "Buena idea, pero mal desarrollada"),
        (1, "No logró engancharme"), (1, "Demasiado larga para lo que cuenta"),
    ];

    private const int CleaningMinutes = 15;
    private const int DaysOfShowtimes = 14;

    /// <summary>Nombre de archivo de un título: sin tildes, en minúsculas y con guiones ("Ciudad de Cristal" → "ciudad-de-cristal").</summary>
    public static string Slug(string title)
    {
        var decomposed = title.Normalize(NormalizationForm.FormD);
        var ascii = new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return NonAlphanumeric().Replace(ascii.ToLowerInvariant(), "-").Trim('-');
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        if (!opts.Enabled)
            return;

        await SeedUsersAsync(opts, ct);
        await SeedCatalogAsync(ct);
        await SeedReviewsAsync(ct);
    }

    private async Task SeedUsersAsync(SeedOptions opts, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
            return;

        if (string.IsNullOrWhiteSpace(opts.AdminPassword) || string.IsNullOrWhiteSpace(opts.CustomerPassword))
        {
            logger.LogWarning("Seed habilitado pero sin Seed:AdminPassword / Seed:CustomerPassword: no se crean usuarios demo.");
            return;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var admin = new User { Email = AdminEmail, FullName = "Administrador Demo", RoleId = Roles.AdminId, CreatedAt = now };
        admin.PasswordHash = hasher.HashPassword(admin, opts.AdminPassword);
        db.Users.Add(admin);

        for (var i = 1; i <= CustomerCount; i++)
        {
            var customer = new User { Email = $"cliente{i}@cinerama.test", FullName = CustomerNames[i - 1], RoleId = Roles.CustomerId, CreatedAt = now };
            customer.PasswordHash = hasher.HashPassword(customer, opts.CustomerPassword);
            db.Users.Add(customer);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: 1 administrador y {Count} clientes demo creados.", CustomerCount);
    }

    private async Task SeedCatalogAsync(CancellationToken ct)
    {
        if (await db.Movies.AnyAsync(ct))
            return;

        var data = Cartelera.Value;
        var now = time.GetUtcNow().UtcDateTime;
        var zone = cinemaOptions.Value.ResolveTimeZone();
        var today = time.LocalToday(zone);

        var genres = data.Movies.Select(m => m.Genre).Distinct().ToDictionary(n => n, n => new Genre { Name = n });
        db.Genres.AddRange(genres.Values);

        var movies = data.Movies.ToDictionary(m => m.Title, m => new Movie
        {
            Title = m.Title, Synopsis = m.Synopsis, DurationMinutes = m.Minutes, Classification = m.Classification,
            PosterUrl = $"posters/{Slug(m.Title)}.jpg", TrailerUrl = m.TrailerUrl, PreviewVideoUrl = m.PreviewVideo,
            ReleaseDate = m.ReleaseDate,
            // La página marca la preventa en el título; abre el día de la consulta (nunca después del estreno).
            PreSaleStartsOn = m.PreSale && m.ReleaseDate > today ? today : null,
            IsActive = true, CreatedAt = now, UpdatedAt = now,
            Genres = [genres[m.Genre]],
        });
        db.Movies.AddRange(movies.Values);

        var cinemaRooms = new List<(CinemaData Cinema, CinemaRoom Room, decimal Price)>();
        foreach (var cinemaData in data.Cinemas)
        {
            var cinema = new Cinema
            {
                Name = cinemaData.Name, Address = cinemaData.Address, City = City, Region = cinemaData.Region,
                Latitude = cinemaData.Latitude, Longitude = cinemaData.Longitude, LogoUrl = cinemaData.LogoUrl, IsActive = true,
            };
            foreach (var (name, rows, positions, aisle, price) in cinemaData.Rooms)
            {
                var room = new CinemaRoom { Name = name, IsActive = true };
                for (var r = 0; r < rows; r++)
                    for (var n = 1; n <= positions; n++)
                        if (n != aisle) // la posición del pasillo no tiene asiento (corta la contigüidad)
                            room.Seats.Add(new Seat { RowLabel = (char)('A' + r), Number = (byte)n, IsActive = true });
                cinema.Rooms.Add(room);
                cinemaRooms.Add((cinemaData, room, price));
            }
            db.Cinemas.Add(cinema);
        }

        await db.SaveChangesAsync(ct);

        var showtimes = new List<Showtime>();
        foreach (var cinemaData in data.Cinemas)
        {
            var rooms = cinemaRooms.Where(x => x.Cinema == cinemaData).Select(x => (x.Room, x.Price)).ToList();
            showtimes.AddRange(ScheduleShowtimes(cinemaData, movies, rooms, today, zone, now));
        }

        db.Showtimes.AddRange(showtimes);
        await db.SaveChangesAsync(ct);

        foreach (var showtime in showtimes)
            await ShowSeatGenerator.GenerateAsync(db, showtime.Id, showtime.CinemaRoomId, ct);

        logger.LogInformation("Seed: {Movies} películas, {Cinemas} cines, {Rooms} salas y {Showtimes} funciones creadas.",
            movies.Count, data.Cinemas.Count, cinemaRooms.Count, showtimes.Count);
    }

    /// <summary>
    /// Repite cada día los horarios que el cine publica para cada película (la página solo muestra el día de la consulta),
    /// durante <see cref="DaysOfShowtimes"/> días. Cada horario va a la primera sala libre; si ninguna lo está, se omite en vez de
    /// solaparlo (RN-13). Solo se programan películas con la venta abierta hoy (RN-42), a partir de su estreno: una preventa
    /// tiene funciones desde que se estrena, y las "próximamente" no tienen ninguna.
    /// </summary>
    private static List<Showtime> ScheduleShowtimes(
        CinemaData cinema, Dictionary<string, Movie> movies, List<(CinemaRoom Room, decimal Price)> rooms, DateOnly today, TimeZoneInfo zone, DateTime now)
    {
        var onSale = cinema.Screenings
            .Where(s => movies.TryGetValue(s.Movie, out var m) && m.IsSalesOpenOn(today))
            .Select(s => (Movie: movies[s.Movie], Times: s.Times.Select(TimeOnly.Parse).Order().ToList()))
            .ToList();

        // Para cada película, los días a programar: desde hoy o desde su estreno (lo que ocurra después).
        var occupancy = rooms.ToDictionary(r => r.Room, _ => new List<(DateTime Start, DateTime End)>());
        var showtimes = new List<Showtime>();
        var firstDay = onSale.Count == 0 ? today : onSale.Min(x => x.Movie.ReleaseDate > today ? x.Movie.ReleaseDate : today);
        var lastDay = onSale.Count == 0 ? today : onSale.Max(x => (x.Movie.ReleaseDate > today ? x.Movie.ReleaseDate : today)).AddDays(DaysOfShowtimes - 1);

        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            var slots = onSale
                .Where(x => day >= (x.Movie.ReleaseDate > today ? x.Movie.ReleaseDate : today) && day < (x.Movie.ReleaseDate > today ? x.Movie.ReleaseDate : today).AddDays(DaysOfShowtimes))
                .SelectMany(x => x.Times.Select(t => (x.Movie, Start: TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(t), zone))))
                .Where(s => s.Start > now)
                .OrderBy(s => s.Start).ThenBy(s => s.Movie.Title);

            foreach (var (movie, start) in slots)
            {
                var end = start.AddMinutes(movie.DurationMinutes + CleaningMinutes);
                var free = rooms.FirstOrDefault(r => occupancy[r.Room].All(o => end <= o.Start || start >= o.End));
                if (free.Room is null)
                    continue;

                occupancy[free.Room].Add((start, end));
                showtimes.Add(new Showtime
                {
                    Movie = movie, CinemaRoom = free.Room, StartsAt = start, EndsAt = end,
                    Price = free.Price, Status = ShowtimeStatus.Scheduled, CreatedAt = now,
                });
            }
        }

        return showtimes;
    }

    /// <summary>
    /// Opiniones ficticias de las películas ya estrenadas, escritas por las cuentas demo. Una por persona y película, con
    /// fechas posteriores al estreno. Solo se cargan una vez y solo si existen cuentas.
    /// </summary>
    private async Task SeedReviewsAsync(CancellationToken ct)
    {
        if (await db.Reviews.AnyAsync(ct))
            return;

        var customerIds = await db.Users.Where(u => u.RoleId == Roles.CustomerId).OrderBy(u => u.Id).Select(u => u.Id).ToListAsync(ct);
        if (customerIds.Count == 0)
            return;

        var now = time.GetUtcNow().UtcDateTime;
        var today = time.LocalToday(cinemaOptions.Value.ResolveTimeZone());
        var random = new Random(2027);

        var released = await db.Movies.Where(m => m.ReleaseDate <= today).OrderBy(m => m.Id)
            .Select(m => new { m.Id, m.ReleaseDate }).ToListAsync(ct);

        var created = 0;
        foreach (var movie in released)
        {
            var daysSinceRelease = today.DayNumber - movie.ReleaseDate.DayNumber;
            var window = Math.Min(30, daysSinceRelease) * 24 * 60 + 60; // minutos hacia atrás: nunca antes del estreno
            var authors = customerIds.OrderBy(_ => random.Next()).Take(random.Next(8, Math.Min(20, customerIds.Count) + 1));

            foreach (var userId in authors)
            {
                var rating = PickRating(random);
                var at = now.AddMinutes(-random.Next(0, window));
                db.Reviews.Add(new Review
                {
                    MovieId = movie.Id, UserId = userId, Rating = (byte)rating,
                    Comment = random.NextDouble() < 0.8 ? PickComment(random, rating) : null,
                    CreatedAt = at, UpdatedAt = at,
                });
                created++;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: {Count} opiniones creadas.", created);
    }

    // Distribución realista: la mayoría entre 4 y 5 estrellas.
    private static int PickRating(Random random)
    {
        var roll = random.Next(100);
        return roll switch { < 3 => 1, < 10 => 2, < 30 => 3, < 70 => 4, _ => 5 };
    }

    private static string PickComment(Random random, int rating)
    {
        var options = ReviewComments.Where(c => c.Rating == rating).ToList();
        return options[random.Next(options.Count)].Text;
    }
}
