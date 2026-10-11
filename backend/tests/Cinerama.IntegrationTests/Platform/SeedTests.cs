using Cinerama.Domain.Enums;
using Cinerama.Infrastructure.Persistence;
using Cinerama.Infrastructure.Persistence.Seed;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Platform;

/// <summary>Los datos de demostración (ficticios) son consistentes con las reglas de negocio y se cargan una sola vez.</summary>
public class SeedTests(ApiFixture fixture) : IntegrationTest(fixture)
{
    private Dictionary<string, string?> SeedSettings => new()
    {
        ["Seed:Enabled"] = "true",
        ["Seed:AdminPassword"] = "Admin_semilla_2026",
        ["Seed:CustomerPassword"] = "Cliente_semilla_2026",
    };

    [Fact]
    public async Task Seed_CreatesConsistentDemoData_AndTheDemoAccountsCanLogIn()
    {
        await using var factory = Fixture.CreateFactory(SeedSettings);
        using var client = NewClient(factory);
        _ = factory.Server; // el arranque ejecuta migraciones y seed

        // Un cine (Cinerama Chimbote) con siete salas de demostración, con pasillos: 60, 130, 200, 60, 88, 130 y 60 asientos.
        var capacities = await Db(db => db.CinemaRooms.OrderBy(r => r.Cinema.Name).ThenBy(r => r.Name)
            .Select(r => new { Cinema = r.Cinema.Name, r.Name, Seats = r.Seats.Count }).ToListAsync(), factory);
        capacities.Select(c => (c.Cinema, c.Name, c.Seats)).ShouldBe(
        [
            ("Cinerama - Chimbote", "Sala 1", 60), ("Cinerama - Chimbote", "Sala 2", 130), ("Cinerama - Chimbote", "Sala 3", 200),
            ("Cinerama - Chimbote", "Sala 4", 60), ("Cinerama - Chimbote", "Sala 5", 88), ("Cinerama - Chimbote", "Sala 6", 130),
            ("Cinerama - Chimbote", "Sala 7", 60),
        ]);

        // RN-15: cada función tiene exactamente un ShowSeat Available por cada asiento de su sala.
        var showtimes = await Db(db => db.Showtimes
            .Select(s => new { s.Id, s.CinemaRoomId, s.StartsAt, s.EndsAt, s.Status, Expected = s.CinemaRoom.Seats.Count, Actual = s.ShowSeats.Count, Available = s.ShowSeats.Count(x => x.Status == ShowSeatStatus.Available) })
            .ToListAsync(), factory);
        showtimes.Count.ShouldBeGreaterThan(100);
        showtimes.ShouldAllBe(s => s.Actual == s.Expected && s.Available == s.Expected && s.Status == ShowtimeStatus.Scheduled);

        // RN-13: ninguna función se solapa con otra de la misma sala.
        foreach (var room in showtimes.GroupBy(s => s.CinemaRoomId))
        {
            var ordered = room.OrderBy(s => s.StartsAt).ToList();
            for (var i = 1; i < ordered.Count; i++)
                ordered[i].StartsAt.ShouldBeGreaterThanOrEqualTo(ordered[i - 1].EndsAt);
        }

        // Datos ficticios: todos los emails usan el dominio reservado .test.
        (await Db(db => db.Users.AllAsync(u => u.Email.EndsWith("@cinerama.test")), factory)).ShouldBeTrue();

        // Las cuentas demo funcionan con las contraseñas tomadas del entorno.
        var admin = await Send(HttpMethod.Post, "/api/v1/auth/login", new { email = "admin@cinerama.test", password = "Admin_semilla_2026" }, client: client);
        admin.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var customer = await Send(HttpMethod.Post, "/api/v1/auth/login", new { email = "cliente7@cinerama.test", password = "Cliente_semilla_2026" }, client: client);
        customer.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Seed_RunsOnlyOnce()
    {
        await using var factory = Fixture.CreateFactory(SeedSettings);
        _ = factory.Server;
        var before = await Db(async db => (await db.Users.CountAsync(), await db.Movies.CountAsync(), await db.Showtimes.CountAsync(), await db.ShowSeats.CountAsync()), factory);

        await DatabaseInitializer.InitializeAsync(factory.Services); // segundo arranque

        var after = await Db(async db => (await db.Users.CountAsync(), await db.Movies.CountAsync(), await db.Showtimes.CountAsync(), await db.ShowSeats.CountAsync()), factory);
        after.ShouldBe(before);
    }

    [Fact]
    public async Task Seed_WithoutPasswords_CreatesNoUsers()
    {
        await using var factory = Fixture.CreateFactory(new() { ["Seed:Enabled"] = "true" });
        _ = factory.Server;

        (await Db(db => db.Users.CountAsync(), factory)).ShouldBe(0);
        (await Db(db => db.Movies.CountAsync(), factory)).ShouldBeGreaterThan(0); // el catálogo no depende de las contraseñas
    }

    [Fact]
    public async Task Seed_Disabled_LeavesTheDatabaseEmpty()
    {
        await using var factory = Fixture.CreateFactory(new() { ["Seed:Enabled"] = "false" });
        _ = factory.Server;

        (await Db(db => db.Movies.CountAsync(), factory)).ShouldBe(0);
    }
}
