using CShells.Features;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Backs the workflow-runtime persistence seams with Groundwork, on a target of the host's choosing.
/// <para>
/// Pointing this at a target of its own is what separates execution state from the authoring catalog: the
/// two then have independent scaling, backup and retention, and a runaway execution workload cannot degrade
/// the catalog. Leave <see cref="Target"/> unset and runtime shares the default target as before.
/// </para>
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsRuntimeGroundworkPersistence",
    DisplayName = "Workflows Runtime Groundwork Persistence",
    Description = "Persists workflow execution state, bookmarks, executables, checkpoints, the post-commit outbox and the durable scheduler through Groundwork. Binds to a named Groundwork target, so runtime state can live in its own database.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class WorkflowsRuntimeGroundworkPersistenceFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Target",
        Description = "The Groundwork target holding runtime state. Defaults to 'default'.",
        Category = "Persistence")]
    public string? Target { get; set; }

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

    public void ConfigureServices(IServiceCollection services) =>
        services.AddGroundworkRuntimeStores(
            new WorkflowExecutableCacheOptions
            {
                Enabled = CacheWorkflowExecutables,
                Capacity = WorkflowExecutableCacheCapacity
            },
            Target);
}
