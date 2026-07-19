using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Persistence.Groundwork;

internal static class BookmarkStateGroundworkStoragePhysicalizer
{
    public static StorageManifest AddCompositeRoutes(StorageManifest manifest) => manifest with
    {
        StorageUnits = manifest.StorageUnits.Select(unit =>
            StringComparer.Ordinal.Equals(unit.Identity.Value, ElsaRuntimeStorageManifest.BookmarkStateDocumentKind)
                ? AddCompositeRoutes(unit)
                : unit).ToArray()
    };

    private static StorageUnit AddCompositeRoutes(StorageUnit unit)
    {
        if (unit.PhysicalStorage is not { } storage ||
            storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException("The bookmark-state storage unit requires an explicit shared-document physicalization.");
        }

        var stimulusAndType = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.BookmarkStateByStimulusAndType,
            [
                new IndexField(ElsaRuntimeStorageManifest.StimulusHashField),
                new IndexField(ElsaRuntimeStorageManifest.StimulusTypeField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var augmentedDefinition = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            ElsaRuntimeStorageManifest.BoundStimulusProjectionColumns(definition.ProjectedColumns),
            definition.Indexes.Concat(
            [
                new PhysicalIndexDefinition(
                    stimulusAndType.Identity,
                    [
                        new PhysicalIndexColumnDefinition(new DocumentEnvelopeDefinition().StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.ByStimulusIndex, 1),
                        new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.ByStimulusTypeIndex, 2)
                    ])
            ]).ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(augmentedDefinition),
                storage.LogicalIndexes.Concat([stimulusAndType]).ToArray(),
                storage.BoundedQueries.Concat(
                [
                    new BoundedQueryDeclaration(
                        ElsaRuntimeStorageManifest.ListBookmarksByStimulusAndTypeQuery,
                        stimulusAndType.Identity,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                        QuerySortSupport.None,
                        QueryPagingSupport.Offset,
                        predicateFields:
                        [
                            new BoundedQueryPredicateField(
                                ElsaRuntimeStorageManifest.StimulusHashField,
                                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
                            new BoundedQueryPredicateField(
                                ElsaRuntimeStorageManifest.StimulusTypeField,
                                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
                        ])
                ]).ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }
}
