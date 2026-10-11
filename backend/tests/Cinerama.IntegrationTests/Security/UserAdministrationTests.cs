using System.Net;
using Cinerama.Application.Auth;
using Cinerama.Application.Common;
using Cinerama.Application.Users;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Security;

public class UserAdministrationTests(ApiFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task List_ReturnsPagedUsersAndSupportsSearch()
    {
        var admin = await CreateAdminAndLoginAsync();
        await RegisterAsync("ana@example.test", name: "Ana López");
        await RegisterAsync("luis@example.test", name: "Luis Díaz");

        var all = await Read<PagedResult<AdminUserDto>>(await Get("/api/v1/users?pageSize=2", admin.AccessToken));
        all.TotalCount.ShouldBe(3);
        all.Items.Count.ShouldBe(2);

        var found = await Read<PagedResult<AdminUserDto>>(await Get("/api/v1/users?search=lópez", admin.AccessToken));
        found.Items.Select(u => u.Email).ShouldBe(["ana@example.test"]);
    }

    [Fact]
    public async Task List_InvalidPaging_Returns400()
    {
        var admin = await CreateAdminAndLoginAsync();

        var response = await Get("/api/v1/users?page=0&pageSize=500", admin.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Errors.ShouldNotBeNull().Keys.ShouldBe(["page", "pageSize"], ignoreOrder: true);
    }

    [Fact]
    [Trait("Case", "TC-SEC-08")]
    public async Task Deactivating_AUser_RevokesTheirRefreshTokensAndBlocksLogin()
    {
        var admin = await CreateAdminAndLoginAsync();
        var customer = await RegisterAsync("ana@example.test");

        var response = await Send(HttpMethod.Put, $"/api/v1/users/{customer.User.Id}/status", new { isActive = false }, admin.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Read<AdminUserDto>(response)).IsActive.ShouldBeFalse();

        var refresh = await Post("/api/v1/auth/refresh", new { refreshToken = customer.RefreshToken });
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var login = await Post("/api/v1/auth/login", new { email = "ana@example.test", password = DefaultPassword });
        login.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reactivating_AUser_LetsThemLogInAgain()
    {
        var admin = await CreateAdminAndLoginAsync();
        var customer = await RegisterAsync("ana@example.test");
        await Send(HttpMethod.Put, $"/api/v1/users/{customer.User.Id}/status", new { isActive = false }, admin.AccessToken);

        await Send(HttpMethod.Put, $"/api/v1/users/{customer.User.Id}/status", new { isActive = true }, admin.AccessToken);

        (await LoginAsync("ana@example.test")).User.Id.ShouldBe(customer.User.Id);
    }

    [Fact]
    public async Task Deactivating_YourOwnAccount_Returns422()
    {
        var admin = await CreateAdminAndLoginAsync();

        var response = await Send(HttpMethod.Put, $"/api/v1/users/{admin.User.Id}/status", new { isActive = false }, admin.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(response)).Code.ShouldBe("cannot_deactivate_self");
        (await Db(db => db.Users.SingleAsync(u => u.Id == admin.User.Id))).IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Deactivating_AnUnknownUser_Returns404()
    {
        var admin = await CreateAdminAndLoginAsync();

        var response = await Send(HttpMethod.Put, "/api/v1/users/99999/status", new { isActive = false }, admin.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadProblem(response)).Code.ShouldBe("user_not_found");
    }

    [Fact]
    [Trait("Case", "RN-03")]
    public async Task Deactivating_OnlyRevokesThatUsersSessions()
    {
        var admin = await CreateAdminAndLoginAsync();
        var ana = await RegisterAsync("ana@example.test");
        var luis = await RegisterAsync("luis@example.test");

        await Send(HttpMethod.Put, $"/api/v1/users/{ana.User.Id}/status", new { isActive = false }, admin.AccessToken);

        (await Post("/api/v1/auth/refresh", new { refreshToken = luis.RefreshToken })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
