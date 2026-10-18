using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Builds the scheduler work items that carry a still-running structural child's staged notifications to its
/// own committed parent for evaluation (seam C). Sibling of <see cref="ChildFaultParentEvaluation"/>:
/// the target is derived from the notifying child's committed <c>ParentActivityExecutionId</c> — never named by
/// the child — and each item routes to <c>WorkflowNotifyParentActivitySchedulerWorkHandler</c> as a dedicated
/// <see cref="WorkflowExecutionCommandKind.NotifyParentActivity"/> command. Emitted as post-commit intents on the
/// notifying child's own Defer/Complete checkpoint so they commit atomically with the child's state, while the
/// child keeps running.
/// </summary>
internal static class ParentNotificationEvaluation
{
    internal sealed record Result(
        IReadOnlyList<RuntimeSchedulerWorkItem> WorkItems,
        IReadOnlyList<ActivityChildNotifiedContext> FaultOutcomeNotifications)
    {
        public static readonly Result Empty = new([], []);
    }

    /// <summary>
    /// Validates the staged notifications against the child's continuation and committed parentage, then builds
    /// one deterministically-derived work item per notification (ordinal = staging order). A returned fault keeps
    /// its ordered notifications for the single durable child-outcome work item emitted with that fault's incident;
    /// notifications are never split into separate scheduler rows from that fault. Throws when a root activity (no
    /// committed parent) stages a notification or when notifications accompany a <c>Cancel</c> continuation.
    /// Returns an empty result when nothing was staged.
    /// </summary>
    public static async ValueTask<Result> BuildAsync(
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
            return Result.Empty;

        if (continuation.Kind == RuntimeStructuralContinuationKind.Cancel)
            throw new InvalidOperationException("A cancelling structural decision cannot also notify its parent in the same evaluation.");

        if (notifyingChildState.ParentActivityExecutionId is not { } parentActivityExecutionId)
            throw new InvalidOperationException("A root activity has no parent to notify.");

        var parentState = await activityExecutionStateStore.FindAsync(sourceWorkItem.WorkflowExecutionId, parentActivityExecutionId, cancellationToken)
            ?? throw new InvalidOperationException($"Parent notification references missing parent activity execution '{parentActivityExecutionId}' for workflow execution '{sourceWorkItem.WorkflowExecutionId}'.");

        var notifyingChildActivityExecutionId = notifyingChildState.Execution.ActivityExecutionId;
        if (continuation.Kind == RuntimeStructuralContinuationKind.Fault)
        {
            var outcomeNotifications = requests
                .Select(request => new ActivityChildNotifiedContext(
                    notifyingChildActivityExecutionId,
                    notifyingChildState.Execution.ExecutableNodeId,
                    request.Code,
                    notifyingChildState.IterationId,
                    request.Payload))
                .ToArray();
            return new Result([], Array.AsReadOnly(outcomeNotifications));
        }

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

        return new Result(workItems, []);
    }

