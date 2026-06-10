namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Minimal single-writer scheduler continuation state for a workflow execution.
/// </summary>
public sealed record SchedulerState
{
    private IReadOnlyCollection<ScheduledActivityWorkItem> _pendingWork = [];
    private IReadOnlyCollection<SchedulerCompletionWorkItem> _pendingCompletionWork = [];
    private IReadOnlyCollection<SchedulerContinuationWorkItem> _pendingContinuations = [];
    private IReadOnlyCollection<VolatileWaitRegistration> _volatileWaits = [];

    /// <remarks>
    /// `pendingCompletionWork` is appended to preserve the existing positional constructor order.
    /// The completion-propagation contract expects schedulers to drain `PendingCompletionWork` before unrelated scheduled activity work.
    /// Prefer named arguments for new call sites.
    /// </remarks>
    public SchedulerState(
        string workflowExecutionId,
        long version,
        IReadOnlyCollection<ScheduledActivityWorkItem>? pendingWork = null,
        IReadOnlyCollection<SchedulerContinuationWorkItem>? pendingContinuations = null,
        IReadOnlyCollection<VolatileWaitRegistration>? volatileWaits = null,
        IReadOnlyCollection<SchedulerCompletionWorkItem>? pendingCompletionWork = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        WorkflowExecutionId = workflowExecutionId;
        Version = version;
        PendingWork = pendingWork ?? [];
        PendingCompletionWork = pendingCompletionWork ?? [];
        PendingContinuations = pendingContinuations ?? [];
        VolatileWaits = volatileWaits ?? [];
    }

    public string WorkflowExecutionId { get; init; }
    public long Version { get; init; }

    public IReadOnlyCollection<ScheduledActivityWorkItem> PendingWork
    {
        get => _pendingWork;
        init => _pendingWork = Snapshot(value);
    }

    public IReadOnlyCollection<SchedulerCompletionWorkItem> PendingCompletionWork
    {
        get => _pendingCompletionWork;
        init => _pendingCompletionWork = Snapshot(value);
    }

    public IReadOnlyCollection<SchedulerContinuationWorkItem> PendingContinuations
    {
        get => _pendingContinuations;
        init => _pendingContinuations = Snapshot(value);
    }

    public IReadOnlyCollection<VolatileWaitRegistration> VolatileWaits
    {
        get => _volatileWaits;
        init => _volatileWaits = Snapshot(value);
    }

    private static IReadOnlyCollection<T> Snapshot<T>(IReadOnlyCollection<T>? values) => (values ?? []).ToArray();
}

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

