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
    Description = "Backs the six provider-level Elsa persistence families with one admission-gated Groundwork SQLite target; Identity remains an explicit host selection. Safe missing document structures and diagnostic streams can be auto-applied at startup; otherwise apply them through Groundwork.Tool. Compose alongside Workflows Runtime Resumption so durable work is re-driven after a restart.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class SqliteGroundworkUnifiedPersistenceShellFeature : GroundworkUnifiedPersistenceShellFeatureBase
{
    public SqliteGroundworkUnifiedPersistenceShellFeature(ShellFeatureContext context)
        : base(context)
    {
    }

    public const string DefaultConnectionString = "Data Source=elsa-groundwork.db";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "SQLite connection string used by the single Groundwork document store backing every Elsa lane.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Skip schema inspection when the plan is unchanged",
        Description = "When enabled, startup records an applied-plan fingerprint and skips the full schema inspection/validation walk on later boots whose composed plan is unchanged, cutting warm-boot admission from a full re-validation to a single fingerprint read. Off by default: the fingerprint proves the plan is current but cannot detect schema changed out-of-band while the host was down. Leave disabled to keep per-boot drift re-validation.",
        Category = "Persistence")]
    public bool SkipSchemaInspectionWhenPlanUnchanged { get; set; }

    [ManifestSetting(
        DisplayName = "Reuse access-bound stores",
        Description = "Reuse immutable, access-bound Groundwork store adapters while each operation continues to own an independent SQLite connection. Enabled by default.",
        Category = "Performance")]
    public bool ReuseAccessBoundStores { get; set; } = true;

    [ManifestSetting(
        DisplayName = "Access-bound store cache capacity",
        Description = "Maximum number of tenant, scope, and privilege bindings retained by this shell. Old bindings are evicted safely when the bounded cache is full.",
        Category = "Performance")]
    public int AccessBoundStoreCacheCapacity { get; set; } =
        SqliteGroundworkStoreCacheOptions.DefaultCapacity;

    public override void ConfigureServices(IServiceCollection services) =>
        services.AddGroundworkSqliteUnifiedPersistence(
            ValueOrDefault(ConnectionString, DefaultConnectionString),
            Context,
            CreateWorkflowExecutableCacheOptions(),
            AutoApplySchemaOnStartup,
            SkipSchemaInspectionWhenPlanUnchanged,
            new SqliteGroundworkStoreCacheOptions
            {
                Enabled = ReuseAccessBoundStores,
                Capacity = AccessBoundStoreCacheCapacity
            });
}
