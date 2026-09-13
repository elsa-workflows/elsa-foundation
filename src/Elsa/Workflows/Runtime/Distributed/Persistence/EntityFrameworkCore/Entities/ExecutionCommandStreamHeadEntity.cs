namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;

/// <summary>Provider-neutral durable high-water and pending summary for one scoped execution command stream.</summary>
public sealed class ExecutionCommandStreamHeadEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public byte[] WorkflowExecutionIdOrderKey { get; set; } = null!;
    public long LastSequence { get; set; }
    public long PendingCount { get; set; }
    public long PendingVisibleAtUtcTicks { get; set; }
    public long PendingSequence { get; set; }
    public long Revision { get; set; }
}