public sealed class SchedulerCompletionWorkItem
{
    public SchedulerCompletionWorkItem(
        string workItemId,
        string workflowExecutionId,
        string subjectActivityExecutionId,
        string? completedChildActivityExecutionId,
        string? branchId,
        SchedulerCompletionKind kind,
        long sequence,
        DateTimeOffset enqueuedAt,
        string reason,
        IReadOnlyCollection<string>? outcomeNames = null,
        IReadOnlyCollection<string>? requiredCompletedActivityExecutionIds = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (completedChildActivityExecutionId is not null && string.IsNullOrWhiteSpace(completedChildActivityExecutionId))
            throw new ArgumentException("Completed child activity execution ID cannot be blank when provided.", nameof(completedChildActivityExecutionId));

        if (branchId is not null && string.IsNullOrWhiteSpace(branchId))
            throw new ArgumentException("Branch ID cannot be blank when provided.", nameof(branchId));

        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Completion work sequence cannot be negative.");

        if (kind == SchedulerCompletionKind.ParentCompletionEvaluation && completedChildActivityExecutionId is null)
            throw new ArgumentException("Parent completion evaluation work requires a completed child activity execution ID.", nameof(completedChildActivityExecutionId));

        if (kind != SchedulerCompletionKind.ParentCompletionEvaluation && completedChildActivityExecutionId is not null)
            throw new ArgumentException("Completed child activity execution ID is only valid for parent completion evaluation work.", nameof(completedChildActivityExecutionId));

        if (kind == SchedulerCompletionKind.ParentCompletionEvaluation && completedChildActivityExecutionId == subjectActivityExecutionId)
            throw new ArgumentException("Parent completion evaluation cannot use the same activity execution as parent and completed child.", nameof(completedChildActivityExecutionId));

        var outcomeSnapshot = SnapshotNonBlank(outcomeNames, nameof(outcomeNames), "Outcome name");
        var requiredCompletionSnapshot = SnapshotNonBlank(requiredCompletedActivityExecutionIds, nameof(requiredCompletedActivityExecutionIds), "Required completed activity execution ID");

        if (requiredCompletionSnapshot.Distinct(StringComparer.Ordinal).Count() != requiredCompletionSnapshot.Count)
            throw new ArgumentException("Required completed activity execution IDs cannot contain duplicates.", nameof(requiredCompletedActivityExecutionIds));

        if (kind != SchedulerCompletionKind.ParentCompletionEvaluation && requiredCompletionSnapshot.Count > 0)
            throw new ArgumentException("Required completed activity execution IDs are only valid for parent completion evaluation work.", nameof(requiredCompletedActivityExecutionIds));

        if (kind == SchedulerCompletionKind.ParentCompletionEvaluation
            && requiredCompletionSnapshot.Count > 0
            && !requiredCompletionSnapshot.Contains(completedChildActivityExecutionId!, StringComparer.Ordinal))
            throw new ArgumentException("Required completed activity execution IDs must include the completed child activity execution ID.", nameof(requiredCompletedActivityExecutionIds));

        WorkItemId = workItemId;
        WorkflowExecutionId = workflowExecutionId;
        SubjectActivityExecutionId = subjectActivityExecutionId;
        CompletedChildActivityExecutionId = completedChildActivityExecutionId;
        BranchId = branchId;
        Kind = kind;
        Sequence = sequence;
        EnqueuedAt = enqueuedAt;
        Reason = reason;
        OutcomeNames = outcomeSnapshot;
        RequiredCompletedActivityExecutionIds = requiredCompletionSnapshot;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string WorkItemId { get; }
    public string WorkflowExecutionId { get; }
    public string SubjectActivityExecutionId { get; }
    public string? CompletedChildActivityExecutionId { get; }
    public string? BranchId { get; }
    public SchedulerCompletionKind Kind { get; }
    public long Sequence { get; }
    public DateTimeOffset EnqueuedAt { get; }
    public string Reason { get; }
    public IReadOnlyCollection<string> OutcomeNames { get; }
    public IReadOnlyCollection<string> RequiredCompletedActivityExecutionIds { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static IReadOnlyCollection<string> SnapshotNonBlank(IReadOnlyCollection<string>? values, string parameterName, string label)
    {
        if (values is null)
            return [];

        var snapshot = values.ToArray();
        if (snapshot.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"{label} cannot be blank.", parameterName);

        return snapshot;
    }
}

public sealed class SchedulerContinuationWorkItem
{
    public SchedulerContinuationWorkItem(
        string workItemId,
        string workflowExecutionId,
        string activityExecutionId,
        string? branchId,
        SchedulerContinuationKind kind,
        string? volatileWaitId,
        DateTimeOffset enqueuedAt,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (branchId is not null && string.IsNullOrWhiteSpace(branchId))
            throw new ArgumentException("Branch ID cannot be blank when provided.", nameof(branchId));

        var isVolatileWaitContinuation = RequiresVolatileWaitId(kind);

        if (isVolatileWaitContinuation && string.IsNullOrWhiteSpace(volatileWaitId))
            throw new ArgumentException("Volatile wait continuation work requires a volatile wait ID.", nameof(volatileWaitId));

        if (!isVolatileWaitContinuation && volatileWaitId is not null)
            throw new ArgumentException("Volatile wait ID must not be set for non-volatile-wait continuation kinds.", nameof(volatileWaitId));

        WorkItemId = workItemId;
        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        BranchId = branchId;
        Kind = kind;
        VolatileWaitId = volatileWaitId;
        EnqueuedAt = enqueuedAt;
        Reason = reason;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string WorkItemId { get; }
    public string WorkflowExecutionId { get; }
    public string ActivityExecutionId { get; }
    public string? BranchId { get; }
    public SchedulerContinuationKind Kind { get; }
    public string? VolatileWaitId { get; }
    public DateTimeOffset EnqueuedAt { get; }
    public string Reason { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static bool RequiresVolatileWaitId(SchedulerContinuationKind kind) =>
        kind is SchedulerContinuationKind.VolatileWaitCompleted
            or SchedulerContinuationKind.VolatileWaitCancelled
            or SchedulerContinuationKind.VolatileWaitExpired
            or SchedulerContinuationKind.VolatileWaitFaulted;
}

public sealed class VolatileWaitRegistration
{
    public VolatileWaitRegistration(
        string waitId,
        string workflowExecutionId,
        string activityExecutionId,
        string? branchId,
        DateTimeOffset registeredAt,
        DateTimeOffset? expiresAt,
        string awaitableKind,
        VolatileWaitStatus status,
        VolatileWaitHostShutdownBehavior hostShutdownBehavior,
        VolatileWaitCancellationBehavior cancellationBehavior,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(awaitableKind);

        if (branchId is not null && string.IsNullOrWhiteSpace(branchId))
            throw new ArgumentException("Branch ID cannot be blank when provided.", nameof(branchId));

        if (expiresAt is not null && expiresAt <= registeredAt)
            throw new ArgumentException("Volatile wait expiration must be after registration time.", nameof(expiresAt));

        WaitId = waitId;
        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        BranchId = branchId;
        RegisteredAt = registeredAt;
        ExpiresAt = expiresAt;
        AwaitableKind = awaitableKind;
        Status = status;
        HostShutdownBehavior = hostShutdownBehavior;
        CancellationBehavior = cancellationBehavior;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string WaitId { get; }
    public string WorkflowExecutionId { get; }
    public string ActivityExecutionId { get; }
    public string? BranchId { get; }
    public DateTimeOffset RegisteredAt { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public string AwaitableKind { get; }
    public VolatileWaitStatus Status { get; }
    public RuntimeWaitMode WaitMode => RuntimeWaitMode.VolatileWait;
    public VolatileWaitHostShutdownBehavior HostShutdownBehavior { get; }
    public VolatileWaitCancellationBehavior CancellationBehavior { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class RuntimeVolatileWaitPolicyRequest
{
    public RuntimeVolatileWaitPolicyRequest(
        string workflowExecutionId,
        string activityExecutionId,
        string? branchId,
        string awaitableKind,
        TimeSpan? requestedDuration,
        bool hostSupportsInMemoryContinuation,
        VolatileWaitHostShutdownBehavior requestedHostShutdownBehavior,
        VolatileWaitCancellationBehavior requestedCancellationBehavior,
        VolatileWaitDurableFallbackPolicy durableFallbackPolicy,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(awaitableKind);

        if (branchId is not null && string.IsNullOrWhiteSpace(branchId))
            throw new ArgumentException("Branch ID cannot be blank when provided.", nameof(branchId));

        if (requestedDuration is not null && requestedDuration <= TimeSpan.Zero)
            throw new ArgumentException("Requested volatile wait duration must be positive when provided.", nameof(requestedDuration));

        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        BranchId = branchId;
        AwaitableKind = awaitableKind;
        RequestedDuration = requestedDuration;
        HostSupportsInMemoryContinuation = hostSupportsInMemoryContinuation;
        RequestedHostShutdownBehavior = requestedHostShutdownBehavior;
        RequestedCancellationBehavior = requestedCancellationBehavior;
        DurableFallbackPolicy = durableFallbackPolicy;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string WorkflowExecutionId { get; }
    public string ActivityExecutionId { get; }
    public string? BranchId { get; }
    public string AwaitableKind { get; }
    public TimeSpan? RequestedDuration { get; }
    public bool HostSupportsInMemoryContinuation { get; }
    public VolatileWaitHostShutdownBehavior RequestedHostShutdownBehavior { get; }
    public VolatileWaitCancellationBehavior RequestedCancellationBehavior { get; }
    public VolatileWaitDurableFallbackPolicy DurableFallbackPolicy { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class RuntimeVolatileWaitPolicyDecision
{
    public RuntimeVolatileWaitPolicyDecision(
        bool isAllowed,
        VolatileWaitHostShutdownBehavior hostShutdownBehavior,
        VolatileWaitCancellationBehavior cancellationBehavior,
        VolatileWaitDurableFallbackPolicy durableFallbackPolicy,
        TimeSpan? maximumDuration,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (!isAllowed && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A denied volatile wait decision requires a reason.", nameof(reason));

        if (maximumDuration is not null && maximumDuration <= TimeSpan.Zero)
            throw new ArgumentException("Maximum volatile wait duration must be positive when provided.", nameof(maximumDuration));

        IsAllowed = isAllowed;
        HostShutdownBehavior = hostShutdownBehavior;
        CancellationBehavior = cancellationBehavior;
        DurableFallbackPolicy = durableFallbackPolicy;
        MaximumDuration = maximumDuration;
        Reason = reason;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public bool IsAllowed { get; }
    public VolatileWaitHostShutdownBehavior HostShutdownBehavior { get; }
    public VolatileWaitCancellationBehavior CancellationBehavior { get; }
    public VolatileWaitDurableFallbackPolicy DurableFallbackPolicy { get; }
    public TimeSpan? MaximumDuration { get; }
    public string? Reason { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public enum RuntimeWaitMode
{
    DurableSuspension,
    VolatileWait
}

public enum VolatileWaitStatus
{
    Registered,
    Completed,
    Cancelled,
    Expired,
    Faulted
}

public enum SchedulerContinuationKind
{
    InternalContinuation,
    VolatileWaitCompleted,
    VolatileWaitCancelled,
    VolatileWaitExpired,
    VolatileWaitFaulted
}

public enum SchedulerCompletionKind
{
    ActivityCompleted,
    ParentCompletionEvaluation,
    ContinuationScheduling
}

public enum VolatileWaitHostShutdownBehavior
{
    CancelWait,
    FaultWorkflow,
    DrainInFlight,
    RequireDurableFallback
}

public enum VolatileWaitCancellationBehavior
{
    CancelWait,
    FaultWorkflow,
    CompleteWithCancellationResult
}

public enum VolatileWaitDurableFallbackPolicy
{
    NotAllowed,
    AllowedWhenBookmarkDeclared,
    Required
}
