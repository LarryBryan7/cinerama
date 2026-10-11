namespace Cinerama.Domain.Entities;

public class User
{
    public int Id { get; set; }

    /// <summary>Siempre normalizado (RN-01): sin espacios en los extremos y en minúsculas.</summary>
    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;
    public string FullName { get; set; } = null!;

    /// <summary>Foto de perfil (ruta relativa a la carpeta de medios o URL absoluta). Opcional.</summary>
    public string? AvatarUrl { get; set; }
    public byte RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public List<RefreshToken> RefreshTokens { get; set; } = [];

    /// <summary>Normaliza un email según RN-01. Se usa antes de guardar y antes de comparar.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
