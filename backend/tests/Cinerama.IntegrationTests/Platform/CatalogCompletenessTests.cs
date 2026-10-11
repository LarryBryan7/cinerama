using Cinerama.Application.Catalog;
using Cinerama.Application.Common;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Platform;

/// <summary>
/// "Todo lo que muestra la pantalla está en la base de datos." Cada pantalla de la app se alimenta de un dato real; si el
/// seed deja un hueco (una sinopsis vacía, un póster que no existe, una película sin cine), la pantalla tendría que
/// inventarlo. Este test recorre los datos de demostración TAL COMO LOS VE LA APP (por la API) y falla ante cualquier hueco.
/// El detalle de qué elemento sale de dónde está en docs/ui-data-coverage.md.
/// </summary>
public class CatalogCompletenessTests(ApiFixture fixture) : IntegrationTest(fixture)
{
    private Dictionary<string, string?> SeedSettings => new()
    {
        ["Seed:Enabled"] = "true",
        ["Seed:AdminPassword"] = "Admin_semilla_2026",
        ["Seed:CustomerPassword"] = "Cliente_semilla_2026",
    };

    private static string MediaDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Cinerama.Api", "media")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("No se encontró src/Cinerama.Api/media."), "src", "Cinerama.Api", "media");
    }

    private static string FileOf(string absoluteUrl) =>
        Path.Combine(MediaDirectory(), absoluteUrl.Split("/media/", 2)[1].Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    [Trait("Case", "TC-DATA-01")]
    public async Task EveryMovieHasEverythingTheCarteleraCardNeeds_AndItsImageExists()
    {
        await using var factory = Fixture.CreateFactory(SeedSettings);
        using var client = NewClient(factory);
        _ = factory.Server;

        var movies = (await Read<PagedResult<MovieListItemDto>>(await Send(HttpMethod.Get, "/api/v1/movies?pageSize=100", client: client))).Items;

        // La cartelera real de Cinerama Chimbote (backend/tools/import_cartelera.py): 9 películas, todas con su ficha completa.
        movies.Count.ShouldBe(9);
        movies.Select(m => m.Title).Distinct().Count().ShouldBe(9);
        foreach (var m in movies)
        {
            m.Title.ShouldNotBeNullOrWhiteSpace();
            char.IsUpper(m.Title[0]).ShouldBeTrue($"{m.Title}: el título empieza con mayúscula");
            m.Title[1..].ShouldBe(m.Title[1..].ToLowerInvariant(), "el resto del título va en minúsculas");
            m.Title.ShouldNotContain("preventa", Case.Insensitive, "la marca \"(preventa)\" de la página no es parte del título");
            char.IsUpper(m.Synopsis![0]).ShouldBeTrue($"{m.Title}: la sinopsis empieza con mayúscula");
            m.Classification.ShouldBeOneOf("+14", "Todo espectador"); // "MAYORES DE 14" de la página se muestra como "+14"
            char.IsUpper(m.Genres[0][0]).ShouldBeTrue($"{m.Title}: el género empieza con mayúscula");
            m.Synopsis.ShouldNotBeNullOrWhiteSpace($"{m.Title}: falta la sinopsis");
            m.DurationMinutes.ShouldBeInRange(1, 400);
            m.Classification.ShouldNotBeNullOrWhiteSpace();
            m.Genres.ShouldNotBeEmpty($"{m.Title}: falta al menos un género");
            m.ReleaseDate.ShouldBeGreaterThan(new DateOnly(2000, 1, 1), $"{m.Title}: falta la fecha de estreno");

            m.PosterUrl.ShouldNotBeNull($"{m.Title}: falta el póster");
            m.PosterUrl.ShouldStartWith("http://localhost/media/posters/");
            File.Exists(FileOf(m.PosterUrl)).ShouldBeTrue($"{m.Title}: el archivo del póster no existe ({m.PosterUrl})");
        }
    }

    [Fact]
    [Trait("Case", "TC-DATA-01")]
    public async Task TheDemoShowsTheThreeStates_AndEachOneIsCoherent()
    {
        await using var factory = Fixture.CreateFactory(SeedSettings);
        using var client = NewClient(factory);
        _ = factory.Server;
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");

        var movies = (await Read<PagedResult<MovieListItemDto>>(await Send(HttpMethod.Get, "/api/v1/movies?pageSize=100", client: client))).Items;
        // La cartelera real tiene una preventa (Avengers: Doomsday, estreno 16/12/2026) y el resto ya estrenado. Los conteos
        // dependen de la fecha en que se cargue el seed (con el tiempo la preventa se estrena), así que se comprueba la coherencia
        // del estado con las fechas, no un número fijo.
        movies.Count.ShouldBe(9);
        movies.Count(m => m.Status == "NowShowing").ShouldBeGreaterThanOrEqualTo(8);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        foreach (var m in movies)
            (m.Status == "NowShowing").ShouldBe(m.ReleaseDate <= today, $"{m.Title}: estado {m.Status} con estreno {m.ReleaseDate}");

        foreach (var summary in movies)
        {
            var detail = await Read<MovieDetailDto>(await Send(HttpMethod.Get, $"/api/v1/movies/{summary.Id}", client: client));
            detail.Status.ShouldBe(summary.Status);

            switch (summary.Status)
            {
                case "NowShowing" or "PreSale":
                    detail.UpcomingShowtimes.ShouldNotBeEmpty($"{summary.Title}: se puede reservar pero no tiene funciones");
                    summary.Cinemas.ShouldNotBeEmpty($"{summary.Title}: no se ve en ningún cine");
                    detail.UpcomingShowtimes.ShouldAllBe(s => s.AvailableSeats > 0);
                    detail.UpcomingShowtimes.ShouldAllBe(s => s.CinemaName != "" && s.RoomName != "" && s.Price > 0);
                    // Ninguna función antes del estreno: la preventa vende asientos de funciones que empiezan al estrenarse.
                    detail.UpcomingShowtimes.ShouldAllBe(s =>
                        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(s.StartsAt, zone)) >= summary.ReleaseDate);
                    break;
                default: // ComingSoon
                    detail.UpcomingShowtimes.ShouldBeEmpty($"{summary.Title}: es solo informativa");
                    summary.Cinemas.ShouldBeEmpty();
                    break;
            }
        }
    }

    [Fact]
    [Trait("Case", "TC-DATA-01")]
    public async Task EveryReleasedMovieHasRatingsAndAFeaturedReview_AndTheOthersHaveNone()
    {
        await using var factory = Fixture.CreateFactory(SeedSettings);
        using var client = NewClient(factory);
        _ = factory.Server;

        var movies = (await Read<PagedResult<MovieListItemDto>>(await Send(HttpMethod.Get, "/api/v1/movies?pageSize=100", client: client))).Items;

        foreach (var m in movies.Where(m => m.Status == "NowShowing"))
        {
            m.ReviewCount.ShouldBeInRange(8, 20, $"{m.Title}: cantidad de opiniones");
            m.AverageRating.ShouldNotBeNull().ShouldBeInRange(1.0, 5.0);
            var featured = m.FeaturedReview.ShouldNotBeNull($"{m.Title}: falta la opinión destacada");
            featured.Comment.ShouldNotBeNullOrWhiteSpace();
            featured.AuthorName.ShouldMatch(@"^\p{L}+( \p{L}\.)?$"); // "Lucía V.": nunca el nombre completo
        }

        movies.Where(m => m.Status != "NowShowing").ShouldAllBe(m => m.ReviewCount == 0 && m.AverageRating == null && m.FeaturedReview == null);
    }

    [Fact]
    [Trait("Case", "TC-DATA-01")]
    public async Task TheCinemasHaveNameAddressLocationAndRooms_AndAreListedByCity()
    {
        await using var factory = Fixture.CreateFactory(SeedSettings);
        using var client = NewClient(factory);
        _ = factory.Server;

        var cinemas = await Read<List<CinemaDto>>(await Send(HttpMethod.Get, "/api/v1/cinemas", client: client));
        var cities = await Read<List<CityDto>>(await Send(HttpMethod.Get, "/api/v1/cities", client: client));

        // Cinestar (cadena real) todavía no está: su página bloquea la lectura automática y falta su cartelera.
        cinemas.Select(c => c.Name).ShouldBe(["Cinerama - Chimbote"]);
        cities.Single().ShouldBe(new CityDto("Chimbote", "Áncash", 1));
        foreach (var c in cinemas)
        {
            c.Address.ShouldNotBeNullOrWhiteSpace();
            c.Latitude.ShouldBeInRange(-9.5m, -8.5m);   // Chimbote
            c.Longitude.ShouldBeInRange(-79m, -78m);
            if (c.LogoUrl is not null)
                File.Exists(FileOf(c.LogoUrl)).ShouldBeTrue($"{c.Name}: el logo no existe");
        }

        // Dirección real publicada por Cinerama y ubicación real (OpenStreetMap, Av. José Pardo, Chimbote).
        var cinerama = cinemas.Single();
        cinerama.Address.ShouldBe("Av. v. raúl h. de la torre, mega plaza chimbote");
        cinerama.Latitude.ShouldBe(-9.102564m);
        cinerama.Longitude.ShouldBe(-78.557360m);
        cinerama.LogoUrl.ShouldBeNull(); // sin logo se muestra la inicial

        var withoutRooms = await Db(db => db.Cinemas.Where(c => !c.Rooms.Any()).CountAsync(), factory);
        withoutRooms.ShouldBe(0);
    }

    [Fact]
    [Trait("Case", "TC-DATA-01")]
    public async Task TheSeededReviews_AreOnePerPersonAndMovie_AfterThePremiere_AndNeverInTheFuture()
    {
        await using var factory = Fixture.CreateFactory(SeedSettings);
        _ = factory.Server;

        var reviews = await Db(db => db.Reviews.Select(r => new { r.MovieId, r.UserId, r.Rating, r.Comment, r.CreatedAt, r.UpdatedAt, r.Movie.ReleaseDate }).ToListAsync(), factory);

        reviews.Count.ShouldBeGreaterThan(80);
        reviews.GroupBy(r => (r.MovieId, r.UserId)).ShouldAllBe(g => g.Count() == 1);
        reviews.ShouldAllBe(r => r.Rating >= 1 && r.Rating <= 5);
        reviews.ShouldAllBe(r => r.CreatedAt <= DateTime.UtcNow.AddMinutes(1) && r.UpdatedAt >= r.CreatedAt);
        // Nunca una opinión anterior al estreno (con un día de margen por la zona horaria).
        reviews.ShouldAllBe(r => DateOnly.FromDateTime(r.CreatedAt) >= r.ReleaseDate.AddDays(-1));
        reviews.Where(r => r.Comment != null).ShouldAllBe(r => r.Comment!.Length <= 500);
    }

    [Fact]
    [Trait("Case", "TC-DATA-01")]
    public async Task EveryShowtimeStartsAtATimeCineramaPublishes_AndRoomsNeverOverlap()
    {
        await using var factory = Fixture.CreateFactory(SeedSettings);
        _ = factory.Server;
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");

        // Horarios publicados por la página el 7/10/2026 (hora de Lima).
        var published = new Dictionary<string, string[]>
        {
            ["verity: la sombra de un engaño"] = ["15:00", "17:15", "19:30", "21:45"],
            ["vertigo 2"] = ["16:15", "18:15", "20:15", "22:15"],
            ["amigo, por ahí no es"] = ["21:00"],
            ["la isla olvidada"] = ["15:00", "17:00"],
            ["el corazon de la bestia"] = ["16:00", "18:00", "20:00", "22:00"],
            ["avengers: endgame bonus"] = ["15:15", "18:30", "21:45"],
            ["resident evil: noche cero"] = ["15:30", "17:30", "19:15", "21:30"],
            ["coyote vs acme"] = ["19:00"],
            ["avengers: doomsday"] = ["15:00", "15:30", "16:00", "16:30", "18:00", "18:30", "19:00", "19:30", "21:00", "21:30", "22:00", "22:30"],
        };

        var showtimes = await Db(db => db.Showtimes.Select(s => new { s.Movie.Title, s.CinemaRoomId, s.StartsAt, s.EndsAt, s.Movie.ReleaseDate }).ToListAsync(), factory);

        showtimes.Select(s => s.Title.ToLowerInvariant()).Distinct().Order().ShouldBe(published.Keys.Order(), "todas las películas tienen funciones");
        foreach (var s in showtimes)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(s.StartsAt, zone);
            published[s.Title.ToLowerInvariant()].ShouldContain($"{local:HH:mm}", $"{s.Title} no se publica a las {local:HH:mm}");
            DateOnly.FromDateTime(local).ShouldBeGreaterThanOrEqualTo(s.ReleaseDate, $"{s.Title}: función antes del estreno");
        }

        // RN-13 con los horarios reales: ninguna sala tiene dos funciones a la vez.
        foreach (var room in showtimes.GroupBy(s => s.CinemaRoomId))
        {
            var ordered = room.OrderBy(s => s.StartsAt).ToList();
            for (var i = 1; i < ordered.Count; i++)
                ordered[i].StartsAt.ShouldBeGreaterThanOrEqualTo(ordered[i - 1].EndsAt);
        }
    }

    [Fact]
    [Trait("Case", "TC-DATA-01")]
    public async Task ThePreviewVideos_ExistOnDisk_AndOnlyVerityHasOne()
    {
        await using var factory = Fixture.CreateFactory(SeedSettings);
        using var client = NewClient(factory);
        _ = factory.Server;

        var movies = (await Read<PagedResult<MovieListItemDto>>(await Send(HttpMethod.Get, "/api/v1/movies?pageSize=100", client: client))).Items;
        var withVideo = new List<string>();
        foreach (var m in movies)
        {
            var detail = await Read<MovieDetailDto>(await Send(HttpMethod.Get, $"/api/v1/movies/{m.Id}", client: client));
            if (detail.PreviewVideoUrl is null)
                continue;

            withVideo.Add(m.Title);
            detail.PreviewVideoUrl.ShouldStartWith("http://localhost/media/posters/");
            detail.PreviewVideoUrl.ShouldEndWith(".mp4");
            File.Exists(FileOf(detail.PreviewVideoUrl)).ShouldBeTrue($"{m.Title}: el video no existe ({detail.PreviewVideoUrl})");
        }

        withVideo.ShouldBe(["Verity: la sombra de un engaño"]);
    }

    [Fact]
    [Trait("Case", "TC-DATA-01")]
    public async Task TheDemoAccounts_HaveARealisticDisplayName()
    {
        await using var factory = Fixture.CreateFactory(SeedSettings);
        _ = factory.Server;

        var names = await Db(db => db.Users.Where(u => u.Email.StartsWith("cliente")).Select(u => u.FullName).ToListAsync(), factory);

        names.Count.ShouldBe(20);
        names.Distinct().Count().ShouldBe(20);
        names.ShouldAllBe(n => n.Contains(' ') && !n.Contains("Demo"));
    }
}
