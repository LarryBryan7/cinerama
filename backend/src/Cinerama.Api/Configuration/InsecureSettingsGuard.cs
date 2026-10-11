using Microsoft.Data.SqlClient;

namespace Cinerama.Api.Configuration;

/// <summary>
/// Impide que los compromisos válidos solo en local (ADR-014) lleguen a un entorno real: fuera de Development,
/// la API no arranca con la cookie de sesión sin <c>Secure</c> ni conectada con la cuenta <c>sa</c>, a menos que
/// se permita de forma explícita con <c>Local:AllowInsecureSettings=true</c>. Solo Docker Compose lo activa.
/// </summary>
public static class InsecureSettingsGuard
{
    public const string AllowKey = "Local:AllowInsecureSettings";

    public static IReadOnlyList<string> Find(IConfiguration configuration)
    {
        var findings = new List<string>();

        if (!configuration.GetValue("WebSession:RefreshCookieSecure", true))
            findings.Add("WebSession:RefreshCookieSecure=false (la cookie de sesión viajaría sin 'Secure')");

        if (UsesSaLogin(configuration.GetConnectionString("Default")))
            findings.Add("ConnectionStrings:Default usa la cuenta 'sa' (debe ser un login de mínimo privilegio)");

        return findings;
    }

    /// <summary>Lanza si hay valores inseguros fuera de Development sin permiso explícito. Devuelve los hallazgos permitidos.</summary>
    public static IReadOnlyList<string> Enforce(IConfiguration configuration, IHostEnvironment environment)
    {
        var findings = Find(configuration);
        if (findings.Count == 0)
            return findings;

        var allowed = environment.IsDevelopment() || configuration.GetValue<bool>(AllowKey);
        if (!allowed)
            throw new InvalidOperationException(
                $"Configuración insegura fuera de Development: {string.Join("; ", findings)}. " +
                $"Corrígela o, solo en un entorno local, define {AllowKey}=true.");

        return findings;
    }

    private static bool UsesSaLogin(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        try
        {
            return string.Equals(new SqlConnectionStringBuilder(connectionString).UserID, "sa", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false; // una cadena inválida falla al conectar; no es asunto de esta verificación
        }
    }
}
