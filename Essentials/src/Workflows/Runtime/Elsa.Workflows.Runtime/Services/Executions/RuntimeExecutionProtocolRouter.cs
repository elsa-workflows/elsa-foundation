using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Scheduler;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>Default native adapter plus the opt-in model path over the existing queue, claims and lease runner.</summary>
internal sealed class RuntimeExecutionProtocolRouter(RuntimeExecutionProtocolSelector selector,
    WorkflowSchedulerCommandRouter native, IWorkflowSchedulerWorkQueue queue, IWorkflowSchedulerDrainer drainer,
    IRuntimeExecutionOwnershipService ownership, IRuntimeExecutionOwnershipContextAccessor ownershipContext,
    IPersistenceOperationScopeFactory scopes, TimeProvider timeProvider, IWorkflowExecutableRootWriteLeaseManager materialLeases,
    IRuntimeAdmissionController? admissionController = null)
{
    private readonly RuntimeExecutionLeaseRunner leaseRunner = new(ownership, ownershipContext, scopes, timeProvider);

    public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope,
        WorkflowExecutionCommandDispatchOptions options, CancellationToken cancellationToken)
    {
        if (!await selector.IsModelAsync(envelope, cancellationToken))
            return await native.ProcessAsync(envelope, options, cancellationToken);
        if (envelope.Command.Kind is not (WorkflowExecutionCommandKind.ExecuteModel or WorkflowExecutionCommandKind.RunSchedulerWork))
            throw new RuntimeSchedulerWorkUnavailableException("runtime.model.input-kind", "This command cannot advance the selected model execution.");
        if (!queue.SupportsClaimTransitions || !queue.SupportsClaimValidation)
            throw new NotSupportedException("Model execution requires an actual queue with fenced, validated claims.");

        using var admission = envelope.Command.Kind == WorkflowExecutionCommandKind.ExecuteModel ? admissionController?.TryAdmit() : null;
        if (admission is { IsAdmitted: false })
            return WorkflowExecutionCommandProcessResult.FromShed(admission.Reason!, admission.RetryAfter, workQueued: false);
        if (envelope.Command.Kind == WorkflowExecutionCommandKind.ExecuteModel)
        {
            var input = envelope.Command.Payload?.Deserialize<RuntimeModelExecutionCommand>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("A model command requires its typed payload.");
            var work = new RuntimeSchedulerWorkItem(RuntimeModelExecutionCommand.CreateSchedulerWorkItemId(input.InputId), envelope.WorkflowExecutionId,
                envelope.Command.CommandId, envelope.Command.Kind, envelope.EnvelopeId, envelope.IdempotencyKey,
                envelope.EnqueuedAt, timeProvider.GetUtcNow(), envelope.Sequence, envelope.Command.Payload,
                envelope.Command.Metadata, envelope.Metadata);
            var selected = await selector.SelectAsync(work, cancellationToken);
            await selected.Driver.ValidateInputAsync(selected.Context, cancellationToken);
            RuntimeSchedulerWorkItem? queued = null;
            await materialLeases.ExecuteAsync(selected.Context.Material.Identity,
                "model-admission:" + work.WorkItemId, async token =>
                {
                    await selector.SelectAsync(work, token);
                    queued = await queue.EnqueueAsync(work, token);
                }, cancellationToken);
            var retained = await selector.SelectAsync(queued!, cancellationToken);
            if (RuntimeCheckpointCommitFingerprint.ComputeModelInput(selected.Context.Command) !=
                RuntimeCheckpointCommitFingerprint.ComputeModelInput(retained.Context.Command))
                throw new RuntimeCheckpointReplayConflictException(selected.Context.Command.InputId);
        }
        // Recovery never adds a native RunSchedulerWork item to the model's durable input stream.
        var result = await leaseRunner.RunAsync(envelope.WorkflowExecutionId, envelope.Partition,
            token => drainer.DrainAsync(new(envelope.WorkflowExecutionId, ambientServices: options.AmbientServices), token), cancellationToken);
        return WorkflowExecutionCommandProcessResult.FromDrain(result);
    }

}
