using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>
/// Names the workflows-design lane and publishes its v2 units to the host's public Groundwork catalog.
/// <para>
/// The lane declares its storage units directly, so it contributes no composed host manifest. It still
/// carries an identity because operations spanning design, runtime and publishing have to resolve which
/// target holds each lane before they can decide how to commit.
/// </para>
/// </summary>
public sealed class WorkflowsDesignGroundworkStorageManifestSource : IGroundworkStorageLane
{
    public const string FeatureIdentity = "elsa-workflows-design";

    string IGroundworkStorageLane.FeatureIdentity => FeatureIdentity;

    public IReadOnlyList<StorageUnit> CreateUnits() => WorkflowsDesignStorageManifest.CreateUnits();
}
