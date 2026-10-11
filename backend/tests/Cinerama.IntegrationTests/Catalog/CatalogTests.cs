using System.Net;
using System.Text.RegularExpressions;
using Cinerama.Application.Catalog;
using Cinerama.Application.Common;
using Cinerama.Domain.Enums;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Catalog;

public class CatalogTests(ApiFixture fixture) : IntegrationTest(fixture)
{
    private static DateTime Tomorrow(int hour = 20) => DateTime.UtcNow.Date.AddDays(1).AddHours(hour);

    // ── Cartelera ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "HU-06")]
    public async Task Movies_List_IsPublic_PaginatedAndHidesInactiveMovies()
    {
        await Db(async db =>
        {
            for (var i = 1; i <= 25; i++)
                await Seeds.MovieAsync(db, $"Película {i:00}");
            await Seeds.MovieAsync(db, "Película oculta A", active: false);
            await Seeds.MovieAsync(db, "Película oculta B", active: false);
        });

        var page1 = await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies?page=1&pageSize=20")); // sin token
        var page2 = await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies?page=2&pageSize=20"));

        page1.TotalCount.ShouldBe(25);
        page1.Items.Count.ShouldBe(20);
        page2.Items.Count.ShouldBe(5);
        page1.Items.Concat(page2.Items).Select(m => m.Title).ShouldAllBe(t => !t.Contains("oculta"));
        page1.Items.Concat(page2.Items).Select(m => m.Id).Distinct().Count().ShouldBe(25); // sin repetidos entre páginas
    }

    [Fact]
    [Trait("Case", "HU-06")]
    public async Task Movies_List_SearchIgnoresCaseAndCombinesWithTheGenreFilter()
    {
        await Db(async db =>
        {
            var drama = await Seeds.GenreAsync(db, "Drama");
            var comedia = await Seeds.GenreAsync(db, "Comedia");
            await Seeds.MovieAsync(db, "Noche en el Puerto", genres: [drama]);
            await Seeds.MovieAsync(db, "La Noche Eterna", genres: [comedia]);
            await Seeds.MovieAsync(db, "Mañana", genres: [drama]);
        });
        var dramaId = await Db(db => db.Genres.Where(g => g.Name == "Drama").Select(g => g.Id).SingleAsync());

        var bySearch = await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies?search=NOCHE"));
        var combined = await Read<PagedResult<MovieListItemDto>>(await Get($"/api/v1/movies?search=noche&genreId={dramaId}"));

        bySearch.Items.Select(m => m.Title).ShouldBe(["La Noche Eterna", "Noche en el Puerto"]);
        combined.Items.Select(m => m.Title).ShouldBe(["Noche en el Puerto"]);
    }

