using System.Net;
using System.Text;
using Cinerama.Application.Catalog;
using Cinerama.Application.Common.Options;
using Cinerama.Infrastructure.YouTube;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cinerama.UnitTests.Catalog;

public class YouTubeVideoIdTests
{
    [Theory]
    [InlineData("1McLG8FgNaI", "1McLG8FgNaI")]
    [InlineData("https://www.youtube.com/watch?v=1McLG8FgNaI", "1McLG8FgNaI")]
    [InlineData("https://www.youtube.com/watch?v=1McLG8FgNaI&t=30s", "1McLG8FgNaI")]
    [InlineData("https://www.youtube.com/watch?feature=share&v=1McLG8FgNaI", "1McLG8FgNaI")]
    [InlineData("https://youtu.be/1McLG8FgNaI?si=abc", "1McLG8FgNaI")]
    [InlineData("https://m.youtube.com/watch?v=lAr_uspgHm8", "lAr_uspgHm8")]
    [InlineData("https://www.youtube.com/embed/8cI5Bl-v-p8", "8cI5Bl-v-p8")]
    [InlineData("https://www.youtube.com/shorts/6ESEtbv0J8M", "6ESEtbv0J8M")]
    [InlineData("https://www.youtube-nocookie.com/embed/ve282XdTrDM", "ve282XdTrDM")]
    [InlineData("  https://youtu.be/1McLG8FgNaI  ", "1McLG8FgNaI")]
    [InlineData("https://www.youtube.com/live/1McLG8FgNaI", "1McLG8FgNaI")]
    public void ExtractsTheElevenCharacterId(string input, string expected) => YouTubeVideoId.TryParse(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("e")]                                                       // basura que publica la fuente
    [InlineData("dsaf")]
    [InlineData("1McLG8FgNaIX")]                                            // 12 caracteres
    [InlineData("1McLG8FgNa")]                                              // 10 caracteres
    [InlineData("https://www.youtube.com/watch?v=corto")]
    [InlineData("https://www.youtube.com/watch?v=1McLG8FgNaIextra")]        // más largo que un código
    [InlineData("https://vimeo.com/watch?v=1McLG8FgNaI")]                   // otro sitio
    [InlineData("https://evil.example/youtu.be/1McLG8FgNaI")]               // el dominio no es de YouTube
    [InlineData("https://youtube.com.evil.example/watch?v=1McLG8FgNaI")]    // dominio que solo empieza como YouTube
    [InlineData("javascript:alert(1)//watch?v=1McLG8FgNaI")]
    [InlineData("ftp://www.youtube.com/watch?v=1McLG8FgNaI")]
    [InlineData("https://www.youtube.com/")]
    [InlineData("https://youtu.be/")]
    [InlineData("https://youtu.be/corto")]
    public void RejectsAnythingThatIsNotAValidYouTubeVideo(string? input) => YouTubeVideoId.TryParse(input).ShouldBeNull();
}

