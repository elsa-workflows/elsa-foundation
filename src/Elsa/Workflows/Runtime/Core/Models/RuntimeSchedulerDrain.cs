namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimeSchedulerDrainRequest
{
    public RuntimeSchedulerDrainRequest(
        string workflowExecutionId,
        int? maxWorkItems = null,
        IServiceProvider? ambientServices = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        if (maxWorkItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxWorkItems), "Scheduler drain maximum work item count must be greater than zero when provided.");

        WorkflowExecutionId = workflowExecutionId;
        MaxWorkItems = maxWorkItems;
        AmbientServices = ambientServices;
    }

    public string WorkflowExecutionId { get; }
    public int? MaxWorkItems { get; }
    public IServiceProvider? AmbientServices { get; }

    public RuntimeSchedulerDrainRequest WithAmbientServices(IServiceProvider? ambientServices) =>
        new(WorkflowExecutionId, MaxWorkItems, ambientServices);
}

public sealed class RuntimeSchedulerDrainResult
{
    public RuntimeSchedulerDrainResult(
        string workflowExecutionId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        IReadOnlyCollection<RuntimeSchedulerWorkItemResult> items,
        IReadOnlyCollection<RuntimePostCommitOutboxProcessResult>? outboxDeliveryResults = null,
        RuntimeSchedulerDrainStopReason? stopReason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(items);

        var itemSnapshot = items.ToArray();
        var outboxDeliverySnapshot = outboxDeliveryResults?.ToArray() ?? [];

        if (itemSnapshot.Any(item => !string.Equals(item.WorkflowExecutionId, workflowExecutionId, StringComparison.Ordinal)))
            throw new ArgumentException("Scheduler drain item results must belong to the drain workflow execution.", nameof(items));

        WorkflowExecutionId = workflowExecutionId;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Items = itemSnapshot;
        OutboxDeliveryResults = outboxDeliverySnapshot;
        StopReason = stopReason ?? InferStopReason(itemSnapshot);
    }

    public string WorkflowExecutionId { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset CompletedAt { get; }
    public int DrainedCount => Items.Count(item => item.Status is RuntimeSchedulerWorkItemResultStatus.Completed or RuntimeSchedulerWorkItemResultStatus.Faulted);
    public bool StoppedOnFault => Items.Any(item => item.Status == RuntimeSchedulerWorkItemResultStatus.Faulted);
    public bool StoppedOnPause => Items.Any(item => item.Status == RuntimeSchedulerWorkItemResultStatus.Paused);
    public IReadOnlyCollection<RuntimeSchedulerWorkItemResult> Items { get; }
    public IReadOnlyCollection<RuntimePostCommitOutboxProcessResult> OutboxDeliveryResults { get; }
    public int OutboxAttemptedCount => OutboxDeliveryResults.Sum(result => result.AttemptedCount);
    public int OutboxDeliveredCount => OutboxDeliveryResults.Sum(result => result.DeliveredCount);
    public int OutboxFailedCount => OutboxDeliveryResults.Sum(result => result.FailedCount);
    public RuntimeSchedulerDrainStopReason StopReason { get; }

    private static RuntimeSchedulerDrainStopReason InferStopReason(IReadOnlyCollection<RuntimeSchedulerWorkItemResult> items)
    {
        if (items.Any(item => item.Status == RuntimeSchedulerWorkItemResultStatus.Faulted))
            return RuntimeSchedulerDrainStopReason.Faulted;

        if (items.Any(item => item.Status == RuntimeSchedulerWorkItemResultStatus.Paused))
            return RuntimeSchedulerDrainStopReason.Paused;

        return RuntimeSchedulerDrainStopReason.Quiesced;
    }
}

public enum RuntimeSchedulerDrainStopReason
{
    Quiesced,
    Paused,
    Faulted,
    OutboxDeliveryFailed,
    CycleCapExhausted
}

public sealed class RuntimeSchedulerWorkItemResult
{
    public RuntimeSchedulerWorkItemResult(
        string workItemId,
        string workflowExecutionId,
        WorkflowExecutionCommandKind commandKind,
        RuntimeSchedulerWorkItemResultStatus status,
        string handlerName,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string? error = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerName);

        if (status is RuntimeSchedulerWorkItemResultStatus.Faulted or RuntimeSchedulerWorkItemResultStatus.Paused)
            ArgumentException.ThrowIfNullOrWhiteSpace(error);

        if (status == RuntimeSchedulerWorkItemResultStatus.Completed && error is not null)
            throw new ArgumentException("Completed scheduler work item results cannot carry an error.", nameof(error));

        WorkItemId = workItemId;
        WorkflowExecutionId = workflowExecutionId;
        CommandKind = commandKind;
        Status = status;
        HandlerName = handlerName;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Error = error;
    }

    public string WorkItemId { get; }
    public string WorkflowExecutionId { get; }
    public WorkflowExecutionCommandKind CommandKind { get; }
    public RuntimeSchedulerWorkItemResultStatus Status { get; }
    public string HandlerName { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset CompletedAt { get; }
    public string? Error { get; }
}

public enum RuntimeSchedulerWorkItemResultStatus
{
    Completed,
    Faulted,
    Paused
}
