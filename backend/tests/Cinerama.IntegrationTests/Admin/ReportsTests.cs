using System.Net;
using Cinerama.Application.Admin;
using Cinerama.Application.Common;
using Cinerama.Domain.Enums;
using Cinerama.Domain.Entities;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Admin;

/// <summary>KPIs, ventas por día y por película, y ocupación (HU-21, RF-REP-01 a RF-REP-04, RN-36, RN-37, RN-38). Todos los valores se calculan a mano.</summary>
public class ReportsTests(ApiFixture fixture) : ReservationTest(fixture)
{
    // 10 de marzo de 2030, 15:00 en Lima (UTC-5) = 20:00 UTC.
    private static readonly DateTime Day1 = new(2030, 3, 10, 20, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = Day1.AddDays(1);

    private static readonly string From = "2030-03-10";
    private static readonly string To = "2030-03-12";

    private async Task<string> AdminAsync() => (await NewUserAsync(admin: true)).Token;

    private async Task<T> GetReport<T>(string path, string query, string token)
    {
        var response = await Get($"/api/v1/reports/{path}{query}", token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Read<T>(response);
    }

    /// <summary>El escenario de TC-REP-01: dos funciones de 10 asientos.</summary>
    private async Task<(TestShowtime S1, TestShowtime S2)> TcRep01Async()
    {
        var s1 = await NewShowtimeAsync(rows: 2, perRow: 5, price: 10m, startsAtUtc: Day1, roomName: "Sala 1");
        var s2 = await NewShowtimeAsync(rows: 2, perRow: 5, price: 15m, startsAtUtc: Day2, roomName: "Sala 2");
        var ana = await NewUserAsync();
        var beto = await NewUserAsync();
        await BookedAsync(ana, s1, "A1", "A2", "A3");           // 3 × 10,00
        await BookedAsync(ana, s2, "A1", "A2");                // 2 × 15,00
        var cancelled = await BookedAsync(beto, s2, "B1");     // 1 × 15,00 que luego se cancela
        (await CancelBooking(beto, cancelled.Id)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return (s1, s2);
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("sales-daily")]
    [InlineData("sales-by-movie")]
    [InlineData("occupancy")]
    [Trait("Case", "TC-ADM-03")]
    public async Task EveryReport_Rejects401WithoutAToken_And403ToACustomer(string report)
    {
        var url = $"/api/v1/reports/{report}?from={From}&to={To}";
        (await Get(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Get(url, (await NewUserAsync()).Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Case", "TC-REP-01")]
    public async Task TheKpisMatchTheValuesComputedByHand_AndCancelledBookingsDoNotCount()
    {
        await TcRep01Async();

        var summary = await GetReport<SalesSummaryDto>("summary", $"?from={From}&to={To}", await AdminAsync());

        summary.Revenue.ShouldBe(60.00m);            // 3×10 + 2×15
        summary.Bookings.ShouldBe(2);
        summary.TicketsSold.ShouldBe(5);
        summary.AverageTicket.ShouldBe(30.00m);      // 60 / 2
        summary.AverageOccupancy.ShouldBe(0.25m);    // 5 / 20
        summary.Showtimes.ShouldBe(2);
    }

    [Fact]
    public async Task WithoutSalesTheKpisAreZero_NotAnErrorNorADivisionByZero()
    {
        var summary = await GetReport<SalesSummaryDto>("summary", $"?from={From}&to={To}", await AdminAsync());

        (summary.Revenue, summary.Bookings, summary.TicketsSold, summary.AverageTicket, summary.AverageOccupancy, summary.Showtimes).ShouldBe((0m, 0, 0, 0m, 0m, 0));
    }

    [Fact]
    public async Task AShowtimeWithoutSalesStillCountsInTheOccupancy_ButACancelledShowtimeDoesNot()
    {
        var sold = await NewShowtimeAsync(rows: 2, perRow: 5, price: 10m, startsAtUtc: Day1, roomName: "Sala 1");
        await NewShowtimeAsync(rows: 2, perRow: 5, price: 10m, startsAtUtc: Day1.AddHours(3), roomName: "Sala 2");               // vacía: 0 de 10
        await NewShowtimeAsync(rows: 2, perRow: 5, price: 10m, startsAtUtc: Day1.AddHours(6), roomName: "Sala 3", status: ShowtimeStatus.Cancelled); // no cuenta
        await BookedAsync(await NewUserAsync(), sold, "A1", "A2", "A3", "A4", "A5");

        var summary = await GetReport<SalesSummaryDto>("summary", $"?from={From}&to={To}", await AdminAsync());

        summary.AverageOccupancy.ShouldBe(0.25m, "5 vendidas de 20 de capacidad: la cancelada no diluye");
        summary.Showtimes.ShouldBe(2);
    }

    [Fact]
    public async Task TheAverageOccupancyKeepsItsSixDecimals_AndTheMoneyItsTwo()
    {
        // 1 vendida de 7 = 0,142857… (no 0,14); 3 entradas a 3,33 = 9,99 de una sola reserva.
        var a = await NewShowtimeAsync(rows: 1, perRow: 7, price: 3.33m, startsAtUtc: Day1, roomName: "Sala 1");
        await BookedAsync(await NewUserAsync(), a, "A1", "A2", "A3");
        await NewShowtimeAsync(rows: 1, perRow: 7, price: 3.33m, startsAtUtc: Day1.AddHours(3), roomName: "Sala 2"); // vacía: suma capacidad

        var summary = await GetReport<SalesSummaryDto>("summary", $"?from={From}&to={To}", await AdminAsync());

        summary.AverageOccupancy.ShouldBe(0.214286m, "3 vendidas de 14 = 0,2142857… redondeado a 6 decimales por el procedimiento");
        summary.Revenue.ShouldBe(9.99m);
        summary.AverageTicket.ShouldBe(9.99m);
    }

    [Fact]
    public async Task ACancelledShowtimeContributesNothing_EvenIfItHadSales()
    {
        var showtime = await NewShowtimeAsync(rows: 2, perRow: 5, price: 10m, startsAtUtc: Day1);
        await BookedAsync(await NewUserAsync(), showtime, "A1", "A2");
        var admin = await AdminAsync();
        (await Send(HttpMethod.Post, $"/api/v1/showtimes/{showtime.Id}/cancel", null, admin)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var summary = await GetReport<SalesSummaryDto>("summary", $"?from={From}&to={To}", admin);

        (summary.Revenue, summary.Bookings, summary.TicketsSold, summary.Showtimes).ShouldBe((0m, 0, 0, 0));
    }

    [Fact]
    public async Task ThePeriodIsMeasuredByTheShowtimeDate_InTheCinemasLocalCalendar()
    {
        // 03:00 UTC del 11 = 22:00 del 10 en Lima. 05:00 UTC del 11 = 00:00 del 11 en Lima (ya es el día siguiente).
        var lateNight = await NewShowtimeAsync(rows: 1, perRow: 5, price: 10m, startsAtUtc: new DateTime(2030, 3, 11, 3, 0, 0, DateTimeKind.Utc), roomName: "Sala 1");
        var midnight = await NewShowtimeAsync(rows: 1, perRow: 5, price: 20m, startsAtUtc: new DateTime(2030, 3, 11, 5, 0, 0, DateTimeKind.Utc), roomName: "Sala 2");
        var ana = await NewUserAsync();
        await BookedAsync(ana, lateNight, "A1");
        await BookedAsync(ana, midnight, "A1");
        var admin = await AdminAsync();

        var onlyThe10th = await GetReport<SalesSummaryDto>("summary", "?from=2030-03-10&to=2030-03-10", admin);
        var onlyThe11th = await GetReport<SalesSummaryDto>("summary", "?from=2030-03-11&to=2030-03-11", admin);

        onlyThe10th.Revenue.ShouldBe(10m, "la función de las 22:00 de Lima es del día 10");
        onlyThe11th.Revenue.ShouldBe(20m, "la de las 00:00 de Lima es del día 11");
    }

    [Fact]
    public async Task TheRangeIncludesBothEndDates_AndExcludesWhatIsOutside()
    {
        var before = await NewShowtimeAsync(rows: 1, perRow: 3, price: 5m, startsAtUtc: Day1.AddDays(-1), roomName: "Sala 1");
        var first = await NewShowtimeAsync(rows: 1, perRow: 3, price: 6m, startsAtUtc: Day1, roomName: "Sala 2");
        var last = await NewShowtimeAsync(rows: 1, perRow: 3, price: 7m, startsAtUtc: Day1.AddDays(2), roomName: "Sala 3");
        var after = await NewShowtimeAsync(rows: 1, perRow: 3, price: 8m, startsAtUtc: Day1.AddDays(3), roomName: "Sala 4");
        var ana = await NewUserAsync();
        foreach (var s in new[] { before, first, last, after }) await BookedAsync(ana, s, "A1");

        var summary = await GetReport<SalesSummaryDto>("summary", $"?from={From}&to={To}", await AdminAsync());

        summary.Revenue.ShouldBe(13m, "6 (primer día) + 7 (último día); 5 y 8 quedan fuera");
        (summary.From, summary.To).ShouldBe((new DateOnly(2030, 3, 10), new DateOnly(2030, 3, 12)));
    }

    [Fact]
    public async Task DailySalesHasOneRecordPerDay_IncludingDaysWithoutShowtimes_AndAddsUpToTheSummary()
    {
        await TcRep01Async();
        var admin = await AdminAsync();

        var daily = await GetReport<List<DailySalesDto>>("sales-daily", $"?from={From}&to={To}", admin);
        var summary = await GetReport<SalesSummaryDto>("summary", $"?from={From}&to={To}", admin);

        daily.Select(d => d.Date.ToString("yyyy-MM-dd")).ShouldBe(["2030-03-10", "2030-03-11", "2030-03-12"]);
        daily[0].ShouldBe(new DailySalesDto(new DateOnly(2030, 3, 10), 30m, 1, 3, 1, 0.3m));
        daily[1].ShouldBe(new DailySalesDto(new DateOnly(2030, 3, 11), 30m, 1, 2, 1, 0.2m));
        daily[2].ShouldBe(new DailySalesDto(new DateOnly(2030, 3, 12), 0m, 0, 0, 0, 0m), "un día sin funciones, con ceros");
        daily.Sum(d => d.Revenue).ShouldBe(summary.Revenue, "el desglose suma lo mismo que el resumen");
        daily.Sum(d => d.Bookings).ShouldBe(summary.Bookings);
        daily.Sum(d => d.TicketsSold).ShouldBe(summary.TicketsSold);
    }

    [Fact]
    public async Task SalesByMovie_IsOrderedByRevenue_AndComputesEachMoviesOwnOccupancy()
    {
        var (s1, s2) = await TcRep01Async();
        var admin = await AdminAsync();
        var titles = await Db(async db => (await db.Showtimes.Where(s => s.Id == s1.Id || s.Id == s2.Id).Select(s => new { s.Id, s.Movie.Title }).ToListAsync()).ToDictionary(x => x.Id, x => x.Title));

        var rows = await GetReport<List<MovieSalesDto>>("sales-by-movie", $"?from={From}&to={To}", admin);

        rows.Count.ShouldBe(2);
        var second = rows.Single(r => r.Title == titles[s2.Id]);
        var first = rows.Single(r => r.Title == titles[s1.Id]);
        (second.Revenue, second.Bookings, second.TicketsSold, second.Showtimes, second.Occupancy).ShouldBe((30m, 1, 2, 1, 0.2m));
        (first.Revenue, first.Bookings, first.TicketsSold, first.Occupancy).ShouldBe((30m, 1, 3, 0.3m));
        rows.Select(r => r.Revenue).ShouldBe(rows.Select(r => r.Revenue).OrderByDescending(r => r), "de más a menos ingresos");
        rows.Sum(r => r.Revenue).ShouldBe(60m);
    }

    [Fact]
    public async Task SalesByMovie_PutsTheBestSellingMovieFirst()
    {
        var cheap = await NewShowtimeAsync(rows: 1, perRow: 5, price: 5m, startsAtUtc: Day1, roomName: "Sala 1");
        var dear = await NewShowtimeAsync(rows: 1, perRow: 5, price: 50m, startsAtUtc: Day1, roomName: "Sala 2");
        var ana = await NewUserAsync();
        await BookedAsync(ana, cheap, "A1");
        await BookedAsync(ana, dear, "A1");
        var dearTitle = await Db(db => db.Showtimes.Where(x => x.Id == dear.Id).Select(x => x.Movie.Title).SingleAsync());

        var rows = await GetReport<List<MovieSalesDto>>("sales-by-movie", $"?from={From}&to={To}", await AdminAsync());

        rows[0].Title.ShouldBe(dearTitle);
        rows.Select(r => r.Revenue).ShouldBe([50m, 5m]);
    }

    [Fact]
    public async Task OccupancyByShowtime_ListsTheLeastOccupiedFirst_AndByRoomIsWeightedByCapacity()
    {
        var cinema = await Db(async db => (await Seeds.CinemaAsync(db)).Id);
        var small = await NewShowtimeAsync(rows: 1, perRow: 4, price: 10m, startsAtUtc: Day1, roomName: "Chica");     // 4 asientos
        var bigA = await NewShowtimeAsync(rows: 2, perRow: 8, price: 10m, startsAtUtc: Day1, roomName: "Grande");     // 16 asientos
        var bigB = await NewShowtimeInSameRoomAsync(bigA, Day1.AddHours(4));                                         // otra de 16 en la misma sala
        var ana = await NewUserAsync();
        await BookedAsync(ana, small, "A1", "A2", "A3", "A4");   // 4/4 = 100 %
        await BookedAsync(ana, bigA, "A1", "A2", "A3", "A4");    // 4/16 = 25 %
        var admin = await AdminAsync();
        cinema.ShouldBeGreaterThan(0);

        var byShowtime = await GetReport<PagedResult<OccupancyRowDto>>("occupancy", $"?from={From}&to={To}&groupBy=showtime", admin);
        byShowtime.TotalCount.ShouldBe(3);
        byShowtime.Items.Select(i => i.Occupancy).ShouldBe([0m, 0.25m, 1m], "de menos a más ocupada: la vacía primero");
        byShowtime.Items[0].Id.ShouldBe(bigB.Id);

        var byRoom = await GetReport<PagedResult<OccupancyRowDto>>("occupancy", $"?from={From}&to={To}&groupBy=room", admin);
        byRoom.Items.Count.ShouldBe(2);
        var grande = byRoom.Items.Single(i => i.Title.EndsWith("Grande"));
        (grande.Capacity, grande.SeatsSold, grande.Occupancy, grande.Showtimes).ShouldBe((32, 4, 0.125m, 2));   // 4 / 32, ponderado: no el promedio de 25 % y 0 %
        byRoom.Items.Single(i => i.Title.EndsWith("Chica")).Occupancy.ShouldBe(1m);
    }

    [Fact]
    public async Task TheOccupancyListIsPaged()
    {
        for (var i = 0; i < 3; i++)
            await NewShowtimeAsync(rows: 1, perRow: 3, price: 5m, startsAtUtc: Day1.AddHours(i * 3), roomName: $"Sala {i}");
        var admin = await AdminAsync();

        var page2 = await GetReport<PagedResult<OccupancyRowDto>>("occupancy", $"?from={From}&to={To}&page=2&pageSize=2", admin);

        (page2.TotalCount, page2.Items.Count, page2.Page).ShouldBe((3, 1, 2));
    }

    [Theory]
    [InlineData("", "from")]
    [InlineData("?from=2030-03-10", "to")]
    [InlineData("?to=2030-03-10", "from")]
    [InlineData("?from=2030-03-12&to=2030-03-10", "from")]
    [InlineData("?from=2030-01-01&to=2031-03-01", "to")]
    public async Task TheRangeIsValidated_MissingEndsAreReversedOrTooLong(string query, string field)
    {
        foreach (var report in new[] { "summary", "sales-daily", "sales-by-movie", "occupancy" })
        {
            var response = await Get($"/api/v1/reports/{report}{query}", await AdminAsync());

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, report);
            (await ReadProblem(response)).Errors!.ShouldContainKey(field);
        }
    }

    [Fact]
    public async Task ARangeOf366DaysIsTheLimit_AndAnInvalidGroupByIs400()
    {
        var admin = await AdminAsync();

        (await Get("/api/v1/reports/summary?from=2030-01-01&to=2030-12-31", admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Get("/api/v1/reports/summary?from=2030-01-01&to=2031-01-02", admin)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var bad = await Get($"/api/v1/reports/occupancy?from={From}&to={To}&groupBy=cine", admin);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(bad)).Errors!.ShouldContainKey("groupBy");
        (await Get($"/api/v1/reports/occupancy?from={From}&to={To}&pageSize=101", admin)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheViewIsTheSingleDefinition_ItAgreesWithTheSummaryProcedure()
    {
        await TcRep01Async();
        var admin = await AdminAsync();

        var rows = await Db(db => db.ShowtimeSales.AsNoTracking().OrderBy(s => s.StartsAt).ToListAsync());
        var summary = await GetReport<SalesSummaryDto>("summary", $"?from={From}&to={To}", admin);

        rows.Count.ShouldBe(2);
        rows.Sum(r => r.Revenue).ShouldBe(summary.Revenue);
        rows.Sum(r => r.SeatsSold).ShouldBe(summary.TicketsSold);
        rows.Select(r => r.Occupancy).ShouldBe([0.3m, 0.2m]);
        rows.All(r => r.Capacity == 10).ShouldBeTrue();
    }
}
