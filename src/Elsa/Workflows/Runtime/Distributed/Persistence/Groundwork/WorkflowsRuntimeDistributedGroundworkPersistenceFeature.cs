using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;

/// <summary>
/// Opt-in host feature that makes the distributed workflow-execution provider durable: replaces the in-memory
/// placement store and cross-node command transport with scoped Groundwork v2 stores over the host-selected
/// provider connection, so placement leases and queued commands survive node death and failover re-drive works
/// across real processes, not just within one.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsRuntimeDistributedGroundworkPersistence",
    DisplayName = "Workflows Runtime Distributed Groundwork Persistence",
    Description = "Replaces the distributed runtime's in-memory placement store and cross-node command transport with durable Groundwork v2 stores, so per-execution placement leases and the durable command inbox survive restarts and are shared across nodes through the host-selected provider connection.",
    DependsOn = new object[] { "WorkflowsRuntimeDistributed" })]
public sealed class WorkflowsRuntimeDistributedGroundworkPersistenceFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Target",
        Description = "The Groundwork target that owns this feature's v2 storage units. Defaults to 'default'.",
        Category = "Persistence")]
    public string? Target { get; set; }

    public void ConfigureServices(IServiceCollection services) => services.AddGroundworkDistributedRuntimeStores(Target);
}
