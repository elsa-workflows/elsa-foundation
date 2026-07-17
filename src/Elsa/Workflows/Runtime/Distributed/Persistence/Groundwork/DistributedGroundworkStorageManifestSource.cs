using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Distributed.Contracts;

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
            [],
            [],
            ["distributed-execution-placement", "distributed-command-transport"]));
    }
}
