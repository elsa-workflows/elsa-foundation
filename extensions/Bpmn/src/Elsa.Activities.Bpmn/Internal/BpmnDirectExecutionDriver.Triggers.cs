using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

internal sealed partial class BpmnDirectExecutionDriver
{
    private async ValueTask ValidateCommittedTriggerAsync(RuntimeModelExecutionContext context, CancellationToken cancellationToken)
    {
        var start = ReadStart(context);
        var accepted = await (ingress ?? throw new NotSupportedException("Trigger model starts require the physical committed ingress seal reader."))
            .ReadCommittedRecipientAsync(start.Origin!, cancellationToken);
        var recipient = accepted.Recipient;
        var sealedCommand = accepted.Seal.Command.Deserialize<RuntimeModelExecutionCommand>(Web)
            ?? throw new BpmnModelValidationException("The committed model dispatch command is missing.");
        if (accepted.Seal.CommandProtocol != BpmnModelProtocol.Key || accepted.Seal.CommandProtocolVersion != BpmnModelProtocol.Version ||
            !recipient.IsEquivalentTo(start.Trigger) || recipient.StartIdentity.WorkflowExecutionId != context.WorkItem.WorkflowExecutionId ||
            recipient.StartIdentity.InputId != context.Command.InputId || recipient.StartIdentity.Scope != context.Scope ||
            recipient.ModelKey != "bpmn" || recipient.ModelProtocol != context.Material.Model!.Protocol || recipient.ModelProfile != context.Material.Model.Profile ||
            recipient.MaterialIncarnation != context.Material.IncarnationId || recipient.TenantId != context.Command.TenantId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(recipient.Material, context.Material.Identity) ||
            !WorkflowExecutionAuthoritySnapshot.Matches(context.Command.Authority, recipient.Authority.SystemIdentity, recipient.Authority.RootInitiator, recipient.Authority.Metadata) ||
            !SameInputs(recipient.Inputs, start.Inputs!) || recipient.SourceProvenance.SourceReferenceId != start.SourceReferenceId ||
            recipient.InputContractFingerprint != RuntimeIngressAdmission.ComputeInputContractFingerprint(context.Material.Model.InputContract) ||
            RuntimeCheckpointCommitFingerprint.ComputeModelInput(sealedCommand) != RuntimeCheckpointCommitFingerprint.ComputeModelInput(context.Command))
            throw new BpmnModelValidationException("The model start differs from its actual immutable committed ingress seal.");
    }
}
