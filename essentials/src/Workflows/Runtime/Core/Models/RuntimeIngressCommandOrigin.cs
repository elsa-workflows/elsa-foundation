using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Names one immutable physical dispatch seal without carrying an expiring delivery claim.</summary>
/// <remarks>The seal's canonical command contains this origin. Its own hash is deliberately absent to avoid a circular hash.</remarks>
public sealed class RuntimeIngressCommandOrigin
{
    [JsonConstructor]
    public RuntimeIngressCommandOrigin(string sourceOutboxItemId, string sourceFingerprint, string selectionFingerprint,
        PersistenceScope scope, string recipientId)
    {
        RuntimeDeliveryFingerprint.Text(sourceOutboxItemId, nameof(sourceOutboxItemId));
        RuntimeDeliveryFingerprint.Hash(sourceFingerprint, nameof(sourceFingerprint));
        RuntimeDeliveryFingerprint.Hash(selectionFingerprint, nameof(selectionFingerprint));
        RuntimeDeliveryFingerprint.Text(recipientId, nameof(recipientId));
        ArgumentNullException.ThrowIfNull(scope);
        SourceOutboxItemId = sourceOutboxItemId; SourceFingerprint = sourceFingerprint;
        SelectionFingerprint = selectionFingerprint; Scope = scope; RecipientId = recipientId;
    }

    public string SourceOutboxItemId { get; }
    public string SourceFingerprint { get; }
    public string SelectionFingerprint { get; }
    public PersistenceScope Scope { get; }
    public string RecipientId { get; }
    public bool IsEquivalentTo(RuntimeIngressCommandOrigin? other) => other is not null &&
        SourceOutboxItemId == other.SourceOutboxItemId && SourceFingerprint == other.SourceFingerprint &&
        SelectionFingerprint == other.SelectionFingerprint && Scope == other.Scope && RecipientId == other.RecipientId;
}
