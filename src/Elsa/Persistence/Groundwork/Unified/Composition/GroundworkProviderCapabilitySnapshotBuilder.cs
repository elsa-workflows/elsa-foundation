using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Capabilities;

namespace Elsa.Persistence.Groundwork.Unified.Composition;

public static class GroundworkProviderCapabilitySnapshotBuilder
{
    public static async ValueTask<GroundworkProviderCapabilitySnapshot> ForSelectedSourcesAsync(
        ProviderCapabilityReport capabilityReport,
        GroundworkProviderTopologySnapshot topology,
        IEnumerable<IGroundworkStorageManifestSource> sources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilityReport);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(sources);

        var activePaths = new List<GroundworkActiveStoragePath>();
        foreach (var source in sources)
        {
            var declaration = await source.CreateDeclarationAsync(cancellationToken);
            activePaths.AddRange(declaration.RequiredRoutes.Select(route => new GroundworkActiveStoragePath(
                declaration.FeatureIdentity,
                route.StorageUnit,
                route.RouteIdentity,
                route.RequiredCapabilities)));
        }

        return new GroundworkProviderCapabilitySnapshot(capabilityReport, topology, activePaths);
    }
}
