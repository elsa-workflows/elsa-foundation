using CShells.Features;
using Elsa.Persistence.Groundwork.SqlServer.Unified.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.SqlServer.Unified;

/// <summary>
/// Chooses one SQL Server Groundwork target for the six provider-level Elsa persistence families:
/// workflow runtime, secrets, distributed runtime, workflows design, activities design, and workflows
/// publishing. Identity remains an explicit host selection.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkUnifiedPersistenceSqlServer",
    DisplayName = "Groundwork SQL Server Unified Persistence",
    Description = "Backs the six provider-level Elsa persistence families with one admission-gated Groundwork SQL Server target: workflow runtime, secrets, distributed runtime, workflows design, activities design and workflows publishing. Identity remains an explicit host selection. Safe missing document structures and diagnostic streams can be auto-applied at startup; otherwise apply them through Groundwork.Tool.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class SqlServerGroundworkUnifiedPersistenceShellFeature : GroundworkUnifiedPersistenceShellFeatureBase
{
    public SqlServerGroundworkUnifiedPersistenceShellFeature(ShellFeatureContext context)
        : base(context)
    {
    }

    public const string DefaultConnectionString =
        "Server=localhost,1433;Database=elsa;Integrated Security=True;TrustServerCertificate=True";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "SQL Server connection string for the unified Groundwork document store.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    public override void ConfigureServices(IServiceCollection services) =>
        services.AddGroundworkSqlServerUnifiedPersistence(
            ValueOrDefault(ConnectionString, DefaultConnectionString),
            Context,
            CreateWorkflowExecutableCacheOptions(),
            AutoApplySchemaOnStartup);
}
