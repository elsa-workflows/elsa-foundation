namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;

/// <summary>Relational projection of one scoped execution command transport item.</summary>
public sealed class ExecutionCommandTransportItemEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string TransportItemId { get; set; } = null!;
    public string TransportItemIdHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public long Sequence { get; set; }
    public long EnqueuedAtUtcTicks { get; set; }
    public int EnqueuedAtOffsetMinutes { get; set; }
    public long VisibleAtUtcTicks { get; set; }
    public string? LeaseOwnerId { get; set; }
    public long LeaseToken { get; set; }
    public long LeaseExpiresAtUtcTicks { get; set; }
    public int LeaseExpiresAtOffsetMinutes { get; set; }
    public string PayloadJson { get; set; } = null!;
    public long Revision { get; set; }
}
