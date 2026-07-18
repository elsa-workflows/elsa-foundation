using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Activities.Design.Persistence.Groundwork;

/// <summary>
/// Provider-neutral Groundwork storage manifest describing the activity <b>design</b> document kinds. It is
/// the activities-lane counterpart of <c>WorkflowsDesignStorageManifest</c>: a host selects the concrete
/// provider (SQLite, SQL Server, PostgreSQL, MongoDB, ...) without changing this description, so the same
/// host-selected document provider can back every Elsa module.
/// <para>
/// Legacy aggregate units retain their <b>by-collection keyword index</b> contract. Management projection
/// units are different: they declare explicit physical entity tables and admitted scale-bearing bounded
/// queries so visibility, temporal predicates, search, filters, ordering, paging, and exact counts execute
/// at the selected provider boundary.
/// </para>
/// </summary>
public static class ActivitiesDesignStorageManifest
{
    // Frozen legacy stamp. Groundwork physicalizes additive document kinds/indexes from the manifest;
    // changing this value is not a migration mechanism and would make existing envelopes unreadable.
    public const string SchemaVersion = "1.0.0";

    public const string ByCollectionIndex = "by-collection";
    public const string ByDefinitionIndex = "by-definition";
    public const string ByHeadVersionIndex = "by-head-version";
    public const string ByDraftIndex = "by-draft";
    public const string ByDefinitionVersionIndex = "by-definition-version";
    public const string ByOwnerVersionIndex = "by-owner-version";
    public const string ByDependencyVersionIndex = "by-dependency-version";
    public const string CollectionField = "collection";
    public const string DefinitionIdField = "entity.definitionId";
    public const string HeadVersionIdField = "entity.headVersionId";
    public const string DraftIdField = "entity.draftId";
    public const string DefinitionVersionIdField = "entity.definitionVersionId";
    public const string OwnerVersionIdField = "entity.ownerVersionId";
    public const string DependencyVersionIdField = "entity.dependencyVersionId";
    public const string ManagementResourceIdField = "entity.resourceId";
    public const string ManagementValidFromField = "entity.validFromKey";
    public const string ManagementValidToField = "entity.validToKey";
    public const string ManagementVisibilityField = "entity.visibilityKey";
    public const string ManagementSortField = "entity.sortKey";
    public const string ManagementSearchField = "entity.searchText";
    public const int ManagementSearchMaximumLength = 4000;
    public const string ManagementAuthorityField = "entity.contentAuthority.kind";
    public const string ManagementProviderField = "entity.providerKey";
    public const string ManagementHeadProviderField = "entity.headProviderKey";
    public const string ManagementRecommendationProviderField = "entity.recommendationProviderKey";
    public const string ManagementDraftStatusField = "entity.status";
    public const string ManagementVersionLifecycleField = "entity.lifecycle";
    public const string ListAllQuery = "list-all";

    public const string ActivityDefinitionDocumentKind = "activityDefinition";

    /// <summary>Constant partition value stamped on every activity-definition document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string ActivityDefinitionCollection = "activityDefinition";

    public const string ActivityDefinitionVersionDocumentKind = "activityDefinitionVersion";

    /// <summary>Constant partition value stamped on every activity-definition-version document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string ActivityDefinitionVersionCollection = "activityDefinitionVersion";

    public const string ActivityAvailabilitySettingsDocumentKind = "activityAvailabilitySettings";

    /// <summary>Constant partition value stamped on every activity-availability-settings document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string ActivityAvailabilitySettingsCollection = "activityAvailabilitySettings";

    public const string ActivityDefinitionAuthoringStateDocumentKind = "activityDefinitionAuthoringState";
    public const string ActivityDefinitionAuthoringStateCollection = "activityDefinitionAuthoringState";

    public const string ActivityDefinitionDraftDocumentKind = "activityDefinitionDraft";
    public const string ActivityDefinitionDraftCollection = "activityDefinitionDraft";

    public const string ActivityDefinitionDraftLayoutDocumentKind = "activityDefinitionDraftLayout";
    public const string ActivityDefinitionDraftLayoutCollection = "activityDefinitionDraftLayout";

    public const string ActivityDraftValidationDocumentKind = "activityDraftValidation";
    public const string ActivityDraftValidationCollection = "activityDraftValidation";

    public const string ActivityDefinitionVersionPublicationDocumentKind = "activityDefinitionVersionPublication";
    public const string ActivityDefinitionVersionPublicationCollection = "activityDefinitionVersionPublication";

    public const string ActivityDefinitionVersionLayoutDocumentKind = "activityDefinitionVersionLayout";
    public const string ActivityDefinitionVersionLayoutCollection = "activityDefinitionVersionLayout";

