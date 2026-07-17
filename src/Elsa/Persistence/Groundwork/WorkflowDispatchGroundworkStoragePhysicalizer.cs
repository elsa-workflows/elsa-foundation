using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Persistence.Groundwork;

internal static class WorkflowDispatchGroundworkStoragePhysicalizer
{
    public static StorageManifest AddCompositeRoutes(StorageManifest manifest) => manifest with
    {
        StorageUnits = manifest.StorageUnits.Select(unit =>
            StringComparer.Ordinal.Equals(unit.Identity.Value, ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind)
                ? AddCompositeRoutes(unit)
                : unit).ToArray()
    };

    private static StorageUnit AddCompositeRoutes(StorageUnit unit)
    {
        if (unit.PhysicalStorage is not { } storage ||
            storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException("The workflow-dispatch storage unit requires an explicit shared-document physicalization.");
        }

        var parentAndStatus = CompositeIndex(
            ElsaRuntimeStorageManifest.ByParentWorkflowExecutionAndStatusIndex,
            ElsaRuntimeStorageManifest.ParentWorkflowExecutionIdField,
            ElsaRuntimeStorageManifest.StatusField);
        var childAndStatus = CompositeIndex(
            ElsaRuntimeStorageManifest.ByChildWorkflowExecutionAndStatusIndex,
            ElsaRuntimeStorageManifest.ChildWorkflowExecutionIdField,
            ElsaRuntimeStorageManifest.StatusField);
        var sortedRoutes = new[]
        {
            SortedRoute(
                ElsaRuntimeStorageManifest.PageWorkflowDispatchesByParentQuery,
                "by-parent-workflow-execution-created-at-dispatch-id",
                [ElsaRuntimeStorageManifest.ParentWorkflowExecutionIdField],
                [ElsaRuntimeStorageManifest.ByParentWorkflowExecutionIndex]),
            SortedRoute(
                ElsaRuntimeStorageManifest.PageWorkflowDispatchesByStatusQuery,
                "by-status-created-at-dispatch-id",
                [ElsaRuntimeStorageManifest.StatusField],
                [ElsaRuntimeStorageManifest.ByStatusIndex]),
            SortedRoute(
                ElsaRuntimeStorageManifest.PageWorkflowDispatchesByTestScopeQuery,
                "by-test-scope-created-at-dispatch-id",
                [ElsaRuntimeStorageManifest.TestScopeIdField],
                [ElsaRuntimeStorageManifest.ByTestScopeIndex]),
            SortedRoute(
                ElsaRuntimeStorageManifest.PageWorkflowDispatchesByParentAndStatusQuery,
                "by-parent-workflow-execution-status-created-at-dispatch-id",
                [
                    ElsaRuntimeStorageManifest.ParentWorkflowExecutionIdField,
                    ElsaRuntimeStorageManifest.StatusField
                ],
                [
                    ElsaRuntimeStorageManifest.ByParentWorkflowExecutionIndex,
                    ElsaRuntimeStorageManifest.ByStatusIndex
                ]),
            SortedRoute(
                ElsaRuntimeStorageManifest.PageWorkflowDispatchesByParentAndTestScopeQuery,
                "by-parent-workflow-execution-test-scope-created-at-dispatch-id",
                [
                    ElsaRuntimeStorageManifest.ParentWorkflowExecutionIdField,
                    ElsaRuntimeStorageManifest.TestScopeIdField
                ],
                [
                    ElsaRuntimeStorageManifest.ByParentWorkflowExecutionIndex,
                    ElsaRuntimeStorageManifest.ByTestScopeIndex
                ]),
            SortedRoute(
                ElsaRuntimeStorageManifest.PageWorkflowDispatchesByStatusAndTestScopeQuery,
                "by-status-test-scope-created-at-dispatch-id",
                [
                    ElsaRuntimeStorageManifest.StatusField,
                    ElsaRuntimeStorageManifest.TestScopeIdField
                ],
                [
                    ElsaRuntimeStorageManifest.ByStatusIndex,
                    ElsaRuntimeStorageManifest.ByTestScopeIndex
                ]),
            SortedRoute(
                ElsaRuntimeStorageManifest.PageWorkflowDispatchesByParentStatusAndTestScopeQuery,
                "by-parent-workflow-execution-status-test-scope-created-at-dispatch-id",
                [
                    ElsaRuntimeStorageManifest.ParentWorkflowExecutionIdField,
                    ElsaRuntimeStorageManifest.StatusField,
                    ElsaRuntimeStorageManifest.TestScopeIdField
                ],
                [
                    ElsaRuntimeStorageManifest.ByParentWorkflowExecutionIndex,
                    ElsaRuntimeStorageManifest.ByStatusIndex,
                    ElsaRuntimeStorageManifest.ByTestScopeIndex
                ])
        };
        var augmentedDefinition = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            definition.ProjectedColumns,
            definition.Indexes.Concat(
            [
                CompositePhysicalIndex(
                    parentAndStatus.Identity,
                    ElsaRuntimeStorageManifest.ByParentWorkflowExecutionIndex,
                    ElsaRuntimeStorageManifest.ByStatusIndex),
                CompositePhysicalIndex(
                    childAndStatus.Identity,
                    ElsaRuntimeStorageManifest.ByChildWorkflowExecutionIndex,
                    ElsaRuntimeStorageManifest.ByStatusIndex),
                .. sortedRoutes.Select(route => route.PhysicalIndex)
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
                storage.LogicalIndexes.Concat(
                    [parentAndStatus, childAndStatus, .. sortedRoutes.Select(route => route.LogicalIndex)]).ToArray(),
                storage.BoundedQueries.Concat(
                [
                    CompositeQuery(ElsaRuntimeStorageManifest.ListWorkflowDispatchesByParentAndStatusQuery, parentAndStatus),
                    CompositeQuery(ElsaRuntimeStorageManifest.ListWorkflowDispatchesByChildAndStatusQuery, childAndStatus),
                    .. sortedRoutes.Select(route => route.Query)
                ]).ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }

    private static LogicalIndexDeclaration CompositeIndex(string identity, params string[] fields) => new(
        identity,
        fields.Select(field => new IndexField(field)).ToArray(),
        IndexValueKind.Keyword,
        isUnique: false,
        MissingValueBehavior.Excluded);

    private static PhysicalIndexDefinition CompositePhysicalIndex(
        string identity,
        string firstProjectedColumn,
        string secondProjectedColumn) => new(
        identity,
        [
            new PhysicalIndexColumnDefinition(new DocumentEnvelopeDefinition().StorageScopeColumn, 0),
            new PhysicalIndexColumnDefinition(firstProjectedColumn, 1),
            new PhysicalIndexColumnDefinition(secondProjectedColumn, 2)
        ]);

    private static BoundedQueryDeclaration CompositeQuery(
        string identity,
        LogicalIndexDeclaration index) => new(
        identity,
        index.Identity,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.None,
        QueryPagingSupport.Offset,
        predicateFields: index.Fields
            .Select(field => new BoundedQueryPredicateField(
                field.Path,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }))
            .ToArray());

    private static SortedDispatchRoute SortedRoute(
        string queryIdentity,
        string indexIdentity,
        IReadOnlyList<string> filterFields,
        IReadOnlyList<string> filterProjectedColumns)
    {
        var fields = filterFields
            .Select(field => new IndexField(field))
            .Concat(
            [
                new IndexField(ElsaRuntimeStorageManifest.WorkflowDispatchCreatedAtField, IndexValueKind.DateTime),
                new IndexField(ElsaRuntimeStorageManifest.WorkflowDispatchIdField)
            ])
            .ToArray();
        var logicalIndex = new LogicalIndexDeclaration(
            indexIdentity,
            fields,
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var physicalIndex = new PhysicalIndexDefinition(
            indexIdentity,
            [
                new PhysicalIndexColumnDefinition(new DocumentEnvelopeDefinition().StorageScopeColumn, 0),
                .. filterProjectedColumns.Select((column, index) =>
                    new PhysicalIndexColumnDefinition(column, index + 1)),
                new PhysicalIndexColumnDefinition(
                    ElsaRuntimeStorageManifest.ByCreatedAtIndex,
                    filterProjectedColumns.Count + 1),
                new PhysicalIndexColumnDefinition(
                    ElsaRuntimeStorageManifest.ByDispatchIdIndex,
                    filterProjectedColumns.Count + 2)
            ]);
        var query = new BoundedQueryDeclaration(
            queryIdentity,
            logicalIndex.Identity,
            new HashSet<PortableQueryOperation>
            {
                PortableQueryOperation.Equal,
                PortableQueryOperation.GreaterThan
            },
            QuerySortSupport.Ascending,
            QueryPagingSupport.None,
            sortFields: fields
                .Select(field => new BoundedQuerySortField(field.Path, PhysicalSortDirection.Ascending))
                .ToArray(),
            predicateFields:
            [
                .. filterFields.Select(field => new BoundedQueryPredicateField(
                    field,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })),
                new BoundedQueryPredicateField(
                    ElsaRuntimeStorageManifest.WorkflowDispatchCreatedAtField,
                    new HashSet<PortableQueryOperation>
                    {
                        PortableQueryOperation.Equal,
                        PortableQueryOperation.GreaterThan
                    }),
                new BoundedQueryPredicateField(
                    ElsaRuntimeStorageManifest.WorkflowDispatchIdField,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.GreaterThan })
            ]);
        return new SortedDispatchRoute(logicalIndex, physicalIndex, query);
    }

    private sealed record SortedDispatchRoute(
        LogicalIndexDeclaration LogicalIndex,
        PhysicalIndexDefinition PhysicalIndex,
        BoundedQueryDeclaration Query);
}
