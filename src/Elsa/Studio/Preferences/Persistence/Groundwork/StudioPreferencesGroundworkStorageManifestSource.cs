using Elsa.Persistence.Groundwork.Composition;
using Elsa.Studio.Preferences.Core.Contracts;

namespace Elsa.Studio.Preferences.Persistence.Groundwork;

/// <summary>Contributes the governed Studio-preferences document family to host-selected Groundwork composition.</summary>
public sealed class StudioPreferencesGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => "elsa-studio-preferences";

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            StudioPreferencesStorageManifest.Create(),
            [typeof(IStudioPreferenceStore)],
            [],
            [],
            ["studio-preferences"]));
    }
}
