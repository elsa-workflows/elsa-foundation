using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>
/// Provider-neutral Groundwork storage manifest describing the workflow <b>design</b> document kinds.
/// It mirrors <c>ElsaRuntimeStorageManifest</c> for the design lane: a host selects the concrete provider
/// (SQLite, SQL Server, PostgreSQL, MongoDB, ...) without changing this description, so the same
/// host-selected document provider can back every Elsa module.
/// <para>
/// Each design unit declares a <b>by-collection keyword index</b> — equality on a constant partition value
/// stamped on every document of the kind. Groundwork's portable document query supports equality-on-index
/// today, so this index lets the closed <c>Query&lt;TEntity&gt;</c> spec enumerate a kind through the
/// universally-supported equality contract; the richer operators (IN, substring, OR, ordering) are applied
/// by <c>GroundworkReadStore&lt;TEntity&gt;</c>'s in-memory fallback until Groundwork ships the
/// capability-spec uplift, at which point native index declarations can be added without changing the ports.
/// </para>
/// </summary>
public static class WorkflowsDesignStorageManifest
{
    public const string SchemaVersion = "1.0.0";

    public const string ByCollectionIndex = "by-collection";
    public const string CollectionField = "collection";
    public const string ListAllQuery = "list-all";

    public const string WorkflowDefinitionsTable = "workflow_definitions";
    public const string WorkflowDefinitionByNameIndex = "workflow-definition-by-name";
    public const string WorkflowDefinitionByLastModifiedAtIndex = "workflow-definition-by-last-modified-at";
    public const string WorkflowDefinitionByCreatedAtIndex = "workflow-definition-by-created-at";
    public const string PageByNameQuery = "page-by-name";
    public const string PageByLastModifiedAtQuery = "page-by-last-modified-at";
    public const string PageByCreatedAtQuery = "page-by-created-at";
    public const string SearchPageByNameQuery = "search-page-by-name";
    public const string SearchPageByLastModifiedAtQuery = "search-page-by-last-modified-at";
    public const string SearchPageByCreatedAtQuery = "search-page-by-created-at";
    public const string WorkflowDefinitionIdField = "entity.id";
    public const string WorkflowDefinitionNameField = "entity.name";
    public const string WorkflowDefinitionDescriptionField = "entity.description";
    public const string WorkflowDefinitionDeletedAtField = "entity.deletedAt";
    public const string WorkflowDefinitionCreatedAtField = "entity.createdAt";
    public const string WorkflowDefinitionLastModifiedAtField = "entity.lastModifiedAt";

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
                [Query(ListAllQuery, ByCollectionIndex)])
        ],
        new HashSet<string> { "optimistic-concurrency" },
        []);

    /// <summary>
    /// Selects one declared server-side route for the requested bounded list shape. Search routes are
    /// deliberately ordinary because substring predicates can scan; no-search routes stay scale-bearing.
    /// </summary>
    public static string PageQueryIdentity(WorkflowDefinitionListQuery query) =>
        (!string.IsNullOrWhiteSpace(query.Filter.SearchTerm), query.SortBy) switch
        {
            (false, WorkflowDefinitionSortBy.LastModifiedAt) => PageByLastModifiedAtQuery,
            (false, WorkflowDefinitionSortBy.CreatedAt) => PageByCreatedAtQuery,
            (false, _) => PageByNameQuery,
            (true, WorkflowDefinitionSortBy.LastModifiedAt) => SearchPageByLastModifiedAtQuery,
            (true, WorkflowDefinitionSortBy.CreatedAt) => SearchPageByCreatedAtQuery,
            (true, _) => SearchPageByNameQuery
        };

    private static StorageUnit WorkflowDefinitionUnit()
    {
        var envelope = new DocumentEnvelopeDefinition();
        var nameIndex = SortIndex(WorkflowDefinitionByNameIndex, WorkflowDefinitionNameField, IndexValueKind.String);
        var lastModifiedAtIndex = SortIndex(WorkflowDefinitionByLastModifiedAtIndex, WorkflowDefinitionLastModifiedAtField, IndexValueKind.DateTime);
        var createdAtIndex = SortIndex(WorkflowDefinitionByCreatedAtIndex, WorkflowDefinitionCreatedAtField, IndexValueKind.DateTime);
        var definition = PhysicalTableDefinition.PhysicalEntityTable(
            WorkflowDefinitionsTable,
            [
                // Both fields participate in portable composite indexes; keep their SQL Server keys bounded.
                StringProjection(WorkflowDefinitionIdField, length: 128, isNullable: false),
                StringProjection(WorkflowDefinitionNameField, length: 128, isNullable: false),
                StringProjection(WorkflowDefinitionDescriptionField),
                new ProjectedColumnDefinition(WorkflowDefinitionDeletedAtField, WorkflowDefinitionDeletedAtField, PortablePhysicalType.DateTime),
                new ProjectedColumnDefinition(WorkflowDefinitionCreatedAtField, WorkflowDefinitionCreatedAtField, PortablePhysicalType.DateTime, IsNullable: false),
                new ProjectedColumnDefinition(WorkflowDefinitionLastModifiedAtField, WorkflowDefinitionLastModifiedAtField, PortablePhysicalType.DateTime, IsNullable: false)
            ],
            envelope,
            [
                PhysicalIndex(envelope, nameIndex, WorkflowDefinitionNameField),
                PhysicalIndex(envelope, lastModifiedAtIndex, WorkflowDefinitionLastModifiedAtField),
                PhysicalIndex(envelope, createdAtIndex, WorkflowDefinitionCreatedAtField)
            ]);
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
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(definition),
                [nameIndex, lastModifiedAtIndex, createdAtIndex],
                [
                    PageRoute(PageByNameQuery, nameIndex, WorkflowDefinitionNameField, BoundedQueryExecutionClass.ScaleBearing),
                    PageRoute(PageByLastModifiedAtQuery, lastModifiedAtIndex, WorkflowDefinitionLastModifiedAtField, BoundedQueryExecutionClass.ScaleBearing),
                    PageRoute(PageByCreatedAtQuery, createdAtIndex, WorkflowDefinitionCreatedAtField, BoundedQueryExecutionClass.ScaleBearing),
                    PageRoute(SearchPageByNameQuery, nameIndex, WorkflowDefinitionNameField, BoundedQueryExecutionClass.Ordinary, supportsContains: true),
                    PageRoute(SearchPageByLastModifiedAtQuery, lastModifiedAtIndex, WorkflowDefinitionLastModifiedAtField, BoundedQueryExecutionClass.Ordinary, supportsContains: true),
                    PageRoute(SearchPageByCreatedAtQuery, createdAtIndex, WorkflowDefinitionCreatedAtField, BoundedQueryExecutionClass.Ordinary, supportsContains: true)
                ])
        };
    }

    private static LogicalIndexDeclaration SortIndex(string identity, string sortField, IndexValueKind sortValueKind) => new(
        identity,
        [new IndexField(sortField, sortValueKind), new IndexField(WorkflowDefinitionIdField)],
        IndexValueKind.Keyword,
        isUnique: false,
        MissingValueBehavior.Excluded);

    private static PhysicalIndexDefinition PhysicalIndex(
        DocumentEnvelopeDefinition envelope,
        LogicalIndexDeclaration index,
        string sortField) => new(
        index.Identity,
        [
            new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
            new PhysicalIndexColumnDefinition(sortField, 1),
            new PhysicalIndexColumnDefinition(WorkflowDefinitionIdField, 2)
        ]);

    private static BoundedQueryDeclaration PageRoute(
        string identity,
        LogicalIndexDeclaration index,
        string sortField,
        BoundedQueryExecutionClass executionClass,
        bool supportsContains = false)
    {
        var operations = new HashSet<PortableQueryOperation>
        {
            PortableQueryOperation.Equal,
            PortableQueryOperation.NotEqual,
            PortableQueryOperation.In
        };
        if (supportsContains)
            operations.Add(PortableQueryOperation.Contains);

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
            QuerySortSupport.Both,
            QueryPagingSupport.Offset,
            executionClass,
            supportsDisjunction: supportsContains,
            supportsTotalCount: true,
            sortFields:
            [
                new BoundedQuerySortField(sortField, PhysicalSortDirection.Ascending),
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

    private static ProjectedColumnDefinition StringProjection(
        string path,
        int? length = null,
        bool isNullable = true) =>
        new(path, path, PortablePhysicalType.String, Length: length, IsNullable: isNullable);

    private static BoundedQueryResidualPredicateField Residual(
        string path,
        IndexValueKind valueKind,
        params PortableQueryOperation[] operations) =>
        new(path, valueKind, operations.ToHashSet());

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
