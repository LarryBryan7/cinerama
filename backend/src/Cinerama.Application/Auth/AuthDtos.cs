namespace Cinerama.Application.Auth;

public sealed record RegisterRequest(string FullName, string Email, string Password);

/// <param name="UseCookie">Panel web: el refresh token viaja en una cookie HttpOnly y no en el cuerpo (ADR-006).</param>
public sealed record LoginRequest(string Email, string Password, bool UseCookie = false);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record LogoutRequest(string? RefreshToken);

public sealed record UserDto(int Id, string FullName, string Email, string Role);

public sealed record AuthResult(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserDto User);
