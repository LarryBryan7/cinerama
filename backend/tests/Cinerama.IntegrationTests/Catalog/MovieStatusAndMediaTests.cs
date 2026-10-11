using System.Net;
using Cinerama.Application.Catalog;
using Cinerama.Application.Common;
using Cinerama.IntegrationTests.Infrastructure;

namespace Cinerama.IntegrationTests.Catalog;

/// <summary>Estados de estreno (RN-41), imágenes, sinopsis y cines de cada tarjeta de la cartelera.</summary>
public class MovieStatusAndMediaTests(ApiFixture fixture) : ReservationTest(fixture)
{
    private static DateOnly LocalToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Lima")));

    private static string Titles(PagedResult<MovieListItemDto> page) => string.Join(", ", page.Items.Select(m => m.Title));

    private async Task SeedThreeStatesAsync()
    {
        var today = LocalToday();
        await Db(async db =>
        {
            await Seeds.MovieWithReleaseAsync(db, "A Estrenada", today.AddDays(-10));
            await Seeds.MovieWithReleaseAsync(db, "B Estrena Hoy", today);
            await Seeds.MovieWithReleaseAsync(db, "C Preventa Lejana", today.AddDays(9), preSaleStartsOn: today.AddDays(-1));
            await Seeds.MovieWithReleaseAsync(db, "D Preventa Cercana", today.AddDays(3), preSaleStartsOn: today.AddDays(-5));
            await Seeds.MovieWithReleaseAsync(db, "E Próximamente Sin Preventa", today.AddDays(30));
            await Seeds.MovieWithReleaseAsync(db, "F Próximamente Preventa Futura", today.AddDays(20), preSaleStartsOn: today.AddDays(10));
        });
    }

    [Fact]
    [Trait("Case", "RN-41")]
    public async Task EachFilter_ReturnsOnlyItsOwnState_AndEveryMovieReportsItsStatus()
    {
        await SeedThreeStatesAsync();

        var now = await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies?status=nowShowing"));
        var presale = await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies?status=presale"));
        var soon = await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies?status=comingSoon"));
        var all = await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies"));

        now.Items.Select(m => m.Title).ShouldBe(["A Estrenada", "B Estrena Hoy"]); // estrenarse "hoy" ya es cartelera
        now.Items.ShouldAllBe(m => m.Status == "NowShowing");
        presale.Items.Select(m => m.Title).ShouldBe(["D Preventa Cercana", "C Preventa Lejana"]); // la que se estrena antes, primero
        presale.Items.ShouldAllBe(m => m.Status == "PreSale");
        soon.Items.Select(m => m.Title).ShouldBe(["F Próximamente Preventa Futura", "E Próximamente Sin Preventa"]);
        soon.Items.ShouldAllBe(m => m.Status == "ComingSoon");
        all.TotalCount.ShouldBe(6); // sin filtro, todas
    }

    [Fact]
    [Trait("Case", "RN-41")]
    public async Task TheStatusFilter_IsCaseInsensitive_AndAnInvalidOneIs400()
    {
        await SeedThreeStatesAsync();

        (await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies?status=PRESALE"))).TotalCount.ShouldBe(2);

        foreach (var bad in new[] { "estrenos", "7", "-1", "nowShowing,presale" })
        {
            var response = await Get($"/api/v1/movies?status={bad}");
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, bad);
            (await ReadProblem(response)).Errors!.ShouldContainKey("status");
        }
    }

    [Fact]
    [Trait("Case", "RN-41")]
    public async Task TheDetail_ReportsDatesAndStatus()
    {
        var today = LocalToday();
        var id = await Db(async db => (await Seeds.MovieWithReleaseAsync(db, "En Preventa", today.AddDays(4), today.AddDays(-2))).Id);

        var detail = await Read<MovieDetailDto>(await Get($"/api/v1/movies/{id}"));

        detail.Status.ShouldBe("PreSale");
        detail.ReleaseDate.ShouldBe(today.AddDays(4));
        detail.PreSaleStartsOn.ShouldBe(today.AddDays(-2));
    }

    [Fact]
    public async Task TheCartelera_CarriesTheSynopsis_ToEveryCard()
    {
        await Db(async db => await Seeds.MovieAsync(db, "Con sinopsis")); // la sinopsis ficticia del helper

        var page = await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies"));

        page.Items.Single().Synopsis.ShouldBe("Sinopsis ficticia.");
    }

    [Fact]
    [Trait("Case", "RN-48")]
    public async Task Images_AreAlwaysAbsoluteUrls_AndAMissingImageIsNull()
    {
        await Db(async db =>
        {
            var relative = await Seeds.MovieAsync(db, "Imagen relativa");
            relative.PosterUrl = "posters/vertigo-2.jpg";
            relative.TrailerUrl = "https://videos.example.test/trailer.mp4";
            await Seeds.MovieAsync(db, "Sin imagen");
            var absolute = await Seeds.MovieAsync(db, "Imagen absoluta");
            absolute.PosterUrl = "https://imagenes.example.test/p.jpg";
            await db.SaveChangesAsync();
        });

        var page = await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies"));
        var byTitle = page.Items.ToDictionary(m => m.Title);

        byTitle["Imagen relativa"].PosterUrl.ShouldBe("http://localhost/media/posters/vertigo-2.jpg"); // se arma con la dirección de la solicitud
        byTitle["Imagen relativa"].TrailerUrl.ShouldBe("https://videos.example.test/trailer.mp4");
        byTitle["Imagen absoluta"].PosterUrl.ShouldBe("https://imagenes.example.test/p.jpg");
        byTitle["Sin imagen"].PosterUrl.ShouldBeNull();
        byTitle["Sin imagen"].TrailerUrl.ShouldBeNull();
    }

