using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>
    /// Optional single-writer fence that a durable store must validate in the same atomic decision as the checkpoint
    /// state, post-commit outbox, and idempotency marker. It is intentionally excluded from replay identity: an
    /// equivalent already-committed request remains a replay after ownership changes.
    /// </summary>
    public RuntimeExecutionFence? ExpectedFence { get; init; }
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
        IReadOnlyCollection<RuntimeStateChange<ExecutionLivenessState>> operational,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>>? activityExecutionInspections = null,
        IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>>? postCommitOutbox = null,
        IReadOnlyCollection<ActivityScopeCleanupRequest>? activityScopeCleanups = null)
        : this(
            workflowExecution,
            scheduler,
            activityExecutions,
            bookmarks,
            durableValues,
            incidents,
            operational,
            null,
            activityExecutionInspections,
            postCommitOutbox,
            activityScopeCleanups)
    {
    }

    public RuntimeCheckpointStateChangeSet(
        RuntimeStateChange<WorkflowExecutionState>? workflowExecution,
        RuntimeStateChange<SchedulerState>? scheduler,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> activityExecutions,
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>> bookmarks,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValues,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> incidents,
        IReadOnlyCollection<RuntimeStateChange<ExecutionLivenessState>> operational,
        IReadOnlyCollection<RuntimeStateChange<WorkflowDispatchRecord>>? workflowDispatches,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>>? activityExecutionInspections = null,
        IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>>? postCommitOutbox = null,
        IReadOnlyCollection<ActivityScopeCleanupRequest>? activityScopeCleanups = null)
        : this(
            workflowExecution,
            scheduler,
            activityExecutions,
            bookmarks,
            durableValues,
            incidents,
            operational,
            workflowDispatches,
            activityExecutionInspections,
            postCommitOutbox,
            activityScopeCleanups,
            null,
            null)
    {
    }

    [JsonConstructor]
    public RuntimeCheckpointStateChangeSet(
        RuntimeStateChange<WorkflowExecutionState>? workflowExecution,
        RuntimeStateChange<SchedulerState>? scheduler,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> activityExecutions,
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>> bookmarks,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValues,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> incidents,
        IReadOnlyCollection<RuntimeStateChange<ExecutionLivenessState>> operational,
        IReadOnlyCollection<RuntimeStateChange<WorkflowDispatchRecord>>? workflowDispatches,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>>? activityExecutionInspections,
        IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>>? postCommitOutbox,
        IReadOnlyCollection<ActivityScopeCleanupRequest>? activityScopeCleanups,
        IReadOnlyCollection<WorkflowDispatchCancellationRequest>? workflowDispatchCancellations,
        IReadOnlyCollection<ConsumedSchedulerWorkItem>? consumedSchedulerWorkItems = null,
        WorkflowAlterationJobTerminalChange? alterationJobTerminalChange = null)
    {
        activityExecutionInspections ??= [];
        postCommitOutbox ??= [];
        workflowDispatches ??= [];
        activityScopeCleanups ??= [];
        workflowDispatchCancellations ??= [];
        consumedSchedulerWorkItems ??= [];
        ValidateStateIdMatches(activityExecutions, state => state.Execution.ActivityExecutionId, "Activity execution state change StateId must match ActivityExecutionState.Execution.ActivityExecutionId.", nameof(activityExecutions));
        ValidateStateIdMatches(bookmarks, state => state.BookmarkId, "Bookmark state change StateId must match BookmarkState.BookmarkId.", nameof(bookmarks));
        ValidateStateIdMatches(activityExecutionInspections, state => state.ActivityExecutionId, "Activity execution inspection state change StateId must match ActivityExecutionInspectionProjection.ActivityExecutionId.", nameof(activityExecutionInspections));
        ValidateStateIdMatches(durableValues, state => state.DurableValueId, "Durable value state change StateId must match DurableValueState.DurableValueId.", nameof(durableValues));
        ValidateStateIdMatches(incidents, state => state.IncidentId, "Incident state change StateId must match IncidentState.IncidentId.", nameof(incidents));
        ValidateStateIdMatches(operational, state => state.OperationalStateId, "Operational state change StateId must match ExecutionLivenessState.OperationalStateId.", nameof(operational));
        ValidateStateIdMatches(postCommitOutbox, state => state.OutboxItemId, "Post-commit outbox state change StateId must match RuntimePostCommitOutboxItem.OutboxItemId.", nameof(postCommitOutbox));
        ValidateStateIdMatches(workflowDispatches, state => state.DispatchId, "Workflow dispatch state change StateId must match WorkflowDispatchRecord.DispatchId.", nameof(workflowDispatches));

        WorkflowExecution = workflowExecution;
        Scheduler = scheduler;
        ActivityExecutions = activityExecutions;
        ActivityExecutionInspections = activityExecutionInspections;
        Bookmarks = bookmarks;
        DurableValues = durableValues;
        Incidents = incidents;
        Operational = operational;
        PostCommitOutbox = postCommitOutbox;
        ActivityScopeCleanups = activityScopeCleanups;
        WorkflowDispatches = workflowDispatches;
        WorkflowDispatchCancellations = NormalizeCancellations(workflowDispatchCancellations);
        ConsumedSchedulerWorkItems = NormalizeConsumedSchedulerWorkItems(consumedSchedulerWorkItems);
        AlterationJobTerminalChange = NormalizeAlterationJobTerminalChange(alterationJobTerminalChange);
    }

    public RuntimeStateChange<WorkflowExecutionState>? WorkflowExecution { get; }
    public RuntimeStateChange<SchedulerState>? Scheduler { get; }
    public IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> ActivityExecutions { get; }
    public IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> ActivityExecutionInspections { get; }
    public IReadOnlyCollection<RuntimeStateChange<BookmarkState>> Bookmarks { get; }
    public IReadOnlyCollection<RuntimeStateChange<DurableValueState>> DurableValues { get; }
    public IReadOnlyCollection<RuntimeStateChange<IncidentState>> Incidents { get; }
    public IReadOnlyCollection<RuntimeStateChange<ExecutionLivenessState>> Operational { get; }
    public IReadOnlyCollection<ActivityScopeCleanupRequest> ActivityScopeCleanups { get; }

    /// <summary>Workflow-dispatch lifecycle records applied atomically with their checkpoint and outbox intent.</summary>
    public IReadOnlyCollection<RuntimeStateChange<WorkflowDispatchRecord>> WorkflowDispatches { get; }

    /// <summary>Query-independent parent-cancellation requests resolved by the provider at commit time.</summary>
    public IReadOnlyCollection<WorkflowDispatchCancellationRequest> WorkflowDispatchCancellations { get; }

    /// <summary>
    /// Claimed scheduler work items to delete inside this checkpoint's atomic unit-of-work (WU-1 / spec 105). Folding the
    /// claimed item's fence-checked delete here replaces the drainer's separate post-dispatch acknowledgement, so a
    /// single-commit drain step performs one durable transaction instead of two. Deletion is owner+token fence-checked so
    /// a stale claimant's commit fails claim-lost rather than deleting successor-owned work.
    /// </summary>
    public IReadOnlyCollection<ConsumedSchedulerWorkItem> ConsumedSchedulerWorkItems { get; }

    /// <summary>Terminal alteration-job evidence committed in the same atomic unit as the workflow mutation.</summary>
    public WorkflowAlterationJobTerminalChange? AlterationJobTerminalChange { get; }

    /// <summary>
    /// Pending post-commit outbox items, applied atomically with the rest of the change set. Built by the
    /// checkpoint committer from <see cref="RuntimeCheckpointCommit.PostCommitIntents"/>; folding them into the
    /// applied set means every persistence provider durably records them through the same path it uses for all
    /// other state — a provider cannot persist the checkpoint while silently dropping its continuation work.
    /// </summary>
    public IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> PostCommitOutbox { get; }

    /// <summary>Returns a copy of this change set with the supplied post-commit outbox items.</summary>
    public RuntimeCheckpointStateChangeSet WithPostCommitOutbox(
        IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> postCommitOutbox) =>
        new(
            WorkflowExecution,
            Scheduler,
            ActivityExecutions,
            Bookmarks,
            DurableValues,
            Incidents,
            Operational,
            WorkflowDispatches,
            ActivityExecutionInspections,
            postCommitOutbox,
            ActivityScopeCleanups,
            WorkflowDispatchCancellations,
            ConsumedSchedulerWorkItems,
            AlterationJobTerminalChange);

    /// <summary>Returns a copy with the supplied workflow-dispatch lifecycle changes.</summary>
    public RuntimeCheckpointStateChangeSet WithWorkflowDispatches(
        IReadOnlyCollection<RuntimeStateChange<WorkflowDispatchRecord>> workflowDispatches) =>
        new(
            WorkflowExecution,
            Scheduler,
            ActivityExecutions,
            Bookmarks,
            DurableValues,
            Incidents,
            Operational,
            workflowDispatches,
            ActivityExecutionInspections,
            PostCommitOutbox,
            ActivityScopeCleanups,
            WorkflowDispatchCancellations,
            ConsumedSchedulerWorkItems,
            AlterationJobTerminalChange);

    /// <summary>Returns a copy with the supplied provider-resolved dispatch cancellation requests.</summary>
    public RuntimeCheckpointStateChangeSet WithWorkflowDispatchCancellations(
        IReadOnlyCollection<WorkflowDispatchCancellationRequest> workflowDispatchCancellations) =>
        new(
            WorkflowExecution,
            Scheduler,
            ActivityExecutions,
            Bookmarks,
            DurableValues,
            Incidents,
            Operational,
            WorkflowDispatches,
            ActivityExecutionInspections,
            PostCommitOutbox,
            ActivityScopeCleanups,
            workflowDispatchCancellations,
            ConsumedSchedulerWorkItems,
            AlterationJobTerminalChange);

    /// <summary>Returns a copy with the supplied claimed scheduler work items folded in for atomic consumption.</summary>
    public RuntimeCheckpointStateChangeSet WithConsumedSchedulerWorkItems(
        IReadOnlyCollection<ConsumedSchedulerWorkItem> consumedSchedulerWorkItems) =>
        new(
            WorkflowExecution,
            Scheduler,
            ActivityExecutions,
            Bookmarks,
            DurableValues,
            Incidents,
            Operational,
            WorkflowDispatches,
            ActivityExecutionInspections,
            PostCommitOutbox,
            ActivityScopeCleanups,
            WorkflowDispatchCancellations,
            consumedSchedulerWorkItems,
            AlterationJobTerminalChange);

    /// <summary>Returns a copy with checkpoint-integrated terminal alteration evidence.</summary>
    public RuntimeCheckpointStateChangeSet WithAlterationJobTerminalChange(
        WorkflowAlterationJobTerminalChange? alterationJobTerminalChange) =>
        new(
            WorkflowExecution,
            Scheduler,
            ActivityExecutions,
            Bookmarks,
            DurableValues,
            Incidents,
            Operational,
            WorkflowDispatches,
            ActivityExecutionInspections,
            PostCommitOutbox,
            ActivityScopeCleanups,
            WorkflowDispatchCancellations,
            ConsumedSchedulerWorkItems,
            alterationJobTerminalChange);

    private static WorkflowAlterationJobTerminalChange? NormalizeAlterationJobTerminalChange(WorkflowAlterationJobTerminalChange? change) =>
        change is null
            ? null
            : new WorkflowAlterationJobTerminalChange(
                change.JobId,
                change.ClaimToken,
                change.Status,
                change.Outcomes.OrderBy(outcome => outcome.Ordinal).ToArray(),
                change.CheckpointCommitId,
                change.CompletedAt,
                change.SafeFailure);

    private static IReadOnlyCollection<ConsumedSchedulerWorkItem> NormalizeConsumedSchedulerWorkItems(
        IReadOnlyCollection<ConsumedSchedulerWorkItem> items)
    {
        var normalized = new Dictionary<string, ConsumedSchedulerWorkItem>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.WorkflowExecutionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.WorkItemId);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.ClaimOwnerId);
            if (normalized.TryGetValue(item.WorkItemId, out var existing) && existing != item)
            {
                throw new ArgumentException(
                    $"Consumed scheduler work item '{item.WorkItemId}' occurs more than once with conflicting fence state.",
                    nameof(items));
            }
            normalized[item.WorkItemId] = item;
        }
        return normalized.Values.OrderBy(item => item.WorkItemId, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyCollection<WorkflowDispatchCancellationRequest> NormalizeCancellations(
        IReadOnlyCollection<WorkflowDispatchCancellationRequest> requests)
    {
        var normalized = new Dictionary<string, WorkflowDispatchCancellationRequest>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (normalized.TryGetValue(request.DispatchId, out var existing) &&
                !WorkflowDispatchCancellationRequest.Equivalent(existing, request))
            {
                throw new ArgumentException(
                    $"Workflow dispatch cancellation request '{request.DispatchId}' occurs more than once with conflicting state.",
                    nameof(requests));
            }
            normalized[request.DispatchId] = request;
        }
        return normalized.Values.OrderBy(request => request.DispatchId, StringComparer.Ordinal).ToArray();
    }

    private static void ValidateStateIdMatches<TState>(
        IReadOnlyCollection<RuntimeStateChange<TState>> changes,
        Func<TState, string> stateIdSelector,
        string message,
        string parameterName)
    {
        if (changes.Any(change => change.StateId != stateIdSelector(change.State)))
            throw new ArgumentException(message, parameterName);
    }
}

