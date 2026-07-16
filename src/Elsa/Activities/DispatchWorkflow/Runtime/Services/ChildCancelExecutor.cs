using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Delivers one deterministic Cancel command to an admitted DispatchWorkflow child.</summary>
public sealed class ChildCancelExecutor(
    IWorkflowExecutionActorProvider actorProvider,
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IWorkflowDispatchStore workflowDispatchStore) : IRuntimePostCommitIntentHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!StringComparer.Ordinal.Equals(intent.Kind, DispatchWorkflowConstants.CancelChildIntentKind) ||
            intent.Payload is null)
        {
            throw new InvalidOperationException($"ChildCancelExecutor cannot handle post-commit intent '{intent.IntentId}'.");
        }

        var payload = intent.Payload.Value.Deserialize<WorkflowDispatchChildCancelPayload>(SerializerOptions)
            ?? throw new InvalidOperationException($"DispatchWorkflow child-cancel intent '{intent.IntentId}' has an invalid payload.");
        var identity = new WorkflowDispatchIdentity(
            payload.ParentWorkflowExecutionId,
            payload.ParentActivityExecutionId);
        ValidateIntent(intent, payload, identity);

        var dispatch = await workflowDispatchStore.FindAsync(payload.DispatchId, cancellationToken)
            ?? throw new ChildCancelDeferredException(payload.DispatchId, payload.ChildWorkflowExecutionId);
        ValidateDispatch(dispatch, payload);
        if (dispatch.Status is
            WorkflowDispatchStatus.Completed or
            WorkflowDispatchStatus.Faulted or
            WorkflowDispatchStatus.Cancelled or
            WorkflowDispatchStatus.DispatchFailed)
        {
            return;
        }
        if (dispatch.Status != WorkflowDispatchStatus.Started)
            throw new ChildCancelDeferredException(payload.DispatchId, payload.ChildWorkflowExecutionId);

        var child = await workflowExecutionStateStore.FindAsync(payload.ChildWorkflowExecutionId, cancellationToken);
        if (child?.Status.IsTerminal() == true)
            return;
        if (child is null)
            throw new ChildCancelDeferredException(payload.DispatchId, payload.ChildWorkflowExecutionId);

        var metadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.DispatchId] = payload.DispatchId,
            [RuntimeMetadataKeys.ChildWorkflowExecutionId] = payload.ChildWorkflowExecutionId
        };
        var command = new WorkflowExecutionCommand(
            CommandId: identity.ChildCancelCommandId,
            WorkflowExecutionId: payload.ChildWorkflowExecutionId,
            Kind: WorkflowExecutionCommandKind.Cancel,
            EnqueuedAt: intent.RecordedAt,
            Payload: null,
            Metadata: metadata);
        var envelope = new WorkflowExecutionCommandEnvelope(
            envelopeId: identity.ChildCancelEnvelopeId,
            workflowExecutionId: payload.ChildWorkflowExecutionId,
            command: command,
            idempotencyKey: identity.ChildCancelIdempotencyKey,
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: intent.RecordedAt,
            metadata: metadata,
            partition: dispatch.Partition);
        var activation = new WorkflowExecutionActorActivationRequest(
            workflowExecutionId: payload.ChildWorkflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.ControlPlaneCommand,
            requestedAt: intent.RecordedAt,
            requestedBy: dispatch.Authority.SystemIdentity,
            requiredCapabilities: WorkflowExecutionActorCapabilities.None,
            metadata: metadata,
            partition: dispatch.Partition);

        var actor = await actorProvider.GetAgentAsync(activation, cancellationToken);
        await actor.EnqueueAsync(envelope, cancellationToken);

        child = await workflowExecutionStateStore.FindAsync(payload.ChildWorkflowExecutionId, cancellationToken);
        if (child?.Status.IsTerminal() != true)
            throw new ChildCancelDeferredException(payload.DispatchId, payload.ChildWorkflowExecutionId);
    }

    private static void ValidateIntent(
        RuntimePostCommitIntent intent,
        WorkflowDispatchChildCancelPayload payload,
        WorkflowDispatchIdentity identity)
    {
        if (!StringComparer.Ordinal.Equals(intent.IntentId, identity.ChildCancelIntentId) ||
            !StringComparer.Ordinal.Equals(intent.WorkflowExecutionId, payload.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(intent.ActivityExecutionId, payload.ParentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(intent.IdempotencyKey, identity.ChildCancelIdempotencyKey) ||
            intent.Metadata.Count != 2 ||
            !intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var dispatchId) ||
            !StringComparer.Ordinal.Equals(dispatchId, payload.DispatchId) ||
            !intent.Metadata.TryGetValue(RuntimeMetadataKeys.ChildWorkflowExecutionId, out var childId) ||
            !StringComparer.Ordinal.Equals(childId, payload.ChildWorkflowExecutionId))
        {
            throw new InvalidOperationException($"DispatchWorkflow child-cancel intent '{intent.IntentId}' does not match its deterministic dispatch identity.");
        }
    }

    private static void ValidateDispatch(
        WorkflowDispatchRecord dispatch,
        WorkflowDispatchChildCancelPayload payload)
    {
        if (!StringComparer.Ordinal.Equals(dispatch.DispatchId, payload.DispatchId) ||
            !StringComparer.Ordinal.Equals(dispatch.ParentWorkflowExecutionId, payload.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(dispatch.ParentActivityExecutionId, payload.ParentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(dispatch.ChildWorkflowExecutionId, payload.ChildWorkflowExecutionId) ||
            dispatch.Mode != WorkflowDispatchMode.WaitForCompletion ||
            !WorkflowDispatchLifecycle.IsCancellationPropagationEnabled(dispatch))
        {
            throw new InvalidOperationException($"Workflow dispatch '{payload.DispatchId}' conflicts with child-cancel responsibility.");
        }
    }
}
