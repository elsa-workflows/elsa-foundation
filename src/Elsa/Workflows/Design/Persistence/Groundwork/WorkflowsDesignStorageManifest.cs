using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>
/// Provider-neutral Groundwork storage manifest describing the workflow <b>design</b> document kinds.
/// It mirrors <c>ElsaRuntimeStorageManifest</c> for the design lane: a host selects the concrete provider
/// (SQLite, SQL Server, PostgreSQL, MongoDB, ...) without changing this description, so the same
/// host-selected document provider can back every Elsa module.
/// <para>
/// Each design unit declares a <b>by-collection keyword index</b> — equality on a constant partition value
/// stamped on every document of the kind. This remains the compatibility route for legacy
/// <c>ListAsync</c> callers. Workflow-definition paging uses the explicit bounded routes and linked
/// projection below, so filtering, ordering, counting, and the requested window execute in the provider.
/// </para>
/// </summary>
public static class WorkflowsDesignStorageManifest
{
    public const string SchemaVersion = "1.4.0";

    public const string ByCollectionIndex = "by-collection";
    public const string CollectionField = "collection";
    public const string ListAllQuery = "list-all";

    public const string WorkflowDefinitionByNameIndex = "workflow-definition-by-name";
    public const string WorkflowDefinitionByNameDescendingIndex = "workflow-definition-by-name-descending";
    public const string WorkflowDefinitionByLastModifiedAtIndex = "workflow-definition-by-last-modified-at";
    public const string WorkflowDefinitionByLastModifiedAtDescendingIndex = "workflow-definition-by-last-modified-at-descending";
    public const string WorkflowDefinitionByCreatedAtIndex = "workflow-definition-by-created-at";
    public const string WorkflowDefinitionByCreatedAtDescendingIndex = "workflow-definition-by-created-at-descending";
    public const string PageByNameQuery = "page-by-name";
    public const string PageByLastModifiedAtQuery = "page-by-last-modified-at";
    public const string PageByCreatedAtQuery = "page-by-created-at";
    public const string PageByNameDescendingQuery = "page-by-name-descending";
    public const string PageByLastModifiedAtDescendingQuery = "page-by-last-modified-at-descending";
    public const string PageByCreatedAtDescendingQuery = "page-by-created-at-descending";
    public const string SearchPageByNameQuery = "search-page-by-name";
    public const string SearchPageByLastModifiedAtQuery = "search-page-by-last-modified-at";
    public const string SearchPageByCreatedAtQuery = "search-page-by-created-at";
    public const string SearchPageByNameDescendingQuery = "search-page-by-name-descending";
    public const string SearchPageByLastModifiedAtDescendingQuery = "search-page-by-last-modified-at-descending";
    public const string SearchPageByCreatedAtDescendingQuery = "search-page-by-created-at-descending";
    public const string WorkflowDefinitionIdField = "entity.id";
    public const string WorkflowDefinitionNameField = "entity.name";
    public const string WorkflowDefinitionDescriptionField = "entity.description";
    public const string WorkflowDefinitionDeletedAtField = "entity.deletedAt";
    public const string WorkflowDefinitionCreatedAtField = "entity.createdAt";
    public const string WorkflowDefinitionLastModifiedAtField = "entity.lastModifiedAt";
    public const string WorkflowDefinitionMarkerProjectionField = "markerProjection";
    public const string WorkflowDefinitionControlledProjectionField = "controlledProjection";
    public const int WorkflowDefinitionMarkerProjectionLength = 4_000;

    public const string WorkflowDefinitionDocumentKind = "workflowDefinition";

    /// <summary>Constant partition value stamped on every workflow-definition document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string WorkflowDefinitionCollection = "workflowDefinition";

    public const string WorkflowDefinitionVersionDocumentKind = "workflowDefinitionVersion";

    /// <summary>Constant partition value stamped on every workflow-definition-version document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string WorkflowDefinitionVersionCollection = "workflowDefinitionVersion";

    public const string WorkflowDefinitionDraftDocumentKind = "workflowDefinitionDraft";

    /// <summary>Constant partition value stamped on every workflow-definition-draft document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string WorkflowDefinitionDraftCollection = "workflowDefinitionDraft";

    public const string WorkflowDefinitionVersionLayoutDocumentKind = "workflowDefinitionVersionLayout";

    /// <summary>Constant partition value stamped on every workflow-definition-version-layout document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string WorkflowDefinitionVersionLayoutCollection = "workflowDefinitionVersionLayout";

