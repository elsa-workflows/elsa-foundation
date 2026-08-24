using Elsa.Activities.Design.Persistence.Groundwork;
using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Locking.Core;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

/// <summary>Explicit maintenance path for advancing the supported management-cursor retention floor.</summary>
public sealed class GroundworkActivityManagementProjectionRetention(
    GroundworkV2ActivityDesignStore store,
    GroundworkV2ActivityDesignStore boundedStore,
    IDistributedLockProvider lockProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = GroundworkActivitiesDesignJson.Options;

    public async Task ExpireBeforeAsync(
        long oldestRetainedSequence,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        if (oldestRetainedSequence < 1)
            throw new ArgumentOutOfRangeException(nameof(oldestRetainedSequence));
        await using var handle = await lockProvider.AcquireLockAsync(
            GroundworkActivityManagementProjectionWriter.ProjectionLockKey(store),
            null,
            cancellationToken);
        var watermarkEnvelope = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkDocumentKind,
            ActivityManagementProjectionWatermark.CurrentId,
            cancellationToken) ?? throw new InvalidOperationException("The activity-management projection is not initialized.");
        var watermark = Deserialize<ActivityManagementProjectionWatermark>(
            watermarkEnvelope,
            ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkDocumentKind);
        if (oldestRetainedSequence <= watermark.RetainedFromSequence)
            return;
        if (oldestRetainedSequence > watermark.Sequence)
            throw new ArgumentOutOfRangeException(nameof(oldestRetainedSequence));

        var operations = new List<ActivityDesignWriteOperation>();
        foreach (var kind in new[]
                 {
                     ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind,
                     ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind,
                     ActivitiesDesignStorageManifest.ActivityVersionManagementProjectionDocumentKind
                 })
        {
            string? continuation = null;
            var continuations = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                var page = await boundedStore.QueryAsync(
                    new ActivityDesignQuery(
                        kind,
                        ActivitiesDesignStorageManifest.ManagementExpiredQuery,
                        [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.LessThanOrEqual(
                            ActivitiesDesignStorageManifest.ManagementValidToField,
                            GroundworkActivityManagementProjectionWriter.SequenceKey(oldestRetainedSequence)))],
                        [
                            new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.ManagementValidToField),
                            new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.ManagementResourceIdField),
                            new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.ManagementValidFromField)
                        ],
                        Take: ActivityDesignQueryPager.PageSize,
                        ContinuationToken: continuation),
                    cancellationToken);
                operations.AddRange(page.Documents.Select(x => ActivityDesignWriteOperation.Delete(
                    new ActivityDesignDeleteRequest(kind, x.Id, x.Version))));
                if (page.NextContinuationToken is null)
                    break;
                if (page.Documents.Count == 0 || !continuations.Add(page.NextContinuationToken))
                    throw new InvalidDataException("Activity-management retention continuation repeated or advanced an empty page.");
                continuation = page.NextContinuationToken;
            }
        }

        for (var sequence = watermark.RetainedFromSequence; sequence < oldestRetainedSequence; sequence++)
        {
            var marker = await store.LoadAsync(
                ActivitiesDesignStorageManifest.ActivityManagementProjectionSnapshotDocumentKind,
                GroundworkActivityManagementProjectionWriter.SequenceKey(sequence),
                cancellationToken);
            if (marker is not null)
            {
                operations.Add(ActivityDesignWriteOperation.Delete(new ActivityDesignDeleteRequest(
                    marker.DocumentKind,
                    marker.Id,
                    marker.Version)));
            }
        }

        watermark.RetainedFromSequence = oldestRetainedSequence;
        watermark.LastModifiedAt = changedAt;
        var save = GroundworkV2ActivityDesignDocumentWriter.ToSaveRequest(
            ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkDocumentKind,
            ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkCollection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            watermark,
            JsonOptions);
        operations.Add(ActivityDesignWriteOperation.Save(new ActivityDesignSaveRequest(
            save.DocumentKind,
            save.Id,
            save.SchemaVersion,
            save.ContentJson,
            watermarkEnvelope.Version)));

        await store.WriteAllAsync(
            ActivityDesignCommitScope.Of(
                ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityVersionManagementProjectionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityManagementProjectionSnapshotDocumentKind,
                ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkDocumentKind),
            operations,
            cancellationToken);
    }

    private static T Deserialize<T>(ActivityDesignDocument envelope, string kind)
        where T : Elsa.Primitives.Entities.Entity
    {
        var document = JsonSerializer.Deserialize<GroundworkV2ActivityDesignDocument<T>>(envelope.ContentJson, JsonOptions);
        return document?.Entity ?? throw new InvalidOperationException(
            $"Document '{envelope.Id}' of kind '{kind}' could not be deserialized as {typeof(T).Name}.");
    }
}
