using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;

/// <summary>
/// Opt-in host feature that makes the distributed workflow-execution provider durable: replaces the in-memory
/// placement store and cross-node command transport with Groundwork-backed stores over the host-selected document
/// store, so placement leases and queued commands survive node death and failover re-drive works across real
/// processes, not just within one.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsRuntimeDistributedGroundworkPersistence",
    DisplayName = "Workflows Runtime Distributed Groundwork Persistence",
    Description = "Replaces the distributed runtime's in-memory placement store and cross-node command transport with durable Groundwork-backed stores, so per-execution placement leases and the durable command inbox survive restarts and are shared across nodes through the host-selected document store.",
    DependsOn = new object[] { "WorkflowsRuntimeDistributed" })]
public sealed class WorkflowsRuntimeDistributedGroundworkPersistenceFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddGroundworkDistributedRuntimeStores();
}
