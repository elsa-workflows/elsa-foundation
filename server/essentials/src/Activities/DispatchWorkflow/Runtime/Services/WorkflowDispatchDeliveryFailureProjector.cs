using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Projects exhausted deterministic child-start delivery into safe dispatch and wait-parent state.</summary>
public sealed class WorkflowDispatchDeliveryFailureProjector(
    IWorkflowDispatchStore workflowDispatchStore) : IPostCommitFailureProjector
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly RuntimePostCommitRetryPolicy ResumeRetryPolicy =
        RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1));

    public async ValueTask<PostCommitFailureProjection?> ProjectAsync(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxDeliveryResult finalResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(finalResult);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsEffectiveFinalFailure(item, finalResult) ||
            !StringComparer.Ordinal.Equals(item.Intent.Kind, DispatchWorkflowConstants.StartChildIntentKind))
            return null;
        if (!item.Intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var dispatchId))
            throw new InvalidOperationException("A final DispatchWorkflow start failure requires its deterministic dispatch identity.");

        var dispatch = await workflowDispatchStore.FindAsync(dispatchId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' was not found for final child-start delivery failure.");
        var identity = new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);
        ValidateStartIntent(item.Intent, dispatch, identity);
        var generation = WorkflowDispatchLifecycle.ReadDeliveryGeneration(dispatch);
        var attemptCount = RuntimePostCommitRetryPolicy.SaturatingIncrement(item.DeliveryAttemptCount);
        var firstAttemptAt = RuntimePostCommitOutboxClaimTransitions.ReadFirstDeliveryAttemptedAt(item) ??
            item.DeliveryStartedAt ??
            finalResult.RecordedAt;
        var failedDispatch = dispatch.Status == WorkflowDispatchStatus.DispatchFailed
            ? dispatch
            : WorkflowDispatchLifecycle.TransitionToDispatchFailed(
                dispatch,
                item.OutboxItemId,
                generation,
                attemptCount,
                firstAttemptAt,
                finalResult.RecordedAt);

        if (dispatch.Status == WorkflowDispatchStatus.DispatchFailed)
        {
            if (!StringComparer.Ordinal.Equals(WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(dispatch), item.OutboxItemId) ||
                !StringComparer.Ordinal.Equals(WorkflowDispatchLifecycle.ReadDeliveryIncidentId(dispatch), identity.DeliveryIncidentId(generation)))
            {
                throw new InvalidOperationException($"Workflow dispatch '{dispatch.DispatchId}' conflicts with its final delivery failure.");
            }
        }

        var followUp = failedDispatch.Mode == WorkflowDispatchMode.WaitForCompletion
            ? CreateParentResume(failedDispatch, identity, generation, finalResult.RecordedAt)
            : null;
        return new PostCommitFailureProjection(failedDispatch, followUp);
    }

    private static RuntimePostCommitOutboxItem CreateParentResume(
        WorkflowDispatchRecord dispatch,
        WorkflowDispatchIdentity identity,
        int generation,
        DateTimeOffset recordedAt)
    {
        var incidentId = WorkflowDispatchLifecycle.ReadDeliveryIncidentId(dispatch)
            ?? throw new InvalidOperationException($"Workflow dispatch '{dispatch.DispatchId}' has no safe delivery incident identity.");
        var result = new DispatchWorkflowResult(
            dispatch.ChildWorkflowExecutionId,
            WorkflowDispatchStatus.DispatchFailed,
            diagnosticMetadata: DispatchWorkflowDiagnostics.DispatchFailed(incidentId));
        var payload = new WorkflowDispatchParentResumePayload(
            dispatch.DispatchId,
            dispatch.ParentWorkflowExecutionId,
            dispatch.ParentActivityExecutionId,
            dispatch.ChildWorkflowExecutionId,
            identity.WaitBookmarkId,
            DispatchWorkflowConstants.WaitStimulusType,
            identity.WaitStimulusHash,
            result);
        var intent = new RuntimePostCommitIntent(
            identity.ParentResumeIntentId,
            dispatch.ParentWorkflowExecutionId,
            DispatchWorkflowConstants.ResumeParentIntentKind,
            recordedAt,
            dispatch.ParentActivityExecutionId,
            identity.ParentResumeIdempotencyKey,
            JsonSerializer.SerializeToElement(payload, SerializerOptions),
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId,
                [RuntimeMetadataKeys.ChildWorkflowExecutionId] = dispatch.ChildWorkflowExecutionId
            });
        return new RuntimePostCommitOutboxItem(
            identity.WaitFailureResumeOutboxItemId(generation),
            intent,
            RuntimePostCommitOutboxStatus.Pending,
            recordedAt,
            recordedAt,
            ResumeRetryPolicy);
    }

    private static void ValidateStartIntent(
        RuntimePostCommitIntent intent,
        WorkflowDispatchRecord dispatch,
        WorkflowDispatchIdentity identity)
    {
        if (!StringComparer.Ordinal.Equals(dispatch.DispatchId, identity.DispatchId) ||
            !StringComparer.Ordinal.Equals(intent.IntentId, identity.StartIntentId) ||
            !StringComparer.Ordinal.Equals(intent.IdempotencyKey, identity.StartIdempotencyKey) ||
            !StringComparer.Ordinal.Equals(intent.WorkflowExecutionId, dispatch.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(intent.ActivityExecutionId, dispatch.ParentActivityExecutionId))
        {
            throw new InvalidOperationException($"Child-start intent '{intent.IntentId}' conflicts with workflow dispatch '{dispatch.DispatchId}'.");
        }
    }

    private static bool IsEffectiveFinalFailure(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxDeliveryResult result) =>
        result.Status == RuntimePostCommitOutboxStatus.FailedFinal ||
        result.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
        item.RetryPolicy.IsExhaustedAfterAttempt(
            RuntimePostCommitRetryPolicy.SaturatingIncrement(item.DeliveryAttemptCount));
}
