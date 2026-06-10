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
        string IntentId,
        string WorkflowExecutionId,
        string Kind,
        DateTimeOffset RecordedAt,
        string? ActivityExecutionId,
        string? IdempotencyKey,
        JsonElement? Payload,
        IReadOnlyDictionary<string, string>? Metadata = null,
        string? DependsOnWaitRegistrationId = null,
        RuntimeWaitDependentIntentFailurePolicy? WaitFailurePolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(IntentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Kind);

        if (WaitFailurePolicy is not null && DependsOnWaitRegistrationId is null)
            throw new ArgumentException("A wait failure policy requires a wait registration dependency.", nameof(WaitFailurePolicy));

        if (DependsOnWaitRegistrationId is not null && WaitFailurePolicy is null)
            throw new ArgumentException("A wait-dependent post-commit intent requires a wait failure policy.", nameof(WaitFailurePolicy));

        this.IntentId = IntentId;
        this.WorkflowExecutionId = WorkflowExecutionId;
        this.Kind = Kind;
        this.RecordedAt = RecordedAt;
        this.ActivityExecutionId = ActivityExecutionId;
        this.IdempotencyKey = IdempotencyKey;
        this.Payload = Payload?.Clone();
        this.Metadata = RuntimeModelMetadata.Snapshot(Metadata);
        this.DependsOnWaitRegistrationId = DependsOnWaitRegistrationId;
        this.WaitFailurePolicy = WaitFailurePolicy;
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
    public bool IsWaitDependent => DependsOnWaitRegistrationId is not null;
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
