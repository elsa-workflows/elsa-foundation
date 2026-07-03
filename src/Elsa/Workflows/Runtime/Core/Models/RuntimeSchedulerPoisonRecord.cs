namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// A durable record of a scheduler work handler crash. When a handler throws, the drainer no longer drops the
/// already-dequeued work item silently: it records the fault here (honoring <see cref="Contracts.IRuntimeDomainRetryPolicy"/>)
/// so an operator or a recovery pump can inspect poisoned work and, when the policy asks to retry, the item is
/// re-enqueued instead of lost (RT-1 gap b).
/// </summary>
public sealed record RuntimeSchedulerPoisonRecord
{
    public RuntimeSchedulerPoisonRecord(
        string workflowExecutionId,
        string workItemId,
        WorkflowExecutionCommandKind commandKind,
        string handlerName,
        RuntimeFaultInfo fault,
        int failureCount,
        RuntimeSchedulerPoisonDisposition disposition,
        DateTimeOffset firstFailedAt,
        DateTimeOffset lastFailedAt,
        DateTimeOffset? nextRetryAt = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerName);
        ArgumentNullException.ThrowIfNull(fault);

        if (failureCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(failureCount), "A poison record must record at least one failure.");

        if (lastFailedAt < firstFailedAt)
            throw new ArgumentException("Last failure time cannot be earlier than the first failure time.", nameof(lastFailedAt));

        if (disposition == RuntimeSchedulerPoisonDisposition.RetryScheduled && nextRetryAt is null)
            throw new ArgumentException("A retry-scheduled poison record requires a next-retry time.", nameof(nextRetryAt));

        if (disposition == RuntimeSchedulerPoisonDisposition.Poisoned && nextRetryAt is not null)
            throw new ArgumentException("A poisoned record cannot carry a next-retry time.", nameof(nextRetryAt));

        WorkflowExecutionId = workflowExecutionId;
        WorkItemId = workItemId;
        CommandKind = commandKind;
        HandlerName = handlerName;
        Fault = fault;
        FailureCount = failureCount;
        Disposition = disposition;
        FirstFailedAt = firstFailedAt;
        LastFailedAt = lastFailedAt;
        NextRetryAt = nextRetryAt;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string WorkflowExecutionId { get; }
    public string WorkItemId { get; }
    public WorkflowExecutionCommandKind CommandKind { get; }
    public string HandlerName { get; }
    public RuntimeFaultInfo Fault { get; }
    public int FailureCount { get; }
    public RuntimeSchedulerPoisonDisposition Disposition { get; }
    public DateTimeOffset FirstFailedAt { get; }
    public DateTimeOffset LastFailedAt { get; }
    public DateTimeOffset? NextRetryAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public enum RuntimeSchedulerPoisonDisposition
{
    /// <summary>The retry policy declined to retry; the work item is parked for inspection/intervention.</summary>
    Poisoned,

    /// <summary>The retry policy asked to retry; the work item was (or will be) re-driven at <c>NextRetryAt</c>.</summary>
    RetryScheduled
}
