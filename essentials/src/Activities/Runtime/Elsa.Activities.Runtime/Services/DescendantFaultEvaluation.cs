using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Builds the work item that delivers a fault past composites that do not handle child faults to the nearest ancestor
/// that opted into descendant faults (spec 258 R-3). It is built when a faulted child's direct parent declines the
/// fault, rides that parent's suspended checkpoint, and routes to
/// <see cref="WorkflowParentActivityCompletionSchedulerWorkHandler"/> as a child-fault evaluation marked
/// <see cref="RuntimeMetadataKeys.DescendantFault"/>. The walk reads only committed ancestry and the capability each
/// node was compiled with: it stops at a node that handles child faults (that composite expects faults from its
/// direct children only) and at a reusable-activity boundary, so nothing changes unless an opted-in ancestor is reached.
/// </summary>
internal static class DescendantFaultEvaluation
{
    /// <summary>
    /// Returns the delivery work item, or <c>null</c> when no opted-in ancestor is in reach. The walk starts at
    /// <paramref name="decliningParentState"/>, the faulted child's direct parent, which declined the fault.
    /// </summary>
    public static async ValueTask<RuntimeSchedulerWorkItem?> TryBuildAsync(
        IActivityExecutionStateStore activityExecutionStateStore,
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem sourceWorkItem,
        WorkflowExecutable executable,
        ActivityExecutionState decliningParentState,
        ActivityExecutionState faultedState,
        string incidentId,
        CancellationToken cancellationToken)
    {
        var match = await FindReceiverAsync(activityExecutionStateStore, sourceWorkItem.WorkflowExecutionId, executable, decliningParentState, faultedState, cancellationToken);
        if (match is not var (receiver, viaChild))
            return null;

        var receiverId = receiver.Execution.ActivityExecutionId;
        var faultedId = faultedState.Execution.ActivityExecutionId;
        var payload = new RuntimeCompleteActivityCommandPayload(
            executable.Identity,
            receiver.Execution.ExecutableNodeId,
            receiverId,
            receiver.ParentActivityExecutionId,
            receiver.BranchId,
            outcomeNames: [],
            RuntimeCompleteActivityCommandPayload.ParentCompletionEvaluationReason,
            SchedulerCompletionKind.ParentCompletionEvaluation,
            faultedId);

        // The receiver routes the fault by the via child, so its durable scheduling metadata rides on top of the
        // faulted chain's command metadata, as an operator retry rebuilds a command from provenance.
        var commandMetadata = sourceWorkItem.CommandMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var (key, value) in viaChild.Provenance.Metadata)
            commandMetadata[key] = value;
        commandMetadata[RuntimeMetadataKeys.ChildFaulted] = bool.TrueString;
        commandMetadata[RuntimeMetadataKeys.IncidentId] = incidentId;
        commandMetadata[RuntimeMetadataKeys.ParentActivityExecutionId] = receiverId;
        commandMetadata[RuntimeMetadataKeys.CompletedChildActivityExecutionId] = faultedId;
        commandMetadata[RuntimeMetadataKeys.DescendantFault] = bool.TrueString;
        commandMetadata[RuntimeMetadataKeys.DescendantFaultViaChildActivityExecutionId] = viaChild.Execution.ActivityExecutionId;
        commandMetadata[RuntimeMetadataKeys.DescendantFaultViaChildExecutableNodeId] = viaChild.Execution.ExecutableNodeId;

        var suffix = $"descendant-fault:{receiverId}:child:{faultedId}";
        var now = timeProvider.GetUtcNow();
        return new RuntimeSchedulerWorkItem(
            workItemId: RuntimeChainId.Derive(sourceWorkItem.WorkItemId, suffix),
            workflowExecutionId: sourceWorkItem.WorkflowExecutionId,
            commandId: RuntimeChainId.Derive(sourceWorkItem.CommandId, suffix),
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: sourceWorkItem.EnvelopeId,
            idempotencyKey: RuntimeChainId.Derive(sourceWorkItem.IdempotencyKey, suffix),
            enqueuedAt: now,
            recordedAt: now,
            sequence: sourceWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: commandMetadata,
            envelopeMetadata: sourceWorkItem.EnvelopeMetadata,
            executionScopeId: receiver.ExecutionScopeId ?? receiver.Provenance.ExecutionScopeId,
            attempt: receiver.Attempt ?? receiver.Provenance.Attempt);
    }

    public static bool IsDescendantFault(RuntimeSchedulerWorkItem workItem) =>
        workItem.CommandMetadata.TryGetValue(RuntimeMetadataKeys.DescendantFault, out var value) &&
        bool.TryParse(value, out var isDescendantFault) &&
        isDescendantFault;

    public static string? ReadViaChildActivityExecutionId(RuntimeSchedulerWorkItem workItem) =>
        workItem.CommandMetadata.TryGetValue(RuntimeMetadataKeys.DescendantFaultViaChildActivityExecutionId, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    public static ActivityExecutionState MarkProcessed(ActivityExecutionState faultedState, string workItemId)
    {
        var metadata = faultedState.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.DescendantFaultProcessedWorkItemId] = workItemId;
        return faultedState with { Metadata = RuntimeModelMetadata.Snapshot(metadata) };
    }

    public static bool WasProcessed(ActivityExecutionState faultedState, string workItemId) =>
        faultedState.Metadata.TryGetValue(RuntimeMetadataKeys.DescendantFaultProcessedWorkItemId, out var processedWorkItemId) &&
        StringComparer.Ordinal.Equals(processedWorkItemId, workItemId);

    private static async ValueTask<(ActivityExecutionState Receiver, ActivityExecutionState ViaChild)?> FindReceiverAsync(
        IActivityExecutionStateStore activityExecutionStateStore,
        string workflowExecutionId,
        WorkflowExecutable executable,
        ActivityExecutionState decliningParentState,
        ActivityExecutionState faultedState,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var viaChild = faultedState;
        var candidate = decliningParentState;
        while (visited.Add(candidate.Execution.ActivityExecutionId))
        {
            if (!executable.NodesById.TryGetValue(candidate.Execution.ExecutableNodeId, out var node) ||
                StringComparer.Ordinal.Equals(node.Descriptor.ConsumerKey, WellKnownRuntimeActivityConsumers.GraphActivity))
                return null;

            var faultHandling = node.Metadata.GetValueOrDefault(RuntimeMetadataKeys.StructuralFaultHandling);
            if (StringComparer.Ordinal.Equals(faultHandling, RuntimeMetadataKeys.StructuralFaultHandlingDescendant))
                return (candidate, viaChild);
            if (StringComparer.Ordinal.Equals(faultHandling, RuntimeMetadataKeys.StructuralFaultHandlingChild))
                return null;

            if (candidate.ParentActivityExecutionId is not { } parentId)
                return null;

            viaChild = candidate;
            candidate = await activityExecutionStateStore.FindAsync(workflowExecutionId, parentId, cancellationToken)
                        ?? throw new InvalidOperationException($"Descendant-fault delivery references missing activity execution '{parentId}' for workflow execution '{workflowExecutionId}'.");
        }

        return null;
    }
}
