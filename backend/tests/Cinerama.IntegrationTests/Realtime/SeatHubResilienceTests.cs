using System.Net;
using Cinerama.Api.Realtime;
using Cinerama.Application.Bookings;
using Cinerama.Application.Common;
using Cinerama.Domain.Enums;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cinerama.IntegrationTests.Realtime;

/// <summary>Reconexión y SignalR caído: REST es la fuente de verdad, los avisos solo aceleran la pantalla.</summary>
public class SeatHubReconnectionTests(ApiFixture fixture) : RealtimeTest(fixture)
{
    [Fact]
    [Trait("Case", "TC-RT-06")]
    public async Task AfterReconnecting_NothingIsReplayed_TheGroupsAreLost_AndTheRestMapHasTheFullTruth()
    {
        var showtime = await NewShowtimeAsync();
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
        var watcher = await ConnectedAsync(luis, showtime.Id);
        await Lock(ana, showtime, "A1");
        await watcher.NextEventAsync();

        // Se cae la conexión y, mientras tanto, cambian cosas.
        await watcher.Connection.StopAsync();
        await Lock(ana, showtime, "A2");
        await Book(ana, showtime, ["A1"], client: Http);

        await watcher.Connection.StartAsync(); // conexión nueva
        await watcher.AssertNoEventAsync();    // SignalR no repite lo que se perdió

        // El mapa por REST sí trae TODO el estado actual: ahí está la verdad.
        var map = (await MapAsync(luis, showtime, Http)).Seats.ToDictionary(s => s.Label);
        map["A1"].Status.ShouldBe("Booked");
        map["A2"].Status.ShouldBe("Locked");

        // Al reconectar se pierden los grupos: hasta que el cliente se vuelva a suscribir, no llega nada.
        await Lock(ana, showtime, "A3");
        await watcher.AssertNoEventAsync();

        await watcher.Join(showtime.Id);
        await Lock(ana, showtime, "A4");
        (await watcher.NextEventAsync()).GetProperty("changes")[0].GetProperty("seatId").GetInt32().ShouldBe(showtime.Seat("A4").SeatId);
    }

    [Fact]
    public async Task AClientThatWasNeverConnected_StillSeesTheTruthThroughRest()
    {
        var showtime = await NewShowtimeAsync();
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());

        await Lock(ana, showtime, "A1");
        await Lock(ana, showtime, "A2");

        var map = (await MapAsync(luis, showtime, Http)).Seats.ToDictionary(s => s.Label);
        map["A1"].Status.ShouldBe("Locked");
        map["A2"].Status.ShouldBe("Locked");
        map["A3"].Status.ShouldBe("Available");
    }
}

/// <summary>
/// Los avisos solo se envían después del commit. El decorador lee la base, desde otra conexión y con un tiempo de espera
/// corto, justo antes de entregar cada aviso a SignalR: si el aviso saliera antes del commit, la fila seguiría bloqueada
/// por la transacción de la API y la lectura fallaría.
/// </summary>
public class SeatHubCommitOrderTests(ApiFixture fixture) : RealtimeTest(fixture)
{
    private readonly List<(int SeatId, ShowSeatStatus Seen)> _seenAtNotificationTime = [];

    protected override Action<IServiceCollection> ConfigureApi => services =>
    {
        var connectionString = Fixture.ConnectionString;
        services.RemoveAll<ISeatNotifier>();
        services.AddSingleton<ISeatNotifier>(sp => new CommitCheckingNotifier(
            new SignalRSeatNotifier(sp.GetRequiredService<IHubContext<SeatHub>>()), connectionString, _seenAtNotificationTime));
    };

    private sealed class CommitCheckingNotifier(ISeatNotifier inner, string connectionString, List<(int, ShowSeatStatus)> seen) : ISeatNotifier
    {
        public async Task NotifyAsync(int showtimeId, IReadOnlyList<SeatChange> changes, CancellationToken ct = default)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await new SqlCommand("SET LOCK_TIMEOUT 3000", connection).ExecuteNonQueryAsync(ct);

            foreach (var change in changes)
            {
                await using var command = new SqlCommand("SELECT Status FROM ShowSeats WHERE ShowtimeId = @s AND SeatId = @seat", connection);
                command.Parameters.AddWithValue("@s", showtimeId);
                command.Parameters.AddWithValue("@seat", change.SeatId);
                var status = Enum.Parse<ShowSeatStatus>((string)(await command.ExecuteScalarAsync(ct))!);
                lock (seen) seen.Add((change.SeatId, status));
            }

