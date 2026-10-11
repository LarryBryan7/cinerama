using System.Net;
using Cinerama.Application.Bookings;
using Cinerama.Domain.Enums;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Seating;

/// <summary>RN-42: cuándo se pueden reservar asientos. La preventa permite reservar antes del estreno; "próximamente" no.</summary>
public class PreSaleSalesTests(ApiFixture fixture) : ReservationTest(fixture)
{
    private static DateOnly LocalToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Lima")));

    /// <summary>Función de una película con las fechas dadas, en una sala de 2 filas × 5.</summary>
    private async Task<TestShowtime> ShowtimeOfAsync(DateOnly release, DateOnly? preSale, bool cinemaActive = true)
    {
        var starts = DateTime.UtcNow.AddDays(2);
        return await Db(async db =>
        {
            var cinema = await Seeds.CinemaAsync(db, "Cine de la función " + Guid.NewGuid().ToString("N")[..5], active: cinemaActive);
            var room = await Seeds.RoomAsync(db, "Sala T", 2, 5, cinema: cinema);
            var movie = await Seeds.MovieWithReleaseAsync(db, "Película " + Guid.NewGuid().ToString("N")[..6], release, preSale);
            var showtime = await Seeds.ShowtimeAsync(db, movie, room, starts, 10m);
            var seats = await db.Seats.Where(s => s.CinemaRoomId == room.Id).OrderBy(s => s.RowLabel).ThenBy(s => s.Number)
                .Select(s => new { s.Id, s.RowLabel, s.Number }).ToListAsync();
            return new TestShowtime(showtime.Id, room.Id, 10m, starts, seats.Select(s => new TestSeat(s.Id, $"{s.RowLabel}{s.Number}")).ToList());
        });
    }

    [Fact]
    [Trait("Case", "RN-42")]
    public async Task DuringThePreSale_SeatsCanBeLockedAndBooked_BeforeThePremiere()
    {
        var today = LocalToday();
        var showtime = await ShowtimeOfAsync(release: today.AddDays(5), preSale: today.AddDays(-1));
        var ana = await NewUserAsync();

        var booking = await BookedAsync(ana, showtime, "A1", "A2"); // bloquear + confirmar: 200 y 201

        booking.Status.ShouldBe("Confirmed");
        (await Db(db => db.BookingSeats.CountAsync(b => b.ReleasedAt == null))).ShouldBe(2);
    }

    [Theory]
    [InlineData(30, null)]   // próximamente, sin preventa
    [InlineData(30, 10)]     // próximamente: la preventa abrirá más adelante
    [InlineData(2, 1)]       // la preventa abre mañana
    public async Task BeforeThePreSaleOpens_NothingCanBeLockedOrBooked_AndNothingChangesInTheDatabase(int releaseOffset, int? preSaleOffset)
    {
        var today = LocalToday();
        var showtime = await ShowtimeOfAsync(today.AddDays(releaseOffset), preSaleOffset is { } d ? today.AddDays(d) : null);
        var ana = await NewUserAsync();

        var lockResponse = await LockSeats(ana, showtime, ["A1"]);
        var bookResponse = await Book(ana, showtime, ["A1"], key: Guid.NewGuid().ToString());

        foreach (var response in new[] { lockResponse, bookResponse })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await ReadProblem(response)).Code.ShouldBe("sales_not_open");
        }

        (await Db(db => db.ShowSeats.CountAsync(s => s.Status != ShowSeatStatus.Available))).ShouldBe(0);
        (await Db(db => db.Bookings.CountAsync())).ShouldBe(0);
        Fixture.Notifier.Events.ShouldBeEmpty(); // y nadie recibe avisos de algo que no pasó
    }

    [Fact]
    [Trait("Case", "RN-42")]
    public async Task ThePremiereOpensTheSales_WhenThereIsNoPreSale()
    {
        var today = LocalToday();
        var showtime = await ShowtimeOfAsync(release: today, preSale: null);

        (await LockSeats(await NewUserAsync(), showtime, ["A1"])).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Case", "RN-50")]
    public async Task AShowtimeInAClosedCinema_CannotBeSold()
    {
        var showtime = await ShowtimeOfAsync(LocalToday().AddDays(-10), null, cinemaActive: false);

        var response = await LockSeats(await NewUserAsync(), showtime, ["A1"]);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(response)).Code.ShouldBe("cinema_closed");
    }

    [Fact]
    public async Task ReleasingAndCancelling_StillWork_EvenIfTheSalesWindowCloses()
    {
        // Quien ya reservó puede cancelar (RN-30) aunque la película cambie de estado: la guarda de venta no toca estas operaciones.
        var today = LocalToday();
        var showtime = await ShowtimeOfAsync(release: today.AddDays(5), preSale: today.AddDays(-1));
        var ana = await NewUserAsync();
        var booking = await BookedAsync(ana, showtime, "B1");

        (await CancelBooking(ana, booking.Id)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
