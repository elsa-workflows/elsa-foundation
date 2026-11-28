using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Validates the exact committed native delivery evidence without a runner or work queue.</summary>
internal static class RuntimeIngressNativeDeliveryEvidence
{
    internal const string Protocol = "elsa.runtime.native-ingress-start";

    internal static RuntimeIngressCommandOrigin Origin(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient) =>
        new(source.OutboxItemId, source.SourceFingerprint, source.SubscriptionDelivery?.SelectionFingerprint ?? throw Invalid(),
            recipient.StartIdentity.Scope, recipient.RecipientId);

    internal static WorkflowExecutionCommandEnvelope ReadEnvelope(RuntimeDispatchSeal seal)
    {
        if (seal.CommandProtocol != Protocol || seal.CommandProtocolVersion != 1) throw Unsupported();
        try { return seal.Command.Deserialize<WorkflowExecutionCommandEnvelope>() ?? throw Invalid(); }
        catch (JsonException exception) { throw new TriggerOperationException("conflict", "The sealed native command is invalid.", innerException: exception); }
    }

    internal static void ValidateState(RuntimeCommittedRecipient committed, WorkflowExecutionState state)
    {
        var recipient = committed.Recipient;
        var start = ReadEnvelope(committed.Seal).Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        if (state.WorkflowExecutionId != recipient.StartIdentity.WorkflowExecutionId ||
            state.HistoryCaptureEnabledAtAdmission != start.HistoryCaptureEnabledAtAdmission ||
            !(start.HistoryCapturePolicy?.IsEquivalentTo(state.HistoryCapturePolicy) ?? state.HistoryCapturePolicy is null) ||
            state.IngressOrigin?.IsEquivalentTo(Origin(committed.Item, recipient)) != true ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(state.PinnedExecutable, recipient.Material) ||
            !Equal(state.PinnedSource, recipient.SourceProvenance) || !Equal(state.Authority, recipient.Authority) ||
            state.TenantId != recipient.TenantId || state.Partition != recipient.Partition || state.ParentWorkflowExecutionId is not null ||
            state.RunKind != WorkflowRunKind.PublishedRun || state.TestScope is not null) throw Invalid();
    }

    internal static bool Equal<T>(T left, T right) => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left), JsonSerializer.SerializeToElement(right));
    internal static TriggerOperationException Invalid() => new("conflict", "Native ingress responsibility differs from its exact committed origin.");
    internal static TriggerOperationException Unsupported() => new("unavailable", "The selected native target evidence boundary is unsupported.");
}
