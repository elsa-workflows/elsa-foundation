namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

// Identity/position receipts only. Prefix retention must preserve these with the journal's replay
// receipts; full run deletion removes them in the same caller-owned transaction.
internal sealed class RuntimeHistoryOccurrenceEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
    public string ActivityExecutionId { get; set; } = null!;
    public string ActivityExecutionIdHash { get; set; } = null!;
    public string FirstHistoryEventId { get; set; } = null!;
    public long FirstSequence { get; set; }
    public string? ParentActivityExecutionId { get; set; }
    public bool AncestryKnown { get; set; }
}

internal sealed class RuntimeHistoryOccurrenceMembershipEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
    public string ScopeOwnerId { get; set; } = null!;
    public string? ActivityScopeId { get; set; }
    public string ActivityExecutionId { get; set; } = null!;
    public string ActivityExecutionIdHash { get; set; } = null!;
    public long FirstSequence { get; set; }
    public long Ordinal { get; set; }
}

internal sealed class RuntimeHistoryOccurrenceCounterEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
    public string? ActivityScopeId { get; set; }
    public string ActivityScopeIdHash { get; set; } = null!;
    public long NextOrdinal { get; set; }
    public long ProjectionStartsAtSequence { get; set; }
    public long CoveredThroughSequence { get; set; }
    public long? FirstUnknownObservationSequence { get; set; }
}
