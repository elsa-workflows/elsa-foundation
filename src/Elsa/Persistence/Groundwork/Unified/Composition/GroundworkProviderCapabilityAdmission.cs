using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Capabilities;

namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>
/// Publishes the provider-capability snapshot that was admitted with the selected physical store.
/// A snapshot is deliberately absent until physical schema admission has succeeded, so configuration
/// intent and package support alone cannot enable a runtime capability claim.
/// </summary>
public sealed class GroundworkProviderCapabilityAdmission
{
    private GroundworkProviderCapabilitySnapshot? _snapshot;

    /// <summary>Whether a selected provider path has completed physical admission.</summary>
    public bool IsAdmitted => Volatile.Read(ref _snapshot) is not null;

    /// <summary>
    /// Publishes the immutable snapshot exactly once after the corresponding provider session has
    /// been admitted. A later provider cannot replace the evidence for an already selected store.
    /// </summary>
    public bool TrySet(GroundworkProviderCapabilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Interlocked.CompareExchange(ref _snapshot, snapshot, null) is null;
    }

    /// <summary>
    /// Returns whether an exact selected route was both provider-supported and evidenced at the time
    /// its physical store was admitted.
    /// </summary>
    public bool IsCapabilityAdmitted(
        GroundworkActiveStoragePath path,
        CapabilityId capability,
        GroundworkStorageTopologyRequirement topologyRequirement)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(topologyRequirement);
        var snapshot = Volatile.Read(ref _snapshot);
        return snapshot?.IsCapabilityAvailable(path, capability) is true &&
               snapshot.SupportsTopology(topologyRequirement);
    }
}
