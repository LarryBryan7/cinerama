using System.Net;
using Cinerama.Application.Bookings;
using Cinerama.Application.Common;
using Cinerama.Domain.Enums;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Bookings;

/// <summary>Acceso a reservas ajenas (IDOR). Una reserva de otra persona responde exactamente igual que una inexistente (RN-32).</summary>
public class BookingAuthorizationTests(ApiFixture fixture) : ReservationTest(fixture)
{
    private async Task<(TestShowtime Showtime, TestUser Ana, TestUser Luis, BookingDto AnasBooking)> ScenarioAsync()
    {
        var showtime = await NewShowtimeAsync(rows: 2, perRow: 5, startsAtUtc: DateTime.UtcNow.AddHours(24));
        var ana = await NewUserAsync();
        var luis = await NewUserAsync();
        return (showtime, ana, luis, await BookedAsync(ana, showtime, "A1"));
    }

    [Fact]
    [Trait("Case", "TC-SEC-04")]
    public async Task Get_AnotherUsersBooking_Returns404_IdenticalToAnUnknownId()
    {
        var (_, ana, luis, booking) = await ScenarioAsync();

        var foreign = await Get($"/api/v1/bookings/{booking.Id}", luis.Token);
        var unknown = await Get("/api/v1/bookings/999999", luis.Token);

        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound); // 404, no 403: no se revela que existe
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var a = await ReadProblem(foreign);
        var b = await ReadProblem(unknown);
        (a.Code, a.Title, a.Detail, a.Status).ShouldBe((b.Code, b.Title, b.Detail, b.Status));

        (await Get($"/api/v1/bookings/{booking.Id}", ana.Token)).StatusCode.ShouldBe(HttpStatusCode.OK); // la dueña sí la ve
    }

    [Fact]
    [Trait("Case", "TC-SEC-04")]
    public async Task Get_SequentialIdsOfOthers_CannotBeEnumerated()
    {
        var showtime = await NewShowtimeAsync(rows: 3, perRow: 5, startsAtUtc: DateTime.UtcNow.AddHours(24));
        var ana = await NewUserAsync();
        var luis = await NewUserAsync();
        var ids = new List<int>();
        foreach (var label in new[] { "A1", "A2", "A3" })
            ids.Add((await BookedAsync(ana, showtime, label)).Id);

        foreach (var id in ids)
            (await Get($"/api/v1/bookings/{id}", luis.Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Case", "TC-SEC-05")]
    public async Task Cancel_AnotherUsersBooking_Returns404_AndNothingChanges()
    {
        var (showtime, _, luis, booking) = await ScenarioAsync();
        Fixture.Notifier.Clear();

        var response = await CancelBooking(luis, booking.Id);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadProblem(response)).Code.ShouldBe("booking_not_found");
        (await CountInDb(db => db.Bookings.CountAsync(b => b.Status == BookingStatus.Confirmed))).ShouldBe(1);
        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Booked);
        Fixture.Notifier.Events.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Case", "TC-SEC-06")]
    public async Task List_ReturnsOnlyMyBookings_AndIgnoresAnyOwnerParameter()
    {
        var showtime = await NewShowtimeAsync(rows: 2, perRow: 5, startsAtUtc: DateTime.UtcNow.AddHours(24));
        var ana = await NewUserAsync();
        var luis = await NewUserAsync();
        await BookedAsync(ana, showtime, "A1");
        await BookedAsync(ana, showtime, "A2");
        await BookedAsync(luis, showtime, "B1");

        var anas = await Read<PagedResult<BookingListItemDto>>(await Get("/api/v1/bookings", ana.Token));
        var spoofed = await Read<PagedResult<BookingListItemDto>>(await Get($"/api/v1/bookings?userId={luis.Id}&UserId={luis.Id}", ana.Token));

        anas.TotalCount.ShouldBe(2);
        spoofed.Items.Select(b => b.Id).ShouldBe(anas.Items.Select(b => b.Id)); // el parámetro no existe: se ignora
        (await Read<PagedResult<BookingListItemDto>>(await Get("/api/v1/bookings", luis.Token))).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Confirm_UsingSomeoneElsesIdempotencyKey_NeverReturnsTheirBooking()
    {
        var (showtime, _, luis, booking) = await ScenarioAsync();
        await LockSeats(luis, showtime, ["B1"]);

        var response = await Book(luis, showtime, ["B1"], key: "una-clave"); // la misma clave que usó Ana no importa: es por usuario

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Read<BookingDto>(response)).Id.ShouldNotBe(booking.Id);
    }

    [Fact]
    public async Task AnAdmin_CanReadAnyBooking_ButTheListIsStillTheirOwn()
    {
        var (_, _, _, booking) = await ScenarioAsync();
        var admin = await NewUserAsync(admin: true);

        (await Get($"/api/v1/bookings/{booking.Id}", admin.Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Read<PagedResult<BookingListItemDto>>(await Get("/api/v1/bookings", admin.Token))).TotalCount.ShouldBe(0);
        // El listado general del administrador con filtros llega con el panel (fase 4).
    }

    [Fact]
    public async Task BookingEndpoints_RequireAuthentication()
    {
        var (_, _, _, booking) = await ScenarioAsync();

        (await Get("/api/v1/bookings")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Get($"/api/v1/bookings/{booking.Id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Post($"/api/v1/bookings/{booking.Id}/cancel")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Post("/api/v1/bookings", new { showtimeId = 1, seatIds = new[] { 1 } })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
