using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Shared machinery for the structural parent-evaluation handlers (child completion/fault — spec 112/115 —
/// and child→parent notification — spec 126). Consolidates the transient reconstruction of a structural
/// parent from its pinned snapshot and the seam-A child-subtree cancellation planning + single-commit
/// change-set projection, so the completion handler and the notification handler share one mutation home
/// (DRY; no duplicated validation or terminalization logic).
/// </summary>
internal static class StructuralParentEvaluationSupport
{
    internal sealed record ConstructedActivity(
        IActivity Activity,
        ActivityInputSnapshot InputSnapshot,
        ActivityActivationLease ActivationLease);

    /// <summary>
    /// Reactivates the structural activity for <paramref name="state"/> from its pinned executable node and
    /// committed input snapshot, returning the lease the caller must dispose. Used by every parent-evaluation
    /// handler before dispatching a structural callback. When the activity opts into
    /// <see cref="IRuntimeRematerializeInputsOnChildCompletion"/> and the caller supplies
    /// <paramref name="inputRematerializer"/> (the child-completion evaluation does; the notification and
    /// bookmark-resume evaluations do not), the inputs are re-materialized from live committed variable
    /// frames and the activity is re-activated on the fresh snapshot, so a loop condition observes state the
    /// completed child mutated (issue #977). The fresh snapshot is transient — the pinned
    /// <c>state.InputSnapshot</c> is never rewritten (ADR 0045).
    /// </summary>
    public static async ValueTask<ConstructedActivity> ConstructActivityAsync(
        IServiceProvider serviceProvider,
        ExecutableNode executableNode,
        ActivityExecutionState state,
        CancellationToken cancellationToken,
        RuntimeActivityInputSnapshotMaterializer? inputRematerializer = null,
        WorkflowExecutable? executable = null,
        DateTimeOffset? materializedAt = null)
    {
        var contract = executableNode.ActivityContract
            ?? throw new InvalidOperationException($"VF-ACT-001: Executable CLR activity node '{executableNode.ExecutableNodeId}' has no pinned activity contract.");
        state.EnsureValueFlowCompatible();
        var snapshot = RequireCommittedSnapshot(state, contract);
        var attempt = state.Attempts?.LastOrDefault(item => item.EndedAt is null)
            ?? throw new InvalidOperationException($"VF-ACT-009: Running typed activity invocation '{state.InvocationId}' has no open committed attempt.");
        var activator = serviceProvider.GetRequiredService<IActivityActivator>();
        var activationLease = await activator.ActivateAsync(
            new ActivityActivationRequest(contract, snapshot, attempt, state.PrivateState, Descriptor: executableNode.Descriptor),
            cancellationToken);

        if (inputRematerializer is null || executable is null || activationLease.Activity is not IRuntimeRematerializeInputsOnChildCompletion)
            return new ConstructedActivity(activationLease.Activity, snapshot, activationLease);

        // Issue #977: re-read the opt-in composite's inputs from live committed frames. Materialization runs
        // while the probe lease is still open; a fault disposes it before propagating (no lease leak) and is
        // handled by the caller exactly like any other construction fault.
        ActivityInputSnapshot freshSnapshot;
        try
        {
            freshSnapshot = await inputRematerializer.MaterializeAsync(
                executable,
                executableNode,
                state,
                serviceProvider,
                materializedAt ?? throw new InvalidOperationException($"Typed activity invocation '{state.InvocationId}' requires a materialization timestamp to re-materialize its inputs."),
                cancellationToken);
        }
        catch (Exception exception)
        {
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
            if (disposalException is not null)
                throw ActivityActivationLeaseDisposer.Combine(exception, disposalException);
            throw;
        }

        await activationLease.DisposeAsync();
        var freshLease = await activator.ActivateAsync(
            new ActivityActivationRequest(contract, freshSnapshot, attempt, state.PrivateState, Descriptor: executableNode.Descriptor),
            cancellationToken);
        return new ConstructedActivity(freshLease.Activity, freshSnapshot, freshLease);
    }

    public static ActivityInputSnapshot RequireCommittedSnapshot(ActivityExecutionState state, ActivityContract contract)
    {
        var snapshot = state.InputSnapshot
            ?? throw new InvalidOperationException($"VF-ACT-009: Typed activity invocation '{state.InvocationId}' has no committed input snapshot.");

        if (!StringComparer.Ordinal.Equals(snapshot.InvocationId, state.InvocationId) ||
            !StringComparer.Ordinal.Equals(snapshot.ContractFingerprint, contract.SchemaFingerprint))
            throw new InvalidOperationException($"VF-ACT-001: Typed activity invocation '{state.InvocationId}' does not match its pinned input snapshot contract.");

        if (state.ContractIdentity is not { } identity ||
            !StringComparer.Ordinal.Equals(identity.ActivityTypeKey, contract.ActivityTypeKey) ||
            !StringComparer.Ordinal.Equals(identity.ContractVersion, contract.ContractVersion) ||
            !StringComparer.Ordinal.Equals(identity.SchemaFingerprint, contract.SchemaFingerprint))
            throw new InvalidOperationException($"VF-ACT-001: Typed activity invocation '{state.InvocationId}' does not match its pinned activity contract.");

        return snapshot;
    }

    /// <summary>
    /// Loads a structural parent's direct, non-terminal child executions (spec 119 D4) for an opt-in
    /// <c>IRuntimeLiveChildActivityConsumer</c> parent, so its structural callback can resolve a losing
    /// sibling's node id (and iteration id) to its live activity-execution id before staging a seam-A subtree
    /// cancellation. Terminal children are excluded — they are never cancellation targets.
    /// </summary>
    public static async ValueTask<IReadOnlyCollection<RuntimeLiveChildActivity>> LoadLiveChildActivitiesAsync(
        IActivityExecutionStateStore activityExecutionStateStore,
        string workflowExecutionId,
        string parentActivityExecutionId,
        CancellationToken cancellationToken)
    {
        var children = await activityExecutionStateStore.ListAllByParentAsync(workflowExecutionId, parentActivityExecutionId, cancellationToken);
        return children
            .Where(child => child.Status is not (
                ActivityExecutionStatus.Completed or
                ActivityExecutionStatus.Faulted or
                ActivityExecutionStatus.Cancelled or
                ActivityExecutionStatus.Recovered))
            .Select(child => new RuntimeLiveChildActivity(child.Execution.ActivityExecutionId, child.Execution.ExecutableNodeId, child.Status, child.IterationId))
            .ToArray();
    }

    /// <summary>
    /// Validates and plans the seam-A (spec 112) child-subtree cancellations a structural callback staged.
    /// Structural misuse (unknown target, non-child target, duplicate, terminal continuation) faults the
    /// evaluation; a target that is already terminal is a legal first-completion-wins race and is skipped.
    /// The exact validation and message text are preserved from the child-completion handler so seam-A
    /// behavior is identical whether staged from a completion, fault, or notification evaluation.
    /// </summary>
    public static async ValueTask<IReadOnlyCollection<ActivitySubtreeCancellationPlan>> PlanChildSubtreeCancellationsAsync(
        IServiceProvider serviceProvider,
        IActivityExecutionStateStore activityExecutionStateStore,
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem workItem,
        string parentActivityExecutionId,
        IReadOnlyCollection<RuntimeChildSubtreeCancellationRequest> requests,
        RuntimeStructuralContinuation continuation,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return [];

        if (continuation.Kind is RuntimeStructuralContinuationKind.Fault or RuntimeStructuralContinuationKind.Cancel)
            throw new InvalidOperationException("A faulting or cancelling structural decision cannot also cancel child subtrees in the same child-completion evaluation.");

        var duplicateTarget = requests.GroupBy(request => request.ChildActivityExecutionId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTarget is not null)
            throw new InvalidOperationException($"Child subtree cancellation targets activity execution '{duplicateTarget.Key}' more than once.");

        var planner = serviceProvider.GetRequiredService<ActivitySubtreeCancellationPlanner>();
        var allStates = await activityExecutionStateStore.ListAllAsync(workItem.WorkflowExecutionId, cancellationToken);
        var byId = allStates.ToDictionary(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal);
        var occurredAt = timeProvider.GetUtcNow();
        var plans = new List<ActivitySubtreeCancellationPlan>(requests.Count);
        foreach (var request in requests)
        {
            if (!byId.TryGetValue(request.ChildActivityExecutionId, out var target))
                throw new InvalidOperationException($"Child subtree cancellation references missing activity execution '{request.ChildActivityExecutionId}'.");
            if (!StringComparer.Ordinal.Equals(target.ParentActivityExecutionId, parentActivityExecutionId))
                throw new InvalidOperationException($"Child subtree cancellation targets activity execution '{request.ChildActivityExecutionId}', which is not a child of parent activity execution '{parentActivityExecutionId}'.");
            if (target.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Recovered)
                continue;

            plans.Add(await planner.PlanAsync(
                workItem.WorkflowExecutionId,
                target,
                allStates,
                subStatus: "ParentCancelled",
                NewStagedCancellationMetadata(workItem, request.Reason, parentActivityExecutionId, request.Metadata),
                occurredAt,
                cancellationToken));
        }

        return plans;
    }

    public static Dictionary<string, string> NewStagedCancellationMetadata(
        RuntimeSchedulerWorkItem workItem,
        string reason,
        string requestedByActivityExecutionId,
        IReadOnlyDictionary<string, string> requestMetadata)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = workItem.CommandId,
            [RuntimeMetadataKeys.ScopeCancellationReason] = reason,
            [RuntimeMetadataKeys.SubtreeCancellationRequestedBy] = requestedByActivityExecutionId
        };
        foreach (var item in requestMetadata)
            metadata[item.Key] = item.Value;
        return metadata;
    }

    /// <summary>
    /// Projects planned subtree cancellations (spec 112) onto the typed change-set slots of the one commit
    /// that also persists the parent's continuation (single-commit atomicity).
    /// </summary>
    public static async ValueTask<SubtreeCancellationCommitChanges> BuildSubtreeCancellationChangesAsync(
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        IReadOnlyCollection<ActivitySubtreeCancellationPlan> plans,
        string checkpointId,
        DateTimeOffset occurredAt,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        if (plans.Count == 0)
            return SubtreeCancellationCommitChanges.Empty;

        var cancelledStates = plans.SelectMany(plan => plan.CancelledStates).ToArray();
        var activityExecutionChanges = cancelledStates
            .Select(state => new RuntimeStateChange<ActivityExecutionState>(
                state.Execution.ActivityExecutionId,
                RuntimeStateChangeOperation.Upsert,
                state,
                metadata))
            .ToArray();
        var incidentChanges = plans.SelectMany(plan => plan.IncidentChanges)
            .Select(incident => new RuntimeStateChange<IncidentState>(
                incident.IncidentId,
                RuntimeStateChangeOperation.Upsert,
                incident,
                metadata))
            .ToArray();
        var inspectionChanges = new List<RuntimeStateChange<ActivityExecutionInspectionProjection>>(cancelledStates.Length);
        if (inspectionAccumulator is not null)
        {
            foreach (var state in cancelledStates)
            {
                var projection = await inspectionAccumulator.BuildProjectionAsync(
                    state, checkpointId, occurredAt, metadata: metadata, cancellationToken: cancellationToken);
                inspectionChanges.Add(new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                    state.Execution.ActivityExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    projection,
                    metadata));
            }
        }

        return new SubtreeCancellationCommitChanges(
            activityExecutionChanges,
            incidentChanges,
            inspectionChanges,
            plans.Select(plan => plan.Cleanup).ToArray(),
            cancelledStates.Select(state => state.Execution.ActivityExecutionId).ToArray());
    }

    /// <summary>
    /// What a structural parent-evaluation commit takes from its source work item: the parent's pinned identity,
    /// the checkpoint/commit metadata the handler assembled (each handler records its own key set), and the
    /// command metadata the derived child-schedule and upward-completion work items inherit.
    /// </summary>
    internal sealed record ParentEvaluationCommitSource(
        RuntimeSchedulerWorkItem WorkItem,
        WorkflowExecutableIdentity PinnedExecutable,
        string ExecutableNodeId,
        string ActivityExecutionId,
        IReadOnlyDictionary<string, string> Metadata,
        IReadOnlyDictionary<string, string> DerivedCommandMetadata);

    /// <summary>
    /// Commits a structural parent that stays running after evaluating a child: persists the parent (and any
    /// <paramref name="processedChildStates"/> the evaluation marked), captures its inspection projection, and
    /// enqueues the newly scheduled children followed by <paramref name="followUpWorkItems"/> after the commit
    /// lands, all in one checkpoint alongside the staged subtree cancellations.
    /// </summary>
    public static async ValueTask CommitDeferredParentActivityAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        IRuntimeExecutionIdGenerator idGenerator,
        TimeProvider timeProvider,
        ParentEvaluationCommitSource source,
        ActivityExecutionState parentState,
        IReadOnlyCollection<ActivityExecutionState> processedChildStates,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> scheduleRequests,
        IReadOnlyCollection<RuntimeSchedulerWorkItem> followUpWorkItems,
        IReadOnlyCollection<ActivitySubtreeCancellationPlan> subtreeCancellations,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        CancellationToken cancellationToken)
    {
        var occurredAt = timeProvider.GetUtcNow();
        var (workItem, activityExecutionId, metadata) = (source.WorkItem, source.ActivityExecutionId, source.Metadata);
        var checkpointId = $"checkpoint:{workItem.WorkItemId}:activity-inspection-captured:{activityExecutionId}";
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> inspectionChanges = inspectionAccumulator is null
            ? []
            :
            [
                new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                    StateId: activityExecutionId,
                    Operation: RuntimeStateChangeOperation.Upsert,
                    State: await inspectionAccumulator.BuildProjectionAsync(
                        parentState, checkpointId, occurredAt, valueSnapshots: valueSnapshots, metadata: metadata, cancellationToken: cancellationToken),
                    Metadata: metadata)
            ];
        var childWorkItems = SchedulerWorkItems.NewChildActivityScheduleWorkItems(
            timeProvider, idGenerator, workItem, source.PinnedExecutable, activityExecutionId, scheduleRequests, source.DerivedCommandMetadata).ToArray();
        var cancellationChanges = await BuildSubtreeCancellationChangesAsync(
            inspectionAccumulator, subtreeCancellations, checkpointId, occurredAt, metadata, cancellationToken);
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{workItem.WorkItemId}:activity-inspection-captured:{activityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityInspectionCaptured,
                WorkflowExecutionId: workItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds:
                [
                    activityExecutionId,
                    .. processedChildStates.Select(state => state.Execution.ActivityExecutionId),
                    .. cancellationChanges.CancelledActivityExecutionIds
                ],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: activityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: parentState,
                        Metadata: metadata),
                    .. processedChildStates.Select(state => new RuntimeStateChange<ActivityExecutionState>(
                        StateId: state.Execution.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: state,
                        Metadata: metadata)),
                    .. cancellationChanges.ActivityExecutions
                ],
                bookmarks: [],
                durableValues: [],
                incidents: cancellationChanges.Incidents,
                operational: [],
                activityExecutionInspections: [.. inspectionChanges, .. cancellationChanges.Inspections],
                activityScopeCleanups: cancellationChanges.Cleanups),
            PostCommitIntents: childWorkItems
                .Concat(followUpWorkItems)
                .Select(item => SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(workItem, activityExecutionId, item, occurredAt))
                .ToArray(),
            Metadata: metadata);

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    /// <summary>
    /// Commits a structural parent that completed after evaluating a child: persists the completed parent with its
    /// inspection projection, durable-value changes and workflow-variable write-back, and enqueues its upward
    /// completion work item followed by <paramref name="parentNotifications"/> after the commit lands, all in one
    /// checkpoint alongside the staged subtree cancellations.
    /// </summary>
    public static async ValueTask CommitCompletedParentActivityAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        TimeProvider timeProvider,
        ParentEvaluationCommitSource source,
        ActivityExecutionState completedParentState,
        IReadOnlyCollection<string> outcomeNames,
        IReadOnlyCollection<ActivitySubtreeCancellationPlan> subtreeCancellations,
        IReadOnlyCollection<RuntimeSchedulerWorkItem> parentNotifications,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges,
        RuntimeStateChange<WorkflowExecutionState>? workflowVariableWriteBack,
        CancellationToken cancellationToken)
    {
        var occurredAt = timeProvider.GetUtcNow();
        var (workItem, activityExecutionId, metadata) = (source.WorkItem, source.ActivityExecutionId, source.Metadata);
        var checkpointId = $"checkpoint:{workItem.WorkItemId}:parent-activity-completed:{activityExecutionId}";
        var inspection = inspectionAccumulator is null
            ? null
            : await inspectionAccumulator.BuildProjectionAsync(
                completedParentState, checkpointId, occurredAt, outcomeNames: outcomeNames, valueSnapshots: valueSnapshots, metadata: metadata, cancellationToken: cancellationToken);
        var completionWorkItem = SchedulerWorkItems.NewCompletionWorkItem(
            timeProvider, workItem, source.PinnedExecutable, source.ExecutableNodeId, activityExecutionId, completedParentState, commandMetadata: source.DerivedCommandMetadata);
        var cancellationChanges = await BuildSubtreeCancellationChangesAsync(
            inspectionAccumulator, subtreeCancellations, checkpointId, occurredAt, metadata, cancellationToken);
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{workItem.WorkItemId}:parent-activity-completed:{activityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityCompleted,
                WorkflowExecutionId: workItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [activityExecutionId, .. cancellationChanges.CancelledActivityExecutionIds],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: workflowVariableWriteBack,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: activityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: completedParentState,
                        Metadata: metadata),
                    .. cancellationChanges.ActivityExecutions
                ],
                bookmarks: [],
                durableValues: durableValueChanges,
                incidents: cancellationChanges.Incidents,
                operational: [],
                activityExecutionInspections: inspection is null
                    ? [.. cancellationChanges.Inspections]
                    :
                    [
                        new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                            StateId: activityExecutionId,
                            Operation: RuntimeStateChangeOperation.Upsert,
                            State: inspection,
                            Metadata: metadata),
                        .. cancellationChanges.Inspections
                    ],
                activityScopeCleanups: cancellationChanges.Cleanups),
            PostCommitIntents: new[] { completionWorkItem }
                .Concat(parentNotifications)
                .Select(item => SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(workItem, activityExecutionId, item, occurredAt))
                .ToArray(),
            Metadata: metadata);

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    internal sealed record SubtreeCancellationCommitChanges(
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> ActivityExecutions,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> Incidents,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> Inspections,
        IReadOnlyCollection<ActivityScopeCleanupRequest> Cleanups,
        IReadOnlyCollection<string> CancelledActivityExecutionIds)
    {
        public static readonly SubtreeCancellationCommitChanges Empty = new([], [], [], [], []);
    }
}
