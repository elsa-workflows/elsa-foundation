using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Locking.Core;
using Elsa.Persistence.Groundwork.Querying;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

/// <summary>Explicit maintenance path for advancing the supported management-cursor retention floor.</summary>
public sealed class GroundworkActivityManagementProjectionRetention(
    IDocumentStore store,
    IBoundedDocumentStore boundedStore,
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

        var operations = new List<DocumentWriteOperation>();
        foreach (var kind in new[]
                 {
                     ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind,
                     ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind,
                     ActivitiesDesignStorageManifest.ActivityVersionManagementProjectionDocumentKind
                 })
        {
            var offset = 0;
            while (true)
            {
                var page = await boundedStore.QueryAsync(
                    new DocumentQuery(
                        kind,
                        ActivitiesDesignStorageManifest.ManagementExpiredQuery,
                        [DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                            ActivitiesDesignStorageManifest.ManagementValidToField,
                            GroundworkActivityManagementProjectionWriter.SequenceKey(oldestRetainedSequence)))],
                        [new DocumentQueryOrder(ActivitiesDesignStorageManifest.ManagementValidToField)],
                        offset,
                        500),
                    cancellationToken);
                operations.AddRange(page.Documents.Select(x => DocumentWriteOperation.Delete(
                    new DeleteDocumentRequest(kind, x.Id, x.Version))));
                offset += page.Documents.Count;
                if (offset >= page.TotalCount)
                    break;
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
                operations.Add(DocumentWriteOperation.Delete(new DeleteDocumentRequest(
                    marker.DocumentKind,
                    marker.Id,
                    marker.Version)));
            }
        }

        watermark.RetainedFromSequence = oldestRetainedSequence;
        watermark.LastModifiedAt = changedAt;
        var save = GroundworkDocumentWriter.ToSaveRequest(
            ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkDocumentKind,
            ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkCollection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            watermark,
            JsonOptions);
        operations.Add(DocumentWriteOperation.Save(new SaveDocumentRequest(
            save.DocumentKind,
            save.Id,
            save.SchemaVersion,
            save.ContentJson,
            watermarkEnvelope.Version)));

        await store.WriteAllAsync(
            DocumentCommitScope.Of(
                ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityVersionManagementProjectionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityManagementProjectionSnapshotDocumentKind,
                ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkDocumentKind),
            operations,
            cancellationToken);
    }

    private static T Deserialize<T>(DocumentEnvelope envelope, string kind)
        where T : Elsa.Primitives.Entities.Entity
    {
        var document = JsonSerializer.Deserialize<GroundworkDocument<T>>(envelope.ContentJson, JsonOptions);
        return document?.Entity ?? throw new InvalidOperationException(
            $"Document '{envelope.Id}' of kind '{kind}' could not be deserialized as {typeof(T).Name}.");
    }
}
