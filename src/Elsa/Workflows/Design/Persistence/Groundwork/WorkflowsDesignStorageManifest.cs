using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>Provider-neutral physical storage declaration for workflow design persistence.</summary>
public static class WorkflowsDesignStorageManifest
{
    public const string SchemaVersion = "1.0.0";

    public const string WorkflowDefinitionDocumentKind = "workflowDefinition";
    public const string WorkflowDefinitionVersionDocumentKind = "workflowDefinitionVersion";
    public const string WorkflowDefinitionDraftDocumentKind = "workflowDefinitionDraft";
    public const string WorkflowDefinitionVersionLayoutDocumentKind = "workflowDefinitionVersionLayout";

    public const string WorkflowDefinitionCollection = WorkflowDefinitionDocumentKind;
    public const string WorkflowDefinitionVersionCollection = WorkflowDefinitionVersionDocumentKind;
    public const string WorkflowDefinitionDraftCollection = WorkflowDefinitionDraftDocumentKind;
    public const string WorkflowDefinitionVersionLayoutCollection = WorkflowDefinitionVersionLayoutDocumentKind;

    public const string DefinitionIdField = "entity.id";
    public const string DefinitionNameField = "entity.name";
    public const string DefinitionDescriptionField = "entity.description";
    public const string DefinitionTenantIdField = "entity.tenantId";
    public const string DocumentIdField = PhysicalDocumentFieldPaths.Id;
    public const string VersionIdField = "entity.id";
    public const string VersionDefinitionIdField = "entity.definitionId";
    public const string VersionSemVerSortKeyField = "entity.semVerSortKey";
    public const string DraftIdField = "entity.id";
    public const string DraftDefinitionIdField = "entity.workflowDefinitionId";
    public const string DraftLastModifiedAtField = "entity.lastModifiedAt";
    public const string DraftCreatedAtField = "entity.createdAt";
    public const string LayoutVersionIdField = "entity.workflowDefinitionVersionId";

    public const string FindDefinitionByIdQuery = "find-definition-by-id";
    public const string ListDefinitionsByIdQuery = "list-definitions-by-id";
    public const string ListDefinitionsByNameQuery = "list-definitions-by-name";
    public const string ListDefinitionsByDescriptionQuery = "list-definitions-by-description";
    public const string SearchDefinitionsQuery = "search-definitions";
    public const string FindVersionByIdQuery = "find-version-by-id";
    public const string ListVersionsByDefinitionQuery = "list-versions-by-definition";
    public const string FindVersionByDefinitionAndSortKeyQuery = "find-version-by-definition-and-sort-key";
    public const string FindLatestVersionQuery = "find-latest-version";
    public const string FindDraftByIdQuery = "find-draft-by-id";
    public const string ListDraftsByDefinitionQuery = "list-drafts-by-definition";
    public const string FindCurrentDraftByDefinitionQuery = "find-current-draft-by-definition";
    public const string FindLayoutByVersionQuery = "find-layout-by-version";

    public static IReadOnlyList<DocumentQueryOrder> WorkflowDefinitionIdOrder { get; } =
        [new(DefinitionIdField, PhysicalSortDirection.Ascending)];

    public static IReadOnlyList<DocumentQueryOrder> WorkflowDefinitionNameOrder { get; } =
    [
        new(DefinitionNameField, PhysicalSortDirection.Ascending),
        new(DefinitionIdField, PhysicalSortDirection.Ascending)
    ];

    public static IReadOnlyList<DocumentQueryOrder> WorkflowDefinitionDescriptionOrder { get; } =
    [
        new(DefinitionDescriptionField, PhysicalSortDirection.Ascending),
        new(DefinitionIdField, PhysicalSortDirection.Ascending)
    ];

    public static IReadOnlyList<DocumentQueryOrder> WorkflowDefinitionSearchOrder { get; } =
        WorkflowDefinitionNameOrder;

    public static IReadOnlyList<DocumentQueryOrder> WorkflowDefinitionVersionOrder { get; } =
    [
        new(VersionDefinitionIdField, PhysicalSortDirection.Ascending),
        new(VersionSemVerSortKeyField, PhysicalSortDirection.Ascending),
        new(VersionIdField, PhysicalSortDirection.Ascending)
    ];

