using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Constants;

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

    /// <summary>Optional model decision written with the fence, consumed input and outbox in one transaction.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeModelStateChange? ModelState { get; init; }

    /// <summary>The exact model decision authorizing initial native worker admission, checked in the same transaction.</summary>
    /// <remarks>A refreshed concurrency condition is excluded from logical replay identity; the native state retains its immutable model-work origin.</remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeModelExecutionState? ExpectedModelExecution { get; init; }

    /// <summary>The observed native parent wait that must remain live when its model child is first admitted.</summary>
    /// <remarks>A concurrency condition, not source authority. Admission preserves the parent bookmark and its generation.</remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StimulusResumeRecipient? ExpectedModelParentWait { get; init; }

    /// <summary>Only a first model decision may depend on another execution's exact native wait.</summary>
    public void EnsureModelParentAdmissionShape()
    {
        if (ExpectedModelParentWait is not { } expected) return;
        if (ModelState is not { ExpectedRevision: null, State.Lifecycle: RuntimeModelLifecycle.Running } model ||
            ExpectedFence is null || ExpectedModelExecution is not null || ExpectedBookmarkResume is not null ||
            expected.WorkflowExecutionId == WorkflowExecutionId || expected.RegistrationCapture is not { } capture ||
            capture.Scope.PersistenceScope != model.State.Scope || capture.Scope.TenantId != model.State.TenantId ||
            StateChanges.WorkflowExecution is not null || StateChanges.Bookmarks.Count != 0 || StateChanges.ActivityExecutions.Count != 0)
            throw new InvalidOperationException("Model child admission requires its initial decision and captured live native parent wait.");
    }

    /// <summary>Only an initial native worker checkpoint may depend on another execution's live model decision.</summary>
    public void EnsureModelAdmissionShape()
    {
        if (ExpectedModelExecution is not { } expected) return;
        if (ModelState is not null || ExpectedBookmarkResume is not null || ExpectedFence is null ||
            Checkpoint.Name != RuntimeCheckpointNames.WorkflowStarted ||
            expected.ExecutionId == WorkflowExecutionId || expected.Lifecycle != RuntimeModelLifecycle.Running ||
            StateChanges.WorkflowExecution is not { Operation: RuntimeStateChangeOperation.Upsert,
                State.Status: WorkflowExecutionStatus.Running } native ||
            native.State.StartedAt is not { } startedAt || startedAt > Checkpoint.OccurredAt || native.State.CreatedAt != startedAt ||
            native.State.ParentWorkflowExecutionId is not null ||
            native.State.TenantId != expected.TenantId || native.State.Partition?.Value != expected.Scope.Value ||
            native.State.Authority is not { } authority ||
            !WorkflowExecutionAuthoritySnapshot.Matches(expected.Authority, authority.SystemIdentity,
                authority.RootInitiator, authority.Metadata))
            throw new InvalidOperationException("Native worker admission requires its matching initial state, authority and exact live model decision.");
        if (native.State.ModelWorkOrigin is { } origin && (!origin.MatchesModel(expected) ||
            origin.WorkerExecutionId != WorkflowExecutionId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(origin.WorkerExecutable, native.State.PinnedExecutable)))
            throw new InvalidOperationException("The native worker's model invocation differs from its admission condition.");
    }

    /// <summary>Reject a model decision that could alias native state or escape its execution fence.</summary>
    public void EnsureModelStateShape()
    {
        if (ModelState is not { } model) return;
        var retainsState = model.ExpectedRevision == model.State.Revision;
        if (model.State.ExecutionId != WorkflowExecutionId || ExpectedFence is null ||
            (!retainsState && model.State.UpdatedAt != Checkpoint.OccurredAt) ||
            model.State.UpdatedAt > Checkpoint.OccurredAt || model.Input.AcceptedAt != Checkpoint.OccurredAt ||
            ExpectedBookmarkResume is not null || StateChanges.WorkflowExecution is not null ||
            StateChanges.ActivityExecutions.Count != 0 || StateChanges.ActivityExecutionInspections.Count != 0 ||
            StateChanges.Bookmarks.Count != 0 || StateChanges.DurableValues.Count != 0 ||
            StateChanges.Incidents.Count != 0 || StateChanges.ActivityScopeCleanups.Count != 0 ||
            StateChanges.AlterationJobTerminalChange is not null || !StateChanges.BackgroundActivityWork.IsEmpty)
            throw new InvalidOperationException("A model decision requires its exact execution, timestamp and fence, without native execution state.");
    }

    /// <summary>Optional exact physical wait condition, atomically checked with initial trigger consumption.</summary>
    /// <remarks>This is a write condition, not source authority. Legacy providers refuse it. Null retains the old wire/fingerprint.</remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StimulusResumeRecipient? ExpectedBookmarkResume { get; init; }


    /// <summary>Validate that the exact wait condition belongs to this initial trigger-claim checkpoint.</summary>
    public void EnsureExactBookmarkResumeShape()
    {
        if (ExpectedBookmarkResume is not { } expected) return;
        var activity = StateChanges.ActivityExecutions.SingleOrDefault(change => change.StateId == expected.ActivityExecutionId);
        var bookmark = StateChanges.Bookmarks.SingleOrDefault(change => change.StateId == expected.BookmarkId);
        if (expected.RegistrationCapture is null || expected.WorkflowExecutionId != WorkflowExecutionId ||
            bookmark is not { Operation: RuntimeStateChangeOperation.Delete } || !expected.Matches(bookmark.State) ||
            activity is not { Operation: RuntimeStateChangeOperation.Upsert, State.Status: ActivityExecutionStatus.Running } ||
            activity.State.Execution.WorkflowExecutionId != WorkflowExecutionId ||
            activity.State.Execution.ActivityExecutionId != expected.ActivityExecutionId || activity.State.Execution.ExecutableNodeId != expected.ExecutableNodeId ||
            !activity.State.Metadata.TryGetValue(Constants.RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var claim) ||
            activity.State.Attempts?.Any(attempt => attempt.AttemptId == claim && attempt.InvocationId == expected.ActivityExecutionId &&
                attempt.Reason == ActivityAttemptReason.Resume && attempt.EndedAt is null) != true)
            throw new InvalidOperationException("Exact bookmark resume requires its captured wait deletion and matching initial activity-attempt claim.");
    }

    /// <summary>
    /// Optional single-writer fence that a durable store must validate in the same atomic decision as the checkpoint
    /// state, post-commit outbox, and idempotency marker. It is intentionally excluded from replay identity: an
    /// equivalent already-committed request remains a replay after ownership changes.
    /// </summary>
    public RuntimeExecutionFence? ExpectedFence { get; init; }

    /// <summary>
    /// Keeps the scheduler work item that caused this checkpoint available until its handler finishes. This is used by
    /// activity and bookmark trigger-claim boundaries: claiming an activity attempt is not the same as accepting its decision.
    /// </summary>
    public bool RetainSchedulerWork { get; init; }
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
        WorkflowAlterationJobTerminalChange? alterationJobTerminalChange = null,
        BackgroundActivityWorkCheckpointChanges? backgroundActivityWork = null)
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
        ValidateStateIdMatches(operational, state => state.ExecutionLivenessStateId, "Execution liveness state change StateId must match ExecutionLivenessState.ExecutionLivenessStateId.", nameof(operational));
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
        BackgroundActivityWork = backgroundActivityWork ?? BackgroundActivityWorkCheckpointChanges.Empty;
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
    /// Claimed scheduler work items to delete inside this checkpoint's atomic unit-of-work. Folding the
    /// claimed item's fence-checked delete here replaces the drainer's separate post-dispatch acknowledgement, so a
    /// single-commit drain step performs one durable transaction instead of two. Deletion is owner+token fence-checked so
    /// a stale claimant's commit fails claim-lost rather than deleting successor-owned work.
    /// </summary>
    public IReadOnlyCollection<ConsumedSchedulerWorkItem> ConsumedSchedulerWorkItems { get; }

    /// <summary>Terminal alteration-job evidence committed in the same atomic unit as the workflow mutation.</summary>
    public WorkflowAlterationJobTerminalChange? AlterationJobTerminalChange { get; }

    /// <summary>Admission, acceptance, and cancellation are atomic participants of this checkpoint.</summary>
    public BackgroundActivityWorkCheckpointChanges BackgroundActivityWork { get; }

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
            AlterationJobTerminalChange,
            BackgroundActivityWork);

    /// <summary>Returns a copy with the supplied activity projections, preserving every other atomic participant.</summary>
    public RuntimeCheckpointStateChangeSet WithActivityExecutions(
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> activityExecutions) =>
        new(
            WorkflowExecution,
            Scheduler,
            activityExecutions,
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
            AlterationJobTerminalChange,
            BackgroundActivityWork);

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
            AlterationJobTerminalChange,
            BackgroundActivityWork);

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
            AlterationJobTerminalChange,
            BackgroundActivityWork);

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
            AlterationJobTerminalChange,
            BackgroundActivityWork);

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
            alterationJobTerminalChange,
            BackgroundActivityWork);

    /// <summary>Returns a copy with background-work admission, acceptance, and cancellation participants.</summary>
    public RuntimeCheckpointStateChangeSet WithBackgroundActivityWork(
        BackgroundActivityWorkCheckpointChanges backgroundActivityWork) =>
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
            AlterationJobTerminalChange,
            backgroundActivityWork ?? throw new ArgumentNullException(nameof(backgroundActivityWork)));

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
        RuntimeSchedulerWorkItem? materializedSchedulerWorkItem = null,
        DateTimeOffset? notBefore = null)
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
        NotBefore = notBefore?.ToUniversalTime();
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
    /// The time before which the intent must not be delivered (R-7, spec 261): its outbox item becomes available then
    /// instead of at the commit. Null, or a time at or before the commit, delivers it after the commit as usual. A
    /// checkpoint that carries a later one is mandatory and durable in every persistence mode, and neither the
    /// in-process hop nor a drain's own delivery hands it over early.
    /// </summary>
    public DateTimeOffset? NotBefore { get; }

    /// <summary>Whether this intent must wait past <paramref name="committedAt"/>, its checkpoint's time.</summary>
    public bool IsDelayedPast(DateTimeOffset committedAt) => NotBefore is { } notBefore && notBefore > committedAt;

    /// <summary>
    /// Whether <paramref name="other"/> is the same logical intent. Payloads compare as JSON values, not text: an intent
    /// read back from durable storage carries a re-serialized payload whose whitespace and string escaping can differ
    /// from the original, and a text comparison would report that replay as a conflict.
    /// </summary>
    public bool IsEquivalentTo(RuntimePostCommitIntent? other) =>
        other is not null &&
        StringComparer.Ordinal.Equals(IntentId, other.IntentId) &&
        StringComparer.Ordinal.Equals(WorkflowExecutionId, other.WorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(Kind, other.Kind) &&
        RecordedAt == other.RecordedAt &&
        StringComparer.Ordinal.Equals(ActivityExecutionId, other.ActivityExecutionId) &&
        StringComparer.Ordinal.Equals(IdempotencyKey, other.IdempotencyKey) &&
        StringComparer.Ordinal.Equals(DependsOnWaitRegistrationId, other.DependsOnWaitRegistrationId) &&
        WaitFailurePolicy == other.WaitFailurePolicy &&
        NotBefore == other.NotBefore &&
        (Payload is { } payload ? other.Payload is { } otherPayload && JsonElement.DeepEquals(payload, otherPayload) : other.Payload is null) &&
        RuntimeModelMetadata.AreEqual(Metadata, other.Metadata);

    /// <summary>
    /// In-process-only conduit: the already-materialized <see cref="RuntimeSchedulerWorkItem"/> this
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