    public const string ActivityDependencyEdgeDocumentKind = "activityDependencyEdge";
    public const string ActivityDependencyEdgeCollection = "activityDependencyEdge";

    public const string ActivityDependencyProjectionDocumentKind = "activityDependencyProjection";
    public const string ActivityDependencyProjectionCollection = "activityDependencyProjection";

    public const string ActivityUpgradePlanDocumentKind = "activityUpgradePlan";
    public const string ActivityUpgradePlanCollection = "activityUpgradePlan";
    public const string ActivityUpgradeApplyReceiptDocumentKind = "activityUpgradeApplyReceipt";
    public const string ActivityUpgradeApplyReceiptCollection = "activityUpgradeApplyReceipt";

    public const string ActivityDefinitionManagementProjectionDocumentKind = "activityDefinitionManagementProjection";
    public const string ActivityDefinitionManagementProjectionCollection = "activityDefinitionManagementProjection";
    public const string ActivityDraftManagementProjectionDocumentKind = "activityDraftManagementProjection";
    public const string ActivityDraftManagementProjectionCollection = "activityDraftManagementProjection";
    public const string ActivityVersionManagementProjectionDocumentKind = "activityVersionManagementProjection";
    public const string ActivityVersionManagementProjectionCollection = "activityVersionManagementProjection";
    public const string ActivityManagementProjectionWatermarkDocumentKind = "activityManagementProjectionWatermark";
    public const string ActivityManagementProjectionWatermarkCollection = "activityManagementProjectionWatermark";
    public const string ActivityManagementProjectionSnapshotDocumentKind = "activityManagementProjectionSnapshot";
    public const string ActivityManagementProjectionSnapshotCollection = "activityManagementProjectionSnapshot";
    public const string ManagementDefinitionCurrentQuery = "management-definition-current";
    public const string ManagementDraftCurrentQuery = "management-draft-current";
    public const string ManagementVersionCurrentQuery = "management-version-current";
    public const string ManagementDefinitionsQuery = "management-definitions-identity-asc";
    public const string ManagementDraftsQuery = "management-drafts-identity-asc";
    public const string ManagementVersionsQuery = "management-versions-identity-asc";
    public const string ManagementExpiredQuery = "management-expired";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-activities-design"),
        new StorageManifestOwner("elsa.activities.design"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(
                ActivityDefinitionDocumentKind,
                "Activity definition",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)]),
            Unit(
                ActivityDefinitionVersionDocumentKind,
                "Activity definition version",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)]),
            Unit(
                ActivityAvailabilitySettingsDocumentKind,
                "Activity availability settings",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)]),
            Unit(
                ActivityDefinitionAuthoringStateDocumentKind,
                "Activity definition authoring state",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(ByDefinitionIndex, DefinitionIdField),
                    Keyword(ByHeadVersionIndex, HeadVersionIdField)
                ],
                [
                    Query(ListAllQuery, ByCollectionIndex),
                    Query("list-by-definition", ByDefinitionIndex),
                    Query("list-by-head-version", ByHeadVersionIndex)
                ]),
            Unit(
                ActivityDefinitionDraftDocumentKind,
                "Activity definition draft",
                [Keyword(ByCollectionIndex, CollectionField), Keyword(ByDefinitionIndex, DefinitionIdField)],
                [Query(ListAllQuery, ByCollectionIndex), Query("list-by-definition", ByDefinitionIndex)]),
            Unit(
                ActivityDefinitionDraftLayoutDocumentKind,
                "Activity definition draft layout",
                [Keyword(ByCollectionIndex, CollectionField), Keyword(ByDraftIndex, DraftIdField)],
                [Query(ListAllQuery, ByCollectionIndex), Query("list-by-draft", ByDraftIndex)]),
            Unit(
                ActivityDraftValidationDocumentKind,
                "Activity draft validation",
                [Keyword(ByCollectionIndex, CollectionField), Keyword(ByDraftIndex, DraftIdField)],
                [Query(ListAllQuery, ByCollectionIndex), Query("list-by-draft", ByDraftIndex)]),
            Unit(
                ActivityDefinitionVersionPublicationDocumentKind,
                "Activity definition version publication",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(ByDefinitionIndex, DefinitionIdField),
                    Keyword(ByDefinitionVersionIndex, DefinitionVersionIdField)
                ],
                [
                    Query(ListAllQuery, ByCollectionIndex),
                    Query("list-by-definition", ByDefinitionIndex),
                    Query("list-by-definition-version", ByDefinitionVersionIndex)
                ]),
            Unit(
                ActivityDefinitionVersionLayoutDocumentKind,
                "Activity definition version layout",
                [Keyword(ByCollectionIndex, CollectionField), Keyword(ByDefinitionVersionIndex, DefinitionVersionIdField)],
                [Query(ListAllQuery, ByCollectionIndex), Query("list-by-definition-version", ByDefinitionVersionIndex)]),
            Unit(
                ActivityDependencyEdgeDocumentKind,
                "Activity dependency edge",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(ByOwnerVersionIndex, OwnerVersionIdField),
                    Keyword(ByDependencyVersionIndex, DependencyVersionIdField)
                ],
                [
                    Query(ListAllQuery, ByCollectionIndex),
                    Query("list-by-owner-version", ByOwnerVersionIndex),
                    Query("list-by-dependency-version", ByDependencyVersionIndex)
                ]),
            Unit(
                ActivityDependencyProjectionDocumentKind,
                "Activity dependency projection",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)]),
            Unit(
                ActivityUpgradePlanDocumentKind,
                "Activity upgrade plan",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)]),
            Unit(
                ActivityUpgradeApplyReceiptDocumentKind,
                "Activity upgrade apply receipt",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)]),
            ManagementUnit(
                ActivityDefinitionManagementProjectionDocumentKind,
                "Activity definition management projection",
                ManagementDefinitionCurrentQuery,
                ManagementDefinitionsQuery,
                DefinitionIdField),
            ManagementUnit(
                ActivityDraftManagementProjectionDocumentKind,
                "Activity draft management projection",
                ManagementDraftCurrentQuery,
                ManagementDraftsQuery,
                DraftIdField),
            ManagementUnit(
                ActivityVersionManagementProjectionDocumentKind,
                "Activity version management projection",
                ManagementVersionCurrentQuery,
                ManagementVersionsQuery,
                DefinitionVersionIdField),
            Unit(
                ActivityManagementProjectionWatermarkDocumentKind,
                "Activity management projection watermark",
                [],
                []),
            Unit(
                ActivityManagementProjectionSnapshotDocumentKind,
                "Activity management projection snapshot",
                [],
                [])
        ],
        new HashSet<string> { "optimistic-concurrency" },
        []);

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

    private static StorageUnit ManagementUnit(
        string documentKind,
        string label,
        string currentQuery,
        string pageQuery,
        string logicalIdField)
    {
        var unit = Unit(
            documentKind,
            label,
            [
                Keyword("management-by-id", logicalIdField),
                Keyword("management-by-sort", ManagementSortField)
            ],
            [
                Query(currentQuery, "management-by-id"),
                Query(pageQuery, "management-by-sort")
            ]);
        var columns = new[]
        {
            Column("resource_id", ManagementResourceIdField, false),
            Column("definition_id", DefinitionIdField, false),
            Column("draft_id", DraftIdField),
            Column("definition_version_id", DefinitionVersionIdField),
            Column("valid_from", ManagementValidFromField, false),
            Column("valid_to", ManagementValidToField, false),
            Column("visibility", ManagementVisibilityField, false),
            Column("sort_key", ManagementSortField, false),
            Column("search_text", ManagementSearchField, false, ManagementSearchMaximumLength),
            Column("authority", ManagementAuthorityField),
            Column("provider", ManagementProviderField),
            Column("head_provider", ManagementHeadProviderField),
            Column("recommendation_provider", ManagementRecommendationProviderField),
            Column("draft_status", ManagementDraftStatusField),
            Column("version_lifecycle", ManagementVersionLifecycleField)
        };
        var equality = new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal };
        var pageOperations = new HashSet<PortableQueryOperation>
        {
            PortableQueryOperation.Equal,
            PortableQueryOperation.Contains,
            PortableQueryOperation.LessThanOrEqual,
            PortableQueryOperation.GreaterThan
        };
        var pagePredicates = new[]
        {
            Predicate(ManagementVisibilityField, PortableQueryOperation.Equal),
            Predicate(ManagementValidFromField, PortableQueryOperation.LessThanOrEqual),
            Predicate(ManagementValidToField, PortableQueryOperation.GreaterThan),
            Predicate(ManagementSearchField, PortableQueryOperation.Contains),
            Predicate(DefinitionIdField, PortableQueryOperation.Equal),
            Predicate(ManagementAuthorityField, PortableQueryOperation.Equal),
            Predicate(ManagementProviderField, PortableQueryOperation.Equal),
            Predicate(ManagementHeadProviderField, PortableQueryOperation.Equal),
            Predicate(ManagementRecommendationProviderField, PortableQueryOperation.Equal),
            Predicate(ManagementDraftStatusField, PortableQueryOperation.Equal),
            Predicate(ManagementVersionLifecycleField, PortableQueryOperation.Equal)
        };
        BoundedQueryPredicateField[] pageIndexPredicates =
        [
            Predicate(ManagementSortField, PortableQueryOperation.Equal),
            .. pagePredicates
        ];
        var logicalIndexes = new[]
        {
            new LogicalIndexDeclaration(
                "management-by-id",
                [
                    new IndexField(logicalIdField, IndexValueKind.Keyword),
                    new IndexField(ManagementValidToField, IndexValueKind.Keyword)
                ],
                IndexValueKind.Keyword,
                false,
                MissingValueBehavior.Excluded),
            new LogicalIndexDeclaration(
                "management-by-sort",
                [
                    .. pageIndexPredicates.Select(predicate => new IndexField(predicate.Path, IndexValueKind.Keyword))
                ],
                IndexValueKind.Keyword,
                false,
                MissingValueBehavior.Excluded),
            new LogicalIndexDeclaration(
                "management-by-valid-to",
                [new IndexField(ManagementValidToField, IndexValueKind.Keyword)],
                IndexValueKind.Keyword,
                false,
                MissingValueBehavior.Excluded)
        };
        var boundedQueries = new[]
        {
            new BoundedQueryDeclaration(
                currentQuery,
                "management-by-id",
                equality,
                QuerySortSupport.None,
                QueryPagingSupport.None,
                BoundedQueryExecutionClass.ScaleBearing,
                predicateFields:
                [
                    Predicate(logicalIdField, PortableQueryOperation.Equal),
                    Predicate(ManagementValidToField, PortableQueryOperation.Equal)
                ]),
            new BoundedQueryDeclaration(
                pageQuery,
                "management-by-sort",
                pageOperations,
                QuerySortSupport.Ascending,
                QueryPagingSupport.Offset,
                BoundedQueryExecutionClass.ScaleBearing,
                supportsDisjunction: true,
                supportsTotalCount: true,
                sortFields: [new BoundedQuerySortField(ManagementSortField, PhysicalSortDirection.Ascending)],
                predicateFields: pageIndexPredicates),
            new BoundedQueryDeclaration(
                ManagementExpiredQuery,
                "management-by-valid-to",
                new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
                QuerySortSupport.Ascending,
                QueryPagingSupport.Offset,
                BoundedQueryExecutionClass.ScaleBearing,
                supportsTotalCount: true,
                sortFields: [new BoundedQuerySortField(ManagementValidToField, PhysicalSortDirection.Ascending)],
                predicateFields: [Predicate(ManagementValidToField, PortableQueryOperation.LessThanOrEqual)])
        };
        var physical = PhysicalTableDefinition.PhysicalEntityTable(
            documentKind,
            columns,
            indexes:
            [
                new PhysicalIndexDefinition(
                    "management-by-id",
                    [
                        new PhysicalIndexColumnDefinition("storage_scope", 0),
                        new PhysicalIndexColumnDefinition(ColumnName(logicalIdField), 1),
                        new PhysicalIndexColumnDefinition("valid_to", 2)
                    ]),
                new PhysicalIndexDefinition(
                    "management-by-sort",
                    [
                        new PhysicalIndexColumnDefinition("storage_scope", 0),
                        .. pageIndexPredicates.Select((predicate, index) =>
                            new PhysicalIndexColumnDefinition(ColumnName(predicate.Path), index + 1))
                    ]),
                new PhysicalIndexDefinition(
                    "management-by-valid-to",
                    [
                        new PhysicalIndexColumnDefinition("storage_scope", 0),
                        new PhysicalIndexColumnDefinition("valid_to", 1)
                    ])
            ]);
        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(physical),
                logicalIndexes,
                boundedQueries)
        };
    }

    private static ProjectedColumnDefinition Column(
        string name,
        string path,
        bool nullable = true,
        int length = 450) => new(name, path, PortablePhysicalType.String, length, IsNullable: nullable);

    private static BoundedQueryPredicateField Predicate(string path, params PortableQueryOperation[] operations) =>
        new(path, operations.ToHashSet());

    private static string ColumnName(string path) => path switch
    {
        DefinitionIdField => "definition_id",
        DraftIdField => "draft_id",
        DefinitionVersionIdField => "definition_version_id",
        ManagementVisibilityField => "visibility",
        ManagementValidFromField => "valid_from",
        ManagementValidToField => "valid_to",
        ManagementSortField => "sort_key",
        ManagementSearchField => "search_text",
        ManagementAuthorityField => "authority",
        ManagementProviderField => "provider",
        ManagementHeadProviderField => "head_provider",
        ManagementRecommendationProviderField => "recommendation_provider",
        ManagementDraftStatusField => "draft_status",
        ManagementVersionLifecycleField => "version_lifecycle",
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, null)
    };

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
