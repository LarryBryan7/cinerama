using System.Net;
using Cinerama.Api.Controllers;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.IntegrationTests.Auth;

public class RegisterTests(ApiFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    [Trait("Case", "HU-01")]
    public async Task Register_ValidRequest_Returns201WithCustomerRoleAndTokens()
    {
        var response = await Post("/api/v1/auth/register", new { fullName = "Ana", email = "ana@example.test", password = "Cine2026x" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await Read<AuthResponse>(response);
        body.User.Role.ShouldBe("Customer");
        body.User.Email.ShouldBe("ana@example.test");
        body.AccessToken.ShouldNotBeNullOrWhiteSpace();
        body.RefreshToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Case", "RN-01")]
    public async Task Register_NormalizesTheEmail()
    {
        await RegisterAsync("  Ana@Example.TEST ");

        (await Db(db => db.Users.Select(u => u.Email).SingleAsync())).ShouldBe("ana@example.test");
    }

    [Fact]
    [Trait("Case", "RN-01")]
    public async Task Register_DuplicateEmailWithDifferentCase_Returns409()
    {
        await RegisterAsync("ana@example.test");

        var response = await Post("/api/v1/auth/register", new { fullName = "Otra", email = " ANA@Example.test ", password = DefaultPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblem(response)).Code.ShouldBe("email_already_registered");
    }

    [Theory]
    [Trait("Case", "RN-02")]
    [InlineData("corta1", "password")]
    [InlineData("12345678", "password")]
    [InlineData("soloLetras", "password")]
    public async Task Register_WeakPassword_Returns400WithFieldErrors(string password, string field)
    {
        var response = await Post("/api/v1/auth/register", new { fullName = "Ana", email = "ana@example.test", password });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await ReadProblem(response);
        problem.Code.ShouldBe("validation_failed");
        problem.Errors.ShouldNotBeNull().ShouldContainKey(field);
    }

    [Fact]
    [Trait("Case", "RN-04")]
    public async Task Register_IgnoresRoleAndOtherPrivilegedFieldsInTheBody()
    {
        var response = await Post("/api/v1/auth/register", new
        {
            fullName = "Intruso", email = "intruso@example.test", password = DefaultPassword,
            role = "Admin", roleId = 1, isActive = false, id = 999,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var user = (await Read<AuthResponse>(response)).User;
        user.Role.ShouldBe("Customer");
        user.Id.ShouldNotBe(999);

        var stored = await Db(db => db.Users.SingleAsync());
        stored.RoleId.ShouldBe(Cinerama.Domain.Entities.Roles.CustomerId);
        stored.IsActive.ShouldBeTrue();
    }

    [Fact]
    [Trait("Case", "RNF-02")]
    public async Task Register_StoresOnlyAHashOfThePassword()
    {
        await RegisterAsync("ana@example.test", "Cine2026x");

        var hash = await Db(db => db.Users.Select(u => u.PasswordHash).SingleAsync());
        hash.ShouldNotContain("Cine2026x");
        hash.Length.ShouldBeGreaterThan(40);
    }

    [Fact]
    public async Task Register_SimultaneousRequestsWithTheSameEmail_OnlyOneIsCreated()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Post("/api/v1/auth/register", new { fullName = $"Ana {i}", email = "carrera@example.test", password = DefaultPassword })));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict);
        (await Db(db => db.Users.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Register_MalformedJson_Returns400Problem()
    {
        var response = await Client.PostAsync("/api/v1/auth/register",
            new StringContent("{ esto no es json", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await ReadProblem(response)).Code.ShouldBe("validation_failed");
    }
}
