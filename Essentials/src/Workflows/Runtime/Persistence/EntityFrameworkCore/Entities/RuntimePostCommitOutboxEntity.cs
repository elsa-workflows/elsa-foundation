namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>Relational envelope and delivery projections for one durable post-commit outbox item.</summary>
public sealed class RuntimePostCommitOutboxEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string OutboxItemId { get; set; } = null!;
    public string OutboxItemIdHash { get; set; } = null!;
    public string OutboxItemIdOrderKey { get; set; } = null!;
    public string? WorkflowExecutionId { get; set; }
    public string? WorkflowExecutionIdHash { get; set; }
    public string? WorkflowExecutionIdOrderKey { get; set; }
    /// <summary>Null preserves the historical workflow source; external sources have explicit checked projections.</summary>
    public int? SourceKind { get; set; }
    public string? SourceId { get; set; }
    public string? TriggerIdHash { get; set; }
    public string? SourceKeyHash { get; set; }
    public string IntentKind { get; set; } = null!;
    public string IntentKindHash { get; set; } = null!;
    public int Status { get; set; }
    public long RecordedAtUtcTicks { get; set; }
    public long? DeliverableAtUtcTicks { get; set; }
    public long ClaimableAtUtcTicks { get; set; }
    public bool ClaimableIsEligible { get; set; }

    /// <summary>
    /// When a claim delivered the item (#3661): its delivery time once it is Delivered under a fencing token, otherwise
    /// null. Null also on a row last written before the column existed.
    /// </summary>
    public long? ClaimDeliveredAtUtcTicks { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