    [Fact]
    [Trait("Case", "RN-48")]
    public async Task TheConfiguredPublicBaseUrl_WinsOverTheRequestHost()
    {
        await Db(async db =>
        {
            var movie = await Seeds.MovieAsync(db, "Una");
            movie.PosterUrl = "posters/x.jpg";
            await db.SaveChangesAsync();
        });
        await using var factory = Fixture.CreateFactory(new() { ["Media:PublicBaseUrl"] = "https://api.cinerama.example/" });
        using var client = NewClient(factory);

        var page = await Read<PagedResult<MovieListItemDto>>(await Send(HttpMethod.Get, "/api/v1/movies", client: client));

        page.Items.Single().PosterUrl.ShouldBe("https://api.cinerama.example/media/posters/x.jpg");
    }

    [Fact]
    public void Startup_WithARelativePublicBaseUrl_Fails()
    {
        using var factory = Fixture.CreateFactory(new() { ["Media:PublicBaseUrl"] = "no-es-una-url" });

        Should.Throw<Exception>(() => _ = factory.Server);
    }

    [Fact]
    [Trait("Case", "RN-48")]
    public async Task TheMediaFolder_ServesImagesToAnyone_ButNothingElse()
    {
        // Las imágenes de muestra viajan con la API: se piden sin token.
        var poster = await Get("/media/posters/vertigo-2.jpg");
        poster.StatusCode.ShouldBe(HttpStatusCode.OK);
        poster.Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");
        poster.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");

        (await Get("/media/cinemas/cine-star.png")).Content.Headers.ContentType!.MediaType.ShouldBe("image/png");

        // Un archivo que no es imagen no se sirve, aunque exista en la carpeta, y salir de la carpeta no es posible.
        (await Get("/media/../appsettings.json")).StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await Get("/media/%2e%2e/appsettings.json")).StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await Get("/media/posters/no-existe.jpg")).StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Case", "RN-48")]
    public async Task TheMediaFolder_ServesTheMp4WithRangeSupport_SoThePlayerCanStartWithoutDownloadingEverything()
    {
        var video = await Get("/media/posters/verity-la-sombra-de-un-engano.mp4");
        video.StatusCode.ShouldBe(HttpStatusCode.OK);
        video.Content.Headers.ContentType!.MediaType.ShouldBe("video/mp4");
        video.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        video.Headers.AcceptRanges.ShouldContain("bytes");

        // Un reproductor pide trozos: 206 con exactamente el rango pedido.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/media/posters/verity-la-sombra-de-un-engano.mp4");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99);
        var partial = await Client.SendAsync(request);
        partial.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        (await partial.Content.ReadAsByteArrayAsync()).Length.ShouldBe(100);
        partial.Content.Headers.ContentRange!.From.ShouldBe(0);

        // Solo se sirven imágenes y .mp4: otros formatos de video siguen sin servirse.
        (await Get("/media/posters/verity-la-sombra-de-un-engano.mov")).StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await Get("/media/posters/verity-la-sombra-de-un-engano.mp4/../../../appsettings.json")).StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Case", "RN-48")]
    public async Task TheDetailExposesThePreviewVideoAsAnAbsoluteUrl_AndAMovieWithoutOneHasNull()
    {
        var (withVideo, withAbsolute, without) = await Db(async db =>
        {
            var a = await Seeds.MovieAsync(db, "Con video");
            a.PreviewVideoUrl = "posters/con-video.mp4";
            var b = await Seeds.MovieAsync(db, "Con video absoluto");
            b.PreviewVideoUrl = "https://videos.example.test/v.mp4";
            var c = await Seeds.MovieAsync(db, "Sin video");
            await db.SaveChangesAsync();
            return (a.Id, b.Id, c.Id);
        });

        (await Read<MovieDetailDto>(await Get($"/api/v1/movies/{withVideo}"))).PreviewVideoUrl.ShouldBe("http://localhost/media/posters/con-video.mp4");
        (await Read<MovieDetailDto>(await Get($"/api/v1/movies/{withAbsolute}"))).PreviewVideoUrl.ShouldBe("https://videos.example.test/v.mp4");
        (await Read<MovieDetailDto>(await Get($"/api/v1/movies/{without}"))).PreviewVideoUrl.ShouldBeNull();

        // La tarjeta del carrusel (la lista) también lo necesita para reproducirlo sin pedir el detalle de cada película.
        var list = (await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies"))).Items.ToDictionary(m => m.Title);
        list["Con video"].PreviewVideoUrl.ShouldBe("http://localhost/media/posters/con-video.mp4");
        list["Con video absoluto"].PreviewVideoUrl.ShouldBe("https://videos.example.test/v.mp4");
        list["Sin video"].PreviewVideoUrl.ShouldBeNull();
    }
}
