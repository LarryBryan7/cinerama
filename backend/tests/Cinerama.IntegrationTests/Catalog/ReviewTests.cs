using System.Net;
using Cinerama.Application.Catalog;
using Cinerama.Application.Common;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Catalog;

/// <summary>Opiniones y calificaciones: RN-43 a RN-47.</summary>
public class ReviewTests(ApiFixture fixture) : ReservationTest(fixture)
{
    private static DateOnly LocalToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Lima")));

    private static string Url(int movieId, string suffix = "") => $"/api/v1/movies/{movieId}/reviews{suffix}";

    private async Task<int> ReleasedMovieAsync(string title = "Película estrenada") =>
        await Db(async db => (await Seeds.MovieAsync(db, title)).Id);

    private Task<HttpResponseMessage> Put(TestUser user, int movieId, object body, HttpClient? client = null) =>
        Send(HttpMethod.Put, Url(movieId, "/mine"), body, user.Token, client: client);

    // ── Calificación y opinión destacada de cada tarjeta ────────────────────

    [Fact]
    [Trait("Case", "RN-45")]
    public async Task EachCard_ShowsTheRoundedAverage_AndTheNumberOfReviews()
    {
        var ids = await Db(async db =>
        {
            var movie = await Seeds.MovieAsync(db, "Con opiniones");
            await Seeds.MovieAsync(db, "Sin opiniones");
            foreach (var (rating, i) in new[] { 5, 4, 4, 5 }.Select((r, i) => (r, i)))
                await Seeds.ReviewAsync(db, movie, await Seeds.UserAsync(db, $"u{i}@example.test"), rating);
            return movie.Id;
        });

        var page = await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies"));
        var detail = await Read<MovieDetailDto>(await Get($"/api/v1/movies/{ids}"));

        var with = page.Items.Single(m => m.Title == "Con opiniones");
        with.AverageRating.ShouldBe(4.5);
        with.ReviewCount.ShouldBe(4);
        detail.AverageRating.ShouldBe(4.5);
        detail.ReviewCount.ShouldBe(4);

        var without = page.Items.Single(m => m.Title == "Sin opiniones");
        without.AverageRating.ShouldBeNull(); // no se inventa un "0"
        without.ReviewCount.ShouldBe(0);
        without.FeaturedReview.ShouldBeNull();
    }

