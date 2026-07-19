using Elsa.Tagging.Core.Models;

namespace Elsa.Tagging.Core.Contracts;

public interface ITagDefinitionStore
{
    ValueTask<TagDefinition?> FindByCanonicalKeyAsync(string canonicalKey, CancellationToken cancellationToken = default);
    ValueTask<TagDefinitionRevisionedRecord?> FindWithRevisionAsync(string tagDefinitionId, CancellationToken cancellationToken = default);
    async ValueTask<IReadOnlyList<TagDefinition>> ListByIdsAsync(
        IReadOnlyCollection<string> tagDefinitionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagDefinitionIds);
        if (tagDefinitionIds.Count > 100)
            throw new ArgumentOutOfRangeException(nameof(tagDefinitionIds), "At most 100 tag definitions can be resolved at once.");

        var definitions = new List<TagDefinition>(tagDefinitionIds.Count);
        foreach (var tagDefinitionId in tagDefinitionIds.Distinct(StringComparer.Ordinal))
        {
            var record = await FindWithRevisionAsync(tagDefinitionId, cancellationToken);
            if (record is not null)
                definitions.Add(record.Definition);
        }
        return definitions;
    }
    ValueTask<IReadOnlyList<TagDefinition>> ListAsync(TagDefinitionListRequest request, CancellationToken cancellationToken = default);
    async ValueTask<IReadOnlyList<TagDefinitionRevisionedRecord>> ListWithRevisionsAsync(
        TagDefinitionListRequest request,
        CancellationToken cancellationToken = default)
    {
        var definitions = await ListAsync(request, cancellationToken);
        var records = new List<TagDefinitionRevisionedRecord>(definitions.Count);
        foreach (var definition in definitions)
        {
            var record = await FindWithRevisionAsync(definition.Id, cancellationToken);
            if (record is not null)
                records.Add(record);
        }
        return records;
    }
    ValueTask<bool> TryAddAsync(TagDefinition definition, CancellationToken cancellationToken = default);
    ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(TagDefinition definition, string expectedRevision, CancellationToken cancellationToken = default);
}

public interface ITagDefinitionAuditStore
{
    ValueTask AppendAsync(TagDefinitionAuditRecord record, CancellationToken cancellationToken = default);
}

public interface IControlledTagValueStore
{
    ValueTask<ControlledTagValue?> FindByCanonicalKeyAsync(string tagDefinitionId, string canonicalKey, CancellationToken cancellationToken = default);
    ValueTask<ControlledTagValueRevisionedRecord?> FindWithRevisionAsync(string controlledTagValueId, CancellationToken cancellationToken = default);
    async ValueTask<IReadOnlyList<ControlledTagValue>> ListByIdsAsync(
        IReadOnlyCollection<string> controlledTagValueIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(controlledTagValueIds);
        if (controlledTagValueIds.Count > 100)
            throw new ArgumentOutOfRangeException(
                nameof(controlledTagValueIds),
                "At most 100 controlled tag values can be resolved at once.");

        var values = new List<ControlledTagValue>(controlledTagValueIds.Count);
        foreach (var controlledTagValueId in controlledTagValueIds.Distinct(StringComparer.Ordinal))
        {
            var record = await FindWithRevisionAsync(controlledTagValueId, cancellationToken);
            if (record is not null)
                values.Add(record.Value);
        }

        return values;
    }
    ValueTask<IReadOnlyList<ControlledTagValueRevisionedRecord>> ListWithRevisionsAsync(ControlledTagValueListRequest request, CancellationToken cancellationToken = default);
    ValueTask<bool> TryAddAsync(ControlledTagValue value, CancellationToken cancellationToken = default);
    ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(ControlledTagValue value, string expectedRevision, CancellationToken cancellationToken = default);
}

public interface IControlledTagValueAuditStore
{
    ValueTask AppendAsync(ControlledTagValueAuditRecord record, CancellationToken cancellationToken = default);
}

