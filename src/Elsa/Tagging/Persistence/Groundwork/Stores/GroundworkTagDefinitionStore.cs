using System.Text.Json;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Groundwork.Documents.Store;
using Groundwork.Core.Queries;

namespace Elsa.Tagging.Persistence.Groundwork.Stores;

public sealed class GroundworkTagDefinitionStore(
    IDocumentStore store,
    IBoundedDocumentStore? boundedStore = null) : ITagDefinitionStore, ITagDefinitionAuditStore
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

    public async ValueTask<IReadOnlyList<TagDefinition>> ListAsync(TagDefinitionListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var clauses = request.ActiveOnly
            ? new[] { DocumentQueryClause.Of(DocumentQueryComparison.Equal(TaggingStorageManifest.StatusField, "active")) }
            : [];
        var query = new DocumentQuery(
            TaggingStorageManifest.TagDefinitionDocumentKind,
            TaggingStorageManifest.ListQuery,
            clauses,
            [new DocumentQueryOrder(TaggingStorageManifest.CanonicalKeyField)],
            skip: 0,
            take: 250);
        var result = await BoundedStore.QueryAsync(query, cancellationToken);
        return result.Documents.Select(MapDefinition).ToArray();
    }

    public async ValueTask<bool> TryAddAsync(TagDefinition definition, CancellationToken cancellationToken = default)
    {
        var result = await SaveCoreAsync(definition, expectedVersion: 0, cancellationToken);
        return result.Status == DocumentStoreWriteStatus.Saved;
    }

    public async ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(TagDefinition definition, string expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!TagDefinitionRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return new TagDefinitionSaveResult(TagDefinitionSaveStatus.Conflict);
        return TagDefinitionRevisionMapper.ToResult(await SaveCoreAsync(definition, expectedVersion, cancellationToken));
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
        TagDefinitionConstraints.ValidateCanonicalKey(definition.CanonicalKey, isHostProvisioning: true);
        TagDefinitionConstraints.ValidateMutableFields(definition.DisplayName, definition.Description, definition.Color);
        var document = new TagDefinitionDocument(definition.Id, definition.CanonicalKey, definition.Status.ToString().ToLowerInvariant(), definition);
        return await store.SaveAsync(new SaveDocumentRequest(
            TaggingStorageManifest.TagDefinitionDocumentKind,
            definition.CanonicalKey,
            TaggingStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(document, TaggingGroundworkJson.Options),
            expectedVersion), cancellationToken);
    }

    private static TagDefinition MapDefinition(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<TagDefinitionDocument>(envelope.ContentJson, TaggingGroundworkJson.Options)!.Definition;

    private sealed record TagDefinitionDocument(string TagDefinitionId, string CanonicalKey, string Status, TagDefinition Definition);
}
