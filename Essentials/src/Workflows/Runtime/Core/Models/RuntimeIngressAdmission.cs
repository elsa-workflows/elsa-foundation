using System.Text.Json;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A known observation for every enabled association, including refused and absent targets. Provider failures are not observations.</summary>
public sealed record RuntimeIngressTargetObservation(string SubscriptionId, string DefinitionId, string SlotName,
    WorkflowActivationSlot? Slot, WorkflowExecutableSourceReference? SourceReference);

/// <summary>A prepared complete cohort. The selected physical provider rechecks resource, grant, slot, source and material before acceptance.</summary>
public sealed record RuntimeIngressAdmissionRequest(TriggerScope Scope, long ExpectedTriggerRevision,
    RuntimePostCommitOutboxItem Candidate, IReadOnlyList<RuntimeIngressTargetObservation> Targets)
{
    public RuntimeIngressReservation? Reservation { get; init; }
    public RecurringTriggerOccurrenceClaim? ScheduleClaim { get; init; }
    public RuntimeIngressWaitCandidate? WaitCandidate { get; init; }
}

public sealed record RuntimeIngressAdmissionReceipt(RuntimePostCommitOutboxItem Item, bool Replayed);

/// <summary>The immutable physical dispatch commitment. Claim expiry does not revoke target enqueue responsibility.</summary>
public sealed record RuntimeCommittedRecipient(RuntimePostCommitOutboxItem Item, RuntimeSubscriptionRecipient Recipient,
    RuntimeDispatchSeal Seal);

/// <summary>Cancel the named still-pending recipients of this exact accepted cohort. A committed seal wins.</summary>
public sealed record RuntimeIngressCancellationRequest(TriggerScope Scope, string SourceOutboxItemId,
    string SourceFingerprint, string SelectionFingerprint, IReadOnlyList<string> RecipientIds);

public sealed record RuntimeIngressCancellationReceipt(RuntimePostCommitOutboxItem Item,
    IReadOnlyList<string> CancelledRecipientIds, IReadOnlyList<string> CommittedRecipientIds);

/// <summary>Canonical data helpers for prepared admission; these do not grant physical acceptance.</summary>
public static class RuntimeIngressAdmission
{
    /// <summary>Always frames submitted data and authorized producer metadata separately from server source/configuration facts.</summary>
    public static JsonElement ComputeSubmittedIntent(JsonElement payload, JsonElement metadata)
    {
        if (payload.ValueKind == JsonValueKind.Undefined || metadata.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Submitted intent requires a defined payload and an object of detached producer metadata.");
        return JsonSerializer.SerializeToElement(new { payload, metadata }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public static string ComputeInputContractFingerprint(WorkflowExecutableInputContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-input-contract.v1", contract);
    }
}
