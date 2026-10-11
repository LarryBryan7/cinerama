using System.Net;
using Cinerama.Application.Admin;
using Cinerama.Application.Catalog;
using Cinerama.Application.Common;
using Cinerama.Domain.Enums;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Admin;

/// <summary>Géneros, películas y cines desde el panel (RF-CAT-03, RF-CAT-04, RN-07 a RN-09, RN-48, RN-50). Todo exige ser administrador.</summary>
public class CatalogAdminTests(ApiFixture fixture) : ReservationTest(fixture)
{
    private static readonly DateOnly Release = new(2026, 12, 25);

    private async Task<string> AdminAsync() => (await NewUserAsync(admin: true)).Token;

    private static object MovieBody(
        string title = "Película nueva", int minutes = 110, int[]? genreIds = null, string? poster = "posters/pelicula-nueva.jpg",
        string? trailer = "https://youtu.be/1McLG8FgNaI", string? video = null, DateOnly? release = null, DateOnly? preSale = null, bool isActive = true) =>
        new
        {
            title, synopsis = "Una sinopsis.", durationMinutes = minutes, classification = "+14", genreIds = genreIds ?? [],
            posterUrl = poster, trailerUrl = trailer, previewVideoUrl = video, releaseDate = release ?? Release, preSaleStartsOn = preSale, isActive,
        };

    private async Task<int> GenreIdAsync(string name = "Drama") => await Db(async db => (await Seeds.GenreAsync(db, name)).Id);

