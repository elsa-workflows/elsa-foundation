using CShells.Features;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Sqlite;

/// <summary>
/// Opt-in host feature that backs the runtime persistence seams with Groundwork over SQLite. The
/// host owns the provider choice: composing this feature selects SQLite and supplies its connection
/// string. Runtime and domain code never reference Groundwork or SQLite. When this feature is not
/// composed, the runtime keeps its in-memory defaults.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkRuntimePersistenceSqlite",
    DisplayName = "Groundwork SQLite Runtime Persistence",
    Description = "Backs the workflow runtime persistence seams with Groundwork over SQLite. Durable storage keeps checkpoints, post-commit outbox items and queued scheduler work across a crash; compose alongside Workflows Runtime Resumption so a background pump re-drives that work after a restart.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public sealed class SqliteGroundworkRuntimePersistenceShellFeature : IShellFeature
{
    public const string DefaultConnectionString = "Data Source=elsa-groundwork-runtime.db";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "SQLite connection string used by the Groundwork runtime document store.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Auto-apply schema on startup",
        Description = "When enabled, safe pending schema operations are applied automatically at startup instead of requiring Groundwork.Tool. Destructive operations are never auto-applied.",
        Category = "Persistence")]
    public bool AutoApplySchemaOnStartup { get; set; } = true;

    [ManifestSetting(
        DisplayName = "Skip schema inspection when the plan is unchanged",
        Description = "When enabled, startup records an applied-plan fingerprint and skips the full schema inspection/validation walk on later boots whose composed plan is unchanged, cutting warm-boot admission from a full re-validation to a single fingerprint read. Off by default: the fingerprint proves the plan is current but cannot detect schema changed out-of-band while the host was down. Leave disabled to keep per-boot drift re-validation.",
        Category = "Persistence")]
    public bool SkipSchemaInspectionWhenPlanUnchanged { get; set; }

    [ManifestSetting(
        DisplayName = "Cache workflow executables",
        Description = "Retain a bounded shell-local cache of immutable workflow executable artifacts loaded from durable storage, isolated by persistence scope.",
        Category = "Performance")]
    public bool CacheWorkflowExecutables { get; set; } = true;

    [ManifestSetting(
        DisplayName = "Workflow executable cache capacity",
        Description = "Maximum number of immutable workflow executable artifacts retained by this shell. Must be positive when caching is enabled.",
        Category = "Performance")]
    public int WorkflowExecutableCacheCapacity { get; set; } = WorkflowExecutableCacheOptions.DefaultCapacity;

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

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = string.IsNullOrWhiteSpace(ConnectionString) ? DefaultConnectionString : ConnectionString;

        services.AddSqliteGroundworkDocumentStore(
            connectionString,
            AutoApplySchemaOnStartup,
            SkipSchemaInspectionWhenPlanUnchanged,
            new SqliteGroundworkStoreCacheOptions
            {
                Enabled = ReuseAccessBoundStores,
                Capacity = AccessBoundStoreCacheCapacity
            });

        services.AddGroundworkRuntimeStores(new WorkflowExecutableCacheOptions
        {
            Enabled = CacheWorkflowExecutables,
            Capacity = WorkflowExecutableCacheCapacity
        });
    }
}