/// <summary>Commits a catalog mutation and its immutable audit fact as one persistence operation.</summary>
public interface ITagDefinitionAtomicChangeStore
{
    ValueTask<bool> TryAddAndAppendAuditAsync(
        TagDefinition definition,
        TagDefinitionAuditRecord audit,
        CancellationToken cancellationToken = default);

    ValueTask<TagDefinitionSaveResult> SaveWithRevisionAndAppendAuditAsync(
        TagDefinition definition,
        string expectedRevision,
        TagDefinitionAuditRecord audit,
        CancellationToken cancellationToken = default);
}

public interface IControlledTagValueAtomicChangeStore
{
    ValueTask<ControlledTagValueCreateResult> TryAddWithinLimitAndAppendAuditAsync(
        ControlledTagValue value,
        ControlledTagValueAuditRecord audit,
        int expectedCount,
        int maximumCount,
        CancellationToken cancellationToken = default);

    ValueTask<TagDefinitionSaveResult> SaveWithRevisionAndAppendAuditAsync(ControlledTagValue value, string expectedRevision, ControlledTagValueAuditRecord audit, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marks a tag catalog persistence provider that is durable and safe to expose through the authoring API.
/// </summary>
/// <remarks>
/// The default in-memory tag store intentionally does not implement this contract. It is useful for
/// isolated tests and local composition, but must not make a tenant catalog discoverable to clients.
/// </remarks>
public interface ITagDefinitionCatalogPersistence
{
}

/// <summary>
/// Marks a durable tag catalog provider whose controlled-value mutations are committed atomically
/// with their audit records and catalog cardinality guard.
/// </summary>
public interface IControlledTagValueCatalogPersistence
{
}

public interface ITagDefinitionAuditContext
{
    string Actor { get; }
    string CorrelationId { get; }
    string? TenantId => null;
}

public interface ITagDefinitionManager
{
    ValueTask<TagDefinition> CreateAsync(CreateTagDefinitionRequest request, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<TagDefinition>> ListAsync(TagDefinitionListRequest request, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<TagDefinitionRevisionedRecord>> ListWithRevisionsAsync(
        TagDefinitionListRequest request,
        CancellationToken cancellationToken = default);
    ValueTask<TagDefinitionRevisionedRecord?> FindWithRevisionAsync(string tagDefinitionId, CancellationToken cancellationToken = default);
    ValueTask<TagDefinitionRevisionedRecord> UpdateAsync(string tagDefinitionId, UpdateTagDefinitionRequest request, string expectedRevision, CancellationToken cancellationToken = default);
}

public interface IControlledTagValueManager
{
    ValueTask<ControlledTagValue> CreateAsync(CreateControlledTagValueRequest request, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ControlledTagValueRevisionedRecord>> ListWithRevisionsAsync(ControlledTagValueListRequest request, CancellationToken cancellationToken = default);
    ValueTask<ControlledTagValueRevisionedRecord?> FindWithRevisionAsync(string controlledTagValueId, CancellationToken cancellationToken = default);
    ValueTask<ControlledTagValueRevisionedRecord> UpdateAsync(string controlledTagValueId, UpdateControlledTagValueRequest request, string expectedRevision, CancellationToken cancellationToken = default);
}

public sealed record TagDefinitionRevisionedRecord(TagDefinition Definition, string Revision);
public sealed record ControlledTagValueRevisionedRecord(ControlledTagValue Value, string Revision);
public sealed record TagDefinitionSaveResult(TagDefinitionSaveStatus Status, string? Revision = null);

public enum ControlledTagValueCreateResult
{
    Created,
    Conflict,
    LimitReached
}

public enum TagDefinitionSaveStatus
{
    Saved,
    Conflict,
    NotFound
}

public sealed class TagDefinitionConflictException(string tagDefinitionId) : InvalidOperationException($"Tag definition '{tagDefinitionId}' changed concurrently.")
{
    public string TagDefinitionId { get; } = tagDefinitionId;
}
