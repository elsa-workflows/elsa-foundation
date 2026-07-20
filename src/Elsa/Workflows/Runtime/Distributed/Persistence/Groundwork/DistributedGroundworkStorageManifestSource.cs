using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;

/// <summary>Contributes the distributed-runtime family's durable Groundwork declaration.</summary>
public sealed class DistributedGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => "elsa-workflows-runtime-distributed";

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = LegacyGroundworkStorageManifestPhysicalizer.Physicalize(
            DistributedGroundworkStorageManifest.Create());

        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            manifest,
            [typeof(IExecutionPlacementStore), typeof(IExecutionCommandTransport)],
            [CreateCommandStreamAppendRouteRequirement()],
            [new GroundworkStorageTopologyRequirement(RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity)],
            ["distributed-execution-placement", "distributed-command-transport"]));
    }

    /// <summary>
    /// Appending a transport item advances the stream head and creates the item in one atomic document unit of work.
    /// This is an executable admission requirement, rather than an informational manifest capability, because a
    /// provider that cannot prove this path would otherwise risk duplicate or missing sequence allocation.
    /// </summary>
    public static GroundworkStorageRouteRequirement CreateCommandStreamAppendRouteRequirement() => new(
        new StorageUnitIdentity(DistributedRuntimeStorageManifest.ExecutionCommandStreamHeadDocumentKind),
        "command-stream-append",
        new HashSet<CapabilityId> { WellKnownCapabilities.AtomicCommit });
}
