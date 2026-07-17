using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.Groundwork.Composition;

namespace Elsa.Activities.Design.Persistence.Groundwork;

/// <summary>Contributes the activities-design family's durable Groundwork declaration.</summary>
public sealed class ActivitiesDesignGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => "elsa-activities-design";

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = LegacyGroundworkStorageManifestPhysicalizer.Physicalize(ActivitiesDesignStorageManifest.Create());

        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            manifest,
            [
                typeof(IActivityDefinitionStore),
                typeof(IActivityDefinitionVersionStore),
                typeof(IActivityAvailabilitySettingsStore),
                typeof(IActivityDefinitionManagementProjectionStore)
            ],
            [],
            [],
            []));
    }
}
