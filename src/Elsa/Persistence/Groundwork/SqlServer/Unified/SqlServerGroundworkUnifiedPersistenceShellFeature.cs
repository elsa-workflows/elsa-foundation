using CShells.Features;
using Elsa.Persistence.Groundwork.SqlServer.Unified.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.SqlServer.Unified;

/// <summary>
/// Chooses one SQL Server Groundwork target for all seven shipped Elsa persistence families:
/// workflow runtime, identity, secrets, distributed runtime, workflows design, activities design,
/// and workflows publishing.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkUnifiedPersistenceSqlServer",
    DisplayName = "Groundwork SQL Server Unified Persistence",
    Description = "Backs all seven shipped Elsa persistence families with one admission-gated Groundwork SQL Server target: workflow runtime, identity, secrets, distributed runtime, workflows design, activities design and workflows publishing. Apply schema through Groundwork.Tool before host startup.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class SqlServerGroundworkUnifiedPersistenceShellFeature : IShellFeature
{
    public const string DefaultConnectionString =
        "Server=localhost,1433;Database=elsa;Integrated Security=True;TrustServerCertificate=True";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "SQL Server connection string for the unified Groundwork document store.",
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
        services.AddGroundworkSqlServerUnifiedPersistence(connectionString, AutoApplySchemaOnStartup);
    }
}
