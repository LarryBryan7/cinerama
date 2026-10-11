namespace Cinerama.Infrastructure.Persistence.Seed;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Carga datos ficticios de demostración si la base está vacía.</summary>
    public bool Enabled { get; set; }

    /// <summary>Contraseñas de las cuentas demo. Nunca se versionan: se leen del entorno. Sin ellas, no se crean usuarios.</summary>
    public string? AdminPassword { get; set; }
    public string? CustomerPassword { get; set; }
}
