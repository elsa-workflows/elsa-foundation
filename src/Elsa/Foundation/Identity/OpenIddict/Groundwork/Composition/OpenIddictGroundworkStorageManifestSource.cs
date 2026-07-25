using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Composition;

/// <summary>
/// Public parameterless declaration used by the provider-driver conformance
/// harness and available for later runtime admission and deployment wiring.
/// </summary>
public sealed class OpenIddictGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => OpenIddictGroundworkStorageManifest.ManifestIdentity;

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            OpenIddictGroundworkStorageManifest.Create(),
            requiredStoreContracts: [],
            OpenIddictGroundworkStorageManifest.BoundedRoutes,
            topologyRequirements:
            [
                new GroundworkStorageTopologyRequirement(
                    RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity)
            ],
            coverageRows: ["openiddict-storage-declaration"]));
    }
}
