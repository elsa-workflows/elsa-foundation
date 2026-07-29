using CShells.Features;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
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
    Description = "Backs the six provider-level Elsa persistence families with one admission-gated Groundwork SQLite target; Identity remains an explicit host selection. Apply schema through Groundwork.Tool before host startup; compose alongside Workflows Runtime Resumption so durable work is re-driven after a restart.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class SqliteGroundworkUnifiedPersistenceShellFeature : IShellFeature
{
    private readonly ShellFeatureContext _context;

    public SqliteGroundworkUnifiedPersistenceShellFeature(ShellFeatureContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public const string DefaultConnectionString = "Data Source=elsa-groundwork.db";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "SQLite connection string used by the single Groundwork document store backing every Elsa lane.",
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

    public virtual void ConfigureServices(IServiceCollection services)
    {
        var connectionString = string.IsNullOrWhiteSpace(ConnectionString) ? DefaultConnectionString : ConnectionString;
        services.AddGroundworkSqliteUnifiedPersistence(
            connectionString,
            _context,
            new WorkflowExecutableCacheOptions
            {
                Enabled = CacheWorkflowExecutables,
                Capacity = WorkflowExecutableCacheCapacity
            },
            AutoApplySchemaOnStartup,
            SkipSchemaInspectionWhenPlanUnchanged,
            new SqliteGroundworkStoreCacheOptions
            {
                Enabled = ReuseAccessBoundStores,
                Capacity = AccessBoundStoreCacheCapacity
            });
    }
}
