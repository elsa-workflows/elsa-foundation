using Elsa.Persistence.Groundwork.Composition;
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
            ElsaRuntimeStorageManifest.BookmarkStateByStimulusAndTypeAndIdentity,
            [
                new IndexField(ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyField),
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                new IndexField(ElsaRuntimeStorageManifest.BookmarkIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false);
        var stimulusType = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.BookmarkStateByStimulusTypeAndIdentity,
            [
                new IndexField(ElsaRuntimeStorageManifest.BookmarkStimulusTypeLookupKeyField),
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                new IndexField(ElsaRuntimeStorageManifest.BookmarkIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false);
        const string bookmarkIdColumn = "bookmark_id";
        const string stimulusLookupColumn = "bookmark_stimulus_lookup_key";
        const string stimulusTypeLookupColumn = "bookmark_stimulus_type_lookup_key";
        var envelope = new DocumentEnvelopeDefinition();
        var augmentedDefinition = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            ElsaRuntimeStorageManifest.BoundStimulusProjectionColumns(definition.ProjectedColumns)
                .Where(column => column.Path is not (
                    ElsaRuntimeStorageManifest.BookmarkIdField or
                    ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyField or
                    ElsaRuntimeStorageManifest.BookmarkStimulusTypeLookupKeyField))
                .Concat(
                [
                    new ProjectedColumnDefinition(
                        bookmarkIdColumn,
                        ElsaRuntimeStorageManifest.BookmarkIdField,
                        PortablePhysicalType.String,
                        Length: ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength),
                    new ProjectedColumnDefinition(
                        stimulusLookupColumn,
                        ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyField,
                        PortablePhysicalType.String,
                        Length: StimulusLookupKey.Length),
                    new ProjectedColumnDefinition(
                        stimulusTypeLookupColumn,
                        ElsaRuntimeStorageManifest.BookmarkStimulusTypeLookupKeyField,
                        PortablePhysicalType.String,
                        Length: StimulusLookupKey.Length)
                ])
                .ToArray(),
            definition.Indexes
                .Where(index => index.LogicalName is not (
                    ElsaRuntimeStorageManifest.BookmarkStateByStimulusAndTypeAndIdentity or
                    ElsaRuntimeStorageManifest.BookmarkStateByStimulusTypeAndIdentity))
                .Concat(
                    [
                        new PhysicalIndexDefinition(
                            stimulusAndType.Identity,
                            [
                                new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                                new PhysicalIndexColumnDefinition(stimulusLookupColumn, 1),
                                new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, 2),
                                new PhysicalIndexColumnDefinition(bookmarkIdColumn, 3),
                                new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 4)
                            ]),
                        new PhysicalIndexDefinition(
                            stimulusType.Identity,
                            [
                                new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                                new PhysicalIndexColumnDefinition(stimulusTypeLookupColumn, 1),
                                new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, 2),
                                new PhysicalIndexColumnDefinition(bookmarkIdColumn, 3),
                                new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 4)
                            ])
                    ])
                .ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(augmentedDefinition),
                storage.LogicalIndexes
                    .Where(index => index.Identity is not (
                        ElsaRuntimeStorageManifest.BookmarkStateByStimulusAndTypeAndIdentity or
                        ElsaRuntimeStorageManifest.BookmarkStateByStimulusTypeAndIdentity))
                    .Concat([stimulusAndType, stimulusType])
                    .ToArray(),
                storage.BoundedQueries
                    .Where(query => query.Identity is not (
                        ElsaRuntimeStorageManifest.ListBookmarksByStimulusAndTypeQuery or
                        ElsaRuntimeStorageManifest.ListBookmarksByStimulusTypeQuery))
                    .Concat(
                        [
                            new BoundedQueryDeclaration(
                                ElsaRuntimeStorageManifest.ListBookmarksByStimulusAndTypeQuery,
                                stimulusAndType.Identity,
                                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                                QuerySortSupport.Ascending,
                                QueryPagingSupport.Cursor,
                                BoundedQueryExecutionClass.ScaleBearing,
                                supportsTotalCount: true,
                                sortFields:
                                [
                                    new BoundedQuerySortField(
                                        ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                                        PhysicalSortDirection.Ascending),
                                    new BoundedQuerySortField(
                                        ElsaRuntimeStorageManifest.BookmarkIdField,
                                        PhysicalSortDirection.Ascending)
                                ],
                                predicateFields:
                                [
                                    new BoundedQueryPredicateField(
                                        ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyField,
                                        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
                                ]),
                            new BoundedQueryDeclaration(
                                ElsaRuntimeStorageManifest.ListBookmarksByStimulusTypeQuery,
                                stimulusType.Identity,
                                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                                QuerySortSupport.Ascending,
                                QueryPagingSupport.Cursor,
                                BoundedQueryExecutionClass.ScaleBearing,
                                supportsTotalCount: true,
                                sortFields:
                                [
                                    new BoundedQuerySortField(
                                        ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                                        PhysicalSortDirection.Ascending),
                                    new BoundedQuerySortField(
                                        ElsaRuntimeStorageManifest.BookmarkIdField,
                                        PhysicalSortDirection.Ascending)
                                ],
                                predicateFields:
                                [
                                    new BoundedQueryPredicateField(
                                        ElsaRuntimeStorageManifest.BookmarkStimulusTypeLookupKeyField,
                                        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
                                ])
                        ])
                    .ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }
}
