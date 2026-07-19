using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;

namespace Elsa.Foundation.Identity.Persistence.Groundwork;

/// <summary>
/// Public parameterless deployment source for hosts that select only the Foundation Identity
/// Groundwork manifest. Full host compositions should use their own deployment schema and include
/// this manifest source only when the Groundwork Identity feature is selected.
/// </summary>
public sealed class IdentityGroundworkDeploymentSchema : IPhysicalSchemaManifestSource
{
    private readonly Lazy<StorageManifest> manifest = new(
        () => new IdentityGroundworkStorageManifestSource()
            .CreateDeclarationAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult()
            .Manifest,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public StorageManifest CreateManifest() => manifest.Value;

    public IPhysicalNamePolicy CreateNamePolicy() => new DelegatePhysicalNamePolicy(
        context => context.FeatureDefaultLogicalName);
}
