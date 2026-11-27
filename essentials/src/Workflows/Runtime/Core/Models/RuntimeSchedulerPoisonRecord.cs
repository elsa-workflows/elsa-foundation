namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// A durable record of a scheduler work handler crash. When a handler throws, the drainer no longer drops the
/// already-dequeued work item silently: it records the fault here (honoring <see cref="Contracts.IRuntimeDomainRetryPolicy"/>)
/// so an operator or a recovery pump can inspect poisoned work. A retry the queue holds is
/// <see cref="RuntimeSchedulerPoisonDisposition.RetryScheduled"/>; everything else is
/// <see cref="RuntimeSchedulerPoisonDisposition.Poisoned"/>. <c>docs/runtime-fault-behavior.md</c> maps each case.
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
        IReadOnlyDictionary<string, string>? metadata = null,
        RuntimeFaultInfo? innerFault = null)
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
        InnerFault = innerFault;
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

    /// <summary>
    /// The handler exception's first inner exception, when present, captured under the same
    /// <see cref="Contracts.IRuntimeFaultCapturePolicy"/> as <see cref="Fault"/>. Handler crashes are routinely
    /// wrapped (e.g. a checkpoint-writer exception around the physical storage fault), and without this the root
    /// cause was lost with the outer capture (#1031).
    /// </summary>
    public RuntimeFaultInfo? InnerFault { get; }

    public int FailureCount { get; }
    public RuntimeSchedulerPoisonDisposition Disposition { get; }
    public DateTimeOffset FirstFailedAt { get; }
    public DateTimeOffset LastFailedAt { get; }
    public DateTimeOffset? NextRetryAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public enum RuntimeSchedulerPoisonDisposition
{
    /// <summary>
    /// No retry is held: the policy declined, threw, or asked for a <c>RetryAfter</c> that was not released. The work
    /// item is parked for inspection/intervention.
    /// </summary>
    Poisoned,

    /// <summary>
    /// The retry policy asked to retry and the queue holds the retry. <c>RetryNow</c> re-enqueued the work item.
    /// <c>RetryAfter</c> released it until <c>NextRetryAt</c>, after which the resumption sweep re-drives it; until then
    /// it holds back the execution's later queued work, because the queue serves each execution in order. Any
    /// <c>RetryAfter</c> that was not released is recorded as <see cref="Poisoned"/> instead, for example when the queue
    /// cannot delay it, no resumption sweep is composed, or the queue refuses the release; a policy that throws is
    /// <see cref="Poisoned"/> too. <c>docs/runtime-fault-behavior.md</c> lists every case.
    /// </summary>
    RetryScheduled
}
