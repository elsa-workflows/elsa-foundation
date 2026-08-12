using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Persistence.Groundwork;

/// <summary>Physicalizes deterministic liveness-state pages independently of recovery candidate routes.</summary>
internal static class ExecutionLivenessStatePagingPhysicalizer
{
    private const string OperationalStateIdColumn = "operational_state_id";

    public static StorageManifest AddRoutes(StorageManifest manifest) => manifest with
    {
        StorageUnits = manifest.StorageUnits.Select(unit =>
            StringComparer.Ordinal.Equals(unit.Identity.Value, ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind)
                ? AddRoutes(unit)
                : unit).ToArray()
    };

    private static StorageUnit AddRoutes(StorageUnit unit)
    {
        if (unit.PhysicalStorage is not { } storage ||
            storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException(
                "The execution-liveness storage unit requires explicit shared-document physicalization.");
        }

        var byWorkflow = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.ExecutionLivenessStateByWorkflowAndStateId,
            [
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                new IndexField(ElsaRuntimeStorageManifest.ExecutionLivenessOperationalStateIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.IncludedAsNull);
        var all = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.ExecutionLivenessStateByCollectionWorkflowAndStateId,
            [
                new IndexField(ElsaRuntimeStorageManifest.CollectionField),
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                new IndexField(ElsaRuntimeStorageManifest.ExecutionLivenessOperationalStateIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.IncludedAsNull);
        var envelope = new DocumentEnvelopeDefinition();
        var projected = definition.ProjectedColumns
            .Where(column => column.Path != ElsaRuntimeStorageManifest.ExecutionLivenessOperationalStateIdField)
            .Select(column => column.Path switch
            {
                ElsaRuntimeStorageManifest.CollectionField => column with
                {
                    Length = ElsaRuntimeStorageManifest.RuntimeCollectionProjectionLength
                },
                ElsaRuntimeStorageManifest.WorkflowExecutionIdField => column with
                {
                    Length = ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength
                },
                _ => column
            })
            .Append(new ProjectedColumnDefinition(
                OperationalStateIdColumn,
                ElsaRuntimeStorageManifest.ExecutionLivenessOperationalStateIdField,
                PortablePhysicalType.String,
                Length: ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength))
            .ToArray();
        var table = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            projected,
            definition.Indexes
                .Where(index => index.LogicalName is not (
                    ElsaRuntimeStorageManifest.ExecutionLivenessStateByWorkflowAndStateId or
                    ElsaRuntimeStorageManifest.ExecutionLivenessStateByCollectionWorkflowAndStateId))
                .Concat(
                [
                    new PhysicalIndexDefinition(
                        byWorkflow.Identity,
                        [
                            new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                            new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, 1),
                            new PhysicalIndexColumnDefinition(OperationalStateIdColumn, 2),
                            new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 3)
                        ]),
                    new PhysicalIndexDefinition(
                        all.Identity,
                        [
                            new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                            new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.ByCollectionIndex, 1),
                            new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, 2),
                            new PhysicalIndexColumnDefinition(OperationalStateIdColumn, 3),
                            new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 4)
                        ])
                ])
                .ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);

        var routes = new[]
        {
            Route(
                ElsaRuntimeStorageManifest.PageExecutionLivenessStatesByWorkflowExecutionQuery,
                byWorkflow,
                [ElsaRuntimeStorageManifest.ExecutionLivenessOperationalStateIdField],
                ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
            Route(
                ElsaRuntimeStorageManifest.PageExecutionLivenessStatesQuery,
                all,
                [
                    ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                    ElsaRuntimeStorageManifest.ExecutionLivenessOperationalStateIdField
                ],
                ElsaRuntimeStorageManifest.CollectionField)
        };

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(table),
                storage.LogicalIndexes
                    .Where(index => index.Identity is not (
                        ElsaRuntimeStorageManifest.ExecutionLivenessStateByWorkflowAndStateId or
                        ElsaRuntimeStorageManifest.ExecutionLivenessStateByCollectionWorkflowAndStateId))
                    .Concat([byWorkflow, all])
                    .ToArray(),
                storage.BoundedQueries
                    .Where(query => query.Identity is not (
                        ElsaRuntimeStorageManifest.PageExecutionLivenessStatesByWorkflowExecutionQuery or
                        ElsaRuntimeStorageManifest.PageExecutionLivenessStatesQuery))
                    .Concat(routes)
                    .ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }

    private static BoundedQueryDeclaration Route(
        string identity,
        LogicalIndexDeclaration index,
        IReadOnlyList<string> order,
        string predicate) =>
        new(
            identity,
            index.Identity,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            QuerySortSupport.Ascending,
            QueryPagingSupport.Cursor,
            BoundedQueryExecutionClass.ScaleBearing,
            sortFields: order
                .Select(path => new BoundedQuerySortField(path, PhysicalSortDirection.Ascending))
                .ToArray(),
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    predicate,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
            ]);
}
