using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>Installs the certified keyset browse route for workflow definitions.</summary>
internal static class WorkflowDefinitionPagingStoragePhysicalizer
{
    private const string TableName = "workflow_definitions";
    private const string CollectionColumn = "collection";
    private const string LastModifiedAtColumn = "last_modified_at";
    private const string DefinitionIdColumn = "definition_id";
    private const string DeletedAtColumn = "deleted_at";
    private const string NameColumn = "name";
    private const string DescriptionColumn = "description";
    private const string FolderIdColumn = "folder_id";

    private static readonly IReadOnlySet<PortableQueryOperation> Lifecycle =
        new HashSet<PortableQueryOperation>
        {
            PortableQueryOperation.Equal,
            PortableQueryOperation.NotEqual
        };
    private static readonly IReadOnlySet<PortableQueryOperation> Contains =
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Contains };

    public static StorageManifest AddRoute(StorageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest with
        {
            StorageUnits = manifest.StorageUnits.Select(unit =>
                StringComparer.Ordinal.Equals(unit.Identity.Value, WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind)
                    ? Physicalize(unit)
                    : unit).ToArray()
        };
    }

    private static StorageUnit Physicalize(StorageUnit unit)
    {
        var storage = unit.PhysicalStorage ?? throw new InvalidOperationException(
            "Workflow-definition paging requires an existing physical storage declaration.");
        var collectionIndex = storage.LogicalIndexes.Single(index =>
            StringComparer.Ordinal.Equals(index.Identity, WorkflowsDesignStorageManifest.ByCollectionIndex));
        var browseIndex = new LogicalIndexDeclaration(
            WorkflowsDesignStorageManifest.WorkflowDefinitionBrowseOrderIndex,
            [
                new IndexField(WorkflowsDesignStorageManifest.WorkflowDefinitionLastModifiedAtField, IndexValueKind.String),
                new IndexField(WorkflowsDesignStorageManifest.WorkflowDefinitionIdField, IndexValueKind.Keyword)
            ],
            IndexValueKind.String,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var folderAllBrowseIndex = new LogicalIndexDeclaration(
            WorkflowsDesignStorageManifest.WorkflowDefinitionFolderAllBrowseOrderIndex,
            [
                new IndexField(WorkflowsDesignStorageManifest.WorkflowDefinitionFolderIdField, IndexValueKind.Keyword),
                new IndexField(WorkflowsDesignStorageManifest.WorkflowDefinitionLastModifiedAtField, IndexValueKind.String),
                new IndexField(WorkflowsDesignStorageManifest.WorkflowDefinitionIdField, IndexValueKind.Keyword)
            ],
            IndexValueKind.String,
            isUnique: false,
            MissingValueBehavior.IncludedAsNull);
        var envelope = new DocumentEnvelopeDefinition();
        var definition = PhysicalTableDefinition.PhysicalEntityTable(
            TableName,
            [
                Projected(CollectionColumn, WorkflowsDesignStorageManifest.CollectionField, PortablePhysicalType.String),
                Projected(LastModifiedAtColumn, WorkflowsDesignStorageManifest.WorkflowDefinitionLastModifiedAtField, PortablePhysicalType.String, 40),
                Projected(DefinitionIdColumn, WorkflowsDesignStorageManifest.WorkflowDefinitionIdField, PortablePhysicalType.String),
                Projected(
                    DeletedAtColumn,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDeletedAtField,
                    PortablePhysicalType.String,
                    40),
                Projected(NameColumn, WorkflowsDesignStorageManifest.WorkflowDefinitionNameField, PortablePhysicalType.String),
                Projected(DescriptionColumn, WorkflowsDesignStorageManifest.WorkflowDefinitionDescriptionField, PortablePhysicalType.String),
                Projected(
                    FolderIdColumn,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionFolderIdField,
                    PortablePhysicalType.String,
                    WorkflowFolderNames.MaximumIdentifierLength)
            ],
            envelope,
            [
                new PhysicalIndexDefinition(
                    collectionIndex.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(CollectionColumn, 1)
                    ]),
                new PhysicalIndexDefinition(
                    browseIndex.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(LastModifiedAtColumn, 1, PhysicalSortDirection.Descending),
                        new PhysicalIndexColumnDefinition(DefinitionIdColumn, 2),
                        new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 3)
                    ]),
                new PhysicalIndexDefinition(
                    folderAllBrowseIndex.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(FolderIdColumn, 1),
                        new PhysicalIndexColumnDefinition(LastModifiedAtColumn, 2, PhysicalSortDirection.Descending),
                        new PhysicalIndexColumnDefinition(DefinitionIdColumn, 3),
                        new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 4)
                    ],
                    missingValueBehavior: MissingValueBehavior.IncludedAsNull)
            ]);
        var browseQuery = new BoundedQueryDeclaration(
            WorkflowsDesignStorageManifest.PageWorkflowDefinitionsQuery,
            browseIndex.Identity,
            new HashSet<PortableQueryOperation>
            {
                PortableQueryOperation.Equal,
                PortableQueryOperation.NotEqual
            },
            QuerySortSupport.Descending,
            QueryPagingSupport.Cursor,
            BoundedQueryExecutionClass.ScaleBearing,
            supportsDisjunction: true,
            sortFields:
            [
                new BoundedQuerySortField(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionLastModifiedAtField,
                    PhysicalSortDirection.Descending),
                new BoundedQuerySortField(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionIdField,
                    PhysicalSortDirection.Ascending)
            ],
            residualPredicateFields:
            [
                Residual(WorkflowsDesignStorageManifest.WorkflowDefinitionDeletedAtField, IndexValueKind.String, Lifecycle),
                Residual(WorkflowsDesignStorageManifest.WorkflowDefinitionFolderIdField, IndexValueKind.String, Lifecycle)
            ]);
        // Substring matching is finite and provider-materialization-bounded, but MongoDB cannot certify its
        // case-insensitive regex semantics as an indexed B-tree operation. Keep the no-search browse route
        // scale-bearing and admit search as a separate ordinary bounded cursor route, matching the Secrets lane.
        var searchQuery = new BoundedQueryDeclaration(
            WorkflowsDesignStorageManifest.SearchWorkflowDefinitionsQuery,
            browseIndex.Identity,
            new HashSet<PortableQueryOperation>
            {
                PortableQueryOperation.Equal,
                PortableQueryOperation.NotEqual,
                PortableQueryOperation.Contains
            },
            QuerySortSupport.Descending,
            QueryPagingSupport.Cursor,
            BoundedQueryExecutionClass.Ordinary,
            supportsDisjunction: true,
            sortFields:
            [
                new BoundedQuerySortField(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionLastModifiedAtField,
                    PhysicalSortDirection.Descending),
                new BoundedQuerySortField(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionIdField,
                    PhysicalSortDirection.Ascending)
            ],
            residualPredicateFields:
            [
                Residual(WorkflowsDesignStorageManifest.WorkflowDefinitionDeletedAtField, IndexValueKind.String, Lifecycle),
                Residual(WorkflowsDesignStorageManifest.WorkflowDefinitionNameField, IndexValueKind.String, Contains),
                Residual(WorkflowsDesignStorageManifest.WorkflowDefinitionDescriptionField, IndexValueKind.String, Contains),
                Residual(WorkflowsDesignStorageManifest.WorkflowDefinitionIdField, IndexValueKind.String, Contains),
                Residual(WorkflowsDesignStorageManifest.WorkflowDefinitionFolderIdField, IndexValueKind.String, Lifecycle)
            ]);
        var folderBrowseQuery = FolderBrowseQuery(
            WorkflowsDesignStorageManifest.PageWorkflowDefinitionsByFolderQuery,
            folderAllBrowseIndex,
            includeLifecycleFilter: true);
        var folderAllBrowseQuery = FolderBrowseQuery(
            WorkflowsDesignStorageManifest.PageAllWorkflowDefinitionsByFolderQuery,
            folderAllBrowseIndex,
            includeLifecycleFilter: false);

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(definition),
                storage.LogicalIndexes
                    .Where(index =>
                        !StringComparer.Ordinal.Equals(index.Identity, browseIndex.Identity) &&
                        !StringComparer.Ordinal.Equals(index.Identity, folderAllBrowseIndex.Identity))
                    .Append(browseIndex)
                    .Append(folderAllBrowseIndex)
                    .ToArray(),
                storage.BoundedQueries
                    .Where(query => !StringComparer.Ordinal.Equals(
                        query.Identity,
                        WorkflowsDesignStorageManifest.PageWorkflowDefinitionsQuery) &&
                        !StringComparer.Ordinal.Equals(
                            query.Identity,
                            WorkflowsDesignStorageManifest.SearchWorkflowDefinitionsQuery) &&
                        !StringComparer.Ordinal.Equals(
                            query.Identity,
                            WorkflowsDesignStorageManifest.PageWorkflowDefinitionsByFolderQuery) &&
                        !StringComparer.Ordinal.Equals(
                            query.Identity,
                            WorkflowsDesignStorageManifest.PageAllWorkflowDefinitionsByFolderQuery))
                    .Append(browseQuery)
                    .Append(searchQuery)
                    .Append(folderBrowseQuery)
                    .Append(folderAllBrowseQuery)
                    .ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }

    private static ProjectedColumnDefinition Projected(
        string logicalName,
        string path,
        PortablePhysicalType type,
        int? length = null) =>
        new(
            logicalName,
            path,
            type,
            Length: type == PortablePhysicalType.String
                ? length ?? LegacyGroundworkStorageManifestPhysicalizer.LegacyStringProjectionLength
                : null);

    private static BoundedQueryDeclaration FolderBrowseQuery(
        string identity,
        LogicalIndexDeclaration index,
        bool includeLifecycleFilter)
    {
        var predicates = new List<BoundedQueryPredicateField>
        {
            new(
                WorkflowsDesignStorageManifest.WorkflowDefinitionFolderIdField,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
        };
        // FolderId remains the leading native index predicate and the requested chronology is covered
        // by the remaining key columns. DeletedAt is intentionally admitted as a provider-side residual:
        // a deleted query needs "not null", which cannot constrain the second index prefix by equality,
        // while deriving a new discriminator would misclassify legacy canonical JSON during projection.
        return new BoundedQueryDeclaration(
            identity,
            index.Identity,
            new HashSet<PortableQueryOperation>
            {
                PortableQueryOperation.Equal,
                PortableQueryOperation.NotEqual
            },
            QuerySortSupport.Descending,
            QueryPagingSupport.Cursor,
            BoundedQueryExecutionClass.ScaleBearing,
            sortFields:
            [
                new BoundedQuerySortField(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionLastModifiedAtField,
                    PhysicalSortDirection.Descending),
                new BoundedQuerySortField(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionIdField,
                    PhysicalSortDirection.Ascending)
            ],
            predicateFields: predicates,
            residualPredicateFields: includeLifecycleFilter
                ? [Residual(WorkflowsDesignStorageManifest.WorkflowDefinitionDeletedAtField, IndexValueKind.String, Lifecycle)]
                : []);
    }

    private static BoundedQueryResidualPredicateField Residual(
        string path,
        IndexValueKind kind,
        IReadOnlySet<PortableQueryOperation> operations) => new(path, kind, operations);
}
