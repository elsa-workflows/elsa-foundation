using CShells.Features;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Sqlite.Unified;

/// <summary>
/// Opt-in host feature that backs <b>every</b> Elsa persistence lane — workflow runtime, workflows-design and
/// activities-design — with a single Groundwork SQLite document store. Composing this one feature is all a host
/// does to select the provider for the whole product; runtime, design and domain code never reference Groundwork
/// or SQLite. It supersedes composing the per-lane features individually when the host wants one database to back
/// everything.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkUnifiedPersistenceSqlite",
    DisplayName = "Groundwork SQLite Unified Persistence",
    Description = "Backs the workflow runtime, workflows-design and activities-design persistence seams with a single Groundwork SQLite document store. Durable storage keeps runtime checkpoints, post-commit outbox items and queued scheduler work across a crash; compose alongside Workflows Runtime Resumption so a background pump re-drives that work after a restart.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public sealed class SqliteGroundworkUnifiedPersistenceShellFeature : IShellFeature
{
    public const string DefaultConnectionString = "Data Source=elsa-groundwork.db";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "SQLite connection string used by the single Groundwork document store backing every Elsa lane.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = string.IsNullOrWhiteSpace(ConnectionString) ? DefaultConnectionString : ConnectionString;
        services.AddGroundworkSqliteUnifiedPersistence(connectionString);
    }
}
