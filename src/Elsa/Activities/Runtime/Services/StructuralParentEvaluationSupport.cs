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
    /// handler before dispatching a structural callback.
    /// </summary>
    public static async ValueTask<ConstructedActivity> ConstructActivityAsync(
        IServiceProvider serviceProvider,
        ExecutableNode executableNode,
        ActivityExecutionState state,
        CancellationToken cancellationToken)
    {
        var contract = executableNode.ActivityContract
            ?? throw new InvalidOperationException($"VF-ACT-001: Executable CLR activity node '{executableNode.ExecutableNodeId}' has no pinned activity contract.");
        state.EnsureValueFlowCompatible();
        var snapshot = RequireCommittedSnapshot(state, contract);
        var attempt = state.Attempts?.LastOrDefault(item => item.EndedAt is null)
            ?? throw new InvalidOperationException($"VF-ACT-009: Running typed activity invocation '{state.InvocationId}' has no open committed attempt.");
        var activationLease = await serviceProvider.GetRequiredService<IActivityActivator>().ActivateAsync(
            new ActivityActivationRequest(contract, snapshot, attempt, state.PrivateState, Descriptor: executableNode.Descriptor),
            cancellationToken);
        return new ConstructedActivity(activationLease.Activity, snapshot, activationLease);
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
