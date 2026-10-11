using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cinerama.UnitTests.Configuration;

/// <summary>
/// El token del hub de SignalR viaja en la URL de la conexión (ADR-016). Para que esa URL no termine en los registros de la
/// API, el nivel de <c>Microsoft.AspNetCore</c> debe ser <c>Warning</c> o más alto en TODOS los archivos de configuración.
/// </summary>
public class LoggingConfigurationTests
{
    private static string ApiProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Cinerama.Api", "appsettings.json")))
        {
            dir = dir.Parent;
        }
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("No se encontró src/Cinerama.Api."), "src", "Cinerama.Api");
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void AspNetCoreLogs_AreWarningOrHigher_SoTheHubUrlWithItsTokenIsNeverRecorded(string file)
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(ApiProjectDirectory(), file)).Build();

        var level = configuration["Logging:LogLevel:Microsoft.AspNetCore"];

        level.ShouldNotBeNull($"{file} debe fijar el nivel de Microsoft.AspNetCore explícitamente");
        ((int)Enum.Parse<LogLevel>(level)).ShouldBeGreaterThanOrEqualTo((int)LogLevel.Warning);
    }
}
