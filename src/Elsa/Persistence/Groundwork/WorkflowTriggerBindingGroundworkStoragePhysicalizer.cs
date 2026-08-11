using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Persistence.Groundwork;

internal static class WorkflowTriggerBindingGroundworkStoragePhysicalizer
{
    public static StorageManifest AddCompositeRoutes(StorageManifest manifest) => manifest with
    {
        StorageUnits = manifest.StorageUnits.Select(unit =>
            StringComparer.Ordinal.Equals(unit.Identity.Value, ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind)
                ? AddCompositeRoutes(unit)
                : unit).ToArray()
    };

    private static StorageUnit AddCompositeRoutes(StorageUnit unit)
    {
        if (unit.PhysicalStorage is not { } storage ||
            storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException("The workflow trigger-binding storage unit requires an explicit shared-document physicalization.");
        }

        var stimulusAndType = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.ByStimulusAndTypeIndex,
            [
                new IndexField(ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusLookupKeyField),
                new IndexField(
                    ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField,
                    IndexValueKind.Boolean),
                new IndexField(ElsaRuntimeStorageManifest.TriggerBindingIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            // Traversal indexes: the bounded sweeps behind these routes must see every binding, so the
            // index cannot omit the ones whose keyed columns have no value.
            MissingValueBehavior.IncludedAsNull);
        var stimulusTypeAndActive = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.WorkflowTriggerBindingByStimulusTypeAndActive,
            [
                new IndexField(ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusTypeLookupKeyField),
                new IndexField(
                    ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField,
                    IndexValueKind.Boolean),
                new IndexField(ElsaRuntimeStorageManifest.TriggerBindingIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            // Traversal indexes: the bounded sweeps behind these routes must see every binding, so the
            // index cannot omit the ones whose keyed columns have no value.
            MissingValueBehavior.IncludedAsNull);
        var publicationAndId = OrderedLookupIndex(
            ElsaRuntimeStorageManifest.WorkflowTriggerBindingByPublicationAndId,
            ElsaRuntimeStorageManifest.PublicationIdField);
        var artifactAndId = OrderedLookupIndex(
            ElsaRuntimeStorageManifest.WorkflowTriggerBindingByArtifactAndId,
            ElsaRuntimeStorageManifest.ArtifactIdField);
        var envelope = new DocumentEnvelopeDefinition();
        const string stimulusLookupColumn = "trigger_binding_stimulus_lookup_key";
        const string stimulusTypeLookupColumn = "trigger_binding_stimulus_type_lookup_key";
        var projectedColumns = ElsaRuntimeStorageManifest.BoundStimulusProjectionColumns(
                definition.ProjectedColumns,
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusTypeProjectionLength)
            .Where(column => column.Path is not (
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusLookupKeyField or
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusTypeLookupKeyField))
            .Concat(
            [
                new ProjectedColumnDefinition(
                    stimulusLookupColumn,
                    ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusLookupKeyField,
                    PortablePhysicalType.String,
                    Length: StimulusLookupKey.Length),
                new ProjectedColumnDefinition(
                    stimulusTypeLookupColumn,
                    ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusTypeLookupKeyField,
                    PortablePhysicalType.String,
                    Length: StimulusLookupKey.Length)
            ])
            .ToArray();
        var augmentedDefinition = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            projectedColumns,
            definition.Indexes.Concat(
            [
                new PhysicalIndexDefinition(
                    stimulusAndType.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(stimulusLookupColumn, 1),
                        new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.WorkflowTriggerBindingByActive, 2),
                        new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.WorkflowTriggerBindingById, 3),
                        new PhysicalIndexColumnDefinition(
                            envelope.IdLookupKeyColumn,
                            4)
                    ],
                    missingValueBehavior: MissingValueBehavior.IncludedAsNull),
                new PhysicalIndexDefinition(
                    stimulusTypeAndActive.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(stimulusTypeLookupColumn, 1),
                        new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.WorkflowTriggerBindingByActive, 2),
                        new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.WorkflowTriggerBindingById, 3),
                        new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 4)
                    ],
                    missingValueBehavior: MissingValueBehavior.IncludedAsNull),
                OrderedLookupPhysicalIndex(
                    publicationAndId,
                    ElsaRuntimeStorageManifest.ByPublicationIndex,
                    envelope),
                OrderedLookupPhysicalIndex(
                    artifactAndId,
                    ElsaRuntimeStorageManifest.ByArtifactIndex,
                    envelope)
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
                storage.LogicalIndexes
                    .Concat([stimulusAndType, stimulusTypeAndActive, publicationAndId, artifactAndId])
                    .ToArray(),
                storage.BoundedQueries
                    .Where(query => query.Identity is not (
                        ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusAndTypeQuery or
                        ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusTypeQuery or
                        ElsaRuntimeStorageManifest.ListTriggerBindingsByPublicationQuery or
                        ElsaRuntimeStorageManifest.ListTriggerBindingsByArtifactQuery))
                    .Concat(
                    [
                        new BoundedQueryDeclaration(
                            ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusAndTypeQuery,
                            stimulusAndType.Identity,
                            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                            QuerySortSupport.Ascending,
                            QueryPagingSupport.Cursor,
                            BoundedQueryExecutionClass.ScaleBearing,
                            supportsTotalCount: true,
                            sortFields:
                            [
                                new BoundedQuerySortField(
                                    ElsaRuntimeStorageManifest.TriggerBindingIdField,
                                    PhysicalSortDirection.Ascending)
                            ],
                            predicateFields:
                            [
                                new BoundedQueryPredicateField(
                                    ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusLookupKeyField,
                                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
                                new BoundedQueryPredicateField(
                                    ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField,
                                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
                            ]),
                        new BoundedQueryDeclaration(
                            ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusTypeQuery,
                            stimulusTypeAndActive.Identity,
                            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                            QuerySortSupport.Ascending,
                            QueryPagingSupport.Cursor,
                            BoundedQueryExecutionClass.ScaleBearing,
                            supportsTotalCount: true,
                            sortFields:
                            [
                                new BoundedQuerySortField(
                                    ElsaRuntimeStorageManifest.TriggerBindingIdField,
                                    PhysicalSortDirection.Ascending)
                            ],
                            predicateFields:
                            [
                                new BoundedQueryPredicateField(
                                    ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusTypeLookupKeyField,
                                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
                                new BoundedQueryPredicateField(
                                    ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField,
                                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
                            ]),
                        OrderedLookupQuery(
                            ElsaRuntimeStorageManifest.ListTriggerBindingsByPublicationQuery,
                            publicationAndId,
                            ElsaRuntimeStorageManifest.PublicationIdField),
                        OrderedLookupQuery(
                            ElsaRuntimeStorageManifest.ListTriggerBindingsByArtifactQuery,
                            artifactAndId,
                            ElsaRuntimeStorageManifest.ArtifactIdField)
                    ]).ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }

    private static LogicalIndexDeclaration OrderedLookupIndex(
        string identity,
        string predicatePath) =>
        new(
            identity,
            [
                new IndexField(predicatePath),
                new IndexField(ElsaRuntimeStorageManifest.TriggerBindingIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            // Traversal indexes: the bounded sweeps behind these routes must see every binding, so the
            // index cannot omit the ones whose keyed columns have no value.
            MissingValueBehavior.IncludedAsNull);

    private static PhysicalIndexDefinition OrderedLookupPhysicalIndex(
        LogicalIndexDeclaration index,
        string predicateColumn,
        DocumentEnvelopeDefinition envelope) =>
        new(
            index.Identity,
            [
                new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                new PhysicalIndexColumnDefinition(predicateColumn, 1),
                new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.WorkflowTriggerBindingById, 2),
                new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 3)
            ],
            // Must match the logical declaration above; the two disagreeing is itself a blocking
            // diagnostic, and these routes sweep every binding.
            missingValueBehavior: MissingValueBehavior.IncludedAsNull);

    private static BoundedQueryDeclaration OrderedLookupQuery(
        string identity,
        LogicalIndexDeclaration index,
        string predicatePath) =>
        new(
            identity,
            index.Identity,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            QuerySortSupport.Ascending,
            QueryPagingSupport.Cursor,
            BoundedQueryExecutionClass.ScaleBearing,
            supportsTotalCount: true,
            sortFields:
            [
                new BoundedQuerySortField(
                    ElsaRuntimeStorageManifest.TriggerBindingIdField,
                    PhysicalSortDirection.Ascending)
            ],
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    predicatePath,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
            ]);
}
