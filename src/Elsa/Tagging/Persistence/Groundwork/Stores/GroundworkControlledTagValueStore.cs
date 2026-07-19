using System.Text.Json;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Tagging.Persistence.Groundwork.Stores;

public sealed class GroundworkControlledTagValueStore(
    IDocumentStore store,
    IBoundedDocumentStore? boundedStore = null) : IControlledTagValueStore, IControlledTagValueAuditStore, IControlledTagValueAtomicChangeStore
{
    private IBoundedDocumentStore BoundedStore => boundedStore ?? store as IBoundedDocumentStore ?? throw new InvalidOperationException("Controlled tag value queries require an admitted bounded document-store runtime.");

    public async ValueTask<ControlledTagValue?> FindByCanonicalKeyAsync(string tagDefinitionId, string canonicalKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagDefinitionId);
        TagDefinitionConstraints.ValidateCanonicalKey(canonicalKey, true);
        var envelope = await store.LoadAsync(TaggingStorageManifest.ControlledTagValueDocumentKind, DocumentId(tagDefinitionId, canonicalKey), cancellationToken);
        return envelope is null ? null : Deserialize(envelope).Value;
    }

    public async ValueTask<ControlledTagValueRevisionedRecord?> FindWithRevisionAsync(string controlledTagValueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlledTagValueId);
        var query = new DocumentQuery(TaggingStorageManifest.ControlledTagValueDocumentKind, TaggingStorageManifest.FindValueByIdQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(TaggingStorageManifest.ControlledTagValueIdField, controlledTagValueId))], [], 0, 1);
        var envelope = (await BoundedStore.QueryAsync(query, cancellationToken)).Documents.SingleOrDefault();
        return envelope is null ? null : new ControlledTagValueRevisionedRecord(Deserialize(envelope).Value, TagDefinitionRevisionMapper.Revision(envelope));
    }

    public async ValueTask<IReadOnlyList<ControlledTagValue>> ListByIdsAsync(
        IReadOnlyCollection<string> controlledTagValueIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(controlledTagValueIds);
        if (controlledTagValueIds.Count > 100)
            throw new ArgumentOutOfRangeException(
                nameof(controlledTagValueIds),
                "At most 100 controlled tag values can be resolved at once.");
        var ids = controlledTagValueIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
            return [];

        var query = new DocumentQuery(
            TaggingStorageManifest.ControlledTagValueDocumentKind,
            TaggingStorageManifest.FindValueByIdQuery,
            [
                DocumentQueryClause.Of(DocumentQueryComparison.In(
                    TaggingStorageManifest.ControlledTagValueIdField,
                    ids))
            ],
            [],
            0,
            ids.Length);
        return (await BoundedStore.QueryAsync(query, cancellationToken)).Documents
            .Select(envelope => Deserialize(envelope).Value)
            .ToArray();
    }

    public async ValueTask<IReadOnlyList<ControlledTagValueRevisionedRecord>> ListWithRevisionsAsync(ControlledTagValueListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TagDefinitionId);
        var records = new List<ControlledTagValueRevisionedRecord>();
        const int pageSize = 250;
        for (var skip = 0; ; skip += pageSize)
        {
            var clauses = new List<DocumentQueryClause> { DocumentQueryClause.Of(DocumentQueryComparison.Equal(TaggingStorageManifest.ControlledTagValueDefinitionIdField, request.TagDefinitionId)) };
            if (request.ActiveOnly) clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.Equal(TaggingStorageManifest.StatusField, "active")));
            var query = new DocumentQuery(TaggingStorageManifest.ControlledTagValueDocumentKind, TaggingStorageManifest.ListValuesByDefinitionQuery, clauses,
                [
                    new DocumentQueryOrder(TaggingStorageManifest.ControlledTagValueSortOrderField),
                    new DocumentQueryOrder(TaggingStorageManifest.ControlledTagValueDisplayNameSortKeyField),
                    new DocumentQueryOrder(TaggingStorageManifest.ControlledTagValueIdField)
                ],
                skip,
                pageSize);
            var result = await BoundedStore.QueryAsync(query, cancellationToken);
            records.AddRange(result.Documents.Select(x => new ControlledTagValueRevisionedRecord(Deserialize(x).Value, TagDefinitionRevisionMapper.Revision(x))));
            if (result.Documents.Count < pageSize || records.Count >= result.TotalCount)
                return records.OrderBy(x => x.Value.SortOrder).ThenBy(x => x.Value.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Value.Id, StringComparer.Ordinal).ToArray();
        }
    }

    public async ValueTask<bool> TryAddAsync(ControlledTagValue value, CancellationToken cancellationToken = default) =>
        (await store.SaveAsync(Save(value, 0), cancellationToken)).Status == DocumentStoreWriteStatus.Saved;

    public async ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(ControlledTagValue value, string expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!TagDefinitionRevisionMapper.TryExpectedVersion(expectedRevision, out var version)) return new(TagDefinitionSaveStatus.Conflict);
        return TagDefinitionRevisionMapper.ToResult(await store.SaveAsync(Save(value, version), cancellationToken));
    }

    public async ValueTask<ControlledTagValueCreateResult> TryAddWithinLimitAndAppendAuditAsync(
        ControlledTagValue value,
        ControlledTagValueAuditRecord audit,
        int expectedCount,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (expectedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedCount));
        if (maximumCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));

        var guardEnvelope = await store.LoadAsync(
            TaggingStorageManifest.ControlledTagValueCatalogGuardDocumentKind,
            value.TagDefinitionId,
            cancellationToken);
        var guard = guardEnvelope is null
            ? null
            : JsonSerializer.Deserialize<ControlledTagValueCatalogGuard>(
                guardEnvelope.ContentJson,
                TaggingGroundworkJson.Options)
              ?? throw new InvalidOperationException(
                  $"Controlled value catalog guard '{value.TagDefinitionId}' could not be deserialized.");
        var currentCount = Math.Max(guard?.Count ?? 0, expectedCount);
        if (currentCount >= maximumCount)
            return ControlledTagValueCreateResult.LimitReached;

        try
        {
            await store.SaveAllAsync(
                DocumentCommitScope.Of(
                    TaggingStorageManifest.ControlledTagValueDocumentKind,
                    TaggingStorageManifest.ControlledTagValueAuditDocumentKind,
                    TaggingStorageManifest.ControlledTagValueCatalogGuardDocumentKind),
                [
                    Save(value, 0),
                    SaveAudit(audit),
                    SaveCatalogGuard(
                        value.TagDefinitionId,
                        currentCount + 1,
                        guardEnvelope?.Version ?? 0)
                ],
                cancellationToken);
            return ControlledTagValueCreateResult.Created;
        }
        catch (DocumentAtomicWriteException)
        {
            return ControlledTagValueCreateResult.Conflict;
        }
    }

    public async ValueTask<TagDefinitionSaveResult> SaveWithRevisionAndAppendAuditAsync(ControlledTagValue value, string expectedRevision, ControlledTagValueAuditRecord audit, CancellationToken cancellationToken = default)
    {
        if (!TagDefinitionRevisionMapper.TryExpectedVersion(expectedRevision, out var version)) return new(TagDefinitionSaveStatus.Conflict);
        try
        {
            await store.SaveAllAsync(DocumentCommitScope.Of(TaggingStorageManifest.ControlledTagValueDocumentKind, TaggingStorageManifest.ControlledTagValueAuditDocumentKind), [Save(value, version), SaveAudit(audit)], cancellationToken);
            return new(TagDefinitionSaveStatus.Saved, $"gw:{(version + 1):D20}");
        }
        catch (DocumentAtomicWriteException exception) when (exception.Status == DocumentStoreWriteStatus.NotFound) { return new(TagDefinitionSaveStatus.NotFound); }
        catch (DocumentAtomicWriteException) { return new(TagDefinitionSaveStatus.Conflict); }
    }

    public async ValueTask AppendAsync(ControlledTagValueAuditRecord record, CancellationToken cancellationToken = default)
    {
        var result = await store.SaveAsync(SaveAudit(record), cancellationToken);
        if (result.Status != DocumentStoreWriteStatus.Saved) throw new InvalidOperationException($"Could not append controlled tag value audit record '{record.Id}'.");
    }

    private static SaveDocumentRequest Save(ControlledTagValue value, long expectedVersion)
    {
        ControlledTagValueConstraints.Validate(value.TagDefinitionId, value.CanonicalKey, value.DisplayName, value.Description, value.Color, value.SortOrder);
        var document = new ControlledTagValueDocument(
            value.Id,
            value.TagDefinitionId,
            value.CanonicalKey,
            value.Status.ToString().ToLowerInvariant(),
            value.SortOrder,
            ControlledTagValueConstraints.DisplayNameSortKey(value.DisplayName),
            value);
        return new(TaggingStorageManifest.ControlledTagValueDocumentKind, DocumentId(value.TagDefinitionId, value.CanonicalKey), TaggingStorageManifest.SchemaVersion, JsonSerializer.Serialize(document, TaggingGroundworkJson.Options), expectedVersion);
    }

    private static SaveDocumentRequest SaveAudit(ControlledTagValueAuditRecord audit) => new(TaggingStorageManifest.ControlledTagValueAuditDocumentKind, audit.Id, TaggingStorageManifest.SchemaVersion, JsonSerializer.Serialize(audit, TaggingGroundworkJson.Options), 0);
    private static SaveDocumentRequest SaveCatalogGuard(
        string tagDefinitionId,
        int count,
        long expectedVersion) => new(
        TaggingStorageManifest.ControlledTagValueCatalogGuardDocumentKind,
        tagDefinitionId,
        TaggingStorageManifest.SchemaVersion,
        JsonSerializer.Serialize(
            new ControlledTagValueCatalogGuard(tagDefinitionId, count),
            TaggingGroundworkJson.Options),
        expectedVersion);

    private static ControlledTagValueDocument Deserialize(DocumentEnvelope envelope) => JsonSerializer.Deserialize<ControlledTagValueDocument>(envelope.ContentJson, TaggingGroundworkJson.Options) ?? throw new InvalidOperationException($"Controlled tag value '{envelope.Id}' could not be deserialized.");
    private static string DocumentId(string tagDefinitionId, string canonicalKey) => tagDefinitionId + ":" + canonicalKey;
    private sealed record ControlledTagValueDocument(
        string ControlledTagValueId,
        string TagDefinitionId,
        string CanonicalKey,
        string Status,
        int SortOrder,
        string DisplayNameSortKey,
        ControlledTagValue Value);
    private sealed record ControlledTagValueCatalogGuard(string TagDefinitionId, int Count);
}
