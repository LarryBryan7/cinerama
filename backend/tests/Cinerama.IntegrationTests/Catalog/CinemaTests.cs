using Cinerama.Application.Catalog;
using Cinerama.Application.Common;
using Cinerama.IntegrationTests.Infrastructure;

namespace Cinerama.IntegrationTests.Catalog;

/// <summary>Cines, ciudades y qué películas se ven en cada uno.</summary>
public class CinemaTests(ApiFixture fixture) : ReservationTest(fixture)
{
    private static DateTime Tomorrow(int hour = 20) => DateTime.UtcNow.Date.AddDays(1).AddHours(hour);

    [Fact]
    [Trait("Case", "RN-49")]
    public async Task Cities_ListsEachCityOnce_WithItsCinemaCount_AndHidesInactiveCinemas()
    {
        await Db(async db =>
        {
            await Seeds.CinemaAsync(db, "Cine A", "Chimbote");
            await Seeds.CinemaAsync(db, "Cine B", "Chimbote");
            await Seeds.CinemaAsync(db, "Cine C", "Trujillo");
            await Seeds.CinemaAsync(db, "Cine cerrado", "Piura", active: false);
        });

        var cities = await Read<List<CityDto>>(await Get("/api/v1/cities")); // sin token

        cities.Select(c => (c.City, c.CinemaCount)).ShouldBe([("Chimbote", 2), ("Trujillo", 1)]);
    }

    [Fact]
    [Trait("Case", "RN-49")]
    public async Task Cinemas_CanBeFilteredByCity_AndCarryTheirCoordinates()
    {
        await Db(async db =>
        {
            await Seeds.CinemaAsync(db, "Cine A", "Chimbote", -9.0745m, -78.5936m);
            await Seeds.CinemaAsync(db, "Cine C", "Trujillo");
        });

        var all = await Read<List<CinemaDto>>(await Get("/api/v1/cinemas"));
        var chimbote = await Read<List<CinemaDto>>(await Get("/api/v1/cinemas?city=chimbote")); // sin distinguir mayúsculas

        all.Count.ShouldBe(2);
        chimbote.Single().Name.ShouldBe("Cine A");
        chimbote.Single().Latitude.ShouldBe(-9.0745m);
        chimbote.Single().Longitude.ShouldBe(-78.5936m);
    }

    [Fact]
    [Trait("Case", "RN-49")]
    public async Task EachMovie_ListsOnlyTheCinemasWhereItHasUpcomingShowtimes()
    {
        await Db(async db =>
        {
            var movie = await Seeds.MovieAsync(db, "En dos cines");
            var a = await Seeds.CinemaAsync(db, "Cine A", "Chimbote");
            var b = await Seeds.CinemaAsync(db, "Cine B", "Chimbote");
            var c = await Seeds.CinemaAsync(db, "Cine C (sin funciones)", "Chimbote");
            var closed = await Seeds.CinemaAsync(db, "Cine cerrado", "Chimbote", active: false);
            await Seeds.ShowtimeAsync(db, movie, await Seeds.RoomAsync(db, "Sala 1", cinema: a), Tomorrow());
            await Seeds.ShowtimeAsync(db, movie, await Seeds.RoomAsync(db, "Sala 1", cinema: b), Tomorrow());
            await Seeds.ShowtimeAsync(db, movie, await Seeds.RoomAsync(db, "Sala 2", cinema: b), Tomorrow(23)); // el mismo cine no se repite
            await Seeds.ShowtimeAsync(db, movie, await Seeds.RoomAsync(db, "Sala 1", cinema: closed), Tomorrow());
            await Seeds.ShowtimeAsync(db, movie, await Seeds.RoomAsync(db, "Sala 9", cinema: c), DateTime.UtcNow.AddDays(-1), generateSeats: false); // ya pasó
        });

        var card = (await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies"))).Items.Single();
        var detail = await Read<MovieDetailDto>(await Get($"/api/v1/movies/{card.Id}"));

        card.Cinemas.Select(c => c.Name).ShouldBe(["Cine A", "Cine B"]);
        detail.Cinemas.Select(c => c.Name).ShouldBe(["Cine A", "Cine B"]);
        detail.UpcomingShowtimes.Select(s => s.CinemaName).ShouldBe(["Cine A", "Cine B", "Cine B"]);
        detail.UpcomingShowtimes.ShouldAllBe(s => s.CinemaId > 0);
    }

    [Fact]
    [Trait("Case", "RN-49")]
    public async Task TheCityFilter_KeepsMoviesShownInThatCity_AndNeverHidesTheComingSoonOnes()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Lima")));
        await Db(async db =>
        {
            var lima = await Seeds.CinemaAsync(db, "Cine Lima", "Lima");
            var chimbote = await Seeds.CinemaAsync(db, "Cine Chimbote", "Chimbote");
            var onlyLima = await Seeds.MovieAsync(db, "Solo en Lima");
            var onlyChimbote = await Seeds.MovieAsync(db, "Solo en Chimbote");
            await Seeds.MovieWithReleaseAsync(db, "Próximamente", today.AddDays(40));
            await Seeds.ShowtimeAsync(db, onlyLima, await Seeds.RoomAsync(db, "Sala 1", cinema: lima), Tomorrow());
            await Seeds.ShowtimeAsync(db, onlyChimbote, await Seeds.RoomAsync(db, "Sala 1", cinema: chimbote), Tomorrow());
        });

        var inChimbote = await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies?city=Chimbote"));
        var presaleOnly = await Read<PagedResult<MovieListItemDto>>(await Get("/api/v1/movies?status=comingSoon&city=Trujillo"));

        inChimbote.Items.Select(m => m.Title).ShouldBe(["Próximamente", "Solo en Chimbote"]);
        presaleOnly.Items.Select(m => m.Title).ShouldBe(["Próximamente"]); // sin funciones todavía: ninguna ciudad la excluye
    }

    [Fact]
    public async Task ShowtimesCanBeFilteredByCinema_AndEachOneSaysWhichCinemaItIsIn()
    {
        await Db(async db =>
        {
            var movie = await Seeds.MovieAsync(db, "Película");
            var a = await Seeds.CinemaAsync(db, "Cine A");
            var b = await Seeds.CinemaAsync(db, "Cine B");
            await Seeds.ShowtimeAsync(db, movie, await Seeds.RoomAsync(db, "Sala 1", cinema: a), Tomorrow());
            await Seeds.ShowtimeAsync(db, movie, await Seeds.RoomAsync(db, "Sala 1", cinema: b), Tomorrow());
        });
        var idA = await Db(db => Task.FromResult(db.Cinemas.Single(c => c.Name == "Cine A").Id));

        var inA = await Read<PagedResult<ShowtimeDto>>(await Get($"/api/v1/showtimes?cinemaId={idA}"));
        var all = await Read<PagedResult<ShowtimeDto>>(await Get("/api/v1/showtimes"));

        inA.Items.Single().CinemaName.ShouldBe("Cine A");
        all.Items.Select(s => s.CinemaName).Order().ShouldBe(["Cine A", "Cine B"]);
    }
}
