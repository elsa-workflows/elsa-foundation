using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Transports existing model/native outbox responsibility through the shared native start and model queues.</summary>
internal sealed class BpmnNativeWorkDelivery(BpmnNativeWorkBoundary boundary, IWorkflowStartDispatcher starts,
    IRuntimeModelExecutionReader models, IWorkflowSchedulerWorkQueue queue, IWorkflowExecutionActorProvider actors,
    TimeProvider clock) : IRuntimePostCommitDurableSourceIntentHandler
{
    public async ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        await boundary.RevalidateSourceAsync(source, cancellationToken);
        if (actors is not InProcessWorkflowExecutionActorProvider)
            throw new NotSupportedException("This native BPMN binding requires the qualified built-in actor provider.");
        if (source.Intent.Kind == BpmnNativeWorkBoundary.StartKind)
        {
            var origin = source.Intent.Payload?.Deserialize<RuntimeModelNativeWorkOrigin>(BpmnNativeWorkBoundary.Web)
                ?? throw new BpmnModelValidationException("The native work start address is missing.");
            if (origin.SourceOutboxItemId != source.SourceOutboxItemId || origin.SourceIntentId != source.Intent.IntentId ||
                origin.Scope != source.Scope || origin.ExecutionId != source.Intent.WorkflowExecutionId)
                throw new BpmnModelValidationException("The native work start does not belong to this actual source.");
            if (await boundary.FindWorkerAsync(origin, cancellationToken) is not null) return;
            var model = await boundary.RequireLiveAsync(origin, cancellationToken);
            var identity = BpmnNativeWorkBoundary.WorkerIdentity(origin.Scope, origin.ExecutionId, origin.InvocationId, origin.AttemptId, origin.WorkerExecutable);
            if (identity.WorkflowExecutionId != origin.WorkerExecutionId)
                throw new BpmnModelValidationException("The native worker identity is not deterministic for this model attempt.");
            await starts.DispatchAsync(new WorkflowExecutionStartDispatchRequest(origin.WorkerExecutable.ArtifactId, model.Authority.SystemIdentity,
                origin.WorkerExecutionId, identity.StartKey, null, null, null, null, null, WorkflowRunKind.PublishedRun,
                null, WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy, null, null, model.TenantId,
                new(origin.Scope.Value), model.Authority, WorkflowExecutableStartAuthority.FromModelWork(origin)),
                cancellationToken: cancellationToken);
            if (await boundary.FindWorkerAsync(origin, cancellationToken) is null)
                throw Pending("bpmn.native-start.pending", "Native worker admission remains owed.");
            return;
        }
        if (source.Intent.Kind != BpmnNativeWorkBoundary.ResultKind)
            throw new BpmnModelValidationException("The native work delivery kind is unsupported.");
        var result = source.Intent.Payload?.Deserialize<BpmnNativeWorkResult>(BpmnNativeWorkBoundary.Web)
            ?? throw new BpmnModelValidationException("The native result is missing.");
        if (result.SourceOutboxItemId != source.SourceOutboxItemId || result.Origin.WorkerExecutionId != source.Intent.WorkflowExecutionId ||
            result.Origin.Scope != source.Scope)
            throw new BpmnModelValidationException("The native result does not belong to this actual terminal source.");
        var current = await models.FindAsync(result.Origin.ExecutionId, cancellationToken)
            ?? throw BpmnNativeWorkBoundary.Unavailable("The destination model execution is unavailable.");
        if (!result.Origin.MatchesModel(current)) throw new BpmnModelValidationException("The native result model pin changed.");
        var inputId = BpmnNativeWorkBoundary.ResultInputId(result.Origin);
        var command = new RuntimeModelExecutionCommand(current.Executable, current.MaterialIncarnationId, inputId, "native-result",
            JsonSerializer.SerializeToElement(result, BpmnNativeWorkBoundary.Web), current.Authority, current.TenantId);
        var fingerprint = RuntimeCheckpointCommitFingerprint.ComputeModelInput(command);
        if (await models.FindInputAsync(current.ExecutionId, inputId, cancellationToken) is { } accepted)
        {
            if (accepted.ContentFingerprint != fingerprint) throw new RuntimeCheckpointReplayConflictException(inputId);
            return;
        }
        if (!queue.SupportsClaimTransitions || !queue.SupportsClaimValidation)
            throw new NotSupportedException("Native BPMN results require the qualified physical model input queue.");
        var envelope = "bpmn-native-result:" + BpmnModelIdentity.Create(current.ExecutionId, inputId);
        // Enqueue without recursively draining an actor that may still own the model while it dispatches
        // a synchronous worker. Ordinary runtime resumption consumes this exact protected model input.
        await queue.EnqueueAsync(new(RuntimeModelExecutionCommand.CreateSchedulerWorkItemId(inputId), current.ExecutionId, inputId,
            WorkflowExecutionCommandKind.ExecuteModel, envelope, envelope, source.Intent.RecordedAt, clock.GetUtcNow(),
            payload: JsonSerializer.SerializeToElement(command, BpmnNativeWorkBoundary.Web)), cancellationToken);
        throw Pending("bpmn.native-result.pending", "The native result is durably queued and remains owed until model consumption.");
    }

    private static RuntimePostCommitDeliveryException Pending(string code, string message) =>
        new(PostCommitFailureKind.Transient, code, message);
}
