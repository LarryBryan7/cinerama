using System.Net;
using Cinerama.Application.Catalog;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cinerama.IntegrationTests.Catalog;

/// <summary>Comentarios del tráiler en YouTube: el contrato del endpoint y sus estados. YouTube se simula (no se llama a internet).</summary>
public class TrailerCommentsTests(ApiFixture fixture) : ReservationTest(fixture)
{
    private sealed class FakeSource(Func<string, int, TrailerCommentsFetch> answer) : ITrailerCommentsSource
    {
        public List<(string VideoId, int Limit)> Calls { get; } = [];

        public Task<TrailerCommentsFetch> FetchAsync(string videoId, int limit, CancellationToken ct)
        {
            lock (Calls) Calls.Add((videoId, limit));
            return Task.FromResult(answer(videoId, limit));
        }
    }

    private static TrailerCommentDto Comment(int i) => new($"Persona {i}", null, $"Comentario {i}", i, new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes(i));

    private static TrailerCommentsFetch Ok(int count) => new(TrailerCommentsOutcome.Ok, Enumerable.Range(1, count).Select(Comment).ToList());

    private Task<(CineramaFactory Factory, HttpClient Client, FakeSource Source)> Start(Func<string, int, TrailerCommentsFetch> answer, string? apiKey = "clave-de-prueba")
    {
        var source = new FakeSource(answer);
        var factory = Fixture.CreateFactory(new() { ["YouTube:ApiKey"] = apiKey }, services =>
        {
            services.RemoveAll<ITrailerCommentsSource>();
            services.AddSingleton<ITrailerCommentsSource>(source);
        });
        _ = factory.Server;
        return Task.FromResult((factory, NewClient(factory), source));
    }

    private Task<int> MovieWithTrailer(string? url, string title = "Con tráiler") => Db(async db =>
    {
        var m = await Seeds.MovieAsync(db, title);
        m.TrailerUrl = url;
        await db.SaveChangesAsync();
        return m.Id;
    });

