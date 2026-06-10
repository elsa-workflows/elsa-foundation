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
        IReadOnlyCollection<RuntimeStateChangeReference> bookmarks,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValues,
        IReadOnlyCollection<RuntimeStateChangeReference> incidents,
        IReadOnlyCollection<RuntimeStateChangeReference> operational)
    {
        ValidateReferences(bookmarks, RuntimeStateCategory.Bookmark, nameof(bookmarks));
        ValidateReferences(incidents, RuntimeStateCategory.Incident, nameof(incidents));
        ValidateReferences(operational, RuntimeStateCategory.Operational, nameof(operational));

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
    public IReadOnlyCollection<RuntimeStateChangeReference> Bookmarks { get; }
    public IReadOnlyCollection<RuntimeStateChange<DurableValueState>> DurableValues { get; }
    public IReadOnlyCollection<RuntimeStateChangeReference> Incidents { get; }
    public IReadOnlyCollection<RuntimeStateChangeReference> Operational { get; }

    private static void ValidateReferences(
        IReadOnlyCollection<RuntimeStateChangeReference> references,
        RuntimeStateCategory expectedCategory,
        string parameterName)
    {
        if (references.Any(reference => reference.Category != expectedCategory))
            throw new ArgumentException($"All state references must use the {expectedCategory} category.", parameterName);
    }
}

public sealed record RuntimeStateChange<TState>(
    string StateId,
    RuntimeStateChangeOperation Operation,
    TState State,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record RuntimeStateChangeReference(
    string StateId,
    RuntimeStateCategory Category,
    RuntimeStateChangeOperation Operation,
    string WorkflowExecutionId,
    string? ActivityExecutionId,
    string? ResumeTargetId,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record RuntimePostCommitIntent(
    string IntentId,
    string WorkflowExecutionId,
    string Kind,
    DateTimeOffset RecordedAt,
    string? ActivityExecutionId,
    string? IdempotencyKey,
    JsonElement? Payload,
    IReadOnlyDictionary<string, string> Metadata);

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
