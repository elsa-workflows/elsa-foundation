using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;

namespace Elsa.Tagging.Core.Services;

internal sealed class InMemoryControlledTagValueStore : IControlledTagValueStore
{
    private readonly Dictionary<string, (ControlledTagValue Value, long Revision)> values = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    public ValueTask<ControlledTagValue?> FindByCanonicalKeyAsync(string tagDefinitionId, string canonicalKey, CancellationToken cancellationToken = default)
    {
        lock (gate)
            return ValueTask.FromResult(values.Values.Select(x => x.Value).SingleOrDefault(x => x.TagDefinitionId == tagDefinitionId && x.CanonicalKey == canonicalKey) is { } value ? Clone(value) : null);
    }

    public ValueTask<ControlledTagValueRevisionedRecord?> FindWithRevisionAsync(string controlledTagValueId, CancellationToken cancellationToken = default)
    {
        lock (gate)
            return ValueTask.FromResult(values.TryGetValue(controlledTagValueId, out var value) ? new ControlledTagValueRevisionedRecord(Clone(value.Value), value.Revision.ToString()) : null);
    }

    public ValueTask<IReadOnlyList<ControlledTagValueRevisionedRecord>> ListWithRevisionsAsync(ControlledTagValueListRequest request, CancellationToken cancellationToken = default)
    {
        lock (gate)
            return ValueTask.FromResult<IReadOnlyList<ControlledTagValueRevisionedRecord>>(values.Values
                .Where(x => x.Value.TagDefinitionId == request.TagDefinitionId && (!request.ActiveOnly || x.Value.Status == TagDefinitionStatus.Active))
                .OrderBy(x => x.Value.SortOrder).ThenBy(x => x.Value.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Value.Id, StringComparer.Ordinal)
                .Select(x => new ControlledTagValueRevisionedRecord(Clone(x.Value), x.Revision.ToString())).ToArray());
    }

    public ValueTask<bool> TryAddAsync(ControlledTagValue value, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (values.ContainsKey(value.Id) || values.Values.Any(x => x.Value.TagDefinitionId == value.TagDefinitionId && x.Value.CanonicalKey == value.CanonicalKey))
                return ValueTask.FromResult(false);
            values.Add(value.Id, (Clone(value), 1));
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(ControlledTagValue value, string expectedRevision, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!values.TryGetValue(value.Id, out var existing)) return ValueTask.FromResult(new TagDefinitionSaveResult(TagDefinitionSaveStatus.NotFound));
            if (!long.TryParse(expectedRevision, out var expected) || expected != existing.Revision) return ValueTask.FromResult(new TagDefinitionSaveResult(TagDefinitionSaveStatus.Conflict));
            values[value.Id] = (Clone(value), existing.Revision + 1);
            return ValueTask.FromResult(new TagDefinitionSaveResult(TagDefinitionSaveStatus.Saved, (existing.Revision + 1).ToString()));
        }
    }

    private static ControlledTagValue Clone(ControlledTagValue value) => new()
    {
        Id = value.Id,
        TagDefinitionId = value.TagDefinitionId,
        CanonicalKey = value.CanonicalKey,
        DisplayName = value.DisplayName,
        Description = value.Description,
        Color = value.Color,
        Status = value.Status,
        SortOrder = value.SortOrder,
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt
    };
}

internal sealed class NullControlledTagValueAuditStore : IControlledTagValueAuditStore
{
    public ValueTask AppendAsync(ControlledTagValueAuditRecord record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}
