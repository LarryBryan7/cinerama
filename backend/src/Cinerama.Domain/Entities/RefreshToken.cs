namespace Cinerama.Domain.Entities;

/// <summary>
/// Refresh token opaco. Solo se guarda el hash SHA-256 (ADR-007), nunca el valor entregado al cliente.
/// <see cref="FamilyId"/> agrupa la cadena de rotación de una misma sesión.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public byte[] TokenHash { get; set; } = null!;
    public Guid FamilyId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Se informa al rotar, al cerrar sesión o al revocar la familia.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Solo se informa cuando el token se revocó <b>por rotación</b>. Es lo que permite distinguir
    /// "se reutilizó un token ya rotado" (posible robo) de "se usó un token de una sesión cerrada".
    /// </summary>
    public int? ReplacedById { get; set; }

    public bool IsActive(DateTime nowUtc) => RevokedAt is null && ExpiresAt > nowUtc;
}
