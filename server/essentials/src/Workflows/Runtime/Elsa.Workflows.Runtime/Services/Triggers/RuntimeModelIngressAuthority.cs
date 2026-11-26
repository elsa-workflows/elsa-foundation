using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Protocol-neutral proof for the new sealed model root; legacy model starts retain their existing tenant policy.</summary>
internal static class RuntimeModelIngressAuthority
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    internal static RuntimeIngressCommandOrigin? ReadOrigin(RuntimeModelExecutionCommand command)
    {
        if (command.InputKind != "start-trigger-v1") return command.IngressOrigin;
        if (command.Payload is not { ValueKind: JsonValueKind.Object } payload || !payload.TryGetProperty("origin", out var origin))
            throw new InvalidDataException("A standalone model start requires its typed saved-ingress origin.");
        var typed = origin.Deserialize<RuntimeIngressCommandOrigin>(Web) ?? throw new InvalidDataException("The standalone model origin is missing.");
        if (!typed.IsEquivalentTo(command.IngressOrigin)) throw new InvalidDataException("The typed model origin differs from its canonical start payload.");
        return typed;
    }

    internal static void ValidateInitialCommand(RuntimeModelExecutionCommand command, PersistenceScope scope, string executionId,
        RuntimeExecutionMaterial material, RuntimeCommittedRecipient accepted)
    {
        var origin = command.InputKind == "start-trigger-v1" ? ReadOrigin(command) : null;
        if (origin is null) throw new InvalidDataException("The model command is not a sealed standalone root.");
        var recipient = accepted.Recipient;
        var sealedCommand = accepted.Seal.Command.Deserialize<RuntimeModelExecutionCommand>(Web) ??
            throw new InvalidDataException("The actual model dispatch seal has no canonical model command.");
        if (origin.Scope != scope || recipient.StartIdentity.Scope != scope || recipient.StartIdentity.WorkflowExecutionId != executionId ||
            recipient.StartIdentity.InputId != command.InputId || recipient.TenantId != command.TenantId ||
            material.Model is not { } model || recipient.ModelProtocol != model.Protocol || recipient.ModelProfile != model.Profile ||
            recipient.InputContractFingerprint != RuntimeIngressAdmission.ComputeInputContractFingerprint(model.InputContract) ||
            recipient.MaterialIncarnation != material.IncarnationId || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(recipient.Material, material.Identity) ||
            accepted.Seal.CommandProtocol != model.Protocol.Key || accepted.Seal.CommandProtocolVersion != model.Protocol.Version ||
            !WorkflowExecutionAuthoritySnapshot.Matches(command.Authority, recipient.Authority.SystemIdentity, recipient.Authority.RootInitiator, recipient.Authority.Metadata) ||
            !origin.IsEquivalentTo(ReadOrigin(sealedCommand)) || RuntimeCheckpointCommitFingerprint.ComputeModelInput(command) != RuntimeCheckpointCommitFingerprint.ComputeModelInput(sealedCommand))
            throw new InvalidDataException("The model command differs from its actual physical ingress seal, material or tenant.");
    }

    internal static void ValidateInitialState(RuntimeCheckpointCommit commit, RuntimeCommittedRecipient accepted)
    {
        var change = commit.ModelState ?? throw new InvalidDataException("The sealed model root has no model decision.");
        var state = change.State; var recipient = accepted.Recipient;
        var sealedCommand = accepted.Seal.Command.Deserialize<RuntimeModelExecutionCommand>(Web) ??
            throw new InvalidDataException("The actual model dispatch seal has no canonical model command.");
        if (change.ExpectedRevision is not null || state.Revision != 1 || state.IngressOrigin is not { } origin ||
            sealedCommand.InputKind != "start-trigger-v1" || sealedCommand.TenantId != state.TenantId ||
            sealedCommand.MaterialIncarnationId != state.MaterialIncarnationId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(sealedCommand.Executable, state.Executable) ||
            accepted.Seal.CommandProtocol != state.Protocol.Key || accepted.Seal.CommandProtocolVersion != state.Protocol.Version ||
            !WorkflowExecutionAuthoritySnapshot.Matches(sealedCommand.Authority, state.Authority.SystemIdentity, state.Authority.RootInitiator, state.Authority.Metadata) ||
            !origin.IsEquivalentTo(ReadOrigin(sealedCommand)) || origin.Scope != state.Scope ||
            state.ExecutionId != recipient.StartIdentity.WorkflowExecutionId || change.Input.InputId != recipient.StartIdentity.InputId ||
            change.Input.InputKind != sealedCommand.InputKind || change.Input.ContentFingerprint != RuntimeCheckpointCommitFingerprint.ComputeModelInput(sealedCommand) ||
            state.TenantId != recipient.TenantId || state.Partition != recipient.Partition || state.Protocol != recipient.ModelProtocol || state.MaterialIncarnationId != recipient.MaterialIncarnation ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(state.Executable, recipient.Material) ||
            !WorkflowExecutionAuthoritySnapshot.Matches(state.Authority, recipient.Authority.SystemIdentity, recipient.Authority.RootInitiator, recipient.Authority.Metadata))
            throw new InvalidDataException("The initial model checkpoint differs from its actual sealed input, material or tenant.");
    }

    internal static void ValidateContinuation(RuntimeModelExecutionState state, RuntimeModelExecutionState prior)
    {
        if (state.IngressOrigin is not { } origin || !origin.IsEquivalentTo(prior.IngressOrigin) || origin.Scope != state.Scope ||
            state.Scope != prior.Scope || state.ExecutionId != prior.ExecutionId || state.TenantId != prior.TenantId || state.Partition != prior.Partition || state.Protocol != prior.Protocol ||
            state.MaterialIncarnationId != prior.MaterialIncarnationId || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(state.Executable, prior.Executable) ||
            !WorkflowExecutionAuthoritySnapshot.Matches(state.Authority, prior.Authority.SystemIdentity, prior.Authority.RootInitiator, prior.Authority.Metadata))
            throw new InvalidDataException("A retained ingress model cannot replace its captured authority, tenant, scope or material.");
    }
}