    [Fact]
    [Trait("Case", "RF-REV-03")]
    public async Task ReturnsUpTo20Comments_AndWhenTheVideoHasOnlyTwo_ReturnsTwo()
    {
        var movie = await MovieWithTrailer("https://www.youtube.com/watch?v=1McLG8FgNaI");
        var (factory, client, _) = await Start((_, _) => Ok(2));
        await using var _f = factory;

        var dto = await Read<TrailerCommentsDto>(await Send(HttpMethod.Get, $"/api/v1/movies/{movie}/trailer-comments", client: client));

        dto.Status.ShouldBe("ok");
        dto.VideoId.ShouldBe("1McLG8FgNaI");
        dto.Items.Select(i => i.Text).ShouldBe(["Comentario 1", "Comentario 2"]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(20, 20)]
    public async Task TheLimitCapsHowManyAreReturned(int limit, int expected)
    {
        var movie = await MovieWithTrailer("https://youtu.be/1McLG8FgNaI");
        var (factory, client, _) = await Start((_, _) => Ok(20));
        await using var _f = factory;

        var dto = await Read<TrailerCommentsDto>(await Send(HttpMethod.Get, $"/api/v1/movies/{movie}/trailer-comments?limit={limit}", client: client));

        dto.Items.Count.ShouldBe(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("21")]
    [InlineData("-3")]
    [InlineData("1000")]
    public async Task ALimitOutsideOneToTwenty_Is400WithTheField(string limit)
    {
        var movie = await MovieWithTrailer("https://youtu.be/1McLG8FgNaI");
        var (factory, client, source) = await Start((_, _) => Ok(5));
        await using var _f = factory;

        var response = await Send(HttpMethod.Get, $"/api/v1/movies/{movie}/trailer-comments?limit={limit}", client: client);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Errors!.ShouldContainKey("limit");
        source.Calls.ShouldBeEmpty("no se gasta cuota con una solicitud inválida");
    }

    [Fact]
    public async Task EveryoneCanReadThem_WithoutAnAccount_AndTheKeyNeverAppearsInTheResponse()
    {
        var movie = await MovieWithTrailer("https://youtu.be/1McLG8FgNaI");
        var (factory, client, _) = await Start((_, _) => Ok(3), apiKey: "CLAVE-QUE-NO-DEBE-SALIR");
        await using var _f = factory;

        var response = await Send(HttpMethod.Get, $"/api/v1/movies/{movie}/trailer-comments", client: client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("CLAVE-QUE-NO-DEBE-SALIR");
    }

    [Fact]
    public async Task ASecondRequest_IsServedFromTheCache_SoTheQuotaIsNotSpent_EvenWithADifferentLimit()
    {
        var movie = await MovieWithTrailer("https://youtu.be/1McLG8FgNaI");
        var (factory, client, source) = await Start((_, _) => Ok(20));
        await using var _f = factory;

        await Send(HttpMethod.Get, $"/api/v1/movies/{movie}/trailer-comments?limit=5", client: client);
        var second = await Read<TrailerCommentsDto>(await Send(HttpMethod.Get, $"/api/v1/movies/{movie}/trailer-comments?limit=15", client: client));

        source.Calls.ShouldBe([("1McLG8FgNaI", 20)], "una sola consulta, siempre por el máximo");
        second.Items.Count.ShouldBe(15);
    }

    [Fact]
    public async Task WithoutAnApiKey_ItIsUnavailable_AndYouTubeIsNeverCalled()
    {
        var movie = await MovieWithTrailer("https://youtu.be/1McLG8FgNaI");
        var (factory, client, source) = await Start((_, _) => Ok(5), apiKey: null);
        await using var _f = factory;

        var response = await Send(HttpMethod.Get, $"/api/v1/movies/{movie}/trailer-comments", client: client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, "la función apagada no es un error");
        var dto = await Read<TrailerCommentsDto>(response);
        dto.Status.ShouldBe("unavailable");
        dto.Items.ShouldBeEmpty();
        source.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("e")]
    [InlineData("https://vimeo.com/123456")]
    public async Task AMovieWithoutAValidYouTubeTrailer_ReportsNoTrailer_WithoutCallingYouTube(string? trailer)
    {
        var movie = await MovieWithTrailer(trailer);
        var (factory, client, source) = await Start((_, _) => Ok(5));
        await using var _f = factory;

        var dto = await Read<TrailerCommentsDto>(await Send(HttpMethod.Get, $"/api/v1/movies/{movie}/trailer-comments", client: client));

        dto.Status.ShouldBe("noTrailer");
        dto.VideoId.ShouldBeNull();
        dto.Items.ShouldBeEmpty();
        source.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task AVideoWithoutComments_IsEmpty_AndClosedCommentsAreDisabled()
    {
        var quiet = await MovieWithTrailer("https://youtu.be/AAAAAAAAAAA", "Sin comentarios");
        var closed = await MovieWithTrailer("https://youtu.be/BBBBBBBBBBB", "Cerrados");
        var (factory, client, _) = await Start((id, _) => id == "AAAAAAAAAAA" ? new(TrailerCommentsOutcome.Ok, []) : new(TrailerCommentsOutcome.Disabled, []));
        await using var _f = factory;

        (await Read<TrailerCommentsDto>(await Send(HttpMethod.Get, $"/api/v1/movies/{quiet}/trailer-comments", client: client))).Status.ShouldBe("empty");
        (await Read<TrailerCommentsDto>(await Send(HttpMethod.Get, $"/api/v1/movies/{closed}/trailer-comments", client: client))).Status.ShouldBe("disabled");
    }

    [Fact]
    public async Task WhenYouTubeFails_TheAnswerIsStillOk_AndTheFailureIsNotRetriedOnEveryRequest()
    {
        var movie = await MovieWithTrailer("https://youtu.be/1McLG8FgNaI");
        var (factory, client, source) = await Start((_, _) => new(TrailerCommentsOutcome.Unavailable, []));
        await using var _f = factory;

        for (var i = 0; i < 3; i++)
        {
            var response = await Send(HttpMethod.Get, $"/api/v1/movies/{movie}/trailer-comments", client: client);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await Read<TrailerCommentsDto>(response)).Status.ShouldBe("unavailable");
        }

        source.Calls.Count.ShouldBe(1, "un fallo se recuerda unos minutos: insistir solo gastaría cuota");
    }

    [Fact]
    public async Task AMovieThatDoesNotExistOrIsInactive_Is404()
    {
        var inactive = await Db(async db =>
        {
            var m = await Seeds.MovieAsync(db, "Dada de baja");
            m.TrailerUrl = "https://youtu.be/1McLG8FgNaI";
            m.IsActive = false;
            await db.SaveChangesAsync();
            return m.Id;
        });
        var (factory, client, source) = await Start((_, _) => Ok(5));
        await using var _f = factory;

        foreach (var id in new[] { 99999, inactive })
        {
            var response = await Send(HttpMethod.Get, $"/api/v1/movies/{id}/trailer-comments", client: client);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ReadProblem(response)).Code.ShouldBe("movie_not_found");
        }

        source.Calls.ShouldBeEmpty();
    }
}
