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
                    ElsaRuntimeStorageManifest.ByStatusIndex)
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
                storage.LogicalIndexes.Concat([parentAndStatus, childAndStatus]).ToArray(),
                storage.BoundedQueries.Concat(
                [
                    CompositeQuery(ElsaRuntimeStorageManifest.ListWorkflowDispatchesByParentAndStatusQuery, parentAndStatus),
                    CompositeQuery(ElsaRuntimeStorageManifest.ListWorkflowDispatchesByChildAndStatusQuery, childAndStatus)
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
}
