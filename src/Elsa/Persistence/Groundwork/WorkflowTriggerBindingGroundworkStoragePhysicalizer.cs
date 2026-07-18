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
                new IndexField(ElsaRuntimeStorageManifest.StimulusHashField),
                new IndexField(ElsaRuntimeStorageManifest.StimulusTypeField),
                new IndexField(
                    ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField,
                    IndexValueKind.Boolean)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var stimulusTypeAndActive = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.WorkflowTriggerBindingByStimulusTypeAndActive,
            [
                new IndexField(ElsaRuntimeStorageManifest.StimulusTypeField),
                new IndexField(
                    ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField,
                    IndexValueKind.Boolean),
                new IndexField(ElsaRuntimeStorageManifest.TriggerBindingIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var envelope = new DocumentEnvelopeDefinition();
        var augmentedDefinition = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            ElsaRuntimeStorageManifest.BoundStimulusProjectionColumns(
                definition.ProjectedColumns,
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusTypeProjectionLength),
            definition.Indexes.Concat(
            [
                new PhysicalIndexDefinition(
                    stimulusAndType.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.ByStimulusIndex, 1),
                        new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.ByStimulusTypeIndex, 2),
                        new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.WorkflowTriggerBindingByActive, 3),
                        new PhysicalIndexColumnDefinition(
                            envelope.IdLookupKeyColumn,
                            4)
                    ]),
                new PhysicalIndexDefinition(
                    stimulusTypeAndActive.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.ByStimulusTypeIndex, 1),
                        new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.WorkflowTriggerBindingByActive, 2),
                        new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.WorkflowTriggerBindingById, 3),
                        new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 4)
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
                storage.LogicalIndexes.Concat([stimulusAndType, stimulusTypeAndActive]).ToArray(),
                storage.BoundedQueries
                    .Where(query => !StringComparer.Ordinal.Equals(
                        query.Identity,
                        ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusTypeQuery))
                    .Concat(
                    [
                        new BoundedQueryDeclaration(
                            ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusAndTypeQuery,
                            stimulusAndType.Identity,
                            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                            QuerySortSupport.None,
                            QueryPagingSupport.Cursor,
                            BoundedQueryExecutionClass.ScaleBearing,
                            supportsTotalCount: true,
                            predicateFields:
                            [
                                new BoundedQueryPredicateField(
                                    ElsaRuntimeStorageManifest.StimulusHashField,
                                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
                                new BoundedQueryPredicateField(
                                    ElsaRuntimeStorageManifest.StimulusTypeField,
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
                                    ElsaRuntimeStorageManifest.StimulusTypeField,
                                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
                                new BoundedQueryPredicateField(
                                    ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField,
                                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
                            ])
                    ]).ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }
}
