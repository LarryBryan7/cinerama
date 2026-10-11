using System.Net;
using System.Reflection;
using System.Text.Json;
using Cinerama.Api.Realtime;
using Cinerama.Domain.Enums;
using Cinerama.Infrastructure.Persistence;
using Cinerama.Infrastructure.Seating;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cinerama.IntegrationTests.Realtime;

/// <summary>Lo que ven los clientes conectados: dos clientes, aislamiento por función, qué viaja y qué no.</summary>
public class SeatHubTests(ApiFixture fixture) : RealtimeTest(fixture)
{
    private static (int ShowtimeId, List<(int SeatId, string Status)> Changes) Parse(JsonElement e) =>
        (e.GetProperty("showtimeId").GetInt32(),
         e.GetProperty("changes").EnumerateArray().Select(c => (c.GetProperty("seatId").GetInt32(), c.GetProperty("status").GetString()!)).ToList());

    /// <summary>Verifica función y cambios de un aviso (compara el contenido de la lista, no su referencia).</summary>
    private static void ShouldBeEvent(JsonElement message, int showtimeId, params (int SeatId, string Status)[] expected)
    {
        var (id, changes) = Parse(message);
        id.ShouldBe(showtimeId);
        changes.ShouldBe(expected.ToList());
    }

    // ── Dos clientes ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "TC-RT-01")]
    public async Task TwoClientsWatchingTheSameShowtime_BothSeeEachOthersChanges()
    {
        var showtime = await NewShowtimeAsync(startsAtUtc: DateTime.UtcNow.AddHours(24));
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
        var (anasScreen, luissScreen) = (await ConnectedAsync(ana, showtime.Id), await ConnectedAsync(luis, showtime.Id));
        var a1 = showtime.Seat("A1").SeatId;

        // Ana bloquea: Luis lo ve sin recargar.
        (await Lock(ana, showtime, "A1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBeEvent(await luissScreen.NextEventAsync(), showtime.Id, (a1, "Locked"));
        ShouldBeEvent(await anasScreen.NextEventAsync(), showtime.Id, (a1, "Locked"));   // quien bloqueó también recibe el eco

        // Ana confirma: el asiento pasa a vendido para todos.
        (await Book(ana, showtime, ["A1"], client: Http)).StatusCode.ShouldBe(HttpStatusCode.Created);
        Parse(await luissScreen.NextEventAsync()).Changes.ShouldBe([(a1, "Booked")]);
        await anasScreen.NextEventAsync();

        // Luis bloquea otro y lo libera.
        await Lock(luis, showtime, "A2");
        (await ReleaseSeats(luis, showtime, ["A2"], Http)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var a2 = showtime.Seat("A2").SeatId;
        Parse(await anasScreen.NextEventAsync()).Changes.ShouldBe([(a2, "Locked")]);
        Parse(await anasScreen.NextEventAsync()).Changes.ShouldBe([(a2, "Available")]);
    }

    [Fact]
    public async Task CancellingABooking_AnnouncesItsSeatsAreAvailableAgain()
    {
        var showtime = await NewShowtimeAsync(startsAtUtc: DateTime.UtcNow.AddHours(24));
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
        await Lock(ana, showtime, "A1", "A2");
        var booking = await Read<Cinerama.Application.Bookings.BookingDto>(await Book(ana, showtime, ["A1", "A2"], client: Http));
        var watcher = await ConnectedAsync(luis, showtime.Id);

        (await CancelBooking(ana, booking.Id, Http)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var (_, changes) = Parse(await watcher.NextEventAsync());
        changes.Select(c => c.Status).ShouldAllBe(s => s == "Available");
        changes.Select(c => c.SeatId).Order().ShouldBe(showtime.Ids("A1", "A2").Order());
    }

    [Fact]
    public async Task WhenTheCleanupReleasesExpiredLocks_ConnectedClientsSeeIt()
    {
        var showtime = await NewShowtimeAsync();
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
        await Db(async db =>
        {
            var ss = await db.ShowSeats.FirstAsync(s => s.SeatId == showtime.Seat("A1").SeatId);
            (ss.Status, ss.LockedByUserId, ss.LockedUntil) = (ShowSeatStatus.Locked, ana.Id, DateTime.UtcNow.AddMinutes(-1));
            await db.SaveChangesAsync();
        });
        var watcher = await ConnectedAsync(luis, showtime.Id);

        await using (var scope = Api.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<SeatLockCleanup>().RunOnceAsync()).ShouldBe(1);

        Parse(await watcher.NextEventAsync()).Changes.ShouldBe([(showtime.Seat("A1").SeatId, "Available")]);
    }

    // ── Qué viaja ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "RN-34")]
    public async Task TheEvent_CarriesOnlyShowtimeSeatAndStatus_NeverWhoHoldsTheSeat()
    {
        var showtime = await NewShowtimeAsync();
        var ana = await NewUserAsync("ana-privada@example.test");
        var luis = await NewUserAsync();
        var watcher = await ConnectedAsync(luis, showtime.Id);

        await Lock(ana, showtime, "A1");
        var message = await watcher.NextEventAsync();

        message.EnumerateObject().Select(p => p.Name).Order().ShouldBe(["changes", "showtimeId"]);
        message.GetProperty("changes")[0].EnumerateObject().Select(p => p.Name).Order().ShouldBe(["seatId", "status"]);
        var raw = message.GetRawText();
        raw.ShouldNotContain("ana-privada");
        raw.ShouldNotContain($"\"{ana.Id}\"");
        raw.ToLowerInvariant().ShouldNotContain("user");
        raw.ToLowerInvariant().ShouldNotContain("lockedby");
        raw.ToLowerInvariant().ShouldNotContain("until"); // tampoco cuándo vence: eso solo lo sabe quien bloqueó
    }

    // ── Aislamiento por función ─────────────────────────────────────────────

    [Fact]
    [Trait("Case", "TC-RT-03")]
    public async Task EachShowtimeIsItsOwnGroup_NobodyReceivesAnotherShowtimesChanges()
    {
        var first = await NewShowtimeAsync(roomName: "Sala 1");
        var second = await NewShowtimeAsync(roomName: "Sala 2");
        var (ana, a, b, nobody) = (await NewUserAsync(), await NewUserAsync(), await NewUserAsync(), await NewUserAsync());
        var watchingFirst = await ConnectedAsync(a, first.Id);
        var watchingSecond = await ConnectedAsync(b, second.Id);
        var watchingNothing = await ConnectedAsync(nobody);
        var watchingBoth = await ConnectedAsync(await NewUserAsync(), first.Id, second.Id);

        await Lock(ana, first, "A1");

        ShouldBeEvent(await watchingFirst.NextEventAsync(), first.Id, (first.Seat("A1").SeatId, "Locked"));
        Parse(await watchingBoth.NextEventAsync()).ShowtimeId.ShouldBe(first.Id);
        await watchingSecond.AssertNoEventAsync();   // otra función
        await watchingNothing.AssertNoEventAsync();  // conectado, pero sin suscribirse

        await Lock(ana, second, "A1");
        Parse(await watchingSecond.NextEventAsync()).ShowtimeId.ShouldBe(second.Id);
        Parse(await watchingBoth.NextEventAsync()).ShowtimeId.ShouldBe(second.Id); // quien mira las dos recibe las dos
        await watchingFirst.AssertNoEventAsync();
    }

    [Fact]
    public async Task AfterLeaving_TheClientStopsReceivingThatShowtime()
    {
        var showtime = await NewShowtimeAsync();
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
        var watcher = await ConnectedAsync(luis, showtime.Id);
        await Lock(ana, showtime, "A1");
        await watcher.NextEventAsync();

        await watcher.Leave(showtime.Id);
        await Lock(ana, showtime, "A2");

        await watcher.AssertNoEventAsync();
    }

    // ── Suscripciones ───────────────────────────────────────────────────────

    [Fact]
    public async Task JoiningAnUnknownShowtime_IsRejected()
    {
        var hub = await ConnectedAsync(await NewUserAsync());

        var ex = await Should.ThrowAsync<HubException>(() => hub.Join(999999));

        ex.Message.ShouldContain("showtime_not_found");
    }

    [Fact]
    public async Task AConnectionCanWatchAtMostFiveShowtimes_AndJoiningTwiceIsHarmless()
    {
        var showtimes = new List<TestShowtime>();
        for (var i = 0; i < 6; i++)
            showtimes.Add(await NewShowtimeAsync(roomName: $"Sala {i}"));
        var hub = await ConnectedAsync(await NewUserAsync());

        foreach (var s in showtimes.Take(5))
            await hub.Join(s.Id);
        await hub.Join(showtimes[0].Id); // repetida: no cuenta ni falla

        var ex = await Should.ThrowAsync<HubException>(() => hub.Join(showtimes[5].Id));
        ex.Message.ShouldContain("too_many_subscriptions");

        await hub.Leave(showtimes[0].Id);
        await Should.NotThrowAsync(() => hub.Join(showtimes[5].Id)); // al liberar un lugar, cabe otra
    }

    [Fact]
    [Trait("Case", "ADR-016")]
    public async Task TheHub_OnlyListens_ItHasNoMethodThatLocksReleasesOrBooks()
    {
        typeof(SeatHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).Order().ShouldBe(["JoinShowtime", "LeaveShowtime"]);

        // Y un cliente que intenta invocar una escritura por el hub no consigue nada.
        var showtime = await NewShowtimeAsync();
        var hub = await ConnectedAsync(await NewUserAsync(), showtime.Id);
        await Should.ThrowAsync<HubException>(() => hub.Connection.InvokeAsync("LockSeats", showtime.Id, new[] { showtime.Seat("A1").SeatId }));
        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Available);
    }
}
