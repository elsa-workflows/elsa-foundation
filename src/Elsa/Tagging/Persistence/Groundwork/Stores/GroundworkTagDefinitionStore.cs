using System.Text.Json;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Groundwork.Core.Queries;

namespace Elsa.Tagging.Persistence.Groundwork.Stores;

public sealed class GroundworkTagDefinitionStore(
    IDocumentStore store,
    IBoundedDocumentStore? boundedStore = null) : ITagDefinitionStore, ITagDefinitionAuditStore, ITagDefinitionAtomicChangeStore
{
    private IBoundedDocumentStore BoundedStore => boundedStore ?? store as IBoundedDocumentStore ?? throw new InvalidOperationException(
        "Tag definition queries require an admitted bounded document-store runtime.");

    public async ValueTask<TagDefinition?> FindByCanonicalKeyAsync(string canonicalKey, CancellationToken cancellationToken = default)
    {
        TagDefinitionConstraints.ValidateCanonicalKey(canonicalKey, isHostProvisioning: true);
        var envelope = await store.LoadAsync(TaggingStorageManifest.TagDefinitionDocumentKind, canonicalKey, cancellationToken);
        return envelope is null ? null : MapDefinition(envelope);
    }

    public async ValueTask<TagDefinitionRevisionedRecord?> FindWithRevisionAsync(string tagDefinitionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tagDefinitionId))
            throw new ArgumentException("A tag definition ID is required.", nameof(tagDefinitionId));
        var query = new DocumentQuery(
            TaggingStorageManifest.TagDefinitionDocumentKind,
            TaggingStorageManifest.FindByIdQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(TaggingStorageManifest.TagDefinitionIdField, tagDefinitionId))],
            [],
            skip: 0,
            take: 1);
        var envelope = (await BoundedStore.QueryAsync(query, cancellationToken)).Documents.SingleOrDefault();
        return envelope is null ? null : new TagDefinitionRevisionedRecord(MapDefinition(envelope), TagDefinitionRevisionMapper.Revision(envelope));
    }

    public async ValueTask<IReadOnlyList<TagDefinition>> ListByIdsAsync(
        IReadOnlyCollection<string> tagDefinitionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagDefinitionIds);
        if (tagDefinitionIds.Count > 100)
            throw new ArgumentOutOfRangeException(nameof(tagDefinitionIds), "At most 100 tag definitions can be resolved at once.");

        var distinctIds = tagDefinitionIds.Distinct(StringComparer.Ordinal).ToArray();
        if (distinctIds.Length == 0)
            return [];

        var query = new DocumentQuery(
            TaggingStorageManifest.TagDefinitionDocumentKind,
            TaggingStorageManifest.FindByIdQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.In(TaggingStorageManifest.TagDefinitionIdField, distinctIds))],
            [],
            skip: 0,
            take: distinctIds.Length);
        return (await BoundedStore.QueryAsync(query, cancellationToken)).Documents
            .Select(MapDefinition)
            .ToArray();
    }

    public async ValueTask<IReadOnlyList<TagDefinition>> ListAsync(TagDefinitionListRequest request, CancellationToken cancellationToken = default)
        => (await ListWithRevisionsAsync(request, cancellationToken))
            .Select(record => record.Definition)
            .ToArray();

    public async ValueTask<IReadOnlyList<TagDefinitionRevisionedRecord>> ListWithRevisionsAsync(
        TagDefinitionListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var clauses = request.ActiveOnly
            ? new[] { DocumentQueryClause.Of(DocumentQueryComparison.Equal(TaggingStorageManifest.StatusField, "active")) }
            : [];
        const int pageSize = 250;
        var records = new List<TagDefinitionRevisionedRecord>();
        for (var skip = 0; ; skip += pageSize)
        {
            var query = new DocumentQuery(
                TaggingStorageManifest.TagDefinitionDocumentKind,
                TaggingStorageManifest.ListQuery,
                clauses,
                [new DocumentQueryOrder(TaggingStorageManifest.CanonicalKeyField)],
                skip,
                take: pageSize);
            var result = await BoundedStore.QueryAsync(query, cancellationToken);
            records.AddRange(result.Documents.Select(envelope => new TagDefinitionRevisionedRecord(
                MapDefinition(envelope),
                TagDefinitionRevisionMapper.Revision(envelope))));
            if (result.Documents.Count < pageSize || records.Count >= result.TotalCount)
                return records;
        }
    }

    public async ValueTask<bool> TryAddAsync(TagDefinition definition, CancellationToken cancellationToken = default)
    {
        var result = await SaveCoreAsync(definition, expectedVersion: 0, cancellationToken);
        return result.Status == DocumentStoreWriteStatus.Saved;
    }

    public async ValueTask<bool> TryAddAndAppendAuditAsync(
        TagDefinition definition,
        TagDefinitionAuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(audit);
        try
        {
            await store.SaveAllAsync(
                DocumentCommitScope.Of(
                    TaggingStorageManifest.TagDefinitionDocumentKind,
                    TaggingStorageManifest.TagDefinitionAuditDocumentKind),
                [SaveDefinition(definition, expectedVersion: 0), SaveAudit(audit)],
                cancellationToken);
            return true;
        }
        catch (DocumentAtomicWriteException exception)
            when (exception.Status is DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound)
        {
            return false;
        }
    }

    public async ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(TagDefinition definition, string expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!TagDefinitionRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return new TagDefinitionSaveResult(TagDefinitionSaveStatus.Conflict);
        return TagDefinitionRevisionMapper.ToResult(await SaveCoreAsync(definition, expectedVersion, cancellationToken));
    }

    public async ValueTask<TagDefinitionSaveResult> SaveWithRevisionAndAppendAuditAsync(
        TagDefinition definition,
        string expectedRevision,
        TagDefinitionAuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(audit);
        if (!TagDefinitionRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return new TagDefinitionSaveResult(TagDefinitionSaveStatus.Conflict);

        try
        {
            await store.SaveAllAsync(
                DocumentCommitScope.Of(
                    TaggingStorageManifest.TagDefinitionDocumentKind,
                    TaggingStorageManifest.TagDefinitionAuditDocumentKind),
                [SaveDefinition(definition, expectedVersion), SaveAudit(audit)],
                cancellationToken);
            return new TagDefinitionSaveResult(
                TagDefinitionSaveStatus.Saved,
                $"gw:{(expectedVersion + 1):D20}");
        }
        catch (DocumentAtomicWriteException exception) when (exception.Status == DocumentStoreWriteStatus.NotFound)
        {
            return new TagDefinitionSaveResult(TagDefinitionSaveStatus.NotFound);
        }
        catch (DocumentAtomicWriteException)
        {
            return new TagDefinitionSaveResult(TagDefinitionSaveStatus.Conflict);
        }
    }

    public async ValueTask AppendAsync(TagDefinitionAuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var result = await store.SaveAsync(new SaveDocumentRequest(
            TaggingStorageManifest.TagDefinitionAuditDocumentKind,
            record.Id,
            TaggingStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(record, TaggingGroundworkJson.Options),
            0), cancellationToken);
        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw new InvalidOperationException($"Could not append tag definition audit record '{record.Id}'.");
    }

    private async ValueTask<DocumentStoreWriteResult> SaveCoreAsync(TagDefinition definition, long expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return await store.SaveAsync(SaveDefinition(definition, expectedVersion), cancellationToken);
    }

    private static SaveDocumentRequest SaveDefinition(TagDefinition definition, long expectedVersion)
    {
        TagDefinitionConstraints.ValidateCanonicalKey(definition.CanonicalKey, isHostProvisioning: true);
        TagDefinitionConstraints.ValidateValueSemantics(definition.ValueMode, definition.Cardinality);
        TagDefinitionConstraints.ValidateMutableFields(definition.DisplayName, definition.Description, definition.Color);
        var document = new TagDefinitionDocument(definition.Id, definition.CanonicalKey, definition.Status.ToString().ToLowerInvariant(), definition);
        return new(
            TaggingStorageManifest.TagDefinitionDocumentKind,
            definition.CanonicalKey,
            TaggingStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(document, TaggingGroundworkJson.Options),
            expectedVersion);
    }

    private static SaveDocumentRequest SaveAudit(TagDefinitionAuditRecord record) => new(
        TaggingStorageManifest.TagDefinitionAuditDocumentKind,
        record.Id,
        TaggingStorageManifest.SchemaVersion,
        JsonSerializer.Serialize(record, TaggingGroundworkJson.Options),
        0);

    private static TagDefinition MapDefinition(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<TagDefinitionDocument>(envelope.ContentJson, TaggingGroundworkJson.Options)!.Definition;

    private sealed record TagDefinitionDocument(string TagDefinitionId, string CanonicalKey, string Status, TagDefinition Definition);
}
