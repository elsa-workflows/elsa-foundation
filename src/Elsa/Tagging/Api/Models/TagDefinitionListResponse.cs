using Elsa.Tagging.Core.Models;
using Elsa.Tagging.Core.Contracts;

namespace Elsa.Tagging.Api.Models;

/// <summary>Envelope reserved for cursor and facet metadata without changing the catalog list contract.</summary>
public sealed record TagDefinitionListResponse(IReadOnlyList<TagDefinitionListItem> Items, bool CanManage);

public sealed record TagDefinitionListItem(
    string Id,
    string CanonicalKey,
    string DisplayName,
    string? Description,
    string? Color,
    TagDefinitionStatus Status,
    TagDefinitionEligibility Eligibility,
    TagValueMode ValueMode,
    TagCardinality Cardinality,
    string Revision)
{
    public static TagDefinitionListItem From(TagDefinitionRevisionedRecord record) =>
        new(
            record.Definition.Id,
            record.Definition.CanonicalKey,
            record.Definition.DisplayName,
            record.Definition.Description,
            record.Definition.Color,
            record.Definition.Status,
            record.Definition.Eligibility,
            record.Definition.ValueMode,
            record.Definition.Cardinality,
            $"\"{record.Revision}\"");
}

public sealed record ControlledTagValueListItem(string Id, string TagDefinitionId, string CanonicalKey, string DisplayName, string? Description, string? Color, TagDefinitionStatus Status, int SortOrder, string Revision)
{
    public static ControlledTagValueListItem From(ControlledTagValueRevisionedRecord record) => new(record.Value.Id, record.Value.TagDefinitionId, record.Value.CanonicalKey, record.Value.DisplayName, record.Value.Description, record.Value.Color, record.Value.Status, record.Value.SortOrder, $"\"{record.Revision}\"");
}

public sealed record ControlledTagValueListResponse(
    IReadOnlyList<ControlledTagValueListItem> Items,
    bool CanManage);