    [Fact]
    public async Task Movies_List_IncludesGenreNamesSortedAlphabetically()
    {
        await Db(async db =>
        {
            var g1 = await Seeds.GenreAsync(db, "Suspenso");
            var g2 = await Seeds.GenreAsync(db, "Drama");
            await Seeds.MovieAsync(db, "Una película", genres: [g1, g2]);
        });

        var list = await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies"));

        list.Items.Single().Genres.ShouldBe(["Drama", "Suspenso"]);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task Movies_List_InvalidPaging_Returns400(string query)
    {
        var response = await Get($"/api/v1/movies?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Code.ShouldBe("validation_failed");
    }

    [Fact]
    public async Task Genres_List_IsPublicAndSortedByName()
    {
        await Db(async db =>
        {
            await Seeds.GenreAsync(db, "Suspenso");
            await Seeds.GenreAsync(db, "Animación");
            await Seeds.GenreAsync(db, "Drama");
        });

        var genres = await Read<List<GenreDto>>(await Get("/api/v1/genres"));

        genres.Select(g => g.Name).ShouldBe(["Animación", "Drama", "Suspenso"]);
    }

    // ── Detalle de película ─────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "HU-07")]
    public async Task MovieDetail_ShowsOnlyUpcomingScheduledShowtimes_WithAvailableSeats()
    {
        var movieId = await Db(async db =>
        {
            var movie = await Seeds.MovieAsync(db, "La Última Proyección");
            var room = await Seeds.RoomAsync(db, "Sala 1", rows: 2, perRow: 5); // 10 asientos
            await Seeds.ShowtimeAsync(db, movie, room, DateTime.UtcNow.AddDays(-1));                                       // ya pasó
            await Seeds.ShowtimeAsync(db, movie, room, Tomorrow(18), status: ShowtimeStatus.Cancelled);                    // cancelada
            await Seeds.ShowtimeAsync(db, movie, room, Tomorrow(23));                                                       // vigente
            await Seeds.ShowtimeAsync(db, movie, room, Tomorrow(20), price: 12.5m);                                         // vigente
            return movie.Id;
        });

        var detail = await Read<MovieDetailDto>(await Get($"/api/v1/movies/{movieId}"));

        detail.Title.ShouldBe("La Última Proyección");
        detail.UpcomingShowtimes.Count.ShouldBe(2);
        detail.UpcomingShowtimes.Select(s => s.StartsAt).ShouldBeInOrder();
        detail.UpcomingShowtimes.ShouldAllBe(s => s.AvailableSeats == 10 && s.RoomName == "Sala 1");
    }

    [Fact]
    public async Task MovieDetail_OfAnInactiveOrUnknownMovie_Returns404()
    {
        var inactiveId = await Db(async db => (await Seeds.MovieAsync(db, "Retirada", active: false)).Id);

        foreach (var id in new[] { inactiveId, 99999 })
        {
            var response = await Get($"/api/v1/movies/{id}");

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ReadProblem(response)).Code.ShouldBe("movie_not_found");
        }
    }

    // ── Funciones ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Showtimes_List_FiltersByMovieAndHidesPastAndCancelled()
    {
        var (movieA, movieB) = await Db(async db =>
        {
            var a = await Seeds.MovieAsync(db, "A");
            var b = await Seeds.MovieAsync(db, "B");
            var room = await Seeds.RoomAsync(db);
            await Seeds.ShowtimeAsync(db, a, room, Tomorrow(18));
            await Seeds.ShowtimeAsync(db, b, room, Tomorrow(21));
            await Seeds.ShowtimeAsync(db, b, room, DateTime.UtcNow.AddHours(-3));
            await Seeds.ShowtimeAsync(db, b, room, Tomorrow(23), status: ShowtimeStatus.Cancelled);
            return (a.Id, b.Id);
        });

        var all = await Read<PagedResult<ShowtimeDto>>(await Get("/api/v1/showtimes"));
        var onlyB = await Read<PagedResult<ShowtimeDto>>(await Get($"/api/v1/showtimes?movieId={movieB}"));

        all.TotalCount.ShouldBe(2);
        onlyB.Items.Select(s => s.MovieId).ShouldBe([movieB]);
        movieA.ShouldNotBe(movieB);
    }

    // RN-38: "el día X" es una fecha local del cine, no una fecha UTC.
    [Fact]
    [Trait("Case", "RN-38")]
    public async Task Showtimes_List_ByDate_UsesTheCinemaLocalDay_NotUtc()
    {
        var lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
        var localDay = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        // 21:00 en Lima son las 02:00 UTC del día siguiente.
        var startsUtc = TimeZoneInfo.ConvertTimeToUtc(localDay.ToDateTime(new TimeOnly(21, 0), DateTimeKind.Unspecified), lima);
        startsUtc.Date.ShouldBe(localDay.AddDays(1).ToDateTime(TimeOnly.MinValue).Date);

        await Db(async db => await Seeds.ShowtimeAsync(db, await Seeds.MovieAsync(db, "Nocturna"), await Seeds.RoomAsync(db), startsUtc));

        async Task<int> CountOn(DateOnly d) =>
            (await Read<PagedResult<ShowtimeDto>>(await Get($"/api/v1/showtimes?date={d:yyyy-MM-dd}"))).TotalCount;

        (await CountOn(localDay)).ShouldBe(1);                  // el día local de la función
        (await CountOn(localDay.AddDays(1))).ShouldBe(0);       // el día UTC no cuenta
        (await CountOn(localDay.AddDays(-1))).ShouldBe(0);
    }

    // RN-23: un bloqueo vencido equivale a un asiento disponible aunque el proceso de limpieza todavía no corra.
    [Fact]
    [Trait("Case", "RN-23")]
    public async Task Showtime_AvailableSeats_CountsExpiredLocksAsAvailable()
    {
        var showtimeId = await Db(async db =>
        {
            var user = await Seeds.UserAsync(db, "ana@example.test");
            var room = await Seeds.RoomAsync(db, rows: 1, perRow: 4);
            var showtime = await Seeds.ShowtimeAsync(db, await Seeds.MovieAsync(db, "Película"), room, Tomorrow());

            var seats = await db.ShowSeats.Where(s => s.ShowtimeId == showtime.Id).OrderBy(s => s.SeatId).ToListAsync();
            var now = DateTime.UtcNow;
            // seats[0] queda Available
            seats[1].Status = ShowSeatStatus.Locked; seats[1].LockedByUserId = user.Id; seats[1].LockedUntil = now.AddMinutes(4); // bloqueo vigente
            seats[2].Status = ShowSeatStatus.Locked; seats[2].LockedByUserId = user.Id; seats[2].LockedUntil = now.AddMinutes(-1); // bloqueo vencido
            seats[3].Status = ShowSeatStatus.Booked;
            await db.SaveChangesAsync();
            return showtime.Id;
        });

        var showtime = await Read<ShowtimeDto>(await Get($"/api/v1/showtimes/{showtimeId}"));

        showtime.Capacity.ShouldBe(4);
        showtime.AvailableSeats.ShouldBe(2); // la disponible + la de bloqueo vencido
    }

    [Fact]
    public async Task Showtime_Get_ReturnsTheDetail_AndUnknownReturns404()
    {
        var id = await Db(async db =>
            (await Seeds.ShowtimeAsync(db, await Seeds.MovieAsync(db, "Película"), await Seeds.RoomAsync(db, "Sala 9"), Tomorrow(), price: 18m)).Id);

        var found = await Read<ShowtimeDto>(await Get($"/api/v1/showtimes/{id}"));
        found.RoomName.ShouldBe("Sala 9");
        found.Price.ShouldBe(18m);
        found.Status.ShouldBe("Scheduled");

        var missing = await Get("/api/v1/showtimes/99999");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadProblem(missing)).Code.ShouldBe("showtime_not_found");
    }

    [Fact]
    [Trait("Case", "RN-38")]
    public async Task Dates_AreSerializedAsUtcWithTheZSuffix()
    {
        await Db(async db => await Seeds.ShowtimeAsync(db, await Seeds.MovieAsync(db, "Película"), await Seeds.RoomAsync(db), Tomorrow()));

        var json = await (await Get("/api/v1/showtimes")).Content.ReadAsStringAsync();

        // Sin la "Z" un cliente interpretaría la hora como local y mostraría la función en otro horario.
        Regex.IsMatch(json, "\"startsAt\":\"[^\"]+Z\"").ShouldBeTrue(json);
        Regex.IsMatch(json, "\"endsAt\":\"[^\"]+Z\"").ShouldBeTrue(json);
    }
}
