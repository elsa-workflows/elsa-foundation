using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Adapts Elsa's pre-three-form manifests to the shared Groundwork document table used by the
/// existing relational adapters. The canonical JSON name remains compatible with their current
/// history and document queries while Groundwork owns each unit's linked projection explicitly.
/// </summary>
public static class LegacyGroundworkStorageManifestPhysicalizer
{
    public const string SharedDocumentsLogicalName = "groundwork_documents";
    public const string CanonicalJsonColumnName = "content_json";

    private static readonly SharedStorageBinding SharedDocumentsBinding = new(SharedDocumentsLogicalName);
    private static readonly SharedDocumentStorageDefinition SharedDocumentsDefinition = new(
        SharedDocumentsBinding,
        SharedDocumentsLogicalName,
        new DocumentEnvelopeDefinition(CanonicalJsonColumn: CanonicalJsonColumnName));

    /// <summary>Physicalizes every legacy unit without changing the authoritative manifest identity.</summary>
    public static StorageManifest Physicalize(StorageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.SharedDocumentStorages.Count != 0 &&
            (manifest.SharedDocumentStorages.Count != 1 || manifest.SharedDocumentStorages[0] != SharedDocumentsDefinition))
        {
            throw new InvalidOperationException(
                $"Manifest '{manifest.Identity.Value}' already declares a different shared document storage definition.");
        }

        return manifest with
        {
            StorageUnits = manifest.StorageUnits
                .Select(unit => unit.PhysicalStorage is null
                    ? LegacyPhysicalStorageBridge.Apply(unit, SharedDocumentsBinding)
                    : unit)
                .ToArray(),
            SharedDocumentStorages = [SharedDocumentsDefinition]
        };
    }
}
