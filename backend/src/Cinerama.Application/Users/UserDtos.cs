namespace Cinerama.Application.Users;

public sealed record UpdateProfileRequest(string FullName);

public sealed record SetUserStatusRequest(bool IsActive);

public sealed record AdminUserDto(int Id, string FullName, string Email, string Role, bool IsActive, DateTime CreatedAt);
