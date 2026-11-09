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
    IWorkflowSchedulerWorkQueue queue, IWorkflowExecutionActorProvider actors,
    TimeProvider clock, IRuntimeModelExecutionReader? models = null) : IRuntimePostCommitDurableSourceIntentHandler
{
    public async ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        try { await HandleCoreAsync(source, cancellationToken); }
        catch (JsonException exception) { throw InvalidOwnedJson(source, exception); }
        catch (BpmnModelValidationException exception) when (exception.InnerException is JsonException cause)
        { throw InvalidOwnedJson(source, cause); }
    }

    private static BpmnModelValidationException InvalidOwnedJson(PostCommitDeliveryContext source, JsonException exception) =>
        new($"BPMN delivery '{source.Intent.Kind}' for execution '{source.Intent.WorkflowExecutionId}', " +
            $"outbox source '{source.SourceOutboxItemId}' in scope '{source.Scope.Value}' has invalid owned JSON.", exception);

    private async ValueTask HandleCoreAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        await boundary.RevalidateSourceAsync(source, cancellationToken);
        var reader = models ?? throw new NotSupportedException("Native BPMN work requires qualified model state persistence.");
        if (actors is not InProcessWorkflowExecutionActorProvider)
            throw new NotSupportedException("This native BPMN binding requires the qualified built-in actor provider.");
        if (source.Intent.Kind == BpmnNativeWorkBoundary.CancelKind)
        {
            var cancellation = source.Intent.Payload?.Deserialize<BpmnNativeWorkCancellation>(BpmnNativeWorkBoundary.Web)
                ?? throw new BpmnModelValidationException("The native work cancellation address is missing.");
            var model = await boundary.RequireCancellationAsync(source, cancellation, cancellationToken);
            var origin = cancellation.Origin;
            var worker = await boundary.FindWorkerAsync(origin, cancellationToken);
            if (worker is null)
            {
                var retained = BpmnDirectExecutionDriver.Read(model).Cancellations!.Single(item => origin.IsEquivalentTo(item.Origin));
                if (!await boundary.HasRetirementReceiptAsync(model, retained, cancellationToken) ||
                    (await boundary.RequireStartSourceAsync(origin, cancellationToken)).Status != RuntimePostCommitOutboxStatus.Delivered)
                    throw Pending("bpmn.native-cancel.admission-pending", "The original native start and its exact retirement receipt remain owed.");
                return;
            }
            if (worker.TenantId != model.TenantId || worker.Authority is null ||
                !WorkflowExecutionAuthoritySnapshot.Matches(model.Authority, worker.Authority.SystemIdentity,
                    worker.Authority.RootInitiator, worker.Authority.Metadata))
                throw new BpmnModelValidationException("The native cancellation worker has different execution authority.");
            if (!worker.Status.IsTerminal())
            {
                if (!queue.SupportsClaimTransitions || !queue.SupportsClaimValidation)
                    throw new NotSupportedException("Native BPMN cancellation requires the qualified physical input queue.");
                var id = "bpmn-native-cancel:" + BpmnModelIdentity.Create(source.SourceOutboxItemId, origin.WorkerExecutionId);
                await queue.EnqueueAsync(new(id, origin.WorkerExecutionId, id, WorkflowExecutionCommandKind.Cancel,
                    id, id, source.Intent.RecordedAt, clock.GetUtcNow()), cancellationToken);
                throw Pending("bpmn.native-cancel.pending", "Cancellation of the original native worker remains owed.");
            }
            var document = BpmnDirectExecutionDriver.Read(model);
            var terminalResult = document.NativeResults?.SingleOrDefault(item => origin.IsEquivalentTo(item.Origin));
            var receipt = await reader.FindInputAsync(model.ExecutionId, origin.ResultInputId, cancellationToken);
            if (document.Cancellations?.Single(item => origin.IsEquivalentTo(item.Origin)).Settled != true ||
                terminalResult is null || terminalResult.Status != worker.Status || receipt is null || receipt.Result.GetProperty("status").GetString() != "Accepted" ||
                receipt.ContentFingerprint != RuntimeCheckpointCommitFingerprint.ComputeModelInput(new(model.Executable,
                    model.MaterialIncarnationId, origin.ResultInputId, "native-result",
                    JsonSerializer.SerializeToElement(terminalResult, BpmnNativeWorkBoundary.Web), model.Authority, model.TenantId)))
                throw Pending("bpmn.native-cancel.result-pending", "The actual native terminal result has not settled the model cancellation.");
            return;
        }
        if (source.Intent.Kind == BpmnNativeWorkBoundary.StartKind)
        {
            var origin = source.Intent.Payload?.Deserialize<RuntimeModelNativeWorkOrigin>(BpmnNativeWorkBoundary.Web)
                ?? throw new BpmnModelValidationException("The native work start address is missing.");
            if (origin.SourceOutboxItemId != source.SourceOutboxItemId || origin.SourceIntentId != source.Intent.IntentId ||
                origin.Scope != source.Scope || origin.ExecutionId != source.Intent.WorkflowExecutionId)
                throw new BpmnModelValidationException("The native work start does not belong to this actual source.");
            if (await boundary.FindWorkerAsync(origin, cancellationToken) is not null) return;
            if (await boundary.FindCancellationAsync(origin, cancellationToken) is { } cancelled)
            {
                if (await boundary.HasRetirementReceiptAsync(cancelled.Model, cancelled.Cancellation, cancellationToken)) return;
                await boundary.RequireNoQueuedStartAsync(origin, cancellationToken);
                await QueueModelAsync(BpmnNativeWorkBoundary.RetirementCommand(cancelled.Model, cancelled.Cancellation),
                    cancelled.Model.ExecutionId, source, cancellationToken);
                throw Pending("bpmn.native-start.retirement-pending", "The exact cancelled native start awaits model retirement consumption.");
            }
            var retainedModel = await reader.FindAsync(origin.ExecutionId, cancellationToken);
            if (retainedModel is not null && origin.MatchesModel(retainedModel) &&
                retainedModel.TerminalResult?.GetProperty("status").GetString() == "Faulted" &&
                BpmnDirectExecutionDriver.Read(retainedModel).Work.Any(work => origin.IsEquivalentTo(work.NativeWork)))
            {
                // Recover the original accepted start through the ordinary model decision queue.
                // Its source stays owed until cancellation and positive retirement/result consumption settle it.
                var cleanup = new RuntimeModelExecutionCommand(retainedModel.Executable, retainedModel.MaterialIncarnationId,
                    "settle-faulted-work:" + BpmnModelIdentity.Create(retainedModel.ExecutionId), "cancel",
                    JsonSerializer.SerializeToElement(new { reason = "Settle native work retained by the final model fault." }, BpmnNativeWorkBoundary.Web),
                    retainedModel.Authority, retainedModel.TenantId);
                await QueueModelAsync(cleanup, retainedModel.ExecutionId, source, cancellationToken);
                throw Pending("bpmn.native-start.fault-settlement-pending", "The faulted model's original native work remains owed until its cancellation decision.");
            }
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
        var current = await reader.FindAsync(result.Origin.ExecutionId, cancellationToken)
            ?? throw BpmnNativeWorkBoundary.Unavailable("The destination model execution is unavailable.");
        if (!result.Origin.MatchesModel(current)) throw new BpmnModelValidationException("The native result model pin changed.");
        var inputId = BpmnNativeWorkBoundary.ResultInputId(result.Origin);
        var command = new RuntimeModelExecutionCommand(current.Executable, current.MaterialIncarnationId, inputId, "native-result",
            JsonSerializer.SerializeToElement(result, BpmnNativeWorkBoundary.Web), current.Authority, current.TenantId);
        var fingerprint = RuntimeCheckpointCommitFingerprint.ComputeModelInput(command);
        if (await reader.FindInputAsync(current.ExecutionId, inputId, cancellationToken) is { } accepted)
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

    private async ValueTask QueueModelAsync(RuntimeModelExecutionCommand command, string executionId,
        PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        if (!queue.SupportsClaimTransitions || !queue.SupportsClaimValidation)
            throw new NotSupportedException("Native start retirement requires the qualified protected model input queue.");
        var envelope = "bpmn-retire-native:" + BpmnModelIdentity.Create(executionId, command.InputId);
        await queue.EnqueueAsync(new(RuntimeModelExecutionCommand.CreateSchedulerWorkItemId(command.InputId), executionId, command.InputId,
            WorkflowExecutionCommandKind.ExecuteModel, envelope, envelope, source.Intent.RecordedAt, clock.GetUtcNow(),
            payload: JsonSerializer.SerializeToElement(command, BpmnNativeWorkBoundary.Web)), cancellationToken);
    }
}
