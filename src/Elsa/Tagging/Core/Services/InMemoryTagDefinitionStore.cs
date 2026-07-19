using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;

namespace Elsa.Tagging.Core.Services;

internal sealed class InMemoryTagDefinitionStore : ITagDefinitionStore
{
    private readonly Dictionary<string, (TagDefinition Definition, long Revision)> definitions = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    public ValueTask<TagDefinition?> FindByCanonicalKeyAsync(string canonicalKey, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Find(canonicalKey) is { } found ? Clone(found.Definition) : null);

    public ValueTask<TagDefinitionRevisionedRecord?> FindWithRevisionAsync(string tagDefinitionId, CancellationToken cancellationToken = default)
    {
        var found = FindById(tagDefinitionId);
        return ValueTask.FromResult(found is null ? null : new TagDefinitionRevisionedRecord(Clone(found.Value.Definition), found.Value.Revision.ToString()));
    }

    public ValueTask<IReadOnlyList<TagDefinition>> ListAsync(TagDefinitionListRequest request, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var values = definitions.Values.Select(value => Clone(value.Definition))
                .Where(value => !request.ActiveOnly || value.Status == TagDefinitionStatus.Active)
                .OrderBy(value => value.CanonicalKey, StringComparer.Ordinal)
                .ToArray();
            return ValueTask.FromResult<IReadOnlyList<TagDefinition>>(values);
        }
    }

    public ValueTask<bool> TryAddAsync(TagDefinition definition, CancellationToken cancellationToken = default)
    {
        lock (gate)
            return ValueTask.FromResult(definitions.TryAdd(definition.CanonicalKey, (Clone(definition), 1)));
    }

    public ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(TagDefinition definition, string expectedRevision, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!definitions.TryGetValue(definition.CanonicalKey, out var existing))
                return ValueTask.FromResult(new TagDefinitionSaveResult(TagDefinitionSaveStatus.NotFound));
            if (!long.TryParse(expectedRevision, out var expected) || existing.Revision != expected)
                return ValueTask.FromResult(new TagDefinitionSaveResult(TagDefinitionSaveStatus.Conflict));
            var revision = existing.Revision + 1;
            definitions[definition.CanonicalKey] = (Clone(definition), revision);
            return ValueTask.FromResult(new TagDefinitionSaveResult(TagDefinitionSaveStatus.Saved, revision.ToString()));
        }
    }

    private (TagDefinition Definition, long Revision)? Find(string canonicalKey)
    {
        lock (gate)
            return definitions.TryGetValue(canonicalKey, out var definition) ? definition : null;
    }

    private (TagDefinition Definition, long Revision)? FindById(string tagDefinitionId)
    {
        lock (gate)
        {
            foreach (var candidate in definitions.Values)
                if (candidate.Definition.Id == tagDefinitionId)
                    return candidate;
            return null;
        }
    }

    private static TagDefinition Clone(TagDefinition definition) => new()
    {
        Id = definition.Id,
        CanonicalKey = definition.CanonicalKey,
        DisplayName = definition.DisplayName,
        Description = definition.Description,
        Color = definition.Color,
        Status = definition.Status,
        Eligibility = definition.Eligibility,
        CreatedAt = definition.CreatedAt,
        UpdatedAt = definition.UpdatedAt
    };
}

internal sealed class NullTagDefinitionAuditStore : ITagDefinitionAuditStore
{
    public ValueTask AppendAsync(TagDefinitionAuditRecord record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

internal sealed class SystemTagDefinitionAuditContext : ITagDefinitionAuditContext
{
    public string Actor => "system";
    public string CorrelationId => "system";
}
