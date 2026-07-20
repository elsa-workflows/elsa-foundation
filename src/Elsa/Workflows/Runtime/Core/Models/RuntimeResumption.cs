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
        TimeSpan? heartbeatTimeout = null,
        int? maxExecutionsPerSweep = null,
        IReadOnlySet<string>? excludedWorkflowExecutionIds = null)
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

        if (maxExecutionsPerSweep <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExecutionsPerSweep), "Max executions per sweep must be greater than zero when provided.");

        OutboxBatchSize = outboxBatchSize;
        BacklogBatchSize = backlogBatchSize;
        RecoveryScanBatchSize = recoveryScanBatchSize;
        LeaseTimeout = leaseTimeout ?? TimeSpan.FromMinutes(5);
        HeartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromMinutes(5);
        MaxExecutionsPerSweep = maxExecutionsPerSweep;
        ExcludedWorkflowExecutionIds = excludedWorkflowExecutionIds ?? EmptySet;
    }

    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>(StringComparer.Ordinal);

    public int OutboxBatchSize { get; }
    public int BacklogBatchSize { get; }
    public int RecoveryScanBatchSize { get; }
    public TimeSpan LeaseTimeout { get; }
    public TimeSpan HeartbeatTimeout { get; }

    /// <summary>
    /// Hard cap on how many workflow executions a single sweep re-drives, applied after backlog and
    /// recovery-candidate discovery. Bounds the work the pump does per tick so a large backlog cannot
    /// produce an unbounded burst of command dispatches. <c>null</c> means "no additional cap beyond
    /// the discovery batch sizes".
    /// </summary>
    public int? MaxExecutionsPerSweep { get; }

    /// <summary>
    /// Workflow execution IDs the sweep must skip this pass. The resumption pump populates this with
    /// executions in per-execution failure backoff so one poisoned execution cannot occupy a re-drive
    /// slot on every tick and starve healthy executions out of the capped set.
    /// </summary>
    public IReadOnlySet<string> ExcludedWorkflowExecutionIds { get; }
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
        IReadOnlyCollection<RuntimeResumptionDispatch> dispatches,
        int terminalExecutionsPurged = 0,
        int purgedWorkItemCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outboxAttemptedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(outboxDeliveredCount);
        ArgumentOutOfRangeException.ThrowIfNegative(outboxFailedCount);
        ArgumentNullException.ThrowIfNull(dispatches);
        ArgumentOutOfRangeException.ThrowIfNegative(terminalExecutionsPurged);
        ArgumentOutOfRangeException.ThrowIfNegative(purgedWorkItemCount);

        OutboxAttemptedCount = outboxAttemptedCount;
        OutboxDeliveredCount = outboxDeliveredCount;
        OutboxFailedCount = outboxFailedCount;
        Dispatches = dispatches.ToArray();
        TerminalExecutionsPurged = terminalExecutionsPurged;
        PurgedWorkItemCount = purgedWorkItemCount;
    }

    public int OutboxAttemptedCount { get; }
    public int OutboxDeliveredCount { get; }
    public int OutboxFailedCount { get; }

    /// <summary>One entry per workflow execution the sweep tried to re-drive.</summary>
    public IReadOnlyCollection<RuntimeResumptionDispatch> Dispatches { get; }

    /// <summary>
    /// How many discovered executions were already in a terminal status and therefore had their residual scheduler
    /// work purged instead of being re-driven (spec 113). A non-zero value means backlog discovery surfaced
    /// completed executions whose stranded work items would otherwise churn a drain span every sweep.
    /// </summary>
    public int TerminalExecutionsPurged { get; }

    /// <summary>The total number of residual scheduler work items removed across all purged terminal executions.</summary>
    public int PurgedWorkItemCount { get; }

    public bool DidWork => OutboxAttemptedCount > 0 || Dispatches.Count > 0 || TerminalExecutionsPurged > 0;
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