    [Fact]
    [Trait("Case", "RN-45")]
    public async Task TheAverage_IsRoundedToOneDecimal()
    {
        await Db(async db =>
        {
            var movie = await Seeds.MovieAsync(db, "Redondeo");
            foreach (var (rating, i) in new[] { 5, 4, 4 }.Select((r, i) => (r, i))) // 13 / 3 = 4,333…
                await Seeds.ReviewAsync(db, movie, await Seeds.UserAsync(db, $"r{i}@example.test"), rating);
        });

        (await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies"))).Items.Single().AverageRating.ShouldBe(4.3);
    }

    [Fact]
    [Trait("Case", "RN-45")]
    public async Task TheFeaturedReview_IsTheNewestOneThatHasAComment_NotTheBestRated()
    {
        await Db(async db =>
        {
            var movie = await Seeds.MovieAsync(db, "Destacada");
            var now = DateTime.UtcNow;
            await Seeds.ReviewAsync(db, movie, await Seeds.UserAsync(db, "a@example.test", fullName: "Ana Gómez"), 5, "La mejor puntuada, pero vieja", now.AddDays(-5));
            await Seeds.ReviewAsync(db, movie, await Seeds.UserAsync(db, "b@example.test", fullName: "Luis Pérez Rojas"), 3, "La más reciente con comentario", now.AddHours(-1));
            await Seeds.ReviewAsync(db, movie, await Seeds.UserAsync(db, "c@example.test", fullName: "Eva Díaz"), 5, null, now); // más reciente, pero sin comentario
        });

        var featured = (await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies"))).Items.Single().FeaturedReview!;

        featured.Comment.ShouldBe("La más reciente con comentario");
        featured.AuthorName.ShouldBe("Luis R."); // nombre e inicial del último apellido
        featured.Rating.ShouldBe(3);
        featured.IsMine.ShouldBeFalse();
    }

    [Fact]
    [Trait("Case", "RN-44")]
    public async Task NoResponse_RevealsAnAuthorsFullNameOrEmail()
    {
        var movieId = await Db(async db =>
        {
            var movie = await Seeds.MovieAsync(db, "Privacidad");
            await Seeds.ReviewAsync(db, movie, await Seeds.UserAsync(db, "secreto.correo@example.test", fullName: "María Fernanda Quispe Huamán"), 4, "Muy buena");
            return movie.Id;
        });

        var bodies = new[]
        {
            await (await Get("/api/v1/movies")).Content.ReadAsStringAsync(),
            await (await Get($"/api/v1/movies/{movieId}")).Content.ReadAsStringAsync(),
            await (await Get(Url(movieId))).Content.ReadAsStringAsync(),
        };

        foreach (var body in bodies)
        {
            body.ShouldContain("María H.");
            body.ShouldNotContain("Quispe");
            body.ShouldNotContain("Fernanda");
            body.ShouldNotContain("secreto.correo");
        }
    }

    [Theory]
    [InlineData("Lucía Vargas Ríos", "Lucía R.")]
    [InlineData("david flores", "david F.")]
    [InlineData("Madonna", "Madonna")]
    [InlineData("  Ana   María  ", "Ana M.")]
    [InlineData("", "Anónimo")]
    public void PublicName_KeepsTheFirstNameAndTheLastInitialOnly(string fullName, string expected) =>
        ReviewNames.Public(fullName).ShouldBe(expected);

    // ── Leer ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheList_IsPublic_NewestFirst_Paginated_AndMarksTheCallersOwnReview()
    {
        var (movieId, ana) = (await ReleasedMovieAsync(), await NewUserAsync(fullName: "Ana Gómez"));
        await Db(async db =>
        {
            var movie = db.Movies.Single();
            var now = DateTime.UtcNow;
            for (var i = 0; i < 5; i++)
                await Seeds.ReviewAsync(db, movie, await Seeds.UserAsync(db, $"x{i}@example.test"), 4, $"Comentario {i}", now.AddHours(-i - 1));
            await Seeds.ReviewAsync(db, movie, db.Users.Single(u => u.Id == ana.Id), 5, "Mi opinión", now.AddDays(-2));
        });

        var anonymous = await Read<PagedResult<ReviewDto>>(await Get(Url(movieId) + "?pageSize=4")); // sin token
        var secondPage = await Read<PagedResult<ReviewDto>>(await Get(Url(movieId) + "?pageSize=4&page=2"));
        var asAna = await Read<PagedResult<ReviewDto>>(await Get(Url(movieId) + "?pageSize=10", ana.Token));

        anonymous.TotalCount.ShouldBe(6);
        anonymous.Items.Select(r => r.Comment).ShouldBe(["Comentario 0", "Comentario 1", "Comentario 2", "Comentario 3"]);
        secondPage.Items.Select(r => r.Comment).ShouldBe(["Comentario 4", "Mi opinión"]);
        anonymous.Items.ShouldAllBe(r => !r.IsMine);
        asAna.Items.Single(r => r.IsMine).Comment.ShouldBe("Mi opinión");
        asAna.Items.Count(r => r.IsMine).ShouldBe(1);
    }

    [Fact]
    public async Task TheList_OfAnUnknownOrInactiveMovie_Is404_AndBadPagingIs400()
    {
        var hidden = await Db(async db => (await Seeds.MovieAsync(db, "Oculta", active: false)).Id);
        var ok = await ReleasedMovieAsync();

        (await Get(Url(9999))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Get(Url(hidden))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Get(Url(ok) + "?pageSize=1000")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Get(Url(ok) + "?page=0")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ── Escribir ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "RN-43")]
    public async Task WritingRequiresASession()
    {
        var movieId = await ReleasedMovieAsync();

        (await Send(HttpMethod.Put, Url(movieId, "/mine"), new { rating = 5 })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Delete, Url(movieId, "/mine"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Case", "RN-43")]
    public async Task AnyoneWithAnAccount_CanReview_FirstTimeIs201_AndChangingItIs200WithoutDuplicates()
    {
        var movieId = await ReleasedMovieAsync();
        var ana = await NewUserAsync(fullName: "Ana Gómez");

        var first = await Put(ana, movieId, new { rating = 3, comment = "  Está bien  " });
        var second = await Put(ana, movieId, new { rating = 5, comment = "Mejor la segunda vez" });

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await Read<ReviewDto>(first);
        created.Rating.ShouldBe(3);
        created.Comment.ShouldBe("Está bien"); // se recorta
        created.AuthorName.ShouldBe("Ana G.");
        created.IsMine.ShouldBeTrue();

        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await Read<ReviewDto>(second);
        updated.Id.ShouldBe(created.Id);
        updated.CreatedAt.ShouldBe(created.CreatedAt); // la fecha original no cambia
        (await Db(db => db.Reviews.CountAsync())).ShouldBe(1);
        (await Read<MovieDetailDto>(await Get($"/api/v1/movies/{movieId}"))).AverageRating.ShouldBe(5.0);
    }

    [Fact]
    [Trait("Case", "RN-43")]
    public async Task AnAdministrator_CanAlsoReview_BecauseTheyAreAnAccount()
    {
        var movieId = await ReleasedMovieAsync();
        var admin = await NewUserAsync(admin: true);

        (await Put(admin, movieId, new { rating = 4 })).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task TheComment_IsOptional_AndABlankOneIsStoredAsNothing()
    {
        var movieId = await ReleasedMovieAsync();
        var ana = await NewUserAsync();

        var response = await Put(ana, movieId, new { rating = 4, comment = "   " });

        (await Read<ReviewDto>(response)).Comment.ShouldBeNull();
        (await Db(db => db.Reviews.SingleAsync())).Comment.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(100)]
    public async Task ARatingOutsideOneToFive_Is400(int rating)
    {
        var movieId = await ReleasedMovieAsync();
        var response = await Put(await NewUserAsync(), movieId, new { rating });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Errors!.ShouldContainKey("rating");
        (await Db(db => db.Reviews.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task InvalidComments_Are400_ATooLongOneAndOneWithControlCharacters()
    {
        var movieId = await ReleasedMovieAsync();
        var ana = await NewUserAsync();

        (await Put(ana, movieId, new { rating = 4, comment = new string('x', 501) })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Put(ana, movieId, new { rating = 4, comment = "hola\u0000mundo" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Put(ana, movieId, new { rating = 4, comment = new string('x', 500) })).StatusCode.ShouldBe(HttpStatusCode.Created); // el límite exacto sí
        (await Put(ana, movieId, new { rating = 4, comment = "Dos líneas\ncon salto" })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ACommentWithMarkup_IsStoredAsPlainText_NeverInterpreted()
    {
        var movieId = await ReleasedMovieAsync();
        var payload = "<script>alert('x')</script> & más";

        var response = await Put(await NewUserAsync(), movieId, new { rating = 4, comment = payload });

        (await Read<ReviewDto>(response)).Comment.ShouldBe(payload); // la API entrega texto; cada cliente lo muestra como texto
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
    }

    [Fact]
    [Trait("Case", "RN-46")]
    public async Task OnlyAMovieThatAlreadyPremiered_CanBeReviewed()
    {
        var today = LocalToday();
        var (comingSoon, presale, premieresToday) = await Db(async db => (
            (await Seeds.MovieWithReleaseAsync(db, "Próximamente", today.AddDays(30))).Id,
            (await Seeds.MovieWithReleaseAsync(db, "Preventa", today.AddDays(3), today.AddDays(-1))).Id,
            (await Seeds.MovieWithReleaseAsync(db, "Estrena hoy", today)).Id));
        var ana = await NewUserAsync();

        foreach (var id in new[] { comingSoon, presale })
        {
            var response = await Put(ana, id, new { rating = 5 });
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await ReadProblem(response)).Code.ShouldBe("movie_not_released");
        }

        (await Put(ana, premieresToday, new { rating = 5 })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Db(db => db.Reviews.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task ReviewingAnUnknownOrInactiveMovie_Is404()
    {
        var hidden = await Db(async db => (await Seeds.MovieAsync(db, "Oculta", active: false)).Id);
        var ana = await NewUserAsync();

        (await Put(ana, 9999, new { rating = 5 })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Put(ana, hidden, new { rating = 5 })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ── Seguridad: nadie opina ni borra "como" otra persona ─────────────────

    [Fact]
    [Trait("Case", "TC-SEC-14")]
    public async Task TheAuthorIsAlwaysTheCaller_AUserIdInTheBodyIsIgnored()
    {
        var movieId = await ReleasedMovieAsync();
        var (ana, luis) = (await NewUserAsync(fullName: "Ana Gómez"), await NewUserAsync(fullName: "Luis Pérez"));

        await Put(luis, movieId, new { rating = 5, comment = "Opinión de Luis" });
        var forged = await Put(ana, movieId, new { rating = 1, comment = "Intento suplantar", userId = luis.Id, authorName = "Luis P." });

        forged.StatusCode.ShouldBe(HttpStatusCode.Created); // se creó la de Ana, no se tocó la de Luis
        var rows = await Db(db => db.Reviews.OrderBy(r => r.UserId).ToListAsync());
        rows.Single(r => r.UserId == luis.Id).Comment.ShouldBe("Opinión de Luis");
        rows.Single(r => r.UserId == ana.Id).Comment.ShouldBe("Intento suplantar");
    }

    [Fact]
    [Trait("Case", "TC-SEC-14")]
    public async Task DeleteOnlyEverTouchesTheCallersOwnReview()
    {
        var movieId = await ReleasedMovieAsync();
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
        await Put(luis, movieId, new { rating = 5, comment = "De Luis" });

        var attempt = await Send(HttpMethod.Delete, Url(movieId, "/mine"), bearer: ana.Token); // Ana no tiene opinión propia
        var own = await Send(HttpMethod.Delete, Url(movieId, "/mine"), bearer: luis.Token);
        var again = await Send(HttpMethod.Delete, Url(movieId, "/mine"), bearer: luis.Token);

        attempt.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadProblem(attempt)).Code.ShouldBe("review_not_found");
        own.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Db(db => db.Reviews.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task DeletingAReview_RemovesItFromTheAverage()
    {
        var movieId = await ReleasedMovieAsync();
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
        await Put(ana, movieId, new { rating = 5 });
        await Put(luis, movieId, new { rating = 1 });
        (await Read<MovieDetailDto>(await Get($"/api/v1/movies/{movieId}"))).AverageRating.ShouldBe(3.0);

        await Send(HttpMethod.Delete, Url(movieId, "/mine"), bearer: luis.Token);

        var detail = await Read<MovieDetailDto>(await Get($"/api/v1/movies/{movieId}"));
        detail.AverageRating.ShouldBe(5.0);
        detail.ReviewCount.ShouldBe(1);
    }

    // ── Concurrencia y límites ──────────────────────────────────────────────

    [Fact]
    [Trait("Case", "RN-43")]
    public async Task EightSimultaneousFirstReviewsFromTheSamePerson_LeaveExactlyOne_AndNoneFails()
    {
        var movieId = await ReleasedMovieAsync();
        var ana = await NewUserAsync();

        var responses = await AtTheSameTime(Enumerable.Range(1, 8).Select<int, Func<Task<HttpResponseMessage>>>(
            i => () => Put(ana, movieId, new { rating = (i % 5) + 1, comment = $"Intento {i}" })));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.OK);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1); // solo una la creó; las demás la actualizaron
        (await Db(db => db.Reviews.CountAsync())).ShouldBe(1);
    }

    [Fact]
    [Trait("Case", "RN-47")]
    public async Task WritingIsRateLimitedPerUser_NotPerIp_AndReadingIsNot()
    {
        await using var factory = Fixture.CreateFactory(new() { ["RateLimiting:ReviewsPermitPerMinute"] = "3" });
        using var client = NewClient(factory);
        var movieId = await ReleasedMovieAsync();
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());

        for (var i = 1; i <= 3; i++)
            (await Put(ana, movieId, new { rating = 4, comment = $"v{i}" }, client)).IsSuccessStatusCode.ShouldBeTrue();

        var limited = await Put(ana, movieId, new { rating = 4 }, client);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await ReadProblem(limited)).Code.ShouldBe("rate_limited");

        (await Put(luis, movieId, new { rating = 2 }, client)).IsSuccessStatusCode.ShouldBeTrue(); // mismo IP, otro cupo
        (await Send(HttpMethod.Get, Url(movieId), client: client)).StatusCode.ShouldBe(HttpStatusCode.OK); // leer no cuenta
    }
}
