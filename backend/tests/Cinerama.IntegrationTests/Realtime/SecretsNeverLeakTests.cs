using System.Net;
using Cinerama.Application.Bookings;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Cinerama.IntegrationTests.Realtime;

/// <summary>
/// Las claves (JWT y ticket) y la contraseña de la base viven solo en configuración. Estos tests recorren los flujos
/// críticos, incluido el WebSocket con el token en la URL, y comprueban que no aparecen en los registros ni en las respuestas.
/// </summary>
public class SecretsNeverLeakTests(ApiFixture fixture) : RealtimeTest(fixture)
{
    [Fact]
    [Trait("Case", "TC-SEC-13")]
    public async Task NoSecretOrTokenEndsUpInTheLogsOrInTheResponses()
    {
        var responses = new List<string>();
        async Task<HttpResponseMessage> Seen(Task<HttpResponseMessage> call)
        {
            var response = await call;
            responses.Add(await response.Content.ReadAsStringAsync());
            return response;
        }

        // Cuenta real (con refresh token), reservas, errores de todo tipo y una conexión de tiempo real.
        var session = await RegisterAsync("secretos@example.test");
        var user = new TestUser(session.User.Id, session.User.Email, session.AccessToken);
        var other = await NewUserAsync();
        var showtime = await NewShowtimeAsync(startsAtUtc: DateTime.UtcNow.AddHours(24));
        var watcher = await ConnectedAsync(user, showtime.Id);

        await Seen(Lock(other, showtime, "A1"));
        await watcher.NextEventAsync();
        await Seen(Lock(user, showtime, "A1"));                                   // 409
        await Seen(Lock(user, showtime, "A2"));
        var booked = await Seen(Book(user, showtime, ["A2"], client: Http));
        var booking = await Read<BookingDto>(booked);
        await Seen(Get($"/api/v1/bookings/{booking.Id}", user.Token));
        await Seen(Get("/api/v1/bookings", user.Token));
        await Seen(Get($"/api/v1/showtimes/{showtime.Id}/seats", user.Token));
        await Seen(Get("/api/v1/bookings/999999", user.Token));                   // 404
        await Seen(Post("/api/v1/auth/login", new { email = "secretos@example.test", password = "Incorrecta1" }));       // 401
        await Seen(Post("/api/v1/auth/refresh", new { refreshToken = "no-existe" }));                                    // 401
        await Seen(Get("/api/v1/me", "token-invalido"));                          // 401
        await Seen(Send(HttpMethod.Post, "/hubs/seats/negotiate?negotiateVersion=1", new { }, TestTokens.IssuedAt(DateTimeOffset.UtcNow.AddHours(-1), user.Id), client: Http));
        await Seen(CancelBooking(user, booking.Id, Http));
        await Seen(Get("/health"));
        await Seen(Get("/api/v1/ruta-inexistente", user.Token));

        var sqlPassword = new SqlConnectionStringBuilder(Fixture.ConnectionString).Password;
        string[] neverInLogs =
        [
            ApiFixture.TicketKey, ApiFixture.JwtKey, sqlPassword,
            user.Token, session.RefreshToken!,
            "access_token=",   // el token del WebSocket viaja en la URL: no debe quedar en ninguna línea de registro
        ];
        string[] neverInResponses = [ApiFixture.TicketKey, ApiFixture.JwtKey, sqlPassword];

        var logs = string.Join("\n", Api.Logs.Lines);
        logs.ShouldNotBeNullOrWhiteSpace();
        foreach (var secret in neverInLogs)
            logs.ShouldNotContain(secret, Case.Sensitive);

        foreach (var secret in neverInResponses)
            foreach (var body in responses)
                body.ShouldNotContain(secret, Case.Sensitive);
    }

    [Fact]
    public async Task TheTicketPayload_ExposesTheSignatureButNeverTheKey()
    {
        var showtime = await NewShowtimeAsync();
        var user = await NewUserAsync();
        await Lock(user, showtime, "A1");

        var json = await (await Book(user, showtime, ["A1"], client: Http)).Content.ReadAsStringAsync();
        var booking = System.Text.Json.JsonSerializer.Deserialize<BookingDto>(json, Json)!;

        booking.TicketPayload.ShouldStartWith(booking.Code + ".");
        json.ShouldNotContain(ApiFixture.TicketKey);
        booking.TicketPayload.ShouldNotContain(ApiFixture.TicketKey);
    }
}
