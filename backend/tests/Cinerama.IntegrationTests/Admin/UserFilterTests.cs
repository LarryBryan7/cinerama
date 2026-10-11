using System.Net;
using Cinerama.Application.Common;
using Cinerama.Application.Users;
using Cinerama.IntegrationTests.Infrastructure;

namespace Cinerama.IntegrationTests.Admin;

/// <summary>Filtros del listado de usuarios del panel (RF-USR-01): por texto, rol y estado.</summary>
public class UserFilterTests(ApiFixture fixture) : ReservationTest(fixture)
{
    private async Task<(string Token, int AdminId)> SetupAsync()
    {
        var admin = await NewUserAsync("jefe@cinerama.test", admin: true, fullName: "Jefa de Sala");
        await NewUserAsync("ana@example.test", fullName: "Ana Pérez");
        await NewUserAsync("beto@example.test", fullName: "Beto Ruiz");
        await Db(async db =>
        {
            var beto = db.Users.Single(u => u.Email == "beto@example.test");
            beto.IsActive = false;
            await db.SaveChangesAsync();
        });
        return (admin.Token, admin.Id);
    }

    private async Task<List<string>> Emails(string query, string token) =>
        (await Read<PagedResult<AdminUserDto>>(await Get($"/api/v1/users{query}", token))).Items.Select(u => u.Email).Order().ToList();

    [Fact]
    public async Task TheListFiltersByRole_ByStatus_AndBySearch()
    {
        var (token, _) = await SetupAsync();

        (await Emails("", token)).ShouldBe(["ana@example.test", "beto@example.test", "jefe@cinerama.test"]);
        (await Emails("?role=Admin", token)).ShouldBe(["jefe@cinerama.test"]);
        (await Emails("?role=customer", token)).ShouldBe(["ana@example.test", "beto@example.test"]); // sin distinguir mayúsculas
        (await Emails("?isActive=false", token)).ShouldBe(["beto@example.test"]);
        (await Emails("?isActive=true&role=Customer", token)).ShouldBe(["ana@example.test"]);
        (await Emails("?search=Ruiz", token)).ShouldBe(["beto@example.test"]);
        (await Emails("?search=EXAMPLE.test&role=Customer&isActive=true", token)).ShouldBe(["ana@example.test"]);
    }

    [Fact]
    public async Task AnUnknownRoleIs400_AndTheListIsPaged()
    {
        var (token, _) = await SetupAsync();

        var response = await Get("/api/v1/users?role=Dueño", token);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Errors!.ShouldContainKey("role");

        var page = await Read<PagedResult<AdminUserDto>>(await Get("/api/v1/users?page=2&pageSize=2", token));
        (page.TotalCount, page.Items.Count).ShouldBe((3, 1));
    }

    [Fact]
    public async Task ADeactivatedAccountShowsUpAsInactive_AndTheAdminCannotDeactivateHimself()
    {
        var (token, adminId) = await SetupAsync();

        var self = await Send(HttpMethod.Put, $"/api/v1/users/{adminId}/status", new { isActive = false }, token);

        self.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblem(self)).Code.ShouldBe("cannot_deactivate_self");
    }
}