    // ── Seguridad ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("POST", "/api/v1/genres")]
    [InlineData("PUT", "/api/v1/genres/1")]
    [InlineData("DELETE", "/api/v1/genres/1")]
    [InlineData("GET", "/api/v1/admin/genres")]
    [InlineData("POST", "/api/v1/movies")]
    [InlineData("PUT", "/api/v1/movies/1")]
    [InlineData("DELETE", "/api/v1/movies/1")]
    [InlineData("GET", "/api/v1/admin/movies")]
    [InlineData("GET", "/api/v1/admin/movies/1")]
    [InlineData("POST", "/api/v1/cinemas")]
    [InlineData("PUT", "/api/v1/cinemas/1")]
    [InlineData("GET", "/api/v1/admin/cinemas")]
    [Trait("Case", "TC-ADM-03")]
    public async Task EveryAdminEndpoint_Rejects401WithoutAToken_And403ToACustomer(string method, string url)
    {
        var anonymous = await Send(new HttpMethod(method), url, method is "POST" or "PUT" ? new { } : null);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var customer = await NewUserAsync();
        var forbidden = await Send(new HttpMethod(method), url, method is "POST" or "PUT" ? new { } : null, customer.Token);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ── Géneros ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Genres_AreCreatedRenamedAndDeleted_AndTheListShowsHowManyMoviesUseEach()
    {
        var token = await AdminAsync();

        var created = await Send(HttpMethod.Post, "/api/v1/genres", new { name = "  Terror  " }, token);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var genre = await Read<AdminGenreDto>(created);
        genre.Name.ShouldBe("Terror", "se recorta");

        (await Send(HttpMethod.Put, $"/api/v1/genres/{genre.Id}", new { name = "Horror" }, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Read<List<AdminGenreDto>>(await Get("/api/v1/admin/genres", token))).Single().Name.ShouldBe("Horror");
        (await Read<List<GenreDto>>(await Get("/api/v1/genres"))).Select(g => g.Name).ShouldBe(["Horror"], "la lectura pública ve el cambio");

        (await Send(HttpMethod.Delete, $"/api/v1/genres/{genre.Id}", null, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Delete, $"/api/v1/genres/{genre.Id}", null, token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AGenreNeedsAName(string name)
    {
        var response = await Send(HttpMethod.Post, "/api/v1/genres", new { name }, await AdminAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Errors!.ShouldContainKey("name");
    }

    [Fact]
    public async Task AGenreNameCannotBeLongerThan50Characters()
    {
        (await Send(HttpMethod.Post, "/api/v1/genres", new { name = new string('a', 51) }, await AdminAsync())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, "/api/v1/genres", new { name = new string('a', 50) }, await AdminAsync())).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ARepeatedGenreName_IsAConflict_IgnoringCase_OnCreateAndOnRename()
    {
        var token = await AdminAsync();
        await Db(async db => { await Seeds.GenreAsync(db, "Drama"); await Seeds.GenreAsync(db, "Comedia"); });

        var create = await Send(HttpMethod.Post, "/api/v1/genres", new { name = "drama" }, token);
        create.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(create)).Code.ShouldBe("genre_name_taken");

        var comedia = await Db(db => db.Genres.Where(g => g.Name == "Comedia").Select(g => g.Id).SingleAsync());
        var rename = await Send(HttpMethod.Put, $"/api/v1/genres/{comedia}", new { name = "DRAMA" }, token);
        rename.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Guardar un género con su propio nombre (por ejemplo, solo cambiando las mayúsculas) no es un conflicto.
        (await Send(HttpMethod.Put, $"/api/v1/genres/{comedia}", new { name = "Comedia" }, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AGenreUsedByAMovie_CannotBeDeleted()
    {
        var token = await AdminAsync();
        var genre = await GenreIdAsync();
        await Db(async db => await Seeds.MovieAsync(db, "Con género", true, 100, await db.Genres.SingleAsync()));

        var response = await Send(HttpMethod.Delete, $"/api/v1/genres/{genre}", null, token);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(response)).Code.ShouldBe("genre_in_use");
        (await Db(db => db.Genres.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task EditingOrDeletingAGenreThatDoesNotExist_Is404()
    {
        var token = await AdminAsync();

        (await Send(HttpMethod.Put, "/api/v1/genres/999", new { name = "X" }, token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Delete, "/api/v1/genres/999", null, token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ── Películas ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "TC-ADM-04")]
    public async Task AMovieIsCreated_WithItsGenres_ANormalizedTrailer_AndShowsUpInThePublicList()
    {
        var token = await AdminAsync();
        var drama = await GenreIdAsync("Drama");
        var comedia = await GenreIdAsync("Comedia");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Lima")));

        var response = await Send(HttpMethod.Post, "/api/v1/movies",
            MovieBody("Estreno de hoy", genreIds: [drama, comedia], trailer: "https://www.youtube.com/watch?v=1McLG8FgNaI&t=30s", release: today.AddDays(-1)), token);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        response.Headers.Location!.ToString().ShouldBe($"/api/v1/movies/{(await Read<AdminMovieDto>(response)).Id}");
        var movie = await Read<AdminMovieDto>(await Get($"/api/v1/admin/movies/{(await Read<AdminMovieDto>(response)).Id}", token));
        movie.Genres.ShouldBe(["Comedia", "Drama"]);
        movie.TrailerUrl.ShouldBe("https://www.youtube.com/watch?v=1McLG8FgNaI", "se guarda solo el código, sin '&t=30s' ni otros adornos");
        movie.IsActive.ShouldBeTrue();
        movie.Status.ShouldBe("NowShowing");

        var publicList = await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies"));
        publicList.Items.Select(m => m.Title).ShouldContain("Estreno de hoy");
    }

    [Theory]
    [InlineData("", 100, "title")]
    [InlineData("   ", 100, "title")]
    [InlineData("a", 0, "durationMinutes")]
    [InlineData("a", 401, "durationMinutes")]
    [InlineData("a", -5, "durationMinutes")]
    public async Task AMovieNeedsATitleAndADurationBetween1And400Minutes(string title, int minutes, string field)
    {
        var genre = await GenreIdAsync();

        var response = await Send(HttpMethod.Post, "/api/v1/movies", MovieBody(title, minutes, [genre]), await AdminAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Errors!.ShouldContainKey(field);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(400)]
    public async Task TheDurationLimitsThemselvesAreAccepted(int minutes) =>
        (await Send(HttpMethod.Post, "/api/v1/movies", MovieBody($"Duración {minutes}", minutes, [await GenreIdAsync()]), await AdminAsync()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

    [Fact]
    public async Task ATitleOf200CharactersIsAccepted_And201Rejected()
    {
        var genre = await GenreIdAsync();
        var token = await AdminAsync();

        (await Send(HttpMethod.Post, "/api/v1/movies", MovieBody(new string('t', 201), genreIds: [genre]), token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, "/api/v1/movies", MovieBody(new string('t', 200), genreIds: [genre]), token)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AMovieNeedsAtLeastOneGenre_ThatExists_WithoutRepeats()
    {
        var token = await AdminAsync();
        var genre = await GenreIdAsync();

        var none = await Send(HttpMethod.Post, "/api/v1/movies", MovieBody(genreIds: []), token);
        none.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(none)).Errors!.ShouldContainKey("genreIds");

        var unknown = await Send(HttpMethod.Post, "/api/v1/movies", MovieBody(genreIds: [genre, 9999]), token);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(unknown)).Errors!.ShouldContainKey("genreIds");

        (await Send(HttpMethod.Post, "/api/v1/movies", MovieBody(genreIds: [genre, genre]), token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Db(db => db.Movies.CountAsync())).ShouldBe(0, "nada quedó a medias");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://insegura.example/p.jpg")]
    [InlineData("../secreto.jpg")]
    [InlineData("posters/../../appsettings.json")]
    [InlineData("posters//p.jpg")]
    [InlineData("posters\\p.jpg")]
    [InlineData("/posters/p.jpg")]
    [InlineData("posters/p.gif")]
    [InlineData("posters/p")]
    [InlineData("https://ejemplo.test/archivo.exe")]
    public async Task APosterThatIsNotARelativeImageOrAnHttpsUrl_IsRejected(string poster)
    {
        var response = await Send(HttpMethod.Post, "/api/v1/movies", MovieBody(poster: poster, genreIds: [await GenreIdAsync()]), await AdminAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Errors!.ShouldContainKey("posterUrl");
    }

    [Theory]
    [InlineData("posters/marea-alta.jpg")]
    [InlineData("posters/Marea_Alta-2.PNG")]
    [InlineData("https://imagenes.example.test/p.webp")]
    public async Task ValidPosters_AreAccepted(string poster) =>
        (await Send(HttpMethod.Post, "/api/v1/movies", MovieBody(poster: poster, genreIds: [await GenreIdAsync()]), await AdminAsync()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

    [Theory]
    [InlineData("https://vimeo.com/123456")]
    [InlineData("https://www.youtube.com/watch?v=e")]
    [InlineData("e")]
    [InlineData("texto cualquiera")]
    public async Task ATrailerThatIsNotAValidYouTubeLink_IsRejected(string trailer)
    {
        var response = await Send(HttpMethod.Post, "/api/v1/movies", MovieBody(trailer: trailer, genreIds: [await GenreIdAsync()]), await AdminAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Errors!.ShouldContainKey("trailerUrl");
    }

    [Fact]
    public async Task ThePreviewVideoMustBeAnMp4()
    {
        var genre = await GenreIdAsync();
        var token = await AdminAsync();

        (await Send(HttpMethod.Post, "/api/v1/movies", MovieBody(video: "posters/p.mov", genreIds: [genre]), token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, "/api/v1/movies", MovieBody("Con video", video: "posters/con-video.mp4", genreIds: [genre]), token)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ThePreSaleCannotOpenAfterTheRelease_AndMayOpenTheSameDay()
    {
        var genre = await GenreIdAsync();
        var token = await AdminAsync();

        var after = await Send(HttpMethod.Post, "/api/v1/movies", MovieBody(genreIds: [genre], release: Release, preSale: Release.AddDays(1)), token);
        after.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(after)).Errors!.ShouldContainKey("preSaleStartsOn");

        (await Send(HttpMethod.Post, "/api/v1/movies", MovieBody("Mismo día", genreIds: [genre], release: Release, preSale: Release), token)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task TheAdminListShowsInactiveMoviesToo_AndFiltersBySearchActivityAndStatus()
    {
        var token = await AdminAsync();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Lima")));
        await Db(async db =>
        {
            await Seeds.MovieWithReleaseAsync(db, "Vigente", today.AddDays(-5));
            await Seeds.MovieWithReleaseAsync(db, "De baja", today.AddDays(-5), active: false);
            await Seeds.MovieWithReleaseAsync(db, "En preventa", today.AddDays(10), today.AddDays(-1));
            await Seeds.MovieWithReleaseAsync(db, "Muy pronto", today.AddDays(60));
        });

        async Task<string> Titles(string query) =>
            string.Join(",", (await Read<PagedResult<AdminMovieListItemDto>>(await Get($"/api/v1/admin/movies{query}", token))).Items.Select(m => m.Title));

        (await Titles("")).ShouldBe("De baja,En preventa,Muy pronto,Vigente");
        (await Titles("?isActive=false")).ShouldBe("De baja");
        (await Titles("?isActive=true&status=nowShowing")).ShouldBe("Vigente");
        (await Titles("?status=presale")).ShouldBe("En preventa");
        (await Titles("?status=comingSoon")).ShouldBe("Muy pronto");
        (await Titles("?search=baja")).ShouldBe("De baja");
        (await Get("/api/v1/admin/movies?status=nada", token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Get("/api/v1/admin/movies?pageSize=101", token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AMovieIsEditedKeepingItsId_AndItsGenresAreReplaced()
    {
        var token = await AdminAsync();
        var drama = await GenreIdAsync("Drama");
        var terror = await GenreIdAsync("Terror");
        var created = await Read<AdminMovieDto>(await Send(HttpMethod.Post, "/api/v1/movies", MovieBody(genreIds: [drama]), token));

        var response = await Send(HttpMethod.Put, $"/api/v1/movies/{created.Id}", MovieBody("Título corregido", 95, [terror], poster: null, trailer: null), token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var edited = await Read<AdminMovieDto>(response);
        (edited.Id, edited.Title, edited.DurationMinutes).ShouldBe((created.Id, "Título corregido", 95));
        edited.Genres.ShouldBe(["Terror"]);
        edited.PosterUrl.ShouldBeNull();
        edited.TrailerUrl.ShouldBeNull();
        (await Send(HttpMethod.Put, "/api/v1/movies/9999", MovieBody(genreIds: [terror]), token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Case", "TC-ADM-04")]
    public async Task DeletingAMovie_IsALogicalDeactivation_ItDisappearsFromThePublicCatalogButKeepsItsHistory()
    {
        var token = await AdminAsync();
        var movie = await Read<AdminMovieDto>(await Send(HttpMethod.Post, "/api/v1/movies", MovieBody(genreIds: [await GenreIdAsync()]), token));

        (await Send(HttpMethod.Delete, $"/api/v1/movies/{movie.Id}", null, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Delete, $"/api/v1/movies/{movie.Id}", null, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent); // repetirlo no es un error

        (await Get($"/api/v1/movies/{movie.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies"))).Items.ShouldBeEmpty();
        (await Db(db => db.Movies.CountAsync())).ShouldBe(1, "la fila sigue en la base: es una baja lógica");
        (await Read<AdminMovieDto>(await Get($"/api/v1/admin/movies/{movie.Id}", token))).IsActive.ShouldBeFalse();
        (await Send(HttpMethod.Delete, "/api/v1/movies/9999", null, token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ADeactivatedMovieCanBeReactivated()
    {
        var token = await AdminAsync();
        var genre = await GenreIdAsync();
        var movie = await Read<AdminMovieDto>(await Send(HttpMethod.Post, "/api/v1/movies", MovieBody(genreIds: [genre], release: new DateOnly(2020, 1, 1)), token));
        await Send(HttpMethod.Delete, $"/api/v1/movies/{movie.Id}", null, token);

        var response = await Send(HttpMethod.Put, $"/api/v1/movies/{movie.Id}", MovieBody(genreIds: [genre], release: new DateOnly(2020, 1, 1), isActive: true), token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Get($"/api/v1/movies/{movie.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Case", "TC-ADM-04")]
    public async Task ADeactivationIsRejectedWhileAFutureShowtimeHasConfirmedBookings_AndNothingChanges()
    {
        var token = await AdminAsync();
        var showtime = await NewShowtimeAsync();
        var customer = await NewUserAsync();
        await BookedAsync(customer, showtime, "A1");
        var movieId = await Db(db => db.Showtimes.Where(s => s.Id == showtime.Id).Select(s => s.MovieId).SingleAsync());

        var byDelete = await Send(HttpMethod.Delete, $"/api/v1/movies/{movieId}", null, token);
        byDelete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(byDelete)).Code.ShouldBe("movie_has_active_bookings");

        var current = await Read<AdminMovieDto>(await Get($"/api/v1/admin/movies/{movieId}", token));
        var byEdit = await Send(HttpMethod.Put, $"/api/v1/movies/{movieId}", MovieBody(current.Title, genreIds: [await GenreIdAsync()], isActive: false), token);
        byEdit.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await Db(db => db.Movies.Where(m => m.Id == movieId).Select(m => m.IsActive).SingleAsync())).ShouldBeTrue();
        (await Db(db => db.Showtimes.Where(s => s.Id == showtime.Id).Select(s => s.Status).SingleAsync())).ShouldBe(ShowtimeStatus.Scheduled);
        (await Db(db => db.Bookings.CountAsync(b => b.Status == BookingStatus.Confirmed))).ShouldBe(1);
    }

    [Fact]
    public async Task ADeactivationCancelsTheFutureShowtimesThatHadNoSales_AndFreesTheirLocks()
    {
        var token = await AdminAsync();
        var showtime = await NewShowtimeAsync();
        var customer = await NewUserAsync();
        (await LockSeats(customer, showtime, ["A1", "A2"])).StatusCode.ShouldBe(HttpStatusCode.OK);
        var movieId = await Db(db => db.Showtimes.Where(s => s.Id == showtime.Id).Select(s => s.MovieId).SingleAsync());

        (await Send(HttpMethod.Delete, $"/api/v1/movies/{movieId}", null, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Db(db => db.Showtimes.Where(s => s.Id == showtime.Id).Select(s => s.Status).SingleAsync())).ShouldBe(ShowtimeStatus.Cancelled);
        (await Db(db => db.ShowSeats.CountAsync(s => s.ShowtimeId == showtime.Id && s.Status != ShowSeatStatus.Available))).ShouldBe(0);
        Fixture.Notifier.Events.Count.ShouldBeGreaterThan(0, "se avisó a los clientes conectados");
    }

    [Fact]
    public async Task APastShowtimeWithBookingsDoesNotBlockTheDeactivation()
    {
        var token = await AdminAsync();
        var showtime = await NewShowtimeAsync(startsAtUtc: DateTime.UtcNow.AddDays(-2));
        var customer = await NewUserAsync();
        await Db(async db =>
        {
            var seat = await db.ShowSeats.Where(s => s.ShowtimeId == showtime.Id).Include(s => s.Seat).FirstAsync();
            seat.Status = ShowSeatStatus.Booked;
            var booking = new Cinerama.Domain.Entities.Booking { Code = "PASADA01", UserId = customer.Id, ShowtimeId = showtime.Id, TotalAmount = 10, CreatedAt = DateTime.UtcNow.AddDays(-3) };
            booking.Seats.Add(new Cinerama.Domain.Entities.BookingSeat { ShowSeatId = seat.Id, Price = 10 });
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
        });
        var movieId = await Db(db => db.Showtimes.Where(s => s.Id == showtime.Id).Select(s => s.MovieId).SingleAsync());

        (await Send(HttpMethod.Delete, $"/api/v1/movies/{movieId}", null, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // ── Cines ───────────────────────────────────────────────────────────────

    private static object CinemaBody(string name = "Cinerama Centro", double lat = -9.0745, double lon = -78.5936, string? logo = null, bool isActive = true) =>
        new { name, address = "Av. Principal 123", city = "Chimbote", region = "Áncash", latitude = lat, longitude = lon, logoUrl = logo, isActive };

    [Fact]
    public async Task ACinemaIsCreatedAndEdited_AndItsCityAppearsInThePublicList()
    {
        var token = await AdminAsync();

        var created = await Send(HttpMethod.Post, "/api/v1/cinemas", CinemaBody(), token);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var cinema = await Read<AdminCinemaDto>(created);
        (cinema.Name, cinema.City, cinema.IsActive, cinema.RoomCount).ShouldBe(("Cinerama Centro", "Chimbote", true, 0));

        (await Read<List<CityDto>>(await Get("/api/v1/cities"))).Single().City.ShouldBe("Chimbote");

        var edited = await Send(HttpMethod.Put, $"/api/v1/cinemas/{cinema.Id}", CinemaBody("Cinerama Centro Plaza", -9.1, -78.5), token);
        edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Read<AdminCinemaDto>(edited)).Name.ShouldBe("Cinerama Centro Plaza");
        (await Send(HttpMethod.Put, "/api/v1/cinemas/9999", CinemaBody(), token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(90.1, 0)]
    [InlineData(-90.1, 0)]
    [InlineData(0, 180.1)]
    [InlineData(0, -180.1)]
    public async Task TheCoordinatesOfACinemaMustBeOnTheMap(double lat, double lon)
    {
        var response = await Send(HttpMethod.Post, "/api/v1/cinemas", CinemaBody(lat: lat, lon: lon), await AdminAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await ReadProblem(response)).Errors!;
        (errors.ContainsKey("latitude") || errors.ContainsKey("longitude")).ShouldBeTrue();
    }

    [Fact]
    public async Task TheCoordinatesAtTheEdgesAreAccepted() =>
        (await Send(HttpMethod.Post, "/api/v1/cinemas", CinemaBody(lat: -90, lon: 180), await AdminAsync())).StatusCode.ShouldBe(HttpStatusCode.Created);

    [Fact]
    public async Task ACinemaNeedsItsData_AndItsNameIsUnique()
    {
        var token = await AdminAsync();
        (await Send(HttpMethod.Post, "/api/v1/cinemas", new { name = "", address = "", city = "", region = "", latitude = 0, longitude = 0 }, token))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Send(HttpMethod.Post, "/api/v1/cinemas", CinemaBody(), token)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var repeated = await Send(HttpMethod.Post, "/api/v1/cinemas", CinemaBody(), token);
        repeated.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(repeated)).Code.ShouldBe("cinema_name_taken");
    }

    [Fact]
    public async Task ADeactivatedCinemaDropsOutOfTheCities_AndItsFutureShowtimesAreCancelled()
    {
        var token = await AdminAsync();
        var showtime = await NewShowtimeAsync();
        var cinemaId = await Db(db => db.Showtimes.Where(s => s.Id == showtime.Id).Select(s => s.CinemaRoom.CinemaId).SingleAsync());
        var cinema = await Read<AdminCinemaDto>(await Get($"/api/v1/admin/cinemas/{cinemaId}", token));

        var response = await Send(HttpMethod.Put, $"/api/v1/cinemas/{cinemaId}", CinemaBody(cinema.Name, isActive: false), token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await Read<List<CityDto>>(await Get("/api/v1/cities"))).ShouldBeEmpty();
        (await Db(db => db.Showtimes.Where(s => s.Id == showtime.Id).Select(s => s.Status).SingleAsync())).ShouldBe(ShowtimeStatus.Cancelled);
    }

    [Fact]
    public async Task ACinemaWithConfirmedFutureBookingsCannotBeDeactivated()
    {
        var token = await AdminAsync();
        var showtime = await NewShowtimeAsync();
        await BookedAsync(await NewUserAsync(), showtime, "A1");
        var cinemaId = await Db(db => db.Showtimes.Where(s => s.Id == showtime.Id).Select(s => s.CinemaRoom.CinemaId).SingleAsync());
        var cinema = await Read<AdminCinemaDto>(await Get($"/api/v1/admin/cinemas/{cinemaId}", token));

        var response = await Send(HttpMethod.Put, $"/api/v1/cinemas/{cinemaId}", CinemaBody(cinema.Name, isActive: false), token);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(response)).Code.ShouldBe("cinema_has_active_bookings");
        (await Db(db => db.Cinemas.Where(c => c.Id == cinemaId).Select(c => c.IsActive).SingleAsync())).ShouldBeTrue();
    }
}
