using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Elsa.Persistence.Groundwork.Composition;

namespace Elsa.Persistence.Groundwork;

/// <summary>Admits finite scheduler-work queue reads on portable physical storage.</summary>
internal static class SchedulerWorkStoragePhysicalizer
{
    private const string WorkflowExecutionIdColumn = "scheduler_workflow_execution_id";
    private const string OrderKeyColumn = "scheduler_work_order_key";

    public static StorageManifest AddRoutes(StorageManifest manifest) => manifest with
    {
        StorageUnits = manifest.StorageUnits.Select(unit =>
            unit.Identity.Value == ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind
                ? Physicalize(unit)
                : unit).ToArray()
    };

    private static StorageUnit Physicalize(StorageUnit unit)
    {
        if (unit.PhysicalStorage is not { } storage ||
            storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException(
                "Scheduler-work queue storage requires explicit shared-document physicalization.");
        }

        var projected = definition.ProjectedColumns
            .Where(column => column.Path is not (
                ElsaRuntimeStorageManifest.WorkflowExecutionIdField or
                ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField))
            .Select(column => column.Path == ElsaRuntimeStorageManifest.CollectionField
                ? column with
                {
                    Length = ElsaRuntimeStorageManifest.RuntimeCollectionProjectionLength
                }
                : column)
            .Concat(
            [
                new ProjectedColumnDefinition(
                    WorkflowExecutionIdColumn,
                    ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                    PortablePhysicalType.String,
                    Length: ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength),
                new ProjectedColumnDefinition(
                    OrderKeyColumn,
                    ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField,
                    PortablePhysicalType.String,
                    Length: ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyProjectionLength)
            ])
            .ToArray();
        var collectionColumn = projected.Single(column =>
            column.Path == ElsaRuntimeStorageManifest.CollectionField).LogicalName;
        var byWorkflowIndex = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.SchedulerWorkByWorkflowOrderIndex,
            [
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                new IndexField(ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var pendingIndex = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.SchedulerWorkPendingByWorkflowOrderIndex,
            [
                new IndexField(ElsaRuntimeStorageManifest.CollectionField),
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                new IndexField(ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var envelope = new DocumentEnvelopeDefinition();
        var physical = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            projected,
            definition.Indexes
                .Where(index => index.LogicalName is not (
                    ElsaRuntimeStorageManifest.ByCollectionIndex or
                    ElsaRuntimeStorageManifest.SchedulerWorkByWorkflowOrderIndex or
                    ElsaRuntimeStorageManifest.SchedulerWorkPendingByWorkflowOrderIndex))
                .Concat(
                [
                    new PhysicalIndexDefinition(
                        byWorkflowIndex.Identity,
                        [
                            new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                            new PhysicalIndexColumnDefinition(WorkflowExecutionIdColumn, 1),
                            new PhysicalIndexColumnDefinition(OrderKeyColumn, 2),
                            new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 3)
                        ]),
                    new PhysicalIndexDefinition(
                        pendingIndex.Identity,
                        [
                            new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                            new PhysicalIndexColumnDefinition(collectionColumn, 1),
                            new PhysicalIndexColumnDefinition(WorkflowExecutionIdColumn, 2),
                            new PhysicalIndexColumnDefinition(OrderKeyColumn, 3)
                        ])
                ])
                .ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);
        var byWorkflow = new BoundedQueryDeclaration(
            ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery,
            ElsaRuntimeStorageManifest.SchedulerWorkByWorkflowOrderIndex,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            QuerySortSupport.Ascending,
            QueryPagingSupport.Cursor,
            BoundedQueryExecutionClass.ScaleBearing,
            sortFields:
            [new BoundedQuerySortField(ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField, PhysicalSortDirection.Ascending)],
            predicateFields:
            [new BoundedQueryPredicateField(
                ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })]);
        var pending = new BoundedQueryDeclaration(
            ElsaRuntimeStorageManifest.ListPendingSchedulerWorkflowExecutionsQuery,
            pendingIndex.Identity,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            QuerySortSupport.Ascending,
            QueryPagingSupport.None,
            BoundedQueryExecutionClass.ScaleBearing,
            sortFields:
            [
                new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField, PhysicalSortDirection.Ascending)
            ],
            predicateFields:
            [new BoundedQueryPredicateField(
                ElsaRuntimeStorageManifest.CollectionField,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })],
            latestPerKeyPath: ElsaRuntimeStorageManifest.WorkflowExecutionIdField);

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(physical),
                storage.LogicalIndexes
                    .Where(index => index.Identity is not (
                        ElsaRuntimeStorageManifest.ByCollectionIndex or
                        ElsaRuntimeStorageManifest.SchedulerWorkByWorkflowOrderIndex or
                        ElsaRuntimeStorageManifest.SchedulerWorkPendingByWorkflowOrderIndex))
                    .Concat([byWorkflowIndex, pendingIndex])
                    .ToArray(),
                storage.BoundedQueries
                    .Where(query => query.Identity is not (
                        ElsaRuntimeStorageManifest.ListAllQuery or
                        ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery or
                        ElsaRuntimeStorageManifest.ListPendingSchedulerWorkflowExecutionsQuery))
                    .Concat([byWorkflow, pending])
                    .ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }
}
