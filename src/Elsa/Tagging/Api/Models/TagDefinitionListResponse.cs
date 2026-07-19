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
            $"\"{record.Revision}\"");
}
