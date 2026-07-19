using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Manifests;

namespace Elsa.Persistence.Groundwork.Unified;

/// <summary>
/// Compatibility facade for callers that have not yet moved to the host-selected composition
/// snapshot. The snapshot remains the only manifest and target-fingerprint authority.
/// </summary>
public static class GroundworkUnifiedManifest
{
    /// <summary>Returns the manifest from the validated host-selected composition.</summary>
    public static StorageManifest Create(GroundworkStorageCompositionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Manifest;
    }
}