    public static IReadOnlyList<DocumentQueryOrder> WorkflowDefinitionLatestVersionOrder { get; } =
    [
        new(VersionSemVerSortKeyField, PhysicalSortDirection.Descending),
        new(VersionIdField, PhysicalSortDirection.Descending)
    ];

    public static IReadOnlyList<DocumentQueryOrder> WorkflowDefinitionDraftOrder { get; } =
    [
        new(DraftDefinitionIdField, PhysicalSortDirection.Ascending),
        new(DraftLastModifiedAtField, PhysicalSortDirection.Descending),
        new(DraftCreatedAtField, PhysicalSortDirection.Descending),
        new(DraftIdField, PhysicalSortDirection.Descending)
    ];

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-workflows-design"),
        new StorageManifestOwner("elsa.workflows.design"),
        new StorageManifestVersion(SchemaVersion),
        [
            DefinitionUnit(),
            VersionUnit(),
            DraftUnit(),
            LayoutUnit()
        ],
        new HashSet<string> { "optimistic-concurrency" },
        []);

    private static StorageUnit DefinitionUnit()
    {
        var indexes = new[]
        {
            LogicalIndex("definition-by-id-point", [DocumentIdField], unique: true),
            LogicalIndex("definition-by-id-list", [DefinitionIdField]),
            LogicalIndex("definition-by-name", [DefinitionNameField, DefinitionIdField]),
            LogicalIndex("definition-by-description", [DefinitionDescriptionField, DefinitionIdField]),
            LogicalIndex("definition-by-search", [DefinitionNameField, DefinitionIdField, DefinitionDescriptionField])
        };
        var physicalIndexes = new[]
        {
            PointLookupIndex("definition-by-id-point"),
            PhysicalIndex("definition-by-id-list", "definition_id"),
            PhysicalIndex("definition-by-name", "name", "definition_id"),
            PhysicalIndex("definition-by-description", "description", "definition_id"),
            PhysicalIndex("definition-by-search", "name", "definition_id", "description")
        };
        var queries = new[]
        {
            Query(FindDefinitionByIdQuery, "definition-by-id-point", [Predicate(DocumentIdField, PortableQueryOperation.Equal)], QueryPagingSupport.None, QuerySortSupport.None, [BoundedQueryResultOperation.First, BoundedQueryResultOperation.Any]),
            Query(ListDefinitionsByIdQuery, "definition-by-id-list", [Predicate(DefinitionIdField, PortableQueryOperation.Equal, PortableQueryOperation.In, PortableQueryOperation.Contains)], QueryPagingSupport.Offset, QuerySortSupport.Ascending, [BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count], supportsDisjunction: true, sortFields: [Sort(DefinitionIdField)], residualPredicateFields: DefinitionResiduals(DefinitionIdField)),
            Query(ListDefinitionsByNameQuery, "definition-by-name", [Predicate(DefinitionNameField, PortableQueryOperation.Equal, PortableQueryOperation.In, PortableQueryOperation.Contains)], QueryPagingSupport.Offset, QuerySortSupport.Ascending, [BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count], supportsDisjunction: true, sortFields: [Sort(DefinitionNameField), Sort(DefinitionIdField)], residualPredicateFields: DefinitionResiduals(DefinitionNameField)),
            Query(ListDefinitionsByDescriptionQuery, "definition-by-description", [Predicate(DefinitionDescriptionField, PortableQueryOperation.Equal, PortableQueryOperation.Contains)], QueryPagingSupport.Offset, QuerySortSupport.Ascending, [BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count], supportsDisjunction: true, sortFields: [Sort(DefinitionDescriptionField), Sort(DefinitionIdField)], residualPredicateFields: DefinitionResiduals(DefinitionDescriptionField)),
            Query(SearchDefinitionsQuery, "definition-by-search", [Predicate(DefinitionNameField, PortableQueryOperation.Equal, PortableQueryOperation.In, PortableQueryOperation.Contains)], QueryPagingSupport.Offset, QuerySortSupport.Ascending, [BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count], supportsDisjunction: true, sortFields: [Sort(DefinitionNameField), Sort(DefinitionIdField)], residualPredicateFields: DefinitionResiduals(DefinitionNameField))
        };
        return PhysicalUnit(
            WorkflowDefinitionDocumentKind,
            "Workflow definition",
            [
                Column("definition_id", DefinitionIdField, false),
                Column("name", DefinitionNameField),
                Column("description", DefinitionDescriptionField),
                Column("tenant_id", DefinitionTenantIdField)
            ],
            indexes,
            physicalIndexes,
            queries);
    }

    private static StorageUnit VersionUnit()
    {
        var indexes = new[]
        {
            LogicalIndex("version-by-id", [DocumentIdField], unique: true),
            LogicalIndex("versions-by-definition", [VersionDefinitionIdField, VersionSemVerSortKeyField, VersionIdField]),
            LogicalIndex("version-by-definition-and-sort-key", [VersionDefinitionIdField, VersionSemVerSortKeyField], unique: true),
            LogicalIndex("latest-version-by-definition", [VersionDefinitionIdField, VersionSemVerSortKeyField, VersionIdField])
        };
        var physicalIndexes = new[]
        {
            PointLookupIndex("version-by-id"),
            PhysicalIndex("versions-by-definition", "definition_id", "sem_ver_sort_key", "version_id"),
            PhysicalIndex("version-by-definition-and-sort-key", true, "definition_id", "sem_ver_sort_key"),
            OrderedPhysicalIndex(
                "latest-version-by-definition",
                ("definition_id", PhysicalSortDirection.Ascending),
                ("sem_ver_sort_key", PhysicalSortDirection.Descending),
                ("version_id", PhysicalSortDirection.Descending))
        };
        var queries = new[]
        {
            Query(FindVersionByIdQuery, "version-by-id", [Predicate(DocumentIdField, PortableQueryOperation.Equal)], QueryPagingSupport.None, QuerySortSupport.None, [BoundedQueryResultOperation.First, BoundedQueryResultOperation.Any]),
            Query(ListVersionsByDefinitionQuery, "versions-by-definition", [Predicate(VersionDefinitionIdField, PortableQueryOperation.Equal, PortableQueryOperation.In)], QueryPagingSupport.Offset, QuerySortSupport.Ascending, [BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count], sortFields: [Sort(VersionDefinitionIdField), Sort(VersionSemVerSortKeyField), Sort(VersionIdField)]),
            Query(FindVersionByDefinitionAndSortKeyQuery, "version-by-definition-and-sort-key", [Predicate(VersionDefinitionIdField, PortableQueryOperation.Equal), Predicate(VersionSemVerSortKeyField, PortableQueryOperation.Equal)], QueryPagingSupport.None, QuerySortSupport.None, [BoundedQueryResultOperation.First, BoundedQueryResultOperation.Any]),
            Query(FindLatestVersionQuery, "latest-version-by-definition", [Predicate(VersionDefinitionIdField, PortableQueryOperation.Equal)], QueryPagingSupport.None, QuerySortSupport.Descending, [BoundedQueryResultOperation.First], sortFields: [Sort(VersionSemVerSortKeyField, PhysicalSortDirection.Descending), Sort(VersionIdField, PhysicalSortDirection.Descending)])
        };
        return PhysicalUnit(
            WorkflowDefinitionVersionDocumentKind,
            "Workflow definition version",
            [
                Column("version_id", VersionIdField, false),
                Column("definition_id", VersionDefinitionIdField, false),
                Column("sem_ver_sort_key", VersionSemVerSortKeyField, false)
            ],
            indexes,
            physicalIndexes,
            queries);
    }

    private static StorageUnit DraftUnit()
    {
        var indexes = new[]
        {
            LogicalIndex("draft-by-id", [DocumentIdField], unique: true),
            LogicalIndex("drafts-by-definition", [DraftDefinitionIdField, DraftLastModifiedAtField, DraftCreatedAtField, DraftIdField]),
            LogicalIndex("current-draft-by-definition", [DraftDefinitionIdField, DraftLastModifiedAtField, DraftCreatedAtField, DraftIdField])
        };
        var physicalIndexes = new[]
        {
            PointLookupIndex("draft-by-id"),
            OrderedPhysicalIndex(
                "drafts-by-definition",
                ("definition_id", PhysicalSortDirection.Ascending),
                ("last_modified_at", PhysicalSortDirection.Descending),
                ("created_at", PhysicalSortDirection.Descending),
                ("draft_id", PhysicalSortDirection.Descending)),
            OrderedPhysicalIndex(
                "current-draft-by-definition",
                ("definition_id", PhysicalSortDirection.Ascending),
                ("last_modified_at", PhysicalSortDirection.Descending),
                ("created_at", PhysicalSortDirection.Descending),
                ("draft_id", PhysicalSortDirection.Descending))
        };
        var queries = new[]
        {
            Query(FindDraftByIdQuery, "draft-by-id", [Predicate(DocumentIdField, PortableQueryOperation.Equal)], QueryPagingSupport.None, QuerySortSupport.None, [BoundedQueryResultOperation.First, BoundedQueryResultOperation.Any]),
            Query(ListDraftsByDefinitionQuery, "drafts-by-definition", [Predicate(DraftDefinitionIdField, PortableQueryOperation.Equal, PortableQueryOperation.In)], QueryPagingSupport.Offset, QuerySortSupport.Descending, [BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count], sortFields: CurrentDraftSort()),
            Query(FindCurrentDraftByDefinitionQuery, "current-draft-by-definition", [Predicate(DraftDefinitionIdField, PortableQueryOperation.Equal)], QueryPagingSupport.None, QuerySortSupport.Descending, [BoundedQueryResultOperation.First], sortFields: CurrentDraftSort())
        };
        return PhysicalUnit(
            WorkflowDefinitionDraftDocumentKind,
            "Workflow definition draft",
            [
                Column("draft_id", DraftIdField, false),
                Column("definition_id", DraftDefinitionIdField, false),
                DateTimeColumn("last_modified_at", DraftLastModifiedAtField, false),
                DateTimeColumn("created_at", DraftCreatedAtField, false)
            ],
            indexes,
            physicalIndexes,
            queries);
    }

    private static StorageUnit LayoutUnit()
    {
        var indexes = new[] { LogicalIndex("layout-by-version", [LayoutVersionIdField], true) };
        var physicalIndexes = new[] { PhysicalIndex("layout-by-version", true, "version_id") };
        var queries = new[]
        {
            Query(FindLayoutByVersionQuery, "layout-by-version", [Predicate(LayoutVersionIdField, PortableQueryOperation.Equal)], QueryPagingSupport.None, QuerySortSupport.None, [BoundedQueryResultOperation.First, BoundedQueryResultOperation.Any])
        };
        return PhysicalUnit(
            WorkflowDefinitionVersionLayoutDocumentKind,
            "Workflow definition version layout",
            [Column("version_id", LayoutVersionIdField, false)],
            indexes,
            physicalIndexes,
            queries);
    }

    private static StorageUnit PhysicalUnit(
        string documentKind,
        string label,
        ProjectedColumnDefinition[] columns,
        LogicalIndexDeclaration[] logicalIndexes,
        PhysicalIndexDefinition[] physicalIndexes,
        BoundedQueryDeclaration[] boundedQueries)
    {
#pragma warning disable GW0001 // Required bridge-release constructor value; the legacy declarations above are intentionally empty.
        var unit = new StorageUnit(
            new StorageUnitIdentity(documentKind),
            label,
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [],
            [],
            PhysicalizationPolicy.Portable);
#pragma warning restore GW0001

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(PhysicalTableDefinition.PhysicalEntityTable(documentKind, columns, indexes: physicalIndexes)),
                logicalIndexes,
                boundedQueries)
        };
    }

    private static LogicalIndexDeclaration LogicalIndex(string identity, string[] fields, bool unique = false) =>
        new(identity, fields.Select(field => new IndexField(field, ValueKind(field))).ToArray(), IndexValueKind.Keyword, unique, MissingValueBehavior.Excluded);

    private static PhysicalIndexDefinition PhysicalIndex(string identity, params string[] columns) =>
        PhysicalIndex(identity, false, columns);

    private static PhysicalIndexDefinition PhysicalIndex(string identity, bool unique, params string[] columns) =>
        new(identity, [new PhysicalIndexColumnDefinition("storage_scope", 0), .. columns.Select((column, index) => new PhysicalIndexColumnDefinition(column, index + 1))], isUnique: unique, missingValueBehavior: MissingValueBehavior.Excluded);

    private static PhysicalIndexDefinition PointLookupIndex(string identity) =>
        new(identity,
        [
            new PhysicalIndexColumnDefinition("storage_scope", 0),
            new PhysicalIndexColumnDefinition("id_lookup_key", 1),
            new PhysicalIndexColumnDefinition("id_comparison_key", 2)
        ],
        isUnique: true,
        missingValueBehavior: MissingValueBehavior.Excluded);

    private static PhysicalIndexDefinition OrderedPhysicalIndex(
        string identity,
        params (string Column, PhysicalSortDirection Direction)[] columns) =>
        new(identity,
        [
            new PhysicalIndexColumnDefinition("storage_scope", 0),
            .. columns.Select((column, index) => new PhysicalIndexColumnDefinition(column.Column, index + 1, column.Direction))
        ],
        missingValueBehavior: MissingValueBehavior.Excluded);

    private static BoundedQueryDeclaration Query(
        string identity,
        string index,
        BoundedQueryPredicateField[] predicates,
        QueryPagingSupport paging,
        QuerySortSupport sort,
        BoundedQueryResultOperation[] results,
        bool supportsDisjunction = false,
        BoundedQuerySortField[]? sortFields = null,
        BoundedQueryResidualPredicateField[]? residualPredicateFields = null) =>
        new(
            identity,
            index,
            predicates
                .SelectMany(predicate => predicate.Operations)
                .Concat(residualPredicateFields?.SelectMany(predicate => predicate.Operations) ?? [])
                .ToHashSet(),
            sort,
            paging,
            BoundedQueryExecutionClass.ScaleBearing,
            supportsDisjunction,
            supportsTotalCount: results.Contains(BoundedQueryResultOperation.Count),
            sortFields: sortFields,
            predicateFields: predicates,
            resultOperations: results.ToHashSet(),
            residualPredicateFields: residualPredicateFields);

    private static BoundedQueryPredicateField Predicate(string path, params PortableQueryOperation[] operations) =>
        new(path, operations.ToHashSet());

    private static BoundedQueryResidualPredicateField ResidualPredicate(string path, params PortableQueryOperation[] operations) =>
        new(path, IndexValueKind.Keyword, operations.ToHashSet());

    private static BoundedQueryResidualPredicateField[] DefinitionResiduals(params string[] excludedPaths)
    {
        var fields = new[]
        {
            (
                Path: DefinitionIdField,
                Operations: new[]
                {
                    PortableQueryOperation.Equal,
                    PortableQueryOperation.In,
                    PortableQueryOperation.Contains
                }),
            (
                Path: DefinitionNameField,
                Operations: new[]
                {
                    PortableQueryOperation.Equal,
                    PortableQueryOperation.In,
                    PortableQueryOperation.Contains
                }),
            (
                Path: DefinitionDescriptionField,
                Operations: new[]
                {
                    PortableQueryOperation.Equal,
                    PortableQueryOperation.Contains
                })
        };

        return fields
            .Where(field => !excludedPaths.Contains(field.Path, StringComparer.Ordinal))
            .Select(field => ResidualPredicate(field.Path, field.Operations))
            .ToArray();
    }

    private static BoundedQuerySortField Sort(string path, PhysicalSortDirection direction = PhysicalSortDirection.Ascending) =>
        new(path, direction);

    private static BoundedQuerySortField[] CurrentDraftSort() =>
        WorkflowDefinitionDraftOrder
            .Select(order => Sort(order.Path, order.Direction))
            .ToArray();

    private static ProjectedColumnDefinition Column(string name, string path, bool nullable = true) =>
        new(name, path, PortablePhysicalType.String, Length: 450, IsNullable: nullable);

    private static ProjectedColumnDefinition DateTimeColumn(string name, string path, bool nullable = true) =>
        new(name, path, PortablePhysicalType.DateTime, IsNullable: nullable);

    private static IndexValueKind ValueKind(string field) => field is DraftLastModifiedAtField or DraftCreatedAtField
        ? IndexValueKind.DateTime
        : IndexValueKind.Keyword;
}
