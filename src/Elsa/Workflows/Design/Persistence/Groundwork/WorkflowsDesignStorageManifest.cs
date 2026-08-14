using Elsa.Persistence.Groundwork.Composition;
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
    private static readonly DocumentEnvelopeDefinition Envelope = new();
    private const string AdditiveIndexVersionSuffix = "-v2";

    public const string WorkflowDefinitionDocumentKind = "workflowDefinition";
    public const string WorkflowDefinitionVersionDocumentKind = "workflowDefinitionVersion";
    public const string WorkflowDefinitionDraftDocumentKind = "workflowDefinitionDraft";
    public const string WorkflowDefinitionVersionLayoutDocumentKind = "workflowDefinitionVersionLayout";

    public const string WorkflowDefinitionCollection = WorkflowDefinitionDocumentKind;
    public const string WorkflowDefinitionVersionCollection = WorkflowDefinitionVersionDocumentKind;
    public const string WorkflowDefinitionDraftCollection = WorkflowDefinitionDraftDocumentKind;
    public const string WorkflowDefinitionVersionLayoutCollection = WorkflowDefinitionVersionLayoutDocumentKind;

    // Physical entity tables are named after their document kind; these are the projected column
    // names their declarations bind below. Published so direct SQL readers — the dashboard portfolio
    // tile — address the declared names rather than re-deriving them.
    public const string DefinitionIdColumn = "definition_id";
    public const string DefinitionTenantIdColumn = "tenant_id";
    public const string DraftIdColumn = "draft_id";
    public const string DraftDefinitionIdColumn = "definition_id";
    public const string DraftLastModifiedAtColumn = "last_modified_at";
    public const string DraftCreatedAtColumn = "created_at";

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

    public static StorageManifest Create() => new StorageManifest(
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
        [])
    {
        // Physical storage is declared per unit above; the manifest still declares the shared Groundwork
        // document envelope itself instead of having a physicalization wrapper inject it.
        SharedDocumentStorages = [SharedDocumentsStorage.Definition]
    };

    private static StorageUnit DefinitionUnit()
    {
        var indexes = new[]
        {
            LogicalIndex("definition-by-id-point", [DocumentIdField], unique: true),
            LogicalIndex("definition-by-id-list", [DefinitionIdField]),
            LogicalIndex("definition-by-name", [DefinitionNameField, DefinitionIdField]),
            LogicalIndex("definition-by-description", [DefinitionDescriptionField, DefinitionIdField]),
            LogicalIndex("definition-by-search", [DefinitionNameField, DefinitionIdField, DefinitionDescriptionField]),
            LogicalIndex(V2("definition-by-id-list"), [DefinitionIdField], unique: true),
            LogicalIndex(V2("definition-by-name"), [DefinitionNameField, DefinitionIdField], unique: true, missingValues: MissingValueBehavior.IncludedAsNull),
            LogicalIndex(V2("definition-by-description"), [DefinitionDescriptionField, DefinitionIdField], unique: true)
        };
        var physicalIndexes = new[]
        {
            PointLookupIndex("definition-by-id-point"),
            PhysicalIndex("definition-by-id-list", "definition_id"),
            PhysicalIndex("definition-by-name", "name", "definition_id"),
            PhysicalIndex("definition-by-description", "description", "definition_id"),
            PhysicalIndex("definition-by-search", "name", "definition_id", "description"),
            UniquePhysicalIndex(V2("definition-by-id-list"), "definition_id"),
            SearchPhysicalIndex(V2("definition-by-name"), "name", "definition_id"),
            UniquePhysicalIndex(V2("definition-by-description"), "description", "definition_id")
        };
        var queries = new[]
        {
            Query(FindDefinitionByIdQuery, "definition-by-id-point", [Predicate(DocumentIdField, PortableQueryOperation.Equal)], QueryPagingSupport.None, QuerySortSupport.None, [BoundedQueryResultOperation.First, BoundedQueryResultOperation.Any]),
            Query(ListDefinitionsByIdQuery, V2("definition-by-id-list"), [Predicate(DefinitionIdField, PortableQueryOperation.Equal, PortableQueryOperation.In, PortableQueryOperation.Contains)], QueryPagingSupport.Offset, QuerySortSupport.Ascending, [BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count], supportsDisjunction: true, sortFields: [Sort(DefinitionIdField)], residualPredicateFields: DefinitionResiduals(DefinitionIdField)),
            Query(ListDefinitionsByNameQuery, V2("definition-by-name"), [Predicate(DefinitionNameField, PortableQueryOperation.Equal, PortableQueryOperation.In, PortableQueryOperation.Contains)], QueryPagingSupport.Offset, QuerySortSupport.Ascending, [BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count], supportsDisjunction: true, sortFields: [Sort(DefinitionNameField), Sort(DefinitionIdField)], residualPredicateFields: DefinitionResiduals(DefinitionNameField)),
            Query(ListDefinitionsByDescriptionQuery, V2("definition-by-description"), [Predicate(DefinitionDescriptionField, PortableQueryOperation.Equal, PortableQueryOperation.Contains)], QueryPagingSupport.Offset, QuerySortSupport.Ascending, [BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count], supportsDisjunction: true, sortFields: [Sort(DefinitionDescriptionField), Sort(DefinitionIdField)], residualPredicateFields: DefinitionResiduals(DefinitionDescriptionField)),
            Query(SearchDefinitionsQuery, V2("definition-by-name"), [Predicate(DefinitionNameField, PortableQueryOperation.Equal, PortableQueryOperation.In, PortableQueryOperation.Contains)], QueryPagingSupport.Offset, QuerySortSupport.Ascending, [BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count], supportsDisjunction: true, sortFields: [Sort(DefinitionNameField), Sort(DefinitionIdField)], residualPredicateFields: DefinitionResiduals(DefinitionNameField))
        };
        return PhysicalUnit(
            WorkflowDefinitionDocumentKind,
            "Workflow definition",
            [
                Column(DefinitionIdColumn, DefinitionIdField, false, IdentityColumnLength),
                Column("name", DefinitionNameField),
                Column("description", DefinitionDescriptionField),
                Column(DefinitionTenantIdColumn, DefinitionTenantIdField, length: IdentityColumnLength)
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
            LogicalIndex(V2("versions-by-definition"), [VersionDefinitionIdField, VersionSemVerSortKeyField, VersionIdField], unique: true),
            LogicalIndex("version-by-definition-and-sort-key", [VersionDefinitionIdField, VersionSemVerSortKeyField], unique: true),
            LogicalIndex("latest-version-by-definition", [VersionDefinitionIdField, VersionSemVerSortKeyField, VersionIdField])
        };
        var physicalIndexes = new[]
        {
            PointLookupIndex("version-by-id"),
            PhysicalIndex("versions-by-definition", "definition_id", "sem_ver_sort_key", "version_id"),
            UniquePhysicalIndex(V2("versions-by-definition"), "definition_id", "sem_ver_sort_key", "version_id"),
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
            Query(ListVersionsByDefinitionQuery, V2("versions-by-definition"), [Predicate(VersionDefinitionIdField, PortableQueryOperation.Equal, PortableQueryOperation.In)], QueryPagingSupport.Offset, QuerySortSupport.Ascending, [BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count], sortFields: [Sort(VersionDefinitionIdField), Sort(VersionSemVerSortKeyField), Sort(VersionIdField)]),
            Query(FindVersionByDefinitionAndSortKeyQuery, "version-by-definition-and-sort-key", [Predicate(VersionDefinitionIdField, PortableQueryOperation.Equal), Predicate(VersionSemVerSortKeyField, PortableQueryOperation.Equal)], QueryPagingSupport.None, QuerySortSupport.None, [BoundedQueryResultOperation.First, BoundedQueryResultOperation.Any]),
            Query(FindLatestVersionQuery, "latest-version-by-definition", [Predicate(VersionDefinitionIdField, PortableQueryOperation.Equal)], QueryPagingSupport.None, QuerySortSupport.Descending, [BoundedQueryResultOperation.First], sortFields: [Sort(VersionSemVerSortKeyField, PhysicalSortDirection.Descending), Sort(VersionIdField, PhysicalSortDirection.Descending)])
        };
        return PhysicalUnit(
            WorkflowDefinitionVersionDocumentKind,
            "Workflow definition version",
            [
                Column("version_id", VersionIdField, false, IdentityColumnLength),
                Column("definition_id", VersionDefinitionIdField, false, IdentityColumnLength),
                Column("sem_ver_sort_key", VersionSemVerSortKeyField, false, IdentityColumnLength)
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
            LogicalIndex(V2("drafts-by-definition"), [DraftDefinitionIdField, DraftLastModifiedAtField, DraftCreatedAtField, DraftIdField], unique: true)
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
            OrderedUniquePhysicalIndex(
                V2("drafts-by-definition"),
                ("definition_id", PhysicalSortDirection.Ascending),
                ("last_modified_at", PhysicalSortDirection.Descending),
                ("created_at", PhysicalSortDirection.Descending),
                ("draft_id", PhysicalSortDirection.Descending))
        };
        var queries = new[]
        {
            Query(FindDraftByIdQuery, "draft-by-id", [Predicate(DocumentIdField, PortableQueryOperation.Equal)], QueryPagingSupport.None, QuerySortSupport.None, [BoundedQueryResultOperation.First, BoundedQueryResultOperation.Any]),
            Query(ListDraftsByDefinitionQuery, V2("drafts-by-definition"), [Predicate(DraftDefinitionIdField, PortableQueryOperation.Equal, PortableQueryOperation.In)], QueryPagingSupport.Offset, QuerySortSupport.Descending, [BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count], sortFields: CurrentDraftSort()),
            // The current-draft route rides the drafts-by-definition index: a dedicated
            // current-draft index would repeat the identical ordered key pattern, which MongoDB
            // rejects as a duplicate and every provider would pay twice for on writes.
            Query(FindCurrentDraftByDefinitionQuery, V2("drafts-by-definition"), [Predicate(DraftDefinitionIdField, PortableQueryOperation.Equal)], QueryPagingSupport.None, QuerySortSupport.Descending, [BoundedQueryResultOperation.First], sortFields: CurrentDraftSort())
        };
        return PhysicalUnit(
            WorkflowDefinitionDraftDocumentKind,
            "Workflow definition draft",
            [
                Column(DraftIdColumn, DraftIdField, false, IdentityColumnLength),
                Column(DraftDefinitionIdColumn, DraftDefinitionIdField, false, IdentityColumnLength),
                DateTimeColumn(DraftLastModifiedAtColumn, DraftLastModifiedAtField, false),
                DateTimeColumn(DraftCreatedAtColumn, DraftCreatedAtField, false)
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
            [Column("version_id", LayoutVersionIdField, false, IdentityColumnLength)],
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
        BoundedQueryDeclaration[] boundedQueries) =>
        StorageUnit.Create(
            new StorageUnitIdentity(documentKind),
            label,
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(PhysicalTableDefinition.PhysicalEntityTable(documentKind, columns, Envelope, physicalIndexes)),
                logicalIndexes,
                boundedQueries));

    private static LogicalIndexDeclaration LogicalIndex(
        string identity,
        string[] fields,
        bool unique = false,
        MissingValueBehavior missingValues = MissingValueBehavior.Excluded) =>
        new(identity, fields.Select(field => new IndexField(field, ValueKind(field))).ToArray(), IndexValueKind.Keyword, unique, missingValues);

    private static PhysicalIndexDefinition PhysicalIndex(string identity, params string[] columns) =>
        PhysicalIndex(identity, false, columns);

    private static PhysicalIndexDefinition PhysicalIndex(string identity, bool unique, params string[] columns) =>
        PhysicalIndex(identity, unique, MissingValueBehavior.Excluded, columns);

    private static PhysicalIndexDefinition PhysicalIndex(string identity, bool unique, MissingValueBehavior missingValues, params string[] columns) =>
        new(identity, [new PhysicalIndexColumnDefinition(Envelope.StorageScopeColumn, 0), .. columns.Select((column, index) => new PhysicalIndexColumnDefinition(column, index + 1))], isUnique: unique, missingValueBehavior: missingValues);

    /// <summary>
    /// An index for a search that spans several optional fields. It must keep rows that have no value
    /// for its keyed columns, because the disjunction can match them on another field.
    /// </summary>
    /// <remarks>
    /// It stays unique, which is what certifies a total order for offset paging. That is portable here
    /// even though it keys nullable columns: the key contains an already-unique identity column, so no
    /// two rows can share the whole key and the providers' disagreement about whether two missing
    /// values collide is unobservable. The alternative, a non-unique index carrying the document
    /// identity tie-break, does not fit SQL Server's 1700-byte index key budget once a text column is
    /// in the key.
    /// </remarks>
    private static PhysicalIndexDefinition SearchPhysicalIndex(string identity, params string[] columns) =>
        new(
            identity,
            [
                new PhysicalIndexColumnDefinition(Envelope.StorageScopeColumn, 0),
                .. columns.Select((column, index) => new PhysicalIndexColumnDefinition(column, index + 1))
            ],
            isUnique: true,
            missingValueBehavior: MissingValueBehavior.IncludedAsNull);

    private static PhysicalIndexDefinition UniquePhysicalIndex(string identity, params string[] columns) =>
        PhysicalIndex(identity, true, columns);

    private static string V2(string identity) => $"{identity}{AdditiveIndexVersionSuffix}";

    private static PhysicalIndexDefinition PointLookupIndex(string identity) =>
        new(identity,
        [
            new PhysicalIndexColumnDefinition(Envelope.StorageScopeColumn, 0),
            new PhysicalIndexColumnDefinition(Envelope.IdLookupKeyColumn, 1),
            new PhysicalIndexColumnDefinition(Envelope.IdComparisonKeyColumn, 2)
        ],
        isUnique: true,
        missingValueBehavior: MissingValueBehavior.Excluded);

    private static PhysicalIndexDefinition OrderedPhysicalIndex(
        string identity,
        params (string Column, PhysicalSortDirection Direction)[] columns) =>
        new(identity,
        [
            new PhysicalIndexColumnDefinition(Envelope.StorageScopeColumn, 0),
            .. columns.Select((column, index) => new PhysicalIndexColumnDefinition(column.Column, index + 1, column.Direction))
        ],
        missingValueBehavior: MissingValueBehavior.Excluded);

    private static PhysicalIndexDefinition OrderedUniquePhysicalIndex(
        string identity,
        params (string Column, PhysicalSortDirection Direction)[] columns) =>
        new(
            identity,
            [
                new PhysicalIndexColumnDefinition(Envelope.StorageScopeColumn, 0),
                .. columns.Select((column, index) => new PhysicalIndexColumnDefinition(column.Column, index + 1, column.Direction))
            ],
            isUnique: true,
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

    // Searchable text columns are bounded to 256 characters and identity/sort-key columns to 128 so
    // every declared compound index key stays under SQL Server's 1700-byte nonclustered limit
    // (worst case: search = scope 256B + name 512B + definition_id 256B + description 512B = 1536B).
    // Over-limit values fail projection validation rather than truncate, per the ratified data model.
    private const int TextColumnLength = 256;
    private const int IdentityColumnLength = 128;

    private static ProjectedColumnDefinition Column(string name, string path, bool nullable = true, int length = TextColumnLength) =>
        new(name, path, PortablePhysicalType.String, Length: length, IsNullable: nullable);

    private static ProjectedColumnDefinition DateTimeColumn(string name, string path, bool nullable = true) =>
        new(name, path, PortablePhysicalType.DateTime, IsNullable: nullable);

    private static IndexValueKind ValueKind(string field) => field is DraftLastModifiedAtField or DraftCreatedAtField
        ? IndexValueKind.DateTime
        : IndexValueKind.Keyword;
}
