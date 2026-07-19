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

/// <summary>Append-only controlled-value change record. Assignment changes remain target-domain facts.</summary>
public sealed record ControlledTagValueAuditRecord(
    string Id,
    string ControlledTagValueId,
    string TagDefinitionId,
    string CanonicalKey,
    string Operation,
    DateTimeOffset OccurredAt,
    string? TenantId,
    string Actor,
    string CorrelationId,
    ControlledTagValueAuditValues? Before,
    ControlledTagValueAuditValues After);

public sealed record ControlledTagValueAuditValues(
    string DisplayName,
    string? Description,
    string? Color,
    TagDefinitionStatus Status,
    int SortOrder)
{
    public static ControlledTagValueAuditValues From(ControlledTagValue value) => new(
        value.DisplayName,
        value.Description,
        value.Color,
        value.Status,
        value.SortOrder);
}
