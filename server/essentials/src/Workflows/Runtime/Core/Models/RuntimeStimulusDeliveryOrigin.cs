namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Immutable saved-cohort origin of a queued ordinary start. It carries no expiring delivery claim and grants no independent authority.</summary>
public sealed class RuntimeStimulusDeliveryOrigin
{
    public RuntimeStimulusDeliveryOrigin(int version, PersistenceScope scope, string sourceOutboxItemId, string intentFingerprint, string triggerBindingId)
    {
        if (version != 1) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentNullException.ThrowIfNull(scope);
        RuntimeDeliveryFingerprint.Text(sourceOutboxItemId, nameof(sourceOutboxItemId));
        RuntimeDeliveryFingerprint.Hash(intentFingerprint, nameof(intentFingerprint));
        RuntimeDeliveryFingerprint.Text(triggerBindingId, nameof(triggerBindingId));
        Version = version; Scope = scope; SourceOutboxItemId = sourceOutboxItemId; IntentFingerprint = intentFingerprint; TriggerBindingId = triggerBindingId;
    }
    public int Version { get; }
    public PersistenceScope Scope { get; }
    public string SourceOutboxItemId { get; }
    public string IntentFingerprint { get; }
    public string TriggerBindingId { get; }
    public bool IsEquivalentTo(RuntimeStimulusDeliveryOrigin? other) => other is not null && Version == other.Version && Scope == other.Scope &&
        SourceOutboxItemId == other.SourceOutboxItemId && IntentFingerprint == other.IntentFingerprint && TriggerBindingId == other.TriggerBindingId;
}
