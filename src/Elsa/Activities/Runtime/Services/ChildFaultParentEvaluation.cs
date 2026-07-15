using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Builds the scheduler work item that propagates a child <b>fault</b> to its parent activity for
/// evaluation, the fault-side counterpart of the
/// <c>ActivityCompleted -&gt; ParentCompletionEvaluation</c> hop. The item routes to
/// <see cref="WorkflowParentActivityCompletionSchedulerWorkHandler"/> (it is a
/// <see cref="SchedulerCompletionKind.ParentCompletionEvaluation"/> command) and is tagged
/// <see cref="RuntimeMetadataKeys.ChildFaulted"/> so that handler invokes
/// <see cref="Elsa.Activities.Runtime.Core.Contracts.IActivityChildFaultHandler"/> rather than the
/// completion handler. Emitted as a post-commit intent on the fault incident checkpoint so it commits
/// atomically with the recorded incident.
/// </summary>
internal static class ChildFaultParentEvaluation
{
    /// <summary>
    /// Builds the parent-fault evaluation work item for the parent of <paramref name="faultedChildState"/>,
    /// or <c>null</c> when the faulted activity has no parent (nothing to propagate to). The parent's opt-in
    /// (whether it implements <c>IActivityChildFaultHandler</c>) is checked downstream by the handler, so a
    /// parent that does not handle child faults simply no-ops on the work item.
    /// </summary>
    public static async ValueTask<RuntimeSchedulerWorkItem?> TryBuildAsync(
        IActivityExecutionStateStore activityExecutionStateStore,
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem sourceWorkItem,
        WorkflowExecutableIdentity pinnedExecutable,
        ActivityExecutionState faultedChildState,
        string incidentId,
        CancellationToken cancellationToken)
    {
        if (faultedChildState.ParentActivityExecutionId is not { } parentActivityExecutionId)
            return null;

        var parentState = await activityExecutionStateStore.FindAsync(sourceWorkItem.WorkflowExecutionId, parentActivityExecutionId, cancellationToken);
        if (parentState is null)
            return null;

        var faultedChildActivityExecutionId = faultedChildState.Execution.ActivityExecutionId;
        var payload = new RuntimeCompleteActivityCommandPayload(
            pinnedExecutable,
            parentState.Execution.ExecutableNodeId,
            parentActivityExecutionId,
            parentState.ParentActivityExecutionId,
            parentState.BranchId,
            outcomeNames: [],
            RuntimeCompleteActivityCommandPayload.ParentCompletionEvaluationReason,
            SchedulerCompletionKind.ParentCompletionEvaluation,
            faultedChildActivityExecutionId);

        var now = timeProvider.GetUtcNow();
        var commandMetadata = sourceWorkItem.CommandMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        commandMetadata[RuntimeMetadataKeys.ChildFaulted] = bool.TrueString;
        commandMetadata[RuntimeMetadataKeys.IncidentId] = incidentId;
        commandMetadata[RuntimeMetadataKeys.ParentActivityExecutionId] = parentActivityExecutionId;
        commandMetadata[RuntimeMetadataKeys.CompletedChildActivityExecutionId] = faultedChildActivityExecutionId;

        var suffix = $"child-fault-parent:{parentActivityExecutionId}:child:{faultedChildActivityExecutionId}";
        return new RuntimeSchedulerWorkItem(
            workItemId: $"{sourceWorkItem.WorkItemId}:{suffix}",
            workflowExecutionId: sourceWorkItem.WorkflowExecutionId,
            commandId: $"{sourceWorkItem.CommandId}:{suffix}",
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: sourceWorkItem.EnvelopeId,
            idempotencyKey: $"{sourceWorkItem.IdempotencyKey}:{suffix}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: sourceWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: commandMetadata,
            envelopeMetadata: sourceWorkItem.EnvelopeMetadata,
            executionScopeId: parentState.ExecutionScopeId ?? parentState.Provenance.ExecutionScopeId,
            attempt: parentState.Attempt ?? parentState.Provenance.Attempt);
    }
}
