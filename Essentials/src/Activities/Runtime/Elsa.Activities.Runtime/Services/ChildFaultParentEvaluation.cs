using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Builds the scheduler work item that propagates a child <b>fault</b> to its parent activity for
/// evaluation, the fault-side counterpart of the
/// <c>ActivityCompleted -&gt; ParentCompletionEvaluation</c> hop. The item routes to
/// <see cref="WorkflowParentActivityCompletionSchedulerWorkHandler"/> (it is a
/// <see cref="SchedulerCompletionKind.ParentCompletionEvaluation"/> command) and is tagged
/// <see cref="RuntimeMetadataKeys.ChildFaulted"/> so that handler invokes
/// <see cref="Elsa.Workflows.Runtime.Core.Contracts.IRuntimeActivityChildFaultHandler"/> rather than the
/// completion handler. Emitted as a post-commit intent on the fault incident checkpoint so it commits
/// atomically with the recorded incident.
/// </summary>
internal static class ChildFaultParentEvaluation
{
    public static ActivityFaultIncidentRecordRequest MarkPendingOutcome(
        ActivityFaultIncidentRecordRequest request,
        RuntimeSchedulerWorkItem parentOutcomeWorkItem)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(parentOutcomeWorkItem);

        var incidentMetadata = request.IncidentMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        incidentMetadata[RuntimeMetadataKeys.ParentOutcomeWorkItemId] = parentOutcomeWorkItem.WorkItemId;
        return request with { IncidentMetadata = incidentMetadata };
    }

    /// <summary>
    /// Builds the parent-fault evaluation work item for the parent of <paramref name="faultedChildState"/>,
    /// or <c>null</c> when the faulted activity has no parent (nothing to propagate to). The parent's opt-in
    /// (whether it implements <c>IRuntimeActivityChildFaultHandler</c>) is checked downstream by the handler, so a
    /// parent that does not handle child faults simply no-ops on the work item.
    /// </summary>
    public static async ValueTask<RuntimeSchedulerWorkItem?> TryBuildAsync(
        IActivityExecutionStateStore activityExecutionStateStore,
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem sourceWorkItem,
        WorkflowExecutableIdentity pinnedExecutable,
        ActivityExecutionState faultedChildState,
        string incidentId,
        CancellationToken cancellationToken,
        ActivityFault? parentOutcomeFault = null,
        IReadOnlyList<ActivityChildNotifiedContext>? parentOutcomeNotifications = null)
    {
        parentOutcomeNotifications ??= [];
        if (parentOutcomeNotifications.Count > 0 && parentOutcomeFault is null)
            throw new ArgumentException("A combined child outcome requires the masked child fault.", nameof(parentOutcomeFault));

        if (faultedChildState.ParentActivityExecutionId is not { } parentActivityExecutionId)
            return null;

        var parentState = await activityExecutionStateStore.FindAsync(sourceWorkItem.WorkflowExecutionId, parentActivityExecutionId, cancellationToken);
        if (parentState is null)
            return null;

        var faultedChildActivityExecutionId = faultedChildState.Execution.ActivityExecutionId;
        RuntimeParentOutcome? parentOutcome = null;
        if (parentOutcomeNotifications.Count > 0)
        {
            if (faultedChildState.Fault is not { } normalizedFault || !Matches(normalizedFault, parentOutcomeFault!))
                throw new InvalidOperationException("A combined parent outcome must carry the exact masked fault recorded on the child activity state.");

            if (parentOutcomeNotifications.Any(notification =>
                    !StringComparer.Ordinal.Equals(notification.NotifyingChildActivityExecutionId, faultedChildActivityExecutionId) ||
                    !StringComparer.Ordinal.Equals(notification.NotifyingChildExecutableNodeId, faultedChildState.Execution.ExecutableNodeId) ||
                    !StringComparer.Ordinal.Equals(notification.NotifyingChildIterationId, faultedChildState.IterationId)))
                throw new InvalidOperationException("Combined parent-outcome notifications must identify the faulted child activity execution, executable node, and iteration.");

            parentOutcome = new RuntimeParentOutcome(
                RuntimeParentOutcome.CurrentVersion,
                parentOutcomeFault!,
                incidentId,
                parentOutcomeNotifications);
        }

        var payload = new RuntimeCompleteActivityCommandPayload(
            pinnedExecutable,
            parentState.Execution.ExecutableNodeId,
            parentActivityExecutionId,
            parentState.ParentActivityExecutionId,
            parentState.BranchId,
            outcomeNames: [],
            parentOutcome is null
                ? RuntimeCompleteActivityCommandPayload.ParentCompletionEvaluationReason
                : RuntimeCompleteActivityCommandPayload.ParentOutcomeEvaluationReason,
            parentOutcome is null
                ? SchedulerCompletionKind.ParentCompletionEvaluation
                : SchedulerCompletionKind.ParentOutcomeEvaluation,
            faultedChildActivityExecutionId,
            parentOutcome);

        var now = timeProvider.GetUtcNow();
        var commandMetadata = SchedulerWorkItems.WithSchedulingMetadata(sourceWorkItem.CommandMetadata, faultedChildState);
        commandMetadata[RuntimeMetadataKeys.ChildFaulted] = bool.TrueString;
        commandMetadata[RuntimeMetadataKeys.IncidentId] = incidentId;
        commandMetadata[RuntimeMetadataKeys.ParentActivityExecutionId] = parentActivityExecutionId;
        commandMetadata[RuntimeMetadataKeys.CompletedChildActivityExecutionId] = faultedChildActivityExecutionId;

        var suffix = parentOutcome is null
            ? $"child-fault-parent:{parentActivityExecutionId}:child:{faultedChildActivityExecutionId}"
            : $"child-outcome-parent:{parentActivityExecutionId}:child:{faultedChildActivityExecutionId}";
        return new RuntimeSchedulerWorkItem(
            workItemId: RuntimeChainId.Derive(sourceWorkItem.WorkItemId, $"{suffix}"),
            workflowExecutionId: sourceWorkItem.WorkflowExecutionId,
            commandId: RuntimeChainId.Derive(sourceWorkItem.CommandId, $"{suffix}"),
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: sourceWorkItem.EnvelopeId,
            idempotencyKey: RuntimeChainId.Derive(sourceWorkItem.IdempotencyKey, $"{suffix}"),
            enqueuedAt: now,
            recordedAt: now,
            sequence: sourceWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: commandMetadata,
            envelopeMetadata: sourceWorkItem.EnvelopeMetadata,
            executionScopeId: parentState.ExecutionScopeId ?? parentState.Provenance.ExecutionScopeId,
            attempt: parentState.Attempt ?? parentState.Provenance.Attempt);
    }

    internal static bool Matches(NormalizedActivityFault normalized, ActivityFault fault)
    {
        var projected = fault.ToNormalized();
        return StringComparer.Ordinal.Equals(normalized.Code, projected.Code) &&
               StringComparer.Ordinal.Equals(normalized.ExceptionType, projected.ExceptionType) &&
               StringComparer.Ordinal.Equals(normalized.Message, projected.Message) &&
               normalized.SanitizedStackTrace is null &&
               normalized.IsRetryable == projected.IsRetryable &&
               normalized.Metadata.Count == projected.Metadata.Count &&
               normalized.Metadata.All(item => projected.Metadata.TryGetValue(item.Key, out var value) && StringComparer.Ordinal.Equals(item.Value, value));
    }
}
