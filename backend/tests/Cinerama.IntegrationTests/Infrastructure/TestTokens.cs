using System.Security.Claims;
using System.Text;
using Cinerama.Application.Common.Options;
using Cinerama.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cinerama.IntegrationTests.Infrastructure;

/// <summary>Tokens fabricados para probar cómo reacciona la API ante credenciales manipuladas o vencidas.</summary>
public static class TestTokens
{
    private static JwtOptions Options(string key = ApiFixture.JwtKey, string audience = "cinerama-clients") =>
        new() { SigningKey = key, Issuer = "cinerama-api", Audience = audience, AccessTokenMinutes = 15 };

    /// <summary>Token legítimo (misma clave) emitido en <paramref name="issuedAt"/>: si fue hace más de 15 min, ya venció.</summary>
    public static string IssuedAt(DateTimeOffset issuedAt, int userId = 1, string role = "Customer") =>
        new TokenService(Microsoft.Extensions.Options.Options.Create(Options()), new FakeTimeProvider(issuedAt))
            .CreateAccessToken(userId, "t@example.test", role).Value;

    /// <summary>Token legítimo que vence en <paramref name="seconds"/> segundos (para probar el cierre de una conexión abierta).</summary>
    public static string ExpiringIn(int seconds, int userId = 1, string role = "Customer") =>
        IssuedAt(DateTimeOffset.UtcNow - TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(seconds), userId, role);

    /// <summary>Mismos claims, pero firmado con otra clave: un atacante que no conoce el secreto real.</summary>
    public static string SignedWithAnotherKey(int userId = 1, string role = "Admin") =>
        new TokenService(Microsoft.Extensions.Options.Options.Create(Options("otra-clave-completamente-distinta-de-32-chars-o-mas")), new FakeTimeProvider(DateTimeOffset.UtcNow))
            .CreateAccessToken(userId, "t@example.test", role).Value;

    public static string WithAudience(string audience) =>
        new TokenService(Microsoft.Extensions.Options.Options.Create(Options(audience: audience)), new FakeTimeProvider(DateTimeOffset.UtcNow))
            .CreateAccessToken(1, "t@example.test", "Admin").Value;

    /// <summary>Token sin firma (<c>"alg": "none"</c>) que se declara Admin.</summary>
    public static string AlgNone()
    {
        static string B64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        var header = B64("""{"alg":"none","typ":"JWT"}""");
        var payload = B64($$"""{"sub":"1","role":"Admin","iss":"cinerama-api","aud":"cinerama-clients","exp":{{exp}}}""");
        return $"{header}.{payload}.";
    }

    /// <summary>Token legítimo con la firma alterada: se cambia un carácter de la firma.</summary>
    public static string WithTamperedSignature(string token)
    {
        var last = token[^1] == 'A' ? 'B' : 'A';
        return token[..^1] + last;
    }
}
