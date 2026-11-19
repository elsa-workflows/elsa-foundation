using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Retries one exact nonterminal source until caller inbox acceptance or durable target retirement.</summary>
internal sealed class WorkflowControlNotificationExecutor(IPostCommitOutboxLookupStore outbox,
    IRuntimeChildControlAuthorizer authorizer, IWorkflowExecutionStateStore workflows,
    IActivityExecutionStateStore activities, IWorkflowExecutionActorProvider actors) : IRuntimePostCommitIntentHandler
{
    public async ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        RuntimeChildControlTransport? control = null;
        try
        {
            if (intent.Kind != WorkflowControlTransport.NotifyIntentKind || intent.Payload is null)
                throw WorkflowDispatchControlPolicy.Failure("notification intent kind or payload is missing");
            control = intent.Payload.Value.Deserialize<RuntimeChildControlTransport>(WorkflowControlTransport.Json)
                ?? throw WorkflowDispatchControlPolicy.Failure("notification transport is missing");
            if (control.Batch.IsTerminal || !WorkflowControlTransport.Notification(control, intent.RecordedAt).IsEquivalentTo(intent))
                throw WorkflowDispatchControlPolicy.Failure("notification does not match its deterministic nonterminal source");
            var source = await outbox.FindAsync(control.SourceOutboxItemId, cancellationToken)
                ?? throw WorkflowDispatchControlPolicy.Failure("notification source outbox row is missing");
            if (source.OutboxItemId != control.SourceOutboxItemId || !source.Intent.IsEquivalentTo(intent))
                throw WorkflowDispatchControlPolicy.Failure("notification differs from its immutable source row");

            var parent = await workflows.FindAsync(control.ParentWorkflowExecutionId, cancellationToken);
            if (parent is null)
            {
                if (await workflows.IsDeletionTombstonedAsync(control.ParentWorkflowExecutionId, cancellationToken)) return;
                throw new ChildControlDeliveryDeferredException();
            }
            if (parent.Status.IsTerminal()) return;
            var consumer = await activities.FindAsync(control.ParentWorkflowExecutionId, control.ConsumerActivityExecutionId, cancellationToken)
                ?? throw new ChildControlDeliveryDeferredException();
            if (consumer.Status != ActivityExecutionStatus.Running) return;
            var call = await activities.FindAsync(control.ParentWorkflowExecutionId, control.CallingActivityExecutionId, cancellationToken)
                ?? throw new ChildControlDeliveryDeferredException();
            if (call.Status is ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Superseded) return;
            await authorizer.ValidateAsync(control, cancellationToken);
            if (call.ChildControlInbox?.HasAccepted(control) == true) return;

            var payload = new RuntimeChildControlCommandPayload(control.ParentExecutable, consumer.Execution.ExecutableNodeId,
                consumer.InvocationId, consumer.ParentActivityExecutionId, consumer.BranchId, control);
            var metadata = new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.ActivityExecutionId] = consumer.InvocationId,
                [RuntimeMetadataKeys.DispatchId] = control.DispatchId,
                [RuntimeMetadataKeys.ChildWorkflowExecutionId] = control.Batch.ChildWorkflowExecutionId
            };
            var deliveryId = "child-control:v1:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(control.SourceOutboxItemId)));
            var command = new WorkflowExecutionCommand(deliveryId, control.ParentWorkflowExecutionId,
                WorkflowExecutionCommandKind.DeliverChildControl, intent.RecordedAt,
                JsonSerializer.SerializeToElement(payload), metadata);
            var envelope = new WorkflowExecutionCommandEnvelope(deliveryId, control.ParentWorkflowExecutionId, command,
                deliveryId, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, intent.RecordedAt, metadata: metadata,
                partition: parent.Partition);
            var actor = await actors.GetAgentAsync(new WorkflowExecutionActorActivationRequest(control.ParentWorkflowExecutionId,
                WorkflowExecutionActorActivationReason.ControlPlaneCommand, intent.RecordedAt, "runtime.dispatch-workflow-control",
                WorkflowExecutionActorCapabilities.None, metadata, parent.Partition), cancellationToken);
            await actor.EnqueueAsync(envelope, cancellationToken);

            parent = await workflows.FindAsync(control.ParentWorkflowExecutionId, cancellationToken);
            consumer = await activities.FindAsync(control.ParentWorkflowExecutionId, control.ConsumerActivityExecutionId, cancellationToken);
            call = await activities.FindAsync(control.ParentWorkflowExecutionId, control.CallingActivityExecutionId, cancellationToken);
            if (parent?.Status.IsTerminal() == true || consumer is { Status: not ActivityExecutionStatus.Running } ||
                call?.Status is ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Superseded ||
                call?.ChildControlInbox?.HasAccepted(control) == true)
                return;
            throw new ChildControlDeliveryDeferredException();
        }
        catch (OperationCanceledException) { throw; }
        catch (ChildControlDeliveryDeferredException) { throw; }
        catch (WorkflowControlBoundaryException) { throw; }
        catch (Exception exception)
        {
            throw new WorkflowControlBoundaryException(control?.Batch.ChildWorkflowExecutionId ?? intent.WorkflowExecutionId,
                control?.Batch.RootActivityExecutionId, exception);
        }
    }
}

internal sealed class ChildControlDeliveryDeferredException() : Exception("Exact caller inbox acceptance is not yet committed."), IRuntimePostCommitDeferral;
