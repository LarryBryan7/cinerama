using System.Reflection;

namespace Cinerama.Infrastructure.Persistence;

/// <summary>
/// Lee los archivos <c>.sql</c> de <c>Persistence/Sql</c>, incluidos como recursos del ensamblado. Las migraciones
/// los aplican con <c>migrationBuilder.Sql(...)</c>: así un stored procedure se versiona como un archivo SQL
/// legible, cambia junto con el esquema y se despliega igual en cada entorno.
/// </summary>
public static class SqlResources
{
    public static string Read(string fileName)
    {
        var assembly = typeof(SqlResources).Assembly;
        var resource = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith("." + fileName, StringComparison.Ordinal))
            ?? throw new FileNotFoundException($"No se encontró el recurso SQL '{fileName}'.");

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
