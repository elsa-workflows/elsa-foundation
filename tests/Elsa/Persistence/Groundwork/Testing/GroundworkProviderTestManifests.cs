using Groundwork.Core.Manifests;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>Production-equivalent composed manifests shared by provider-facing test doubles.</summary>
public static class GroundworkProviderTestManifests
{
    private static readonly Lazy<StorageManifest> RuntimeManifest = new(() =>
        new RuntimeGroundworkStorageManifestSource()
            .CreateDeclarationAsync()
            .GetAwaiter()
            .GetResult()
            .Manifest);

    public static StorageManifest Runtime => RuntimeManifest.Value;
}
