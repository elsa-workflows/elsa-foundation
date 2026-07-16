using CShells.Features;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.SqlServer.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.SqlServer;

/// <summary>Chooses the SQL Server Groundwork leaf for runtime persistence.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkRuntimePersistenceSqlServer",
    DisplayName = "Groundwork SQL Server Runtime Persistence",
    Description = "Backs workflow runtime persistence with an admission-gated Groundwork SQL Server target. Apply schema through Groundwork.Tool before host startup.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class SqlServerGroundworkRuntimePersistenceShellFeature : IShellFeature
{
    public const string DefaultConnectionString =
        "Server=localhost,1433;Database=elsa;Integrated Security=True;TrustServerCertificate=True";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "SQL Server connection string for the Groundwork runtime document store.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Auto-apply schema on startup",
        Description = "When enabled, safe pending schema operations are applied automatically at startup instead of requiring Groundwork.Tool. Destructive operations are never auto-applied.",
        Category = "Persistence")]
    public bool AutoApplySchemaOnStartup { get; set; } = true;

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = string.IsNullOrWhiteSpace(ConnectionString)
            ? DefaultConnectionString
            : ConnectionString;
        services.AddGroundworkRuntimeStores();
        services.AddSqlServerGroundworkDocumentStore(connectionString, AutoApplySchemaOnStartup);
    }
}
