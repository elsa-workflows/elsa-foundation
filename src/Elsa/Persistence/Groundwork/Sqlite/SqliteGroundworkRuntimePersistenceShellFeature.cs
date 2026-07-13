using CShells.Features;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Capabilities;
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
public class SqliteGroundworkRuntimePersistenceShellFeature : IShellFeature
{
    public const string DefaultConnectionString = "Data Source=elsa-groundwork-runtime.db";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "SQLite connection string used by the Groundwork runtime document store.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Rematerialize on startup",
        Description = "Always run full schema materialization and index backfill on startup. Enable temporarily to repair or verify the SQLite projection.",
        Category = "Persistence")]
    public bool RematerializeOnStartup { get; set; }

    [ManifestSetting(
        DisplayName = "Cache workflow executables",
        Description = "Retain a bounded process-local cache of immutable workflow executable artifacts loaded from durable storage.",
        Category = "Performance")]
    public bool CacheWorkflowExecutables { get; set; } = true;

    [ManifestSetting(
        DisplayName = "Workflow executable cache capacity",
        Description = "Maximum number of immutable workflow executable artifacts retained by this shell. Must be positive when caching is enabled.",
        Category = "Performance")]
    public int WorkflowExecutableCacheCapacity { get; set; } = WorkflowExecutableCacheOptions.DefaultCapacity;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        var connectionString = string.IsNullOrWhiteSpace(ConnectionString) ? DefaultConnectionString : ConnectionString;

        services.AddSqliteGroundworkDocumentStore(
            connectionString,
            ElsaRuntimeStorageManifest.Create(),
            new ProviderIdentity("groundwork-sqlite", "1.0.0"),
            RematerializeOnStartup);

        services.AddGroundworkRuntimeStores(new WorkflowExecutableCacheOptions
        {
            Enabled = CacheWorkflowExecutables,
            Capacity = WorkflowExecutableCacheCapacity
        });
    }
}