public class YouTubeCommentsClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Thread(string author, string text, int likes = 0, string? avatar = "https://yt3.ggpht.com/a/x=s48", string at = "2026-10-01T12:00:00Z") =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            snippet = new { topLevelComment = new { snippet = new { authorDisplayName = author, authorProfileImageUrl = avatar, textDisplay = text, likeCount = likes, publishedAt = at } } },
        });

    private static string Page(params string[] threads) => "{\"items\":[" + string.Join(",", threads) + "]}";

    private static string ErrorBody(int status, string reason) => "{\"error\":{\"code\":" + status + ",\"errors\":[{\"reason\":\"" + reason + "\"}]}}";

    private static (YouTubeCommentsClient Client, StubHandler Handler) Make(Func<HttpRequestMessage, HttpResponseMessage> respond, string? key = "CLAVE-SECRETA")
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri(YouTubeCommentsClient.BaseAddress) };
        return (new YouTubeCommentsClient(http, Options.Create(new YouTubeOptions { ApiKey = key }), NullLogger<YouTubeCommentsClient>.Instance), handler);
    }

    [Fact]
    public async Task ReturnsTheCommentsOfTheVideo_WithTheKeyInAHeaderAndNeverInTheUrl()
    {
        var (client, handler) = Make(_ => Json(HttpStatusCode.OK, Page(Thread("Ana", "Qué buen tráiler", likes: 12))));

        var result = await client.FetchAsync("1McLG8FgNaI", 20, CancellationToken.None);

        result.Outcome.ShouldBe(TrailerCommentsOutcome.Ok);
        var c = result.Items.Single();
        (c.AuthorName, c.Text, c.LikeCount).ShouldBe(("Ana", "Qué buen tráiler", 12));
        c.PublishedAt.ShouldBe(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));
        var request = handler.Requests.Single();
        request.RequestUri!.PathAndQuery.ShouldContain("videoId=1McLG8FgNaI");
        request.RequestUri.PathAndQuery.ShouldContain("maxResults=20");
        request.RequestUri.PathAndQuery.ShouldContain("order=relevance");
        request.RequestUri.ToString().ShouldNotContain("CLAVE-SECRETA", Case.Insensitive, "la clave en la URL acabaría en los registros");
        request.Headers.GetValues("x-goog-api-key").ShouldBe(["CLAVE-SECRETA"]);
    }

    [Fact]
    public async Task WithoutAKey_ItNeverCallsYouTube()
    {
        var (client, handler) = Make(_ => throw new InvalidOperationException("no debería llamar"), key: null);

        (await client.FetchAsync("1McLG8FgNaI", 20, CancellationToken.None)).Outcome.ShouldBe(TrailerCommentsOutcome.Unavailable);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task IfTheVideoHasFewerCommentsThanRequested_ItReturnsJustThose()
    {
        var (client, _) = Make(_ => Json(HttpStatusCode.OK, Page(Thread("A", "uno"), Thread("B", "dos"))));

        var result = await client.FetchAsync("1McLG8FgNaI", 20, CancellationToken.None);

        result.Items.Select(i => i.Text).ShouldBe(["uno", "dos"]);
    }

    [Fact]
    public async Task NeverReturnsMoreThanRequested()
    {
        var (client, _) = Make(_ => Json(HttpStatusCode.OK, Page(Enumerable.Range(1, 30).Select(i => Thread($"U{i}", $"comentario {i}")).ToArray())));

        (await client.FetchAsync("1McLG8FgNaI", 10, CancellationToken.None)).Items.Count.ShouldBe(10);
    }

    [Fact]
    public async Task AVideoWithoutComments_IsOkAndEmpty()
    {
        var (client, _) = Make(_ => Json(HttpStatusCode.OK, """{"items":[]}"""));

        var result = await client.FetchAsync("1McLG8FgNaI", 20, CancellationToken.None);

        result.Outcome.ShouldBe(TrailerCommentsOutcome.Ok);
        result.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "commentsDisabled")]
    [InlineData(HttpStatusCode.NotFound, "videoNotFound")]
    public async Task ClosedCommentsOrAMissingVideo_AreDisabled(HttpStatusCode status, string reason)
    {
        var (client, _) = Make(_ => Json(status, ErrorBody((int)status, reason)));

        (await client.FetchAsync("1McLG8FgNaI", 20, CancellationToken.None)).Outcome.ShouldBe(TrailerCommentsOutcome.Disabled);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "quotaExceeded")]
    [InlineData(HttpStatusCode.BadRequest, "keyInvalid")]
    [InlineData(HttpStatusCode.Forbidden, "forbidden")]
    [InlineData(HttpStatusCode.InternalServerError, "backendError")]
    public async Task QuotaKeyOrServerProblems_AreUnavailable_NotErrors(HttpStatusCode status, string reason)
    {
        var (client, _) = Make(_ => Json(status, ErrorBody((int)status, reason)));

        (await client.FetchAsync("1McLG8FgNaI", 20, CancellationToken.None)).Outcome.ShouldBe(TrailerCommentsOutcome.Unavailable);
    }

    [Fact]
    public async Task ANetworkFailureOrGarbageFromYouTube_IsUnavailable()
    {
        var (down, _) = Make(_ => throw new HttpRequestException("sin red"));
        var (garbage, _) = Make(_ => Json(HttpStatusCode.OK, "<html>no es json</html>"));
        var (errorWithoutBody, _) = Make(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        (await down.FetchAsync("1McLG8FgNaI", 20, CancellationToken.None)).Outcome.ShouldBe(TrailerCommentsOutcome.Unavailable);
        (await garbage.FetchAsync("1McLG8FgNaI", 20, CancellationToken.None)).Outcome.ShouldBe(TrailerCommentsOutcome.Unavailable);
        (await errorWithoutBody.FetchAsync("1McLG8FgNaI", 20, CancellationToken.None)).Outcome.ShouldBe(TrailerCommentsOutcome.Unavailable);
    }

    [Fact]
    public async Task ACancelledRequest_IsNotSwallowed()
    {
        using var cts = new CancellationTokenSource();
        var (client, _) = Make(_ =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        });

        await Should.ThrowAsync<TaskCanceledException>(() => client.FetchAsync("1McLG8FgNaI", 20, cts.Token));
    }

    [Fact]
    public void Parse_CleansTheTextAndDropsWhatCannotBeShown()
    {
        var long600 = new string('a', 600);
        var comments = YouTubeCommentsClient.Parse(Page(
            Thread("Ana", "  hola\u0000   mundo\u0007  "),
            Thread("Sin texto", "   "),
            Thread("", "sin autor"),
            Thread("Larga", long600),
            Thread("Líneas", "uno\n\n\n\n\ndos"),
            Thread("Foto http", "x", avatar: "http://insegura.example/a.jpg"),
            Thread("Foto js", "y", avatar: "javascript:alert(1)")), 20);

        comments.Select(c => c.AuthorName).ShouldBe(["Ana", "Larga", "Líneas", "Foto http", "Foto js"]);
        comments[0].Text.ShouldBe("hola mundo");
        comments[1].Text.Length.ShouldBe(YouTubeCommentsClient.MaxTextLength);
        comments[1].Text.ShouldEndWith("…");
        comments[2].Text.ShouldBe("uno\n\ndos");
        comments[3].AuthorAvatarUrl.ShouldBeNull("solo se aceptan fotos por https");
        comments[4].AuthorAvatarUrl.ShouldBeNull();
        comments[0].AuthorAvatarUrl.ShouldBe("https://yt3.ggpht.com/a/x=s48");
    }

    [Fact]
    public void Parse_ToleratesMissingFieldsAndOddShapes()
    {
        YouTubeCommentsClient.Parse("""{"nada":1}""", 20).ShouldBeEmpty();
        YouTubeCommentsClient.Parse("""{"items":"no es lista"}""", 20).ShouldBeEmpty();
        YouTubeCommentsClient.Parse("""{"items":[{"snippet":{}},{"otra":1}]}""", 20).ShouldBeEmpty();
        var odd = YouTubeCommentsClient.Parse("""{"items":[{"snippet":{"topLevelComment":{"snippet":{"authorDisplayName":"A","textDisplay":"t","likeCount":-5,"publishedAt":"no-es-fecha"}}}}]}""", 20).Single();
        odd.LikeCount.ShouldBe(0);
        odd.PublishedAt.ShouldBe(DateTime.UnixEpoch);
    }
}
