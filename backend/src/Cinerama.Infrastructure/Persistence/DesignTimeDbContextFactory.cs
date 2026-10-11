using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cinerama.Infrastructure.Persistence;

/// <summary>
/// Solo lo usa <c>dotnet ef</c> para generar migraciones. La cadena de conexión es un valor de marcador: crear
/// una migración no se conecta a ninguna base.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CineramaDbContext>
{
    public CineramaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CineramaDbContext>()
            .UseSqlServer("Server=design-time;Database=Cinerama;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new CineramaDbContext(options);
    }
}
