namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Bounds for one resumption sweep pass. Defaults are conservative; hosts tune them through the
/// resumption feature's settings.
/// </summary>
public sealed class RuntimeResumptionSweepRequest
{
    public RuntimeResumptionSweepRequest(
        int outboxBatchSize = 100,
        int backlogBatchSize = 100,
        int recoveryScanBatchSize = 100,
        TimeSpan? leaseTimeout = null,
        TimeSpan? heartbeatTimeout = null)
    {
        if (outboxBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(outboxBatchSize), "Outbox batch size must be greater than zero.");

        if (backlogBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(backlogBatchSize), "Backlog batch size must be greater than zero.");

        if (recoveryScanBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(recoveryScanBatchSize), "Recovery scan batch size must be greater than zero.");

        if (leaseTimeout is { } lease && lease <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseTimeout), "Lease timeout must be greater than zero.");

        if (heartbeatTimeout is { } heartbeat && heartbeat <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(heartbeatTimeout), "Heartbeat timeout must be greater than zero.");

        OutboxBatchSize = outboxBatchSize;
        BacklogBatchSize = backlogBatchSize;
        RecoveryScanBatchSize = recoveryScanBatchSize;
        LeaseTimeout = leaseTimeout ?? TimeSpan.FromMinutes(5);
        HeartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromMinutes(5);
    }

    public int OutboxBatchSize { get; }
    public int BacklogBatchSize { get; }
    public int RecoveryScanBatchSize { get; }
    public TimeSpan LeaseTimeout { get; }
    public TimeSpan HeartbeatTimeout { get; }
}

/// <summary>
/// Outcome of one resumption sweep pass.
/// </summary>
public sealed class RuntimeResumptionSweepResult
{
    public RuntimeResumptionSweepResult(
        int outboxAttemptedCount,
        int outboxDeliveredCount,
        int outboxFailedCount,
        IReadOnlyCollection<RuntimeResumptionDispatch> dispatches)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outboxAttemptedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(outboxDeliveredCount);
        ArgumentOutOfRangeException.ThrowIfNegative(outboxFailedCount);
        ArgumentNullException.ThrowIfNull(dispatches);

        OutboxAttemptedCount = outboxAttemptedCount;
        OutboxDeliveredCount = outboxDeliveredCount;
        OutboxFailedCount = outboxFailedCount;
        Dispatches = dispatches.ToArray();
    }

    public int OutboxAttemptedCount { get; }
    public int OutboxDeliveredCount { get; }
    public int OutboxFailedCount { get; }

    /// <summary>One entry per workflow execution the sweep tried to re-drive.</summary>
    public IReadOnlyCollection<RuntimeResumptionDispatch> Dispatches { get; }

    public bool DidWork => OutboxAttemptedCount > 0 || Dispatches.Count > 0;
}

/// <summary>
/// Result of re-driving one workflow execution during a sweep.
/// </summary>
public sealed record RuntimeResumptionDispatch(
    string WorkflowExecutionId,
    RuntimeResumptionDispatchOutcome Outcome,
    string? EnvelopeId,
    string? Failure);

public enum RuntimeResumptionDispatchOutcome
{
    Accepted,
    Duplicate,
    Rejected,
    Deferred,
    Faulted
}
