using System.Net;
using Cinerama.Application.Auth;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Security;

/// <summary>
/// Acceso a datos ajenos y escalada de privilegios. En la fase 1 la superficie es el perfil y las sesiones;
/// la suite de reservas ajenas (TC-SEC-04 a 06) se agrega con las reservas en la fase 2.
/// </summary>
public class IdorTests(ApiFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    [Trait("Case", "RN-34")]
    public async Task Me_AlwaysReturnsTheTokenOwner_EachUserSeesOnlyTheirOwnProfile()
    {
        var ana = await RegisterAsync("ana@example.test");
        var luis = await RegisterAsync("luis@example.test");

        (await Read<UserDto>(await Get("/api/v1/me", ana.AccessToken))).Email.ShouldBe("ana@example.test");
        (await Read<UserDto>(await Get("/api/v1/me", luis.AccessToken))).Email.ShouldBe("luis@example.test");
    }

    [Theory]
    [Trait("Case", "TC-SEC-06")]
    [InlineData("/api/v1/me?id=1")]
    [InlineData("/api/v1/me?userId=1")]
    [InlineData("/api/v1/me?role=Admin")]
    public async Task Me_IgnoresAnyIdentityParameterInTheQueryString(string url)
    {
        await RegisterAsync("ana@example.test"); // id 1
        var luis = await RegisterAsync("luis@example.test"); // id 2

        var profile = await Read<UserDto>(await Get(url, luis.AccessToken));

        profile.Email.ShouldBe("luis@example.test");
        profile.Role.ShouldBe("Customer");
    }

    [Fact]
    [Trait("Case", "RN-34")]
    public async Task UpdateMe_IgnoresIdRoleEmailAndStatusFromTheBody()
    {
        var ana = await RegisterAsync("ana@example.test");
        var luis = await RegisterAsync("luis@example.test");

        var response = await Send(HttpMethod.Put, "/api/v1/me", new
        {
            fullName = "Nombre Nuevo", id = luis.User.Id, role = "Admin", roleId = 1, email = "otro@example.test", isActive = false,
        }, ana.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await Read<UserDto>(response);
        (updated.Id, updated.Email, updated.Role, updated.FullName).ShouldBe((ana.User.Id, "ana@example.test", "Customer", "Nombre Nuevo"));

        // Los datos del otro usuario no se tocaron.
        var other = await Db(db => db.Users.SingleAsync(u => u.Id == luis.User.Id));
        other.FullName.ShouldNotBe("Nombre Nuevo");
        other.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateMe_WithAnEmptyName_Returns400()
    {
        var ana = await RegisterAsync("ana@example.test");

        var response = await Send(HttpMethod.Put, "/api/v1/me", new { fullName = "" }, ana.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Case", "TC-SEC-03")]
    public async Task ACustomer_CannotDeactivateAnotherUser_AndTheDatabaseDoesNotChange()
    {
        var attacker = await RegisterAsync("atacante@example.test");
        var victim = await RegisterAsync("victima@example.test");

        var response = await Send(HttpMethod.Put, $"/api/v1/users/{victim.User.Id}/status", new { isActive = false }, attacker.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Db(db => db.Users.SingleAsync(u => u.Id == victim.User.Id))).IsActive.ShouldBeTrue();
    }

    [Fact]
    [Trait("Case", "RN-04")]
    public async Task ACustomer_CannotPromoteThemselvesThroughAnyEndpoint()
    {
        var ana = await RegisterAsync("ana@example.test");

        await Send(HttpMethod.Put, "/api/v1/me", new { fullName = "Ana", role = "Admin", roleId = 1 }, ana.AccessToken);
        var viaLogin = await Post("/api/v1/auth/login", new { email = "ana@example.test", password = DefaultPassword, role = "Admin" });

        (await Read<Cinerama.Api.Controllers.AuthResponse>(viaLogin)).User.Role.ShouldBe("Customer");
        (await Db(db => db.Users.SingleAsync())).RoleId.ShouldBe(Cinerama.Domain.Entities.Roles.CustomerId);
    }
}
