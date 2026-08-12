using CShells.Features;
using Elsa.Persistence.Groundwork.PostgreSql.Unified.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.PostgreSql.Unified;

/// <summary>
/// Opt-in host feature that backs the six provider-level Elsa persistence families with one Groundwork
/// PostgreSQL target: workflow runtime, secrets, distributed runtime, workflows design, activities design,
/// and workflows publishing. Identity remains an explicit host selection.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkUnifiedPersistencePostgreSql",
    DisplayName = "Groundwork PostgreSQL Unified Persistence",
    Description = "Backs the six provider-level Elsa persistence families with one admission-gated Groundwork PostgreSQL target; Identity remains an explicit host selection. Safe missing document structures and diagnostic streams can be auto-applied at startup; otherwise apply them through Groundwork.Tool. Compose alongside Workflows Runtime Resumption so durable work is re-driven after a restart.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class PostgreSqlGroundworkUnifiedPersistenceShellFeature : GroundworkUnifiedPersistenceShellFeatureBase
{
    public PostgreSqlGroundworkUnifiedPersistenceShellFeature(ShellFeatureContext context)
        : base(context)
    {
    }

    public const string DefaultConnectionString = "Host=localhost;Port=5432;Database=elsa;Username=postgres;Password=postgres";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "PostgreSQL connection string used by the single Groundwork document store backing every Elsa lane.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    public override void ConfigureServices(IServiceCollection services) =>
        services.AddGroundworkPostgreSqlUnifiedPersistence(
            ValueOrDefault(ConnectionString, DefaultConnectionString),
            Context,
            CreateWorkflowExecutableCacheOptions(),
            AutoApplySchemaOnStartup);
}
