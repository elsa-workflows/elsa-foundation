using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Atomic provider-facing checkpoint envelope for runtime continuation state changes.
/// </summary>
public sealed record RuntimeCheckpointCommit(
    string CommitId,
    RuntimeCheckpoint Checkpoint,
    RuntimeCheckpointStateChangeSet StateChanges,
    IReadOnlyList<RuntimePostCommitIntent> PostCommitIntents,
    IReadOnlyDictionary<string, string> Metadata)
{
    public string WorkflowExecutionId => Checkpoint.WorkflowExecutionId;
}

public sealed class RuntimeCheckpointStateChangeSet
{
    public RuntimeCheckpointStateChangeSet(
        RuntimeStateChange<WorkflowExecutionState>? workflowExecution,
        RuntimeStateChange<SchedulerState>? scheduler,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> activityExecutions,
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>> bookmarks,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValues,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> incidents,
        IReadOnlyCollection<RuntimeStateChange<OperationalState>> operational)
    {
        ValidateBookmarks(bookmarks, nameof(bookmarks));
        ValidateIncidents(incidents, nameof(incidents));
        ValidateOperational(operational, nameof(operational));

        WorkflowExecution = workflowExecution;
        Scheduler = scheduler;
        ActivityExecutions = activityExecutions;
        Bookmarks = bookmarks;
        DurableValues = durableValues;
        Incidents = incidents;
        Operational = operational;
    }

    public RuntimeStateChange<WorkflowExecutionState>? WorkflowExecution { get; }
    public RuntimeStateChange<SchedulerState>? Scheduler { get; }
    public IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> ActivityExecutions { get; }
    public IReadOnlyCollection<RuntimeStateChange<BookmarkState>> Bookmarks { get; }
    public IReadOnlyCollection<RuntimeStateChange<DurableValueState>> DurableValues { get; }
    public IReadOnlyCollection<RuntimeStateChange<IncidentState>> Incidents { get; }
    public IReadOnlyCollection<RuntimeStateChange<OperationalState>> Operational { get; }

    private static void ValidateBookmarks(
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>> bookmarks,
        string parameterName)
    {
        if (bookmarks.Any(change => change.StateId != change.State.BookmarkId))
            throw new ArgumentException("Bookmark state change StateId must match BookmarkState.BookmarkId.", parameterName);
    }

    private static void ValidateIncidents(
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> incidents,
        string parameterName)
    {
        if (incidents.Any(change => change.StateId != change.State.IncidentId))
            throw new ArgumentException("Incident state change StateId must match IncidentState.IncidentId.", parameterName);
    }

    private static void ValidateOperational(
        IReadOnlyCollection<RuntimeStateChange<OperationalState>> operational,
        string parameterName)
    {
        if (operational.Any(change => change.StateId != change.State.OperationalStateId))
            throw new ArgumentException("Operational state change StateId must match OperationalState.OperationalStateId.", parameterName);
    }
}

public sealed record RuntimeStateChange<TState>(
    string StateId,
    RuntimeStateChangeOperation Operation,
    TState State,
    IReadOnlyDictionary<string, string> Metadata);

public sealed class RuntimePostCommitIntent
{
    public RuntimePostCommitIntent(
        string intentId,
        string workflowExecutionId,
        string kind,
        DateTimeOffset recordedAt,
        string? activityExecutionId,
        string? idempotencyKey,
        JsonElement? payload,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? dependsOnWaitRegistrationId = null,
        RuntimeWaitDependentIntentFailurePolicy? waitFailurePolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        if (dependsOnWaitRegistrationId is not null && string.IsNullOrWhiteSpace(dependsOnWaitRegistrationId))
            throw new ArgumentException("A wait registration dependency cannot be blank.", nameof(dependsOnWaitRegistrationId));

        if (waitFailurePolicy is not null && dependsOnWaitRegistrationId is null)
            throw new ArgumentException("A wait failure policy requires a wait registration dependency.", nameof(dependsOnWaitRegistrationId));

        if (dependsOnWaitRegistrationId is not null && waitFailurePolicy is null)
            throw new ArgumentException("A wait-dependent post-commit intent requires a wait failure policy.", nameof(waitFailurePolicy));

        IntentId = intentId;
        WorkflowExecutionId = workflowExecutionId;
        Kind = kind;
        RecordedAt = recordedAt;
        ActivityExecutionId = activityExecutionId;
        IdempotencyKey = idempotencyKey;
        Payload = payload?.Clone();
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        DependsOnWaitRegistrationId = dependsOnWaitRegistrationId;
        WaitFailurePolicy = waitFailurePolicy;
    }

    public string IntentId { get; }
    public string WorkflowExecutionId { get; }
    public string Kind { get; }
    public DateTimeOffset RecordedAt { get; }
    public string? ActivityExecutionId { get; }
    public string? IdempotencyKey { get; }
    public JsonElement? Payload { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public string? DependsOnWaitRegistrationId { get; }
    public RuntimeWaitDependentIntentFailurePolicy? WaitFailurePolicy { get; }
    public bool IsWaitDependent => !string.IsNullOrWhiteSpace(DependsOnWaitRegistrationId);
}

public enum RuntimeWaitDependentIntentFailurePolicy
{
    FaultWorkflow,
    CompleteWithFailureResult,
    CancelWait,
    KeepWaitingManualIntervention
}

public enum RuntimeStateCategory
{
    WorkflowExecution,
    Scheduler,
    ActivityExecution,
    Bookmark,
    DurableValue,
    Incident,
    Operational
}

public enum RuntimeStateChangeOperation
{
    Upsert,
    Delete,
    Append
}
