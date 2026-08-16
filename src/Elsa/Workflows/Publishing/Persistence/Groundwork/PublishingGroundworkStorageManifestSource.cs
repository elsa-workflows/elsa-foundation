using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Publishing.Core.Contracts;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork;

/// <summary>Contributes the publishing family's durable Groundwork declaration.</summary>
public sealed class PublishingGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => "elsa-workflows-publishing";

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = PublishingGroundworkStorageManifest.Create();

        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            manifest,
            [
                typeof(IPublicationRecordStore),
                typeof(IPublicationPolicyStore),
                typeof(IPublicationProjectionIntentStore),
                typeof(IPublicationSnapshotReviewStore),
                typeof(IActivityDraftTestRunStore)
            ],
            [],
            [],
            []));
    }
}
