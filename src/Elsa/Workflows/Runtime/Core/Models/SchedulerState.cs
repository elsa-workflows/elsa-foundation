namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Minimal single-writer scheduler continuation state for a workflow execution.
/// </summary>
public sealed record SchedulerState(
    string WorkflowExecutionId,
    long Version,
    IReadOnlyCollection<ScheduledActivityWorkItem> PendingWork,
    IReadOnlyCollection<VolatileWaitRegistration> VolatileWaits);

public sealed record ScheduledActivityWorkItem(
    string WorkItemId,
    string WorkflowExecutionId,
    string ExecutableNodeId,
    string? ActivityExecutionId,
    string? SchedulingActivityExecutionId,
    string? BranchId,
    string? IterationId,
    DateTimeOffset EnqueuedAt,
    string Reason);

public sealed record VolatileWaitRegistration(
    string WaitId,
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string? BranchId,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? ExpiresAt,
    string AwaitableKind);