public sealed record RuntimeStateChange<TState>(
    string StateId,
    RuntimeStateChangeOperation Operation,
    TState State,
    IReadOnlyDictionary<string, string> Metadata);

public sealed class RuntimePostCommitIntent
{
    public const int MaximumKindLength = 230;

    [JsonConstructor]
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
        RuntimeWaitDependentIntentFailurePolicy? waitFailurePolicy = null,
        RuntimeSchedulerWorkItem? materializedSchedulerWorkItem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ValidateKind(kind, nameof(kind));

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
        MaterializedSchedulerWorkItem = materializedSchedulerWorkItem;
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

    /// <summary>
    /// In-process-only conduit (WU-3, spec 109): the already-materialized <see cref="RuntimeSchedulerWorkItem"/> this
    /// <c>EnqueueSchedulerWork</c> intent's <see cref="Payload"/> was serialized from. Set by
    /// <c>SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent</c> so the checkpoint committer can hand it to the
    /// live drain's in-process-hop carrier without re-parsing. <b>Never serialized</b> (<see cref="JsonIgnoreAttribute"/>):
    /// the durable <see cref="Payload"/> is the sole authoritative form, so any store that persists this intent strips
    /// the conduit and the delivery path deserializes as usual. Null for non-continuation intents and after any
    /// durable round-trip.
    /// </summary>
    [JsonIgnore]
    public RuntimeSchedulerWorkItem? MaterializedSchedulerWorkItem { get; }

    internal static void ValidateKind(string kind, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind, parameterName);
        if (kind.Length > MaximumKindLength)
        {
            throw new ArgumentException(
                $"A runtime post-commit intent kind must be at most {MaximumKindLength} UTF-16 code units.",
                parameterName);
        }
    }
}

public enum RuntimeWaitDependentIntentFailurePolicy
{
    FaultWorkflow,
    CompleteWithFailureResult,
    CancelWait,
    KeepWaitingManualIntervention,
    Compensate
}

public enum RuntimeStateCategory
{
    WorkflowExecution,
    Scheduler,
    ActivityExecution,
    ActivityExecutionInspection,
    Bookmark,
    DurableValue,
    Incident,
    Operational,
    WorkflowDispatch
}

public enum RuntimeStateChangeOperation
{
    Upsert,
    Delete,
    Append
}