    /// <summary>
    /// Builds one notify work item per staged ancestor notification, each addressed to the nearest committed ancestor
    /// whose executable node has the requested consumer key. The walk follows <c>ParentActivityExecutionId</c> from
    /// the requester's parent upward, the same committed chain <c>RuntimeContainerScopeService</c> walks, so no
    /// composite in between can intercept it. Only an initial structural execution that completes may stage one; the
    /// items carry the source work item's own sequence so they order ahead of the requester's completion work item
    /// (sequence + 1) on every queue. Throws <see cref="AncestorNotificationTargetNotFoundException"/> when no ancestor
    /// matches. Returns an empty list when nothing was staged.
    /// </summary>
    public static async ValueTask<IReadOnlyList<RuntimeSchedulerWorkItem>> BuildAncestorAsync(
        IActivityExecutionStateStore activityExecutionStateStore,
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem sourceWorkItem,
        WorkflowExecutable executable,
        ActivityExecutionState notifyingState,
        IReadOnlyCollection<RuntimeAncestorNotificationRequest> requests,
        RuntimeStructuralContinuation continuation,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return [];

        if (continuation.Kind != RuntimeStructuralContinuationKind.Complete)
            throw new InvalidOperationException("An ancestor notification can only accompany a structural decision that completes the requesting activity.");

        var notifyingActivityExecutionId = notifyingState.Execution.ActivityExecutionId;
        var notifyingExecutableNodeId = notifyingState.Execution.ExecutableNodeId;
        var workItems = new List<RuntimeSchedulerWorkItem>(requests.Count);
        var ordinal = 0;
        foreach (var request in requests)
        {
            var ancestorState = await FindNearestAncestorAsync(
                activityExecutionStateStore,
                sourceWorkItem.WorkflowExecutionId,
                executable,
                notifyingState,
                request.AncestorConsumerKey,
                cancellationToken)
                ?? throw new AncestorNotificationTargetNotFoundException(notifyingExecutableNodeId, request.AncestorConsumerKey);

            var suffix = $"notify-ancestor:{ordinal}";
            var now = timeProvider.GetUtcNow();
            var payload = new RuntimeNotifyParentCommandPayload(
                executable.Identity,
                ancestorState.Execution.ExecutableNodeId,
                ancestorState.Execution.ActivityExecutionId,
                ancestorState.ParentActivityExecutionId,
                ancestorState.BranchId,
                notifyingActivityExecutionId,
                notifyingExecutableNodeId,
                notifyingState.IterationId,
                request.Code,
                request.Payload,
                RuntimeNotifyParentCommandPayload.NotifyParentReason);

            var commandMetadata = sourceWorkItem.CommandMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            commandMetadata[RuntimeMetadataKeys.ParentActivityExecutionId] = ancestorState.Execution.ActivityExecutionId;

            workItems.Add(new RuntimeSchedulerWorkItem(
                workItemId: RuntimeChainId.Derive(sourceWorkItem.WorkItemId, suffix),
                workflowExecutionId: sourceWorkItem.WorkflowExecutionId,
                commandId: RuntimeChainId.Derive(sourceWorkItem.CommandId, suffix),
                commandKind: WorkflowExecutionCommandKind.NotifyParentActivity,
                envelopeId: sourceWorkItem.EnvelopeId,
                idempotencyKey: RuntimeChainId.Derive(sourceWorkItem.IdempotencyKey, suffix),
                enqueuedAt: now,
                recordedAt: now,
                sequence: sourceWorkItem.Sequence,
                payload: JsonSerializer.SerializeToElement(payload),
                commandMetadata: commandMetadata,
                envelopeMetadata: sourceWorkItem.EnvelopeMetadata,
                executionScopeId: ancestorState.ExecutionScopeId ?? ancestorState.Provenance.ExecutionScopeId,
                attempt: ancestorState.Attempt ?? ancestorState.Provenance.Attempt));

            ordinal++;
        }

        return workItems;
    }

    private static async ValueTask<ActivityExecutionState?> FindNearestAncestorAsync(
        IActivityExecutionStateStore activityExecutionStateStore,
        string workflowExecutionId,
        WorkflowExecutable executable,
        ActivityExecutionState notifyingState,
        string ancestorConsumerKey,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var ancestorId = notifyingState.ParentActivityExecutionId;
        while (ancestorId is not null && visited.Add(ancestorId))
        {
            var ancestor = await activityExecutionStateStore.FindAsync(workflowExecutionId, ancestorId, cancellationToken)
                ?? throw new InvalidOperationException($"Ancestor notification references missing activity execution '{ancestorId}' for workflow execution '{workflowExecutionId}'.");

            if (executable.NodesById.TryGetValue(ancestor.Execution.ExecutableNodeId, out var node) &&
                StringComparer.Ordinal.Equals(node.Descriptor.ConsumerKey, ancestorConsumerKey))
                return ancestor;

            ancestorId = ancestor.ParentActivityExecutionId;
        }

        return null;
    }
}

/// <summary>
/// Raised while building an ancestor notification when no committed ancestor has the requested consumer key. The
/// invoke handler turns it into the requesting activity's fault, coded <see cref="FaultCode"/>.
/// </summary>
internal sealed class AncestorNotificationTargetNotFoundException(string executableNodeId, string ancestorConsumerKey)
    : Exception($"Activity '{executableNodeId}' requested an ancestor notification, but no enclosing activity with consumer '{ancestorConsumerKey}' was found.")
{
    public const string FaultCode = "runtime.ancestor-notification.target-not-found";

    public string ExecutableNodeId { get; } = executableNodeId;
    public string AncestorConsumerKey { get; } = ancestorConsumerKey;
}
