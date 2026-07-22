using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Builds the scheduler work items that carry a still-running structural child's staged notifications to its
/// own committed parent for evaluation (spec 126, seam C). Sibling of <see cref="ChildFaultParentEvaluation"/>:
/// the target is derived from the notifying child's committed <c>ParentActivityExecutionId</c> — never named by
/// the child — and each item routes to <c>WorkflowNotifyParentActivitySchedulerWorkHandler</c> as a dedicated
/// <see cref="WorkflowExecutionCommandKind.NotifyParentActivity"/> command. Emitted as post-commit intents on the
/// notifying child's own Defer/Complete checkpoint so they commit atomically with the child's state, while the
/// child keeps running.
/// </summary>
internal static class ParentNotificationEvaluation
{
    /// <summary>
    /// Validates the staged notifications against the child's continuation and committed parentage, then builds
    /// one deterministically-derived work item per notification (ordinal = staging order). Throws — so the
    /// enclosing evaluation faults deterministically (the seam-A/B precedent) — when a root activity (no committed
    /// parent) stages a notification or when notifications accompany a <c>Fault</c>/<c>Cancel</c> continuation.
    /// Returns an empty list when nothing was staged.
    /// </summary>
    public static async ValueTask<IReadOnlyList<RuntimeSchedulerWorkItem>> BuildAsync(
        IActivityExecutionStateStore activityExecutionStateStore,
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem sourceWorkItem,
        WorkflowExecutableIdentity pinnedExecutable,
        ActivityExecutionState notifyingChildState,
        IReadOnlyCollection<RuntimeParentNotificationRequest> requests,
        RuntimeStructuralContinuation continuation,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return [];

        if (continuation.Kind is RuntimeStructuralContinuationKind.Fault or RuntimeStructuralContinuationKind.Cancel)
            throw new InvalidOperationException("A faulting or cancelling structural decision cannot also notify its parent in the same evaluation.");

        if (notifyingChildState.ParentActivityExecutionId is not { } parentActivityExecutionId)
            throw new InvalidOperationException("A root activity has no parent to notify.");

        var parentState = await activityExecutionStateStore.FindAsync(sourceWorkItem.WorkflowExecutionId, parentActivityExecutionId, cancellationToken)
            ?? throw new InvalidOperationException($"Parent notification references missing parent activity execution '{parentActivityExecutionId}' for workflow execution '{sourceWorkItem.WorkflowExecutionId}'.");

        var notifyingChildActivityExecutionId = notifyingChildState.Execution.ActivityExecutionId;
        var workItems = new List<RuntimeSchedulerWorkItem>(requests.Count);
        var ordinal = 0;
        foreach (var request in requests)
        {
            var suffix = $"notify-parent:{ordinal}";
            var now = timeProvider.GetUtcNow();
            var payload = new RuntimeNotifyParentCommandPayload(
                pinnedExecutable,
                parentState.Execution.ExecutableNodeId,
                parentActivityExecutionId,
                parentState.ParentActivityExecutionId,
                parentState.BranchId,
                notifyingChildActivityExecutionId,
                notifyingChildState.Execution.ExecutableNodeId,
                notifyingChildState.IterationId,
                request.Code,
                request.Payload,
                RuntimeNotifyParentCommandPayload.NotifyParentReason);

            var commandMetadata = sourceWorkItem.CommandMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            commandMetadata[RuntimeMetadataKeys.ParentActivityExecutionId] = parentActivityExecutionId;

            workItems.Add(new RuntimeSchedulerWorkItem(
                workItemId: RuntimeChainId.Derive(sourceWorkItem.WorkItemId, suffix),
                workflowExecutionId: sourceWorkItem.WorkflowExecutionId,
                commandId: RuntimeChainId.Derive(sourceWorkItem.CommandId, suffix),
                commandKind: WorkflowExecutionCommandKind.NotifyParentActivity,
                envelopeId: sourceWorkItem.EnvelopeId,
                idempotencyKey: RuntimeChainId.Derive(sourceWorkItem.IdempotencyKey, suffix),
                enqueuedAt: now,
                recordedAt: now,
                sequence: sourceWorkItem.Sequence is { } sequence ? sequence + ordinal + 1 : null,
                payload: JsonSerializer.SerializeToElement(payload),
                commandMetadata: commandMetadata,
                envelopeMetadata: sourceWorkItem.EnvelopeMetadata,
                executionScopeId: parentState.ExecutionScopeId ?? parentState.Provenance.ExecutionScopeId,
                attempt: parentState.Attempt ?? parentState.Provenance.Attempt));

            ordinal++;
        }

        return workItems;
    }
}
