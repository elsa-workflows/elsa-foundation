using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork;

/// <summary>
/// Names the publishing lane and publishes its v2 units to the host's public Groundwork catalog.
/// <para>
/// The lane declares its storage units directly, so it contributes no composed host manifest. It still
/// carries an identity because a publication spans design, publishing and runtime, and the command has to
/// resolve which target holds each lane before it can decide how to commit.
/// </para>
/// </summary>
public sealed class PublishingGroundworkStorageManifestSource : IGroundworkStorageLane
{
    public const string FeatureIdentity = GroundworkPublishingStorage.FeatureIdentity;

    string IGroundworkStorageLane.FeatureIdentity => FeatureIdentity;

    public IReadOnlyList<StorageUnit> CreateUnits() => PublishingGroundworkStorageManifest.CreateUnits();
}
