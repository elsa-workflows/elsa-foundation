using CShells.Features;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Sqlite.Unified;

/// <summary>
/// Opt-in host feature that backs the six provider-level Elsa persistence families with one Groundwork
/// SQLite target: workflow runtime, secrets, distributed runtime, workflows design, activities design,
/// and workflows publishing. Identity remains an explicit host selection.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkUnifiedPersistenceSqlite",
    DisplayName = "Groundwork SQLite Unified Persistence",
    Description = "Backs the six provider-level Elsa persistence families with one admission-gated Groundwork SQLite target; Identity remains an explicit host selection. Compose alongside Workflows Runtime Resumption so durable work is re-driven after a restart.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class SqliteGroundworkUnifiedPersistenceShellFeature : GroundworkPersistenceShellFeatureBase
{
    public const string DefaultConnectionString = "Data Source=elsa-groundwork.db";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "SQLite connection string used by the single Groundwork provider connection backing every Elsa lane.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    public override void ConfigureServices(IServiceCollection services) =>
        services.AddGroundworkSqliteUnifiedPersistence(
            ValueOrDefault(ConnectionString, DefaultConnectionString),
            CreateWorkflowExecutableCacheOptions());
}