    public const string WorkflowDefinitionTagSetDocumentKind = "workflowDefinitionTagSet";
    public const string WorkflowDefinitionTagSetCollection = "workflowDefinitionTagSet";
    public const string WorkflowDefinitionTagSetWorkflowDefinitionIdField = "workflowDefinitionId";
    public const string WorkflowDefinitionTagSetByWorkflowDefinitionIdIndex = "workflow-definition-tag-set-by-workflow-definition-id";
    public const string FindWorkflowDefinitionTagSetsByDefinitionIdsQuery = "find-workflow-definition-tag-sets-by-definition-ids";
    public const string WorkflowDefinitionTagAuditDocumentKind = "workflowDefinitionTagAudit";
    public const string WorkflowDefinitionTagAuditCollection = "workflowDefinitionTagAudit";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-workflows-design"),
        new StorageManifestOwner("elsa.workflows.design"),
        new StorageManifestVersion(SchemaVersion),
        [
            WorkflowDefinitionUnit(),
            Unit(
                WorkflowDefinitionVersionDocumentKind,
                "Workflow definition version",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)]),
            Unit(
                WorkflowDefinitionDraftDocumentKind,
                "Workflow definition draft",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)]),
            Unit(
                WorkflowDefinitionVersionLayoutDocumentKind,
                "Workflow definition version layout",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)]),
            WorkflowDefinitionTagSetUnit(),
            Unit(
                WorkflowDefinitionTagAuditDocumentKind,
                "Workflow definition tag audit",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)])
        ],
        new HashSet<string> { "optimistic-concurrency" },
        []);

    /// <summary>
    /// Selects one declared server-side route for the requested bounded list shape. Search routes are
    /// deliberately ordinary because substring predicates can scan; no-search routes stay scale-bearing.
    /// </summary>
    public static string PageQueryIdentity(WorkflowDefinitionListQuery query) =>
        (!string.IsNullOrWhiteSpace(query.Filter.SearchTerm) || query.Filter.MarkerTagClauses is { Count: > 0 } || query.Filter.ControlledTagClauses is { Count: > 0 }, query.SortBy, query.SortDirection) switch
        {
            (false, WorkflowDefinitionSortBy.LastModifiedAt, WorkflowDefinitionSortDirection.Desc) => PageByLastModifiedAtDescendingQuery,
            (false, WorkflowDefinitionSortBy.CreatedAt, WorkflowDefinitionSortDirection.Desc) => PageByCreatedAtDescendingQuery,
            (false, WorkflowDefinitionSortBy.Name, WorkflowDefinitionSortDirection.Desc) => PageByNameDescendingQuery,
            (false, WorkflowDefinitionSortBy.LastModifiedAt, _) => PageByLastModifiedAtQuery,
            (false, WorkflowDefinitionSortBy.CreatedAt, _) => PageByCreatedAtQuery,
            (false, _, _) => PageByNameQuery,
            (true, WorkflowDefinitionSortBy.LastModifiedAt, WorkflowDefinitionSortDirection.Desc) => SearchPageByLastModifiedAtDescendingQuery,
            (true, WorkflowDefinitionSortBy.CreatedAt, WorkflowDefinitionSortDirection.Desc) => SearchPageByCreatedAtDescendingQuery,
            (true, WorkflowDefinitionSortBy.Name, WorkflowDefinitionSortDirection.Desc) => SearchPageByNameDescendingQuery,
            (true, WorkflowDefinitionSortBy.LastModifiedAt, _) => SearchPageByLastModifiedAtQuery,
            (true, WorkflowDefinitionSortBy.CreatedAt, _) => SearchPageByCreatedAtQuery,
            (true, _, _) => SearchPageByNameQuery
        };

    private static StorageUnit WorkflowDefinitionUnit()
    {
        var sharedStorage = new SharedStorageBinding(LegacyGroundworkStorageManifestPhysicalizer.SharedDocumentsLogicalName);
        var envelope = new DocumentEnvelopeDefinition();
        var collectionIndex = CollectionIndex();
        var nameIndex = SortIndex(WorkflowDefinitionByNameIndex, WorkflowDefinitionNameField, IndexValueKind.String);
        var nameDescendingIndex = SortIndex(WorkflowDefinitionByNameDescendingIndex, WorkflowDefinitionNameField, IndexValueKind.String);
        var lastModifiedAtIndex = SortIndex(WorkflowDefinitionByLastModifiedAtIndex, WorkflowDefinitionLastModifiedAtField, IndexValueKind.DateTime);
        var lastModifiedAtDescendingIndex = SortIndex(WorkflowDefinitionByLastModifiedAtDescendingIndex, WorkflowDefinitionLastModifiedAtField, IndexValueKind.DateTime);
        var createdAtIndex = SortIndex(WorkflowDefinitionByCreatedAtIndex, WorkflowDefinitionCreatedAtField, IndexValueKind.DateTime);
        var createdAtDescendingIndex = SortIndex(WorkflowDefinitionByCreatedAtDescendingIndex, WorkflowDefinitionCreatedAtField, IndexValueKind.DateTime);
        var definition = PhysicalTableDefinition.SharedDocuments(
            sharedStorage,
            [
                // Preserve the legacy linked projection unchanged so its existing physical operations remain
                // part of the additive target while the paged routes add their own columns and indexes.
                new ProjectedColumnDefinition(
                    ByCollectionIndex,
                    CollectionField,
                    PortablePhysicalType.String,
                    Length: LegacyGroundworkStorageManifestPhysicalizer.LegacyStringProjectionLength,
                    IsNullable: true),
                StringProjection(WorkflowDefinitionIdField, length: WorkflowDefinitionConstraints.MaximumIdLength, isNullable: false),
                StringProjection(WorkflowDefinitionNameField, length: WorkflowDefinitionConstraints.MaximumNameLength, isNullable: false),
                StringProjection(WorkflowDefinitionDescriptionField),
                new ProjectedColumnDefinition(WorkflowDefinitionDeletedAtField, WorkflowDefinitionDeletedAtField, PortablePhysicalType.DateTime),
                new ProjectedColumnDefinition(WorkflowDefinitionCreatedAtField, WorkflowDefinitionCreatedAtField, PortablePhysicalType.DateTime, IsNullable: false),
                new ProjectedColumnDefinition(WorkflowDefinitionLastModifiedAtField, WorkflowDefinitionLastModifiedAtField, PortablePhysicalType.DateTime, IsNullable: false),
                StringProjection(WorkflowDefinitionMarkerProjectionField, WorkflowDefinitionMarkerProjectionLength),
                StringProjection(WorkflowDefinitionControlledProjectionField, WorkflowDefinitionMarkerProjectionLength)
            ],
            [
                LegacyCollectionPhysicalIndex(envelope, collectionIndex),
                PhysicalIndex(envelope, nameIndex, WorkflowDefinitionNameField),
                PhysicalIndex(envelope, nameDescendingIndex, WorkflowDefinitionNameField, PhysicalSortDirection.Descending),
                PhysicalIndex(envelope, lastModifiedAtIndex, WorkflowDefinitionLastModifiedAtField),
                PhysicalIndex(envelope, lastModifiedAtDescendingIndex, WorkflowDefinitionLastModifiedAtField, PhysicalSortDirection.Descending),
                PhysicalIndex(envelope, createdAtIndex, WorkflowDefinitionCreatedAtField),
                PhysicalIndex(envelope, createdAtDescendingIndex, WorkflowDefinitionCreatedAtField, PhysicalSortDirection.Descending)
            ],
            linkedProjectionLogicalName: "workflowDefinition_projection");
        return new StorageUnit(
            new StorageUnitIdentity(WorkflowDefinitionDocumentKind),
            "Workflow definition",
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [Keyword(ByCollectionIndex, CollectionField)],
            [Query(ListAllQuery, ByCollectionIndex)],
            PhysicalizationPolicy.Portable)
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Dynamic,
                PhysicalStoragePolicy.Explicit(definition),
                [collectionIndex, nameIndex, nameDescendingIndex, lastModifiedAtIndex, lastModifiedAtDescendingIndex, createdAtIndex, createdAtDescendingIndex],
                [
                    LegacyListAllRoute(collectionIndex),
                    PageRoute(PageByNameQuery, nameIndex, WorkflowDefinitionNameField, BoundedQueryExecutionClass.ScaleBearing),
                    PageRoute(PageByNameDescendingQuery, nameDescendingIndex, WorkflowDefinitionNameField, BoundedQueryExecutionClass.ScaleBearing, direction: PhysicalSortDirection.Descending),
                    PageRoute(PageByLastModifiedAtQuery, lastModifiedAtIndex, WorkflowDefinitionLastModifiedAtField, BoundedQueryExecutionClass.ScaleBearing),
                    PageRoute(PageByLastModifiedAtDescendingQuery, lastModifiedAtDescendingIndex, WorkflowDefinitionLastModifiedAtField, BoundedQueryExecutionClass.ScaleBearing, direction: PhysicalSortDirection.Descending),
                    PageRoute(PageByCreatedAtQuery, createdAtIndex, WorkflowDefinitionCreatedAtField, BoundedQueryExecutionClass.ScaleBearing),
                    PageRoute(PageByCreatedAtDescendingQuery, createdAtDescendingIndex, WorkflowDefinitionCreatedAtField, BoundedQueryExecutionClass.ScaleBearing, direction: PhysicalSortDirection.Descending),
                    PageRoute(SearchPageByNameQuery, nameIndex, WorkflowDefinitionNameField, BoundedQueryExecutionClass.Ordinary, supportsContains: true),
                    PageRoute(SearchPageByNameDescendingQuery, nameDescendingIndex, WorkflowDefinitionNameField, BoundedQueryExecutionClass.Ordinary, supportsContains: true, direction: PhysicalSortDirection.Descending),
                    PageRoute(SearchPageByLastModifiedAtQuery, lastModifiedAtIndex, WorkflowDefinitionLastModifiedAtField, BoundedQueryExecutionClass.Ordinary, supportsContains: true),
                    PageRoute(SearchPageByLastModifiedAtDescendingQuery, lastModifiedAtDescendingIndex, WorkflowDefinitionLastModifiedAtField, BoundedQueryExecutionClass.Ordinary, supportsContains: true, direction: PhysicalSortDirection.Descending),
                    PageRoute(SearchPageByCreatedAtQuery, createdAtIndex, WorkflowDefinitionCreatedAtField, BoundedQueryExecutionClass.Ordinary, supportsContains: true),
                    PageRoute(SearchPageByCreatedAtDescendingQuery, createdAtDescendingIndex, WorkflowDefinitionCreatedAtField, BoundedQueryExecutionClass.Ordinary, supportsContains: true, direction: PhysicalSortDirection.Descending)
                ])
        };
    }

    private static StorageUnit WorkflowDefinitionTagSetUnit()
    {
        var definitionIdIndex = new IndexDeclaration(
            WorkflowDefinitionTagSetByWorkflowDefinitionIdIndex,
            [new IndexField(WorkflowDefinitionTagSetWorkflowDefinitionIdField)],
            IndexValueKind.Keyword,
            false,
            true,
            MissingValueBehavior.Excluded,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal, PortableQueryOperation.In },
            IndexPhysicalizationPolicy.Optimized);
        return Unit(
            WorkflowDefinitionTagSetDocumentKind,
            "Workflow definition tag set",
            [Keyword(ByCollectionIndex, CollectionField), definitionIdIndex],
            [
                Query(ListAllQuery, ByCollectionIndex),
                new PortableQueryDeclaration(
                    FindWorkflowDefinitionTagSetsByDefinitionIdsQuery,
                    definitionIdIndex.Identity,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal, PortableQueryOperation.In },
                    QuerySortSupport.None,
                    QueryPagingSupport.Offset)
            ]);
    }

    private static LogicalIndexDeclaration SortIndex(string identity, string sortField, IndexValueKind sortValueKind) => new(
        identity,
        [new IndexField(sortField, sortValueKind), new IndexField(WorkflowDefinitionIdField)],
        IndexValueKind.Keyword,
        isUnique: false,
        MissingValueBehavior.Excluded);

    private static LogicalIndexDeclaration CollectionIndex() => new(
        ByCollectionIndex,
        [new IndexField(CollectionField)],
        IndexValueKind.Keyword,
        isUnique: false,
        MissingValueBehavior.Excluded);

    private static BoundedQueryDeclaration LegacyListAllRoute(LogicalIndexDeclaration index) => new(
        ListAllQuery,
        index.Identity,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.None,
        QueryPagingSupport.Offset,
        BoundedQueryExecutionClass.Ordinary);

    private static PhysicalIndexDefinition PhysicalIndex(
        DocumentEnvelopeDefinition envelope,
        LogicalIndexDeclaration index,
        string sortField,
        PhysicalSortDirection direction = PhysicalSortDirection.Ascending) => new(
        index.Identity,
        [
            new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
            new PhysicalIndexColumnDefinition(sortField, 1, direction),
            new PhysicalIndexColumnDefinition(WorkflowDefinitionIdField, 2)
        ]);

    private static PhysicalIndexDefinition LegacyCollectionPhysicalIndex(
        DocumentEnvelopeDefinition envelope,
        LogicalIndexDeclaration index) => new(
        index.Identity,
        [
            new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
            new PhysicalIndexColumnDefinition(ByCollectionIndex, 1)
        ],
        missingValueBehavior: MissingValueBehavior.Excluded);

    private static BoundedQueryDeclaration PageRoute(
        string identity,
        LogicalIndexDeclaration index,
        string sortField,
        BoundedQueryExecutionClass executionClass,
        bool supportsContains = false,
        PhysicalSortDirection direction = PhysicalSortDirection.Ascending)
    {
        var operations = new HashSet<PortableQueryOperation>
        {
            PortableQueryOperation.Equal,
            PortableQueryOperation.NotEqual,
            PortableQueryOperation.In
        };
        if (supportsContains)
        {
            operations.Add(PortableQueryOperation.Contains);
            operations.Add(PortableQueryOperation.NotContains);
        }

        var residuals = new List<BoundedQueryResidualPredicateField>
        {
            Residual(
                WorkflowDefinitionDescriptionField,
                IndexValueKind.String,
                supportsContains
                    ? [PortableQueryOperation.Equal, PortableQueryOperation.Contains]
                    : [PortableQueryOperation.Equal]),
            Residual(WorkflowDefinitionDeletedAtField, IndexValueKind.DateTime, PortableQueryOperation.Equal, PortableQueryOperation.NotEqual)
        };
        if (supportsContains)
        {
            residuals.Add(Residual(
                WorkflowDefinitionMarkerProjectionField,
                IndexValueKind.String,
                PortableQueryOperation.Contains,
                PortableQueryOperation.NotContains));
            residuals.Add(Residual(
                WorkflowDefinitionControlledProjectionField,
                IndexValueKind.String,
                PortableQueryOperation.Contains,
                PortableQueryOperation.NotContains));
        }
        if (sortField != WorkflowDefinitionNameField)
            residuals.Add(Residual(
                WorkflowDefinitionNameField,
                IndexValueKind.String,
                supportsContains
                    ? [PortableQueryOperation.Equal, PortableQueryOperation.In, PortableQueryOperation.Contains]
                    : [PortableQueryOperation.Equal, PortableQueryOperation.In]));

        return new BoundedQueryDeclaration(
            identity,
            index.Identity,
            operations,
            direction == PhysicalSortDirection.Descending
                ? QuerySortSupport.Descending
                : QuerySortSupport.Ascending,
            QueryPagingSupport.Offset,
            executionClass,
            supportsDisjunction: supportsContains,
            supportsTotalCount: true,
            sortFields:
            [
                new BoundedQuerySortField(sortField, direction),
                new BoundedQuerySortField(WorkflowDefinitionIdField, PhysicalSortDirection.Ascending)
            ],
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    sortField,
                    supportsContains && sortField == WorkflowDefinitionNameField
                        ? new HashSet<PortableQueryOperation>
                        {
                            PortableQueryOperation.Equal,
                            PortableQueryOperation.In,
                            PortableQueryOperation.Contains
                        }
                        : new HashSet<PortableQueryOperation>
                        {
                            PortableQueryOperation.Equal,
                            PortableQueryOperation.In
                        }),
                new BoundedQueryPredicateField(
                    WorkflowDefinitionIdField,
                    supportsContains
                        ? new HashSet<PortableQueryOperation>
                        {
                            PortableQueryOperation.Equal,
                            PortableQueryOperation.In,
                            PortableQueryOperation.Contains
                        }
                        : new HashSet<PortableQueryOperation>
                        {
                            PortableQueryOperation.Equal,
                            PortableQueryOperation.In
                        })
            ],
            residualPredicateFields: residuals);
    }

    private static BoundedQueryResidualPredicateField Residual(
        string path,
        IndexValueKind valueKind,
        params PortableQueryOperation[] operations) =>
        new(path, valueKind, operations.ToHashSet());

    private static ProjectedColumnDefinition StringProjection(
        string path,
        int? length = null,
        bool isNullable = true) =>
        new(path, path, PortablePhysicalType.String, Length: length, IsNullable: isNullable);

    private static StorageUnit Unit(
        string documentKind,
        string label,
        IndexDeclaration[] indexes,
        PortableQueryDeclaration[] queries) => new(
        new StorageUnitIdentity(documentKind),
        label,
        StorageIntent.PortableDocument(),
        LifecyclePolicy.Mutable,
        IdentityPolicy.StringId(),
        TenancyPolicy.Scoped,
        ConcurrencyPolicy.Optimistic(),
        SerializationPolicy.Json(),
        indexes,
        queries,
        PhysicalizationPolicy.Portable);

    private static PortableQueryDeclaration Query(string name, string indexName) => new(
        name,
        indexName,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.None,
        QueryPagingSupport.Offset);

    private static IndexDeclaration Keyword(string identity, string field) => new(
        identity,
        [new IndexField(field)],
        IndexValueKind.Keyword,
        false,
        true,
        MissingValueBehavior.Excluded,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        IndexPhysicalizationPolicy.Optimized);
}
