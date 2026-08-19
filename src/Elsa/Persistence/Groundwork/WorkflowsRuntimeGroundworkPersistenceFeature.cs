using CShells.Features;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
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
    DependsOn = new object[] { "WorkflowsRuntimeResumption", "WorkflowsRuntimeRecurringTriggers" })]
public class WorkflowsRuntimeGroundworkPersistenceFeature : GroundworkPersistenceShellFeatureBase
{
    [ManifestSetting(
        DisplayName = "Target",
        Description = "The Groundwork target holding runtime state. Defaults to 'default'.",
        Category = "Persistence")]
    public string? Target { get; set; }

    public override void ConfigureServices(IServiceCollection services) =>
        services.AddGroundworkRuntimeStores(CreateWorkflowExecutableCacheOptions(), Target);
}
