namespace Cinerama.Application.Common;

public sealed record AccessToken(string Value, DateTime ExpiresAt);

/// <summary>Valor entregado al cliente, su hash (lo único que se guarda) y su vencimiento.</summary>
public sealed record GeneratedRefreshToken(string Value, byte[] Hash, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(int userId, string email, string role);
    GeneratedRefreshToken CreateRefreshToken();
    byte[] HashRefreshToken(string token);
}
