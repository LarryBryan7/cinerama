using System.Security.Cryptography;
using Cinerama.Application.Common.Options;
using Cinerama.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cinerama.UnitTests.Auth;

public class TokenServiceTests
{
    private const string Key = "unit-test-signing-key-with-at-least-32-chars!";

    private static TokenService Create(FakeTimeProvider time, string key = Key) => new(
        Options.Create(new JwtOptions { SigningKey = key, Issuer = "iss", Audience = "aud", AccessTokenMinutes = 15, RefreshTokenDays = 7 }),
        time);

    private static FakeTimeProvider TimeAt(DateTimeOffset at) => new(at);

    [Fact]
    public void AccessToken_Expires15MinutesAfterIssue_RN05()
    {
        var service = Create(TimeAt(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero)));

        var token = service.CreateAccessToken(7, "ana@example.test", "Customer");

        token.ExpiresAt.ShouldBe(new DateTime(2026, 10, 2, 12, 15, 0, DateTimeKind.Utc));
        new JsonWebToken(token.Value).ValidTo.ShouldBe(token.ExpiresAt);
    }

    [Fact]
    public void AccessToken_CarriesSubjectEmailAndRole()
    {
        var jwt = new JsonWebToken(Create(TimeAt(DateTimeOffset.UtcNow)).CreateAccessToken(42, "ana@example.test", "Admin").Value);

        jwt.Subject.ShouldBe("42");
        jwt.GetClaim("email").Value.ShouldBe("ana@example.test");
        jwt.GetClaim(TokenService.RoleClaim).Value.ShouldBe("Admin");
        jwt.Issuer.ShouldBe("iss");
        jwt.Audiences.ShouldContain("aud");
        jwt.Alg.ShouldBe("HS256");
    }

    [Fact]
    public async Task AccessToken_IsValidWithTheSigningKey_AndInvalidWithAnotherKey()
    {
        var token = Create(TimeAt(DateTimeOffset.UtcNow)).CreateAccessToken(1, "a@b.test", "Customer").Value;

        static TokenValidationParameters Parameters(string key) => new()
        {
            ValidIssuer = "iss", ValidAudience = "aud",
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(key)),
        };

        var handler = new JsonWebTokenHandler();
        (await handler.ValidateTokenAsync(token, Parameters(Key))).IsValid.ShouldBeTrue();
        (await handler.ValidateTokenAsync(token, Parameters("another-signing-key-with-at-least-32-chars!"))).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void RefreshToken_IsRandom_AndHasTheConfiguredLifetime()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var service = Create(TimeAt(now));

        var a = service.CreateRefreshToken();
        var b = service.CreateRefreshToken();

        a.Value.ShouldNotBe(b.Value);
        a.Value.Length.ShouldBe(43); // 32 bytes en Base64Url, sin relleno
        a.ExpiresAt.ShouldBe(new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void RefreshToken_OnlyTheSha256HashIsStored_ADR007()
    {
        var service = Create(TimeAt(DateTimeOffset.UtcNow));

        var token = service.CreateRefreshToken();

        token.Hash.Length.ShouldBe(32);
        token.Hash.ShouldBe(service.HashRefreshToken(token.Value));
        token.Hash.ShouldBe(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token.Value)));
        token.Hash.ShouldNotBe(System.Text.Encoding.UTF8.GetBytes(token.Value));
    }

    [Fact]
    public void HashRefreshToken_DifferentTokens_GiveDifferentHashes()
    {
        var service = Create(TimeAt(DateTimeOffset.UtcNow));
        service.HashRefreshToken("a").ShouldNotBe(service.HashRefreshToken("b"));
    }
}
