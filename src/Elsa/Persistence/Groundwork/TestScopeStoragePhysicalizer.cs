using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Persistence.Groundwork;

internal static class TestScopeStoragePhysicalizer
{
    public static StorageManifest AddCompositeRoutes(StorageManifest manifest) => manifest with
    {
        StorageUnits = manifest.StorageUnits.Select(unit =>
            StringComparer.Ordinal.Equals(unit.Identity.Value, ElsaRuntimeStorageManifest.WorkflowTestScopeDocumentKind)
                ? AddCompositeRoutes(unit)
                : unit).ToArray()
    };

    private static StorageUnit AddCompositeRoutes(StorageUnit unit)
    {
        if (unit.PhysicalStorage is not { } storage ||
            storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException("The workflow test-scope storage unit requires an explicit shared-document physicalization.");
        }

        var stateAndScope = Logical(
            ElsaRuntimeStorageManifest.ByStateAndScopeIdIndex,
            new IndexField(ElsaRuntimeStorageManifest.StateField),
            new IndexField(ElsaRuntimeStorageManifest.ScopeIdField));
        var stateScopeAndExpiry = Logical(
            ElsaRuntimeStorageManifest.ByStateAndExpiresAtIndex,
            new IndexField(ElsaRuntimeStorageManifest.StateField),
            new IndexField(ElsaRuntimeStorageManifest.ScopeIdField),
            new IndexField(ElsaRuntimeStorageManifest.ExpiresAtField, IndexValueKind.DateTime));
        var projectedColumns = definition.ProjectedColumns.Select(column =>
            column.LogicalName switch
            {
                ElsaRuntimeStorageManifest.ByStatusIndex =>
                    column with { Length = ElsaRuntimeStorageManifest.RuntimeStatusProjectionLength },
                ElsaRuntimeStorageManifest.ByScopeIdIndex =>
                    column with { Length = WorkflowTestScope.MaximumScopeIdLength },
                _ => column
            }).ToArray();
        var augmentedDefinition = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            projectedColumns,
            definition.Indexes.Concat(
            [
                Physical(stateAndScope.Identity,
                    ElsaRuntimeStorageManifest.ByStatusIndex,
                    ElsaRuntimeStorageManifest.ByScopeIdIndex),
                Physical(stateScopeAndExpiry.Identity,
                    ElsaRuntimeStorageManifest.ByStatusIndex,
                    ElsaRuntimeStorageManifest.ByScopeIdIndex,
                    ElsaRuntimeStorageManifest.ByExpiresAtIndex)
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
                storage.LogicalIndexes.Concat([stateAndScope, stateScopeAndExpiry]).ToArray(),
                storage.BoundedQueries.Concat(
                [
                    StatePageRoute(stateAndScope),
                    ExpiredOpenPageRoute(stateScopeAndExpiry)
                ]).ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }

    private static LogicalIndexDeclaration Logical(string identity, params IndexField[] fields) => new(
        identity,
        fields,
        IndexValueKind.Keyword,
        false);

    private static PhysicalIndexDefinition Physical(string identity, params string[] columns) => new(
        identity,
        [
            new PhysicalIndexColumnDefinition(new DocumentEnvelopeDefinition().StorageScopeColumn, 0),
            .. columns.Select((column, index) => new PhysicalIndexColumnDefinition(column, index + 1))
        ]);

    private static BoundedQueryDeclaration StatePageRoute(LogicalIndexDeclaration index) => new(
        ElsaRuntimeStorageManifest.ListWorkflowTestScopesByStatePageQuery,
        index.Identity,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal, PortableQueryOperation.GreaterThan },
        QuerySortSupport.Ascending,
        QueryPagingSupport.None,
        sortFields:
        [
            new BoundedQuerySortField(ElsaRuntimeStorageManifest.StateField, PhysicalSortDirection.Ascending),
            new BoundedQuerySortField(ElsaRuntimeStorageManifest.ScopeIdField, PhysicalSortDirection.Ascending)
        ],
        predicateFields:
        [
            Equal(ElsaRuntimeStorageManifest.StateField),
            GreaterThan(ElsaRuntimeStorageManifest.ScopeIdField)
        ]);

    private static BoundedQueryDeclaration ExpiredOpenPageRoute(LogicalIndexDeclaration index) => new(
        ElsaRuntimeStorageManifest.ListExpiredOpenWorkflowTestScopesQuery,
        index.Identity,
        new HashSet<PortableQueryOperation>
        {
            PortableQueryOperation.Equal,
            PortableQueryOperation.GreaterThan,
            PortableQueryOperation.LessThanOrEqual
        },
        QuerySortSupport.Ascending,
        QueryPagingSupport.None,
        sortFields:
        [
            new BoundedQuerySortField(ElsaRuntimeStorageManifest.StateField, PhysicalSortDirection.Ascending),
            new BoundedQuerySortField(ElsaRuntimeStorageManifest.ScopeIdField, PhysicalSortDirection.Ascending)
        ],
        predicateFields:
        [
            Equal(ElsaRuntimeStorageManifest.StateField),
            GreaterThan(ElsaRuntimeStorageManifest.ScopeIdField),
            new BoundedQueryPredicateField(
                ElsaRuntimeStorageManifest.ExpiresAtField,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual })
        ]);

    private static BoundedQueryPredicateField Equal(string path) => new(
        path,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal });

    private static BoundedQueryPredicateField GreaterThan(string path) => new(
        path,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.GreaterThan });
}
