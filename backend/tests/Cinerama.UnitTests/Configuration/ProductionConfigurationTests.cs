using Cinerama.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Cinerama.UnitTests.Configuration;

/// <summary>
/// Los compromisos válidos solo en local (ADR-014) no deben heredarse en producción: ni por los valores por
/// defecto del archivo base, ni porque alguien los active sin querer.
/// </summary>
public class ProductionConfigurationTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    private static IHostEnvironment Env(string name)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(name);
        env.SetupGet(e => e.ContentRootFileProvider).Returns(new NullFileProvider());
        return env.Object;
    }

    private const string SaConnection = "Server=db;Database=Cinerama;User Id=sa;Password=x;TrustServerCertificate=True";
    private const string AppConnection = "Server=db;Database=Cinerama;User Id=cinerama_app;Password=x;TrustServerCertificate=True";

    // ── Los valores por defecto son seguros ─────────────────────────────────

    [Fact]
    public void BaseSettingsFile_IsSecureByDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        File.Exists(path).ShouldBeTrue("el archivo base de la API debe copiarse junto a los tests");

        var config = new ConfigurationBuilder().AddJsonFile(path).Build();

        config.GetValue<bool>("WebSession:RefreshCookieSecure").ShouldBeTrue();
        config.GetValue<bool>("Database:MigrateOnStartup").ShouldBeFalse();
        config.GetValue<bool>("Seed:Enabled").ShouldBeFalse();
        config.GetValue<bool>("Swagger:Enabled").ShouldBeFalse();
        config.GetValue<bool>(InsecureSettingsGuard.AllowKey).ShouldBeFalse();
        config["Cors:AllowedOrigins"].ShouldBeNullOrEmpty();
        config.GetConnectionString("Default").ShouldBeNull();   // ninguna cadena de conexión versionada
        config["Jwt:SigningKey"].ShouldBeNull();                // ni clave de firma
        config["Ticket:SigningKey"].ShouldBeNull();
        config["Seed:AdminPassword"].ShouldBeNull();
    }

    [Fact]
    public void DevelopmentSettingsFile_IsTheOnlyOneThatEnablesLocalConveniences()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");
        File.Exists(path).ShouldBeTrue();

        var config = new ConfigurationBuilder().AddJsonFile(path).Build();

        // Esto vive en el archivo de Development, que ASP.NET Core solo carga cuando el entorno es Development.
        config.GetValue<bool>("Database:MigrateOnStartup").ShouldBeTrue();
        config.GetValue<bool>("WebSession:RefreshCookieSecure", true).ShouldBeFalse();
    }

    // ── La protección de arranque ───────────────────────────────────────────

    [Fact]
    public void Production_WithSecureSettings_Passes() =>
        InsecureSettingsGuard.Enforce(
            Config(("ConnectionStrings:Default", AppConnection), ("WebSession:RefreshCookieSecure", "true")),
            Env("Production")).ShouldBeEmpty();

    [Fact]
    public void Production_WithTheDefaultCookieSetting_Passes() =>
        InsecureSettingsGuard.Enforce(Config(("ConnectionStrings:Default", AppConnection)), Env("Production")).ShouldBeEmpty();

    [Fact]
    public void Production_WithAnInsecureCookie_Fails()
    {
        var ex = Should.Throw<InvalidOperationException>(() => InsecureSettingsGuard.Enforce(
            Config(("ConnectionStrings:Default", AppConnection), ("WebSession:RefreshCookieSecure", "false")), Env("Production")));

        ex.Message.ShouldContain("RefreshCookieSecure");
        ex.Message.ShouldContain(InsecureSettingsGuard.AllowKey);
    }

    [Theory]
    [InlineData("sa")]
    [InlineData("SA")]
    public void Production_ConnectedAsSa_Fails(string user)
    {
        var connection = SaConnection.Replace("User Id=sa", $"User Id={user}");

        var ex = Should.Throw<InvalidOperationException>(() => InsecureSettingsGuard.Enforce(
            Config(("ConnectionStrings:Default", connection)), Env("Production")));

        ex.Message.ShouldContain("'sa'");
    }

    [Fact]
    public void Production_ReportsEveryInsecureSetting()
    {
        var ex = Should.Throw<InvalidOperationException>(() => InsecureSettingsGuard.Enforce(
            Config(("ConnectionStrings:Default", SaConnection), ("WebSession:RefreshCookieSecure", "false")), Env("Staging")));

        ex.Message.ShouldContain("RefreshCookieSecure");
        ex.Message.ShouldContain("'sa'");
    }

    [Fact]
    public void ExplicitPermission_AllowsThem_AndTheyAreReportedForLogging()
    {
        var allowed = InsecureSettingsGuard.Enforce(
            Config(("ConnectionStrings:Default", SaConnection), ("WebSession:RefreshCookieSecure", "false"), (InsecureSettingsGuard.AllowKey, "true")),
            Env("Production"));

        allowed.Count.ShouldBe(2);
    }

    [Fact]
    public void Development_AllowsThemWithoutAnyFlag() =>
        InsecureSettingsGuard.Enforce(
            Config(("ConnectionStrings:Default", SaConnection), ("WebSession:RefreshCookieSecure", "false")), Env("Development"))
            .Count.ShouldBe(2);

    [Fact]
    public void AnInvalidConnectionString_IsNotThisGuardsConcern() =>
        InsecureSettingsGuard.Enforce(Config(("ConnectionStrings:Default", "esto no es una cadena válida")), Env("Production")).ShouldBeEmpty();
}
