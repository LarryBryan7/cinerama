using Cinerama.Domain.Enums;
using Cinerama.Infrastructure.Persistence;
using Cinerama.Infrastructure.Seating;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cinerama.IntegrationTests.Seating;

/// <summary>sp_ReleaseExpiredSeatLocks y el BackgroundService que lo ejecuta (CU-06, ADR-005).</summary>
public class SeatLockCleanupTests(ApiFixture fixture) : ReservationTest(fixture)
{
    /// <summary>Deja los asientos indicados bloqueados por <paramref name="userId"/> hasta <paramref name="until"/>.</summary>
    private Task ForceLocks(TestShowtime showtime, int userId, DateTime until, params string[] labels) =>
        Db(async db =>
        {
            var seatIds = showtime.Ids(labels);
            foreach (var ss in await db.ShowSeats.Where(s => s.ShowtimeId == showtime.Id && seatIds.Contains(s.SeatId)).ToListAsync())
                (ss.Status, ss.LockedByUserId, ss.LockedUntil) = (ShowSeatStatus.Locked, userId, until);
            await db.SaveChangesAsync();
        });

    private static async Task<int> RunCleanup(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SeatLockCleanup>().RunOnceAsync();
    }

    private static async Task WaitUntil(Func<Task<bool>> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(50);
        }
        throw new Shouldly.ShouldAssertException($"Se agotó el tiempo esperando: {because}");
    }

    [Fact]
    [Trait("Case", "TC-SEAT-05")]
    public async Task Cleanup_ReleasesOnlyTheExpiredLocks_AndSendsOneEventPerShowtime()
    {
        var a = await NewShowtimeAsync(rows: 2, perRow: 3, roomName: "Sala A");
        var b = await NewShowtimeAsync(rows: 1, perRow: 3, roomName: "Sala B");
        var ana = await NewUserAsync();
        var now = DateTime.UtcNow;
        await ForceLocks(a, ana.Id, now.AddMinutes(-3), "A1", "A2", "B1"); // 3 vencidos
        await ForceLocks(a, ana.Id, now.AddMinutes(2), "A3");              // 1 vigente
        await ForceLocks(b, ana.Id, now.AddMinutes(-1), "A1");             // 1 vencido en otra función
        Fixture.Notifier.Clear();

        var released = await RunCleanup(Fixture.Factory);

        released.ShouldBe(4);
        foreach (var label in new[] { "A1", "A2", "B1" })
            (await SeatInDb(a, label)).ShouldBe(new ShowSeatSnapshot(ShowSeatStatus.Available, null, null));
        (await SeatInDb(a, "A3")).Status.ShouldBe(ShowSeatStatus.Locked);                  // el vigente no se toca
        (await SeatInDb(b, "A1")).Status.ShouldBe(ShowSeatStatus.Available);

        var events = Fixture.Notifier.Events;
        events.Count.ShouldBe(2); // un evento por función afectada
        events.Single(e => e.ShowtimeId == a.Id).Changes.Count.ShouldBe(3);
        events.Single(e => e.ShowtimeId == b.Id).Changes.Count.ShouldBe(1);
        events.SelectMany(e => e.Changes).ShouldAllBe(c => c.Status == ShowSeatStatus.Available);
    }

    [Fact]
    public async Task Cleanup_NeverTouchesSoldOrAvailableSeats()
    {
        var showtime = await NewShowtimeAsync(rows: 1, perRow: 3);
        var ana = await NewUserAsync();
        await BookedAsync(ana, showtime, "A1");
        Fixture.Notifier.Clear();

        (await RunCleanup(Fixture.Factory)).ShouldBe(0);

        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Booked);
        (await SeatInDb(showtime, "A2")).Status.ShouldBe(ShowSeatStatus.Available);
        Fixture.Notifier.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cleanup_IsIdempotent_ASecondRunReleasesAndNotifiesNothing()
    {
        var showtime = await NewShowtimeAsync(rows: 1, perRow: 3);
        await ForceLocks(showtime, (await NewUserAsync()).Id, DateTime.UtcNow.AddMinutes(-1), "A1");

        (await RunCleanup(Fixture.Factory)).ShouldBe(1);
        Fixture.Notifier.Clear();

        (await RunCleanup(Fixture.Factory)).ShouldBe(0);
        Fixture.Notifier.Events.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Case", "RN-23")]
    public async Task Cleanup_TheBoundary_ALockThatExpiresExactlyNowIsReleased_OneThatExpiresLaterIsNot()
    {
        await using var api = new TimedApi(Fixture);
        var showtime = await NewShowtimeAsync(rows: 1, perRow: 3);
        var ana = await NewUserAsync();
        var now = new DateTime(2026, 11, 5, 18, 30, 0, 123, DateTimeKind.Utc);
        api.Time.SetUtcNow(new DateTimeOffset(now));
        await ForceLocks(showtime, ana.Id, now, "A1");                                  // vence exactamente ahora
        await ForceLocks(showtime, ana.Id, now.AddMilliseconds(1), "A2");               // vence un milisegundo después

        (await RunCleanup(api.Factory)).ShouldBe(1);

        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Available);
        (await SeatInDb(showtime, "A2")).Status.ShouldBe(ShowSeatStatus.Locked);
    }

    [Fact]
    public async Task Cleanup_ReleasesMoreThanOneBatch()
    {
        // 600 bloqueos vencidos: el procedimiento libera de a 500, así que hacen falta dos pasadas internas.
        var showtime = await NewShowtimeAsync(rows: 25, perRow: 24);
        var ana = await NewUserAsync();
        await Db(db => db.ShowSeats.ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, ShowSeatStatus.Locked)
            .SetProperty(x => x.LockedByUserId, ana.Id)
            .SetProperty(x => x.LockedUntil, DateTime.UtcNow.AddMinutes(-1))));
        Fixture.Notifier.Clear();

        (await RunCleanup(Fixture.Factory)).ShouldBe(600);

        (await CountInDb(db => db.ShowSeats.CountAsync(s => s.Status == ShowSeatStatus.Locked))).ShouldBe(0);
        Fixture.Notifier.Events.Single().Changes.Count.ShouldBe(600);
    }

    [Fact]
    public async Task Cleanup_StillReleasesTheSeats_WhenTheNotificationServiceIsDown()
    {
        var showtime = await NewShowtimeAsync(rows: 1, perRow: 3);
        await ForceLocks(showtime, (await NewUserAsync()).Id, DateTime.UtcNow.AddMinutes(-1), "A1");
        Fixture.Notifier.ThrowOnNotify = true;

        (await RunCleanup(Fixture.Factory)).ShouldBe(1);

        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Available);
    }

    [Fact]
    public async Task TheStoredProcedure_ExistsWithItsDocumentedParameters()
    {
        var parameters = await Db(db => db.Database.SqlQueryRaw<string>("""
            SELECT p.name AS Value FROM sys.parameters p
            WHERE p.object_id = OBJECT_ID('dbo.sp_ReleaseExpiredSeatLocks') ORDER BY p.parameter_id
            """).ToListAsync());

        parameters.ShouldBe(["@NowUtc", "@BatchSize"]);
    }

    // ── El BackgroundService ────────────────────────────────────────────────

    [Fact]
    [Trait("Case", "CU-06")]
    public async Task TheBackgroundService_ReleasesExpiredLocksEvery30Seconds()
    {
        await using var api = new TimedApi(Fixture, new() { ["Seating:CleanupEnabled"] = "true", ["Seating:CleanupSeconds"] = "30" });
        var showtime = await NewShowtimeAsync(rows: 1, perRow: 4);
        var ana = await NewUserAsync();
        _ = api.Factory.Server; // arranca el host y, con él, el servicio en segundo plano

        // Primera pasada: un bloqueo ya vencido se libera cuando el reloj llega a los 30 s.
        await ForceLocks(showtime, ana.Id, api.Now.AddMinutes(-1), "A1");
        api.Time.Advance(TimeSpan.FromSeconds(30));
        await WaitUntil(async () => (await SeatInDb(showtime, "A1")).Status == ShowSeatStatus.Available, "la primera pasada de limpieza");
        api.Notifier.Events.Count.ShouldBe(1);

        // Segunda pasada: el proceso sigue vivo y vuelve a ejecutarse.
        await ForceLocks(showtime, ana.Id, api.Now.AddMinutes(-1), "A2");
        api.Time.Advance(TimeSpan.FromSeconds(30));
        await WaitUntil(async () => (await SeatInDb(showtime, "A2")).Status == ShowSeatStatus.Available, "la segunda pasada de limpieza");
        api.Notifier.Events.Count.ShouldBe(2);
    }

    [Fact]
    public async Task TheBackgroundService_DoesNothingBeforeItsFirstInterval()
    {
        await using var api = new TimedApi(Fixture, new() { ["Seating:CleanupEnabled"] = "true", ["Seating:CleanupSeconds"] = "30" });
        var showtime = await NewShowtimeAsync(rows: 1, perRow: 3);
        _ = api.Factory.Server;
        await ForceLocks(showtime, (await NewUserAsync()).Id, api.Now.AddMinutes(-1), "A1");

        api.Time.Advance(TimeSpan.FromSeconds(29));
        await Task.Delay(300); // margen para que una pasada indebida tuviera tiempo de ocurrir

        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Locked);
        api.Notifier.Events.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Case", "RN-23")]
    public async Task TheCleanupCanBeDisabled_AndTheSystemStaysCorrect()
    {
        await using var api = new TimedApi(Fixture); // CleanupEnabled=false es el valor por defecto de la factory de pruebas
        var showtime = await NewShowtimeAsync(rows: 1, perRow: 3);
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
        await LockSeats(api.As(ana), showtime, ["A1"], api.Client);

        api.Time.Advance(TimeSpan.FromHours(1));
        await Task.Delay(200);

        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Locked); // nadie lo limpió...
        (await LockSeats(api.As(luis), showtime, ["A1"], api.Client)).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK); // ...y aun así otro usuario lo toma
    }

    [Fact]
    public async Task TheBackgroundService_IsRegisteredAsAHostedService()
    {
        Fixture.Factory.Services.GetServices<IHostedService>().ShouldContain(s => s is SeatLockCleanupService);
        await Task.CompletedTask;
    }
}
