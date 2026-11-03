using System.Text.Json;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Validates exact terminal control before claiming a trigger or activating the ordinary waiting leaf.</summary>
internal sealed class DispatchWorkflowResumeGuard(IRuntimeChildControlAuthorizer authorizer,
    IPostCommitOutboxLookupStore outbox, IWorkflowDispatchStore dispatches) : IEventHandler<ActivityResumeValidating>
{
    public async Task Handle(ActivityResumeValidating @event, CancellationToken cancellationToken)
    {
        var state = @event.State;
        var node = @event.Node;
        var payload = @event.Payload;
        if (node.ActivityType != DispatchWorkflowConstants.ActivityType) return;
        var identity = new WorkflowDispatchIdentity(state.Execution.WorkflowExecutionId, state.InvocationId);
        string? root = null;
        try
        {
            var capability = WorkflowDispatchControlPolicy.ReadCapability(state.Provenance.Metadata);
            if (capability is null && !ContainsControl(payload.Input)) return;
            var message = payload.Input?.Deserialize<WorkflowDispatchParentResumePayload>(WorkflowControlTransport.Json)
                ?? throw WorkflowDispatchControlPolicy.Failure("terminal resume payload is missing");
            if (message.ParentWorkflowExecutionId != state.Execution.WorkflowExecutionId || message.ParentActivityExecutionId != state.InvocationId ||
                message.DispatchId != identity.DispatchId || message.ChildWorkflowExecutionId != identity.ChildWorkflowExecutionId ||
                message.BookmarkId != identity.WaitBookmarkId || payload.BookmarkId != identity.WaitBookmarkId ||
                message.StimulusType != DispatchWorkflowConstants.WaitStimulusType || message.StimulusHash != identity.WaitStimulusHash)
                throw WorkflowDispatchControlPolicy.Failure("terminal resume differs from its exact local call");
            // Admission may fail before a root exists. Authenticate that ordinary failure against its
            // generation-scoped committed receipt; it cannot invent an authored root/control barrier.
            if (message.Result.Status == WorkflowDispatchStatus.DispatchFailed && message.Result.Control is null)
            {
                await ValidateFailedAdmissionAsync(state, message, identity, capability, cancellationToken);
                return;
            }
            if (message.Result.Control is not { } control)
                throw WorkflowDispatchControlPolicy.Failure("admitted control resume omitted its terminal barrier");
            root = control.Batch.RootActivityExecutionId;
            await authorizer.ValidateAsync(control, cancellationToken);
            var source = await outbox.FindAsync(control.SourceOutboxItemId, cancellationToken)
                ?? throw WorkflowDispatchControlPolicy.Failure("terminal resume source is missing");
            if (source.OutboxItemId != control.SourceOutboxItemId || source.Intent.Payload is not { } committed ||
                !JsonElement.DeepEquals(committed, JsonSerializer.SerializeToElement(message, WorkflowControlTransport.Json)))
                throw WorkflowDispatchControlPolicy.Failure("terminal resume result differs from its immutable source");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException exception)
        {
            throw Unavailable(new WorkflowControlBoundaryException(identity.ChildWorkflowExecutionId, root, exception, isUnavailable: true));
        }
        catch (WorkflowControlBoundaryException exception) when (exception.IsUnavailable) { throw Unavailable(exception); }
        catch (WorkflowControlBoundaryException) { throw; }
        catch (Exception exception)
        {
            var failure = WorkflowDispatchControlPolicy.Wrap(identity.ChildWorkflowExecutionId, root, exception);
            if (failure.IsUnavailable) throw Unavailable(failure);
            throw failure;
        }
    }

    private async ValueTask ValidateFailedAdmissionAsync(ActivityExecutionState state,
        WorkflowDispatchParentResumePayload message, WorkflowDispatchIdentity identity,
        Elsa.Workflows.Runtime.Core.Attributes.ActivityChildControlConsumerAttribute? capability,
        CancellationToken cancellationToken)
    {
        var dispatch = await dispatches.FindAsync(identity.DispatchId, cancellationToken)
            ?? throw WorkflowDispatchControlPolicy.Failure("failed admission dispatch is missing");
        var declared = WorkflowDispatchControlPolicy.ReadCapability(dispatch.Metadata);
        if (capability is null || declared is null || capability.Key != declared.Key || capability.Version != declared.Version ||
            dispatch.Status != WorkflowDispatchStatus.DispatchFailed || dispatch.Mode != WorkflowDispatchMode.WaitForCompletion ||
            dispatch.ParentWorkflowExecutionId != state.Execution.WorkflowExecutionId ||
            dispatch.ParentActivityExecutionId != state.InvocationId || dispatch.ChildWorkflowExecutionId != identity.ChildWorkflowExecutionId)
            throw WorkflowDispatchControlPolicy.Failure("failed admission differs from its exact opted-in waited call");
        var itemId = identity.WaitFailureResumeOutboxItemId(WorkflowDispatchLifecycle.ReadDeliveryGeneration(dispatch));
        var source = await outbox.FindAsync(itemId, cancellationToken)
            ?? throw WorkflowDispatchControlPolicy.Failure("failed admission resume source is missing");
        var intent = source.Intent;
        if (source.OutboxItemId != itemId || intent.IntentId != identity.ParentResumeIntentId ||
            intent.Kind != DispatchWorkflowConstants.ResumeParentIntentKind ||
            intent.WorkflowExecutionId != state.Execution.WorkflowExecutionId || intent.ActivityExecutionId != state.InvocationId ||
            intent.IdempotencyKey != identity.ParentResumeIdempotencyKey || intent.Payload is not { } committed ||
            !JsonElement.DeepEquals(committed, JsonSerializer.SerializeToElement(message, WorkflowControlTransport.Json)))
            throw WorkflowDispatchControlPolicy.Failure("failed admission result differs from its immutable source");
    }

    private static RuntimeSchedulerWorkUnavailableException Unavailable(WorkflowControlBoundaryException cause) =>
        new("runtime.child-control.source-unavailable", "The exact child-control source could not be read; resume remains owed.", cause);

    private static bool ContainsControl(JsonElement? input)
    {
        if (input is not { ValueKind: JsonValueKind.Object } value) return false;
        foreach (var property in value.EnumerateObject())
            if (property.Name.Equals("result", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Object)
                foreach (var result in property.Value.EnumerateObject())
                    if (result.Name.Equals("control", StringComparison.OrdinalIgnoreCase) && result.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                        return true;
        return false;
    }
}
