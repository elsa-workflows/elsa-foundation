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
    public const int LegacyStringProjectionLength = 450;

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
                    ? PhysicalizeLegacyUnit(unit)
                    : unit)
                .ToArray(),
            SharedDocumentStorages = [SharedDocumentsDefinition]
        };
    }

    private static StorageUnit PhysicalizeLegacyUnit(StorageUnit unit)
    {
        var physicalized = LegacyPhysicalStorageBridge.Apply(unit, SharedDocumentsBinding);
        var storage = physicalized.PhysicalStorage
            ?? throw new InvalidOperationException($"Legacy physicalization produced no storage declaration for '{unit.Identity.Value}'.");
        if (storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException($"Legacy physicalization produced an unexpected storage form for '{unit.Identity.Value}'.");
        }

        // SQL Server requires every physical index key to have a bounded worst-case width. The legacy bridge cannot
        // infer Elsa's identifier contract, so make the established 450-character bound explicit on every projected
        // string. This remains provider-neutral and keeps the same logical values and query declarations.
        var projectedColumns = definition.ProjectedColumns
            .Select(column => column.Type == PortablePhysicalType.String && column.Length is null
                ? column with { Length = LegacyStringProjectionLength }
                : column)
            .ToArray();
        var boundedDefinition = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            projectedColumns,
            definition.Indexes,
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);
        return physicalized with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(boundedDefinition),
                storage.LogicalIndexes,
                storage.BoundedQueries,
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }
}
