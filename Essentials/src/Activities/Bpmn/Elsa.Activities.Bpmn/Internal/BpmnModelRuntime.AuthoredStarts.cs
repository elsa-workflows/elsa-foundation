using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

internal sealed partial class BpmnModelRuntime
{
    private async ValueTask<BpmnModelCommandOutcome> StartAuthoredAsync(string execution, string requestId,
        WorkflowExecutableSourceReference source, RuntimeExecutionMaterial material,
        IReadOnlyDictionary<string, JsonElement>? submitted, BpmnModelCaller caller, CancellationToken cancellationToken)
    {
        if (authoredAdmission is null || activationSlots is null || startPolicy is null || inputValidator is null)
            throw new NotSupportedException("Authored BPMN starts require protected slot/source/queue admission and the ordinary start policy/input validator.");
        if (!driver.AdmitNewExecutions)
            throw new RuntimeSchedulerWorkUnavailableException("bpmn.admission-disabled", "New BPMN execution admission is disabled.");
        var slot = (await activationSlots.ListByDefinitionAsync(source.DefinitionId, cancellationToken))
            .SingleOrDefault(item => item.SlotId == source.SlotId);
        if (slot is null || source.ActivationId is null || slot.ActiveActivationId != source.ActivationId ||
            slot.Source?.IsSameOwnerAs(WorkflowActivationSource.Publishing) != true)
            throw new BpmnModelResourceMissingException();
        var supplied = submitted ?? new Dictionary<string, JsonElement>();
        var validation = inputValidator.Validate(material.Model!.InputContract, supplied);
        BpmnInputSupport.Check(validation);
        var authority = new WorkflowExecutionAuthoritySnapshot("bpmn-runtime", caller.ActorId);
        var decision = await startPolicy.EvaluateAsync(new(material.Identity, WorkflowExecutableStartAuthorityKind.LiveReference,
            authority.SystemIdentity, authority, WorkflowRunKind.PublishedRun, caller.TenantId, new(Scope(caller).Value)), cancellationToken);
        if (!decision.IsAllowed) throw new WorkflowExecutableStartRejectedException(WorkflowExecutableStartRejectionCategory.Policy,
            decision.ReasonCode!, decision.Message!);
        var payload = JsonSerializer.SerializeToElement(new
        {
            inputs = validation.NormalizedInputs,
            sourceReferenceId = source.SourceReferenceId,
            authored = new AuthoredModelEntry(1, slot, source),
            submittedInputs = supplied
        }, Web);
        var command = new RuntimeModelExecutionCommand(material.Identity, material.IncarnationId, requestId,
            BpmnDirectExecutionDriver.AuthoredKind, payload, authority, caller.TenantId);
        try
        {
            var result = await authoredAdmission.AdmitAsync(new(slot, source, material, execution, command, clock.GetUtcNow()), cancellationToken);
            if (result.IsShed || result.Status is WorkflowExecutionCommandDispatchStatus.Rejected or WorkflowExecutionCommandDispatchStatus.Deferred or
                WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted)
                throw new RuntimeSchedulerWorkUnavailableException("bpmn.admission-unavailable", "The BPMN command could not be admitted for execution.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            if (await FindAuthoredAdmissionAsync(execution, command, caller, cancellationToken) is { } accepted) return accepted;
            throw;
        }
        return await FindAuthoredAdmissionAsync(execution, command, caller, cancellationToken)
            ?? throw new RuntimeSchedulerWorkUnavailableException("bpmn.authored-admission-unconfirmed",
                "No exact queue/state/input receipt proves authored model admission.");
    }

    private async ValueTask<BpmnModelCommandOutcome?> FindAuthoredAdmissionAsync(string execution,
        RuntimeModelExecutionCommand command, BpmnModelCaller caller, CancellationToken cancellationToken)
    {
        var fingerprint = RuntimeCheckpointCommitFingerprint.ComputeModelInput(command);
        // Queue first, then receipt: consumption between these reads cannot create an acknowledgement gap.
        if (await PendingAsync(execution, command.InputId, caller, cancellationToken) is { } pending)
        {
            if (RuntimeCheckpointCommitFingerprint.ComputeModelInput(pending) != fingerprint)
                throw new RuntimeCheckpointReplayConflictException(command.InputId);
            return new(execution, command.InputId, false, true, null);
        }
        if (await States.FindInputAsync(execution, command.InputId, cancellationToken) is { } receipt)
        {
            if (receipt.ContentFingerprint != fingerprint) throw new RuntimeCheckpointReplayConflictException(command.InputId);
            return new(execution, command.InputId, false, false, receipt.Result);
        }
        return null;
    }

    private static void ValidateAuthoredReplay(RuntimeModelExecutionCommand accepted, string sourceReferenceId,
        IReadOnlyDictionary<string, JsonElement>? supplied)
    {
        var payload = accepted.Payload;
        var original = payload.GetProperty("submittedInputs").Deserialize<Dictionary<string, JsonElement>>(Web)!;
        if (payload.GetProperty("sourceReferenceId").GetString() != sourceReferenceId ||
            !BpmnDirectExecutionDriver.SameInputs(original, supplied ?? new Dictionary<string, JsonElement>()))
            throw new RuntimeCheckpointReplayConflictException(accepted.InputId);
    }
}