            await inner.NotifyAsync(showtimeId, changes, ct);
        }
    }

    [Fact]
    [Trait("Case", "TC-RT-07")]
    public async Task EveryEvent_IsSentAfterTheChangeIsCommitted()
    {
        var showtime = await NewShowtimeAsync(startsAtUtc: DateTime.UtcNow.AddHours(24));
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
        var watcher = await ConnectedAsync(luis, showtime.Id);
        var a1 = showtime.Seat("A1").SeatId;

        await Lock(ana, showtime, "A1");
        await watcher.NextEventAsync();
        var booking = await Read<BookingDto>(await Book(ana, showtime, ["A1"], client: Http));
        await watcher.NextEventAsync();
        (await CancelBooking(ana, booking.Id, Http)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await watcher.NextEventAsync();

        // Al momento de cada aviso, la base ya mostraba el estado nuevo.
        _seenAtNotificationTime.ShouldBe([(a1, ShowSeatStatus.Locked), (a1, ShowSeatStatus.Booked), (a1, ShowSeatStatus.Available)]);
    }

    [Fact]
    [Trait("Case", "TC-RT-07")]
    public async Task OperationsThatFail_EmitNothing()
    {
        var showtime = await NewShowtimeAsync();
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());
        await Lock(ana, showtime, "A1");
        var watcher = await ConnectedAsync(luis, showtime.Id);

        (await Lock(luis, showtime, "A1")).StatusCode.ShouldBe(HttpStatusCode.Conflict);                     // asiento de otra persona
        (await Lock(luis, showtime, "A2", "A1")).StatusCode.ShouldBe(HttpStatusCode.Conflict);               // todo o nada
        (await Book(luis, showtime, ["A2"], client: Http)).StatusCode.ShouldBe(HttpStatusCode.Conflict);     // sin bloqueo previo
        (await ReleaseSeats(luis, showtime, ["A1"], Http)).StatusCode.ShouldBe(HttpStatusCode.NotFound);     // no es suyo
        (await LockIds(luis, showtime.Id, [], Http)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);         // inválido

        await watcher.AssertNoEventAsync(TimeSpan.FromSeconds(1.5)); // ni un aviso: ningún cambio llegó a confirmarse
        (await SeatInDb(showtime, "A2")).Status.ShouldBe(ShowSeatStatus.Available);
    }
}

public class SeatHubDownTests(ApiFixture fixture) : RealtimeTest(fixture)
{
    protected override Action<IServiceCollection> ConfigureApi => services =>
        services.Replace(ServiceDescriptor.Singleton<IHubContext<SeatHub>>(new BrokenHubContext()));

    [Fact]
    [Trait("Case", "TC-RT-08")]
    public async Task WhenSignalRIsDown_TheFullFlowStillWorksThroughRest_AndTheFailureIsOnlyLogged()
    {
        var showtime = await NewShowtimeAsync(startsAtUtc: DateTime.UtcNow.AddHours(24));
        var (ana, luis) = (await NewUserAsync(), await NewUserAsync());

        var locked = await Lock(ana, showtime, "A1", "A2");
        locked.StatusCode.ShouldBe(HttpStatusCode.OK);

        // REST sigue siendo la fuente de verdad: otro cliente ve el estado correcto, sin ningún aviso.
        var map = (await MapAsync(luis, showtime, Http)).Seats.ToDictionary(s => s.Label);
        map["A1"].Status.ShouldBe("Locked");
        (await Lock(luis, showtime, "A1")).StatusCode.ShouldBe(HttpStatusCode.Conflict); // la concurrencia se sigue resolviendo en la base

        var booking = await Book(ana, showtime, ["A1", "A2"], client: Http);
        booking.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CancelBooking(ana, (await Read<BookingDto>(booking)).Id, Http)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SeatInDb(showtime, "A1")).Status.ShouldBe(ShowSeatStatus.Available);

        Api.Logs.Lines.ShouldContain(l => l.Contains("No se pudo notificar") && l.Contains("[Warning]")); // se registra, no se propaga
        Api.ServerErrors.Drain().ShouldBeEmpty(); // y nunca fue un error del servidor (500)
    }
}
