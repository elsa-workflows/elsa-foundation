using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Protocol-neutral physical proof for the versioned model trigger-start command.</summary>
internal static class RuntimeIngressModelTargetEvidence
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static RuntimeModelExecutionCommand ReadCommand(RuntimeCommittedRecipient committed)
    {
        var recipient = committed.Recipient; var seal = committed.Seal;
        if (recipient.ModelProtocol is null || seal.CommandProtocol != recipient.ModelProtocol.Key || seal.CommandProtocolVersion != recipient.ModelProtocol.Version)
            throw new NotSupportedException("The sealed model trigger-start protocol differs from its actual accepted material.");
        var command = seal.Command.Deserialize<RuntimeModelExecutionCommand>(Json) ?? throw new InvalidDataException("The model trigger-start command is missing.");
        var payload = command.Payload;
        var expectedOrigin = new RuntimeIngressCommandOrigin(committed.Item.OutboxItemId, committed.Item.SourceFingerprint,
            committed.Item.SubscriptionDelivery!.SelectionFingerprint, recipient.StartIdentity.Scope, recipient.RecipientId);
        if (command.Executable != recipient.Material || command.MaterialIncarnationId != recipient.MaterialIncarnation ||
            command.InputId != recipient.StartIdentity.InputId || command.InputKind != "start-trigger-v1" || command.TenantId != recipient.TenantId ||
            !RuntimeIngressAdmissionValidation.Same(command.Authority, recipient.Authority) ||
            !payload.TryGetProperty("inputs", out var inputs) || !JsonElement.DeepEquals(inputs, JsonSerializer.SerializeToElement(recipient.Inputs, Json)) ||
            !payload.TryGetProperty("sourceReferenceId", out var source) || source.GetString() != recipient.SourceProvenance.SourceReferenceId ||
            command.IngressOrigin?.IsEquivalentTo(expectedOrigin) != true ||
            !payload.TryGetProperty("origin", out var origin) || origin.Deserialize<RuntimeIngressCommandOrigin>(Json)?.IsEquivalentTo(expectedOrigin) != true ||
            !payload.TryGetProperty("trigger", out var trigger) || trigger.Deserialize<RuntimeSubscriptionRecipient>(Json)?.IsEquivalentTo(recipient) != true)
            throw new InvalidDataException("The sealed model command differs from its exact accepted capture.");
        return command;
    }

    internal static async ValueTask<RuntimeTargetAcceptanceReceipt?> ReconcileAsync(RuntimeCommittedRecipient committed,
        IWorkflowSchedulerWorkQueue queue, IRuntimeModelExecutionReader inputs, TimeProvider time, CancellationToken token)
    {
        var command = ReadCommand(committed);
        var recipient = committed.Recipient; var identity = recipient.StartIdentity;
        var workId = RuntimeModelExecutionCommand.CreateSchedulerWorkItemId(identity.InputId);
        var fingerprint = RuntimeCheckpointCommitFingerprint.ComputeModelInput(command);
        var page = await queue.ListAsync(new(identity.WorkflowExecutionId, RuntimeStorePageRequest.MaximumLimit), token);
        foreach (var work in page.Items.Where(work => work.WorkItemId == workId))
        {
            var actual = work.Payload?.Deserialize<RuntimeModelExecutionCommand>(Json) ?? throw new InvalidDataException("The queued model input is missing.");
            if (work.CommandKind != WorkflowExecutionCommandKind.ExecuteModel || work.WorkflowExecutionId != identity.WorkflowExecutionId ||
                work.CommandId != identity.InputId || work.EnvelopeId != identity.EnvelopeId || work.IdempotencyKey != identity.IdempotencyKey ||
                work.EnqueuedAt != committed.Seal.CommittedAt || work.CommandMetadata.Count != 0 || work.EnvelopeMetadata.Count != 0 ||
                work.ExecutionScopeId is not null || work.Attempt is not null || RuntimeCheckpointCommitFingerprint.ComputeModelInput(actual) != fingerprint)
                throw new InvalidDataException("The queued model input differs from its immutable dispatch commitment.");
            return Receipt(RuntimeTargetAcceptanceKind.SchedulerCommand, workId);
        }
        var receipt = await inputs.FindInputAsync(identity.WorkflowExecutionId, identity.InputId, token);
        if (receipt is null) return null;
        if (receipt.Scope != identity.Scope || receipt.ExecutionId != identity.WorkflowExecutionId || receipt.InputId != identity.InputId ||
            receipt.InputKind != command.InputKind || receipt.ContentFingerprint != fingerprint)
            throw new InvalidDataException("The physical model input receipt differs from its immutable dispatch commitment.");
        var state = await inputs.FindAsync(identity.WorkflowExecutionId, token) ??
            throw new InvalidDataException("The physical accepted model input has no committed execution state.");
        if (state.Scope != identity.Scope || state.ExecutionId != identity.WorkflowExecutionId || state.TenantId != recipient.TenantId ||
            state.Executable != recipient.Material || state.MaterialIncarnationId != recipient.MaterialIncarnation || state.Protocol != recipient.ModelProtocol ||
            state.IngressOrigin?.IsEquivalentTo(command.IngressOrigin) != true || state.Partition != recipient.Partition ||
            !RuntimeIngressAdmissionValidation.Same(state.Authority, recipient.Authority))
            throw new InvalidDataException("The physical model execution differs from its accepted target authority.");
        return Receipt(RuntimeTargetAcceptanceKind.ModelInput, identity.InputId);

        RuntimeTargetAcceptanceReceipt Receipt(RuntimeTargetAcceptanceKind kind, string evidence) => new(recipient.RecipientId,
            committed.Seal.Fingerprint, identity, recipient.Material, kind, evidence, time.GetUtcNow());
    }
}
