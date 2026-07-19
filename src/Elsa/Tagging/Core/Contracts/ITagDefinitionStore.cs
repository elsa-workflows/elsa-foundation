using Elsa.Tagging.Core.Models;

namespace Elsa.Tagging.Core.Contracts;

public interface ITagDefinitionStore
{
    ValueTask<TagDefinition?> FindByCanonicalKeyAsync(string canonicalKey, CancellationToken cancellationToken = default);
    ValueTask<TagDefinitionRevisionedRecord?> FindWithRevisionAsync(string tagDefinitionId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<TagDefinition>> ListAsync(TagDefinitionListRequest request, CancellationToken cancellationToken = default);
    ValueTask<bool> TryAddAsync(TagDefinition definition, CancellationToken cancellationToken = default);
    ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(TagDefinition definition, string expectedRevision, CancellationToken cancellationToken = default);
}

public interface ITagDefinitionAuditStore
{
    ValueTask AppendAsync(TagDefinitionAuditRecord record, CancellationToken cancellationToken = default);
}

public interface ITagDefinitionAuditContext
{
    string Actor { get; }
    string CorrelationId { get; }
}

public interface ITagDefinitionManager
{
    ValueTask<TagDefinition> CreateAsync(CreateTagDefinitionRequest request, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<TagDefinition>> ListAsync(TagDefinitionListRequest request, CancellationToken cancellationToken = default);
    ValueTask<TagDefinitionRevisionedRecord?> FindWithRevisionAsync(string tagDefinitionId, CancellationToken cancellationToken = default);
    ValueTask<TagDefinitionRevisionedRecord> UpdateAsync(string tagDefinitionId, UpdateTagDefinitionRequest request, string expectedRevision, CancellationToken cancellationToken = default);
}

public sealed record TagDefinitionRevisionedRecord(TagDefinition Definition, string Revision);
public sealed record TagDefinitionSaveResult(TagDefinitionSaveStatus Status, string? Revision = null);

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
