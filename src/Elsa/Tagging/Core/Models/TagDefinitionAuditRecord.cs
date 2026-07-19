namespace Elsa.Tagging.Core.Models;

/// <summary>Append-only catalog change record. It deliberately contains no workflow assignment data.</summary>
public sealed record TagDefinitionAuditRecord(
    string Id,
    string TagDefinitionId,
    string CanonicalKey,
    string Operation,
    DateTimeOffset OccurredAt,
    string? TenantId,
    string Actor,
    string CorrelationId,
    TagDefinitionAuditValues? Before,
    TagDefinitionAuditValues After);

/// <summary>Semantic catalog state captured by an append-only change fact.</summary>
public sealed record TagDefinitionAuditValues(
    string DisplayName,
    string? Description,
    string? Color,
    TagDefinitionStatus Status)
{
    public static TagDefinitionAuditValues From(TagDefinition definition) => new(
        definition.DisplayName,
        definition.Description,
        definition.Color,
        definition.Status);
}
