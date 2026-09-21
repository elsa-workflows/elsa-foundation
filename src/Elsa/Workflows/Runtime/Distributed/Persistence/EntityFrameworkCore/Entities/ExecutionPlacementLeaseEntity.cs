namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;

/// <summary>Relational projection of one scoped execution-placement lease.</summary>
public sealed class ExecutionPlacementLeaseEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public byte[] WorkflowExecutionIdOrderKey { get; set; } = null!;
    public string OwnerId { get; set; } = null!;
    public string OwnerIdHash { get; set; } = null!;
    public long PlacementToken { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public long ExpiresAtUtcTicks { get; set; }
    public int ExpiresAtOffsetMinutes { get; set; }
    public bool IsReleased { get; set; }

    /// <summary>
    /// Monotonic per row, never reused. <see cref="EfExecutionPlacementStore.IsStagedWrite"/> relies on that to
    /// tell one committed write from another; a delete or purge path added here must preserve it or replayed
    /// claims can mismatch.
    /// </summary>
    public long Revision { get; set; }
}
