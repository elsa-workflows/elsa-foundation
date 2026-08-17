using Groundwork.Kernel;

namespace Elsa.Activities.Design.Persistence.Groundwork;

/// <summary>Publishes the activity-design v2 units to the host's public Groundwork catalog.</summary>
public sealed class ActivitiesDesignGroundworkStorageManifestSource
{
    public const string FeatureIdentity = "elsa-activities-design";

    public IReadOnlyList<StorageUnit> CreateUnits() => ActivitiesDesignStorageManifest.CreateUnits();
}
