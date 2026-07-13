using CShells.Features;
using Elsa.Persistence.Groundwork.PostgreSql.Unified.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.PostgreSql.Unified;

/// <summary>
/// Opt-in host feature that backs <b>every</b> Elsa persistence lane — workflow runtime, workflows-design and
/// activities-design — with a single Groundwork PostgreSQL document store. Composing this one feature is all a host
/// does to select the provider for the whole product; runtime, design and domain code never reference Groundwork
/// or PostgreSQL. It supersedes composing the per-lane features individually when the host wants one database to back
/// everything.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkUnifiedPersistencePostgreSql",
    DisplayName = "Groundwork PostgreSQL Unified Persistence",
    Description = "Backs the workflow runtime, workflows-design and activities-design persistence seams with a single Groundwork PostgreSQL document store. Durable storage keeps runtime checkpoints, post-commit outbox items and queued scheduler work across a crash; compose alongside Workflows Runtime Resumption so a background pump re-drives that work after a restart.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class PostgreSqlGroundworkUnifiedPersistenceShellFeature : IShellFeature
{
    public const string DefaultConnectionString = "Host=localhost;Port=5432;Database=elsa;Username=postgres;Password=postgres";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "PostgreSQL connection string used by the single Groundwork document store backing every Elsa lane.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Cache workflow executables",
        Description = "Retain a bounded process-local cache of immutable workflow executable artifacts loaded from durable storage.",
        Category = "Performance")]
    public bool CacheWorkflowExecutables { get; set; }

    [ManifestSetting(
        DisplayName = "Workflow executable cache capacity",
        Description = "Maximum number of immutable workflow executable artifacts retained by this shell. Must be positive when caching is enabled.",
        Category = "Performance")]
    public int WorkflowExecutableCacheCapacity { get; set; } = WorkflowExecutableCacheOptions.DefaultCapacity;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        var connectionString = string.IsNullOrWhiteSpace(ConnectionString) ? DefaultConnectionString : ConnectionString;
        services.AddGroundworkPostgreSqlUnifiedPersistence(
            connectionString,
            new WorkflowExecutableCacheOptions
            {
                Enabled = CacheWorkflowExecutables,
                Capacity = WorkflowExecutableCacheCapacity
            });
    }
}
