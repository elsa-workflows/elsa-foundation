namespace Elsa.Tagging.Core.Models;

/// <summary>Append-only catalog change record. It deliberately contains no workflow assignment data.</summary>
public sealed record TagDefinitionAuditRecord(
    string Id,
    string TagDefinitionId,
    string CanonicalKey,
    string Operation,
    DateTimeOffset OccurredAt,
    string Actor,
    string CorrelationId);
