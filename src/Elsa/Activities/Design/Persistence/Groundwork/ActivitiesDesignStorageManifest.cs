using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Activities.Design.Persistence.Groundwork;

/// <summary>Provider-neutral physical storage declaration for activity design persistence.</summary>
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
    public const int ManagementSequenceKeyLength = 20;
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

    public const string ActivityForkCandidateDocumentKind = "activityForkCandidate";
    public const string ActivityForkCandidateCollection = "activityForkCandidate";
    public const string ActivityForkCandidateRetentionField = "entity.retentionKey";
    public const string ActivityForkCandidateRetentionIndex = "fork-candidate-by-retention";
    public const string ActivityForkCandidateExpiredQuery = "fork-candidate-expired";
    public const string ActivityForkReceiptDocumentKind = "activityForkReceipt";
    public const string ActivityForkReceiptCollection = "activityForkReceipt";

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
                ActivityForkCandidateDocumentKind,
                "Activity fork candidate",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(
                        ActivityForkCandidateRetentionIndex,
                        ActivityForkCandidateRetentionField,
                        PortableQueryOperation.LessThanOrEqual)
                ],
                [
                    Query(ListAllQuery, ByCollectionIndex),
                    new ActivityQuery(
                        ActivityForkCandidateExpiredQuery,
                        ActivityForkCandidateRetentionIndex,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
                        QuerySortSupport.None,
                        QueryPagingSupport.Offset)
                ]),
            Unit(
                ActivityForkReceiptDocumentKind,
                "Activity fork receipt",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)],
                LifecyclePolicy.AppendOnly),
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
        ActivityIndex[] indexes,
        ActivityQuery[] queries) =>
        Unit(documentKind, label, indexes, queries, LifecyclePolicy.Mutable);

    private static StorageUnit Unit(
        string documentKind,
        string label,
        ActivityIndex[] indexes,
        ActivityQuery[] queries,
        LifecyclePolicy lifecycle) =>
        PhysicalUnit(documentKind, label, lifecycle, indexes, queries);

    private static ActivityQuery Query(string name, string indexName) => new(
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
        var unit = BaseUnit(documentKind, label, LifecyclePolicy.Mutable);
        var columns = new[]
        {
            Column("resource_id", ManagementResourceIdField, false),
            Column("definition_id", DefinitionIdField, false),
            Column("draft_id", DraftIdField),
            Column("definition_version_id", DefinitionVersionIdField),
            Column("valid_from", ManagementValidFromField, false, ManagementSequenceKeyLength),
            Column("valid_to", ManagementValidToField, false, ManagementSequenceKeyLength),
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
            Predicate(ManagementSortField, PortableQueryOperation.Equal)
        ];
        var pageResidualPredicates = pagePredicates
            .Select(predicate => new BoundedQueryResidualPredicateField(
                predicate.Path,
                IndexValueKind.Keyword,
                predicate.Operations))
            .ToArray();
        var logicalIndexes = new[]
        {
            new LogicalIndexDeclaration(
                "management-by-id",
                [
                    new IndexField(logicalIdField, IndexValueKind.Keyword)
                ],
                IndexValueKind.Keyword,
                false,
                MissingValueBehavior.Excluded),
            new LogicalIndexDeclaration(
                "management-by-sort",
                [
                    new IndexField(ManagementSortField, IndexValueKind.Keyword)
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
                    Predicate(logicalIdField, PortableQueryOperation.Equal)
                ],
                residualPredicateFields:
                [
                    ResidualPredicate(ManagementValidToField, PortableQueryOperation.Equal)
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
                predicateFields: pageIndexPredicates,
                residualPredicateFields: pageResidualPredicates),
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
                        new PhysicalIndexColumnDefinition(ColumnName(logicalIdField), 1)
                    ]),
                new PhysicalIndexDefinition(
                    "management-by-sort",
                    [
                        new PhysicalIndexColumnDefinition("storage_scope", 0),
                        new PhysicalIndexColumnDefinition("sort_key", 1)
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

    private static BoundedQueryResidualPredicateField ResidualPredicate(
        string path,
        params PortableQueryOperation[] operations) =>
        new(path, IndexValueKind.Keyword, operations.ToHashSet());

    private static StorageUnit PhysicalUnit(
        string documentKind,
        string label,
        LifecyclePolicy lifecycle,
        ActivityIndex[] indexes,
        ActivityQuery[] queries)
    {
        var logicalIndexes = indexes
            .Select(index => new LogicalIndexDeclaration(
                index.Identity,
                index.Fields.Select(field => new IndexField(field, IndexValueKind.Keyword)).ToArray(),
                IndexValueKind.Keyword,
                index.IsUnique,
                MissingValueBehavior.Excluded))
            .ToArray();
        var indexedColumns = indexes
            .SelectMany(index => index.Fields)
            .Distinct(StringComparer.Ordinal)
            .Select(field => Column(ColumnName(field), field))
            .ToArray();
        var columns = indexedColumns.Length == 0
            ? [Column("entity_id", "entity.id", false)]
            : indexedColumns;
        var physicalIndexes = indexes
            .Select(index => new PhysicalIndexDefinition(
                index.Identity,
                [
                    new PhysicalIndexColumnDefinition("storage_scope", 0),
                    .. index.Fields.Select((field, order) => new PhysicalIndexColumnDefinition(ColumnName(field), order + 1))
                ],
                isUnique: index.IsUnique,
                missingValueBehavior: MissingValueBehavior.Excluded))
            .ToArray();
        var boundedQueries = queries
            .Select(query => new BoundedQueryDeclaration(
                query.Identity,
                query.IndexIdentity,
                query.Operations,
                query.SortSupport,
                query.PagingSupport,
                BoundedQueryExecutionClass.ScaleBearing,
                sortFields: query.SortSupport == QuerySortSupport.None
                    ? null
                    : [new BoundedQuerySortField(indexes.Single(index => index.Identity == query.IndexIdentity).Fields[0], PhysicalSortDirection.Ascending)],
                predicateFields:
                [
                    new BoundedQueryPredicateField(
                        indexes.Single(index => index.Identity == query.IndexIdentity).Fields[0],
                        query.Operations)
                ]))
            .ToArray();

        var unit = BaseUnit(documentKind, label, lifecycle);
        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(PhysicalTableDefinition.PhysicalEntityTable(documentKind, columns, indexes: physicalIndexes)),
                logicalIndexes,
                boundedQueries)
        };
    }

    private static StorageUnit BaseUnit(string documentKind, string label, LifecyclePolicy lifecycle)
    {
#pragma warning disable GW0001 // Bridge-release constructor requirement; the legacy declaration collections are intentionally empty.
        return new StorageUnit(
            new StorageUnitIdentity(documentKind),
            label,
            StorageIntent.PortableDocument(),
            lifecycle,
            IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [],
            [],
            PhysicalizationPolicy.Portable);
#pragma warning restore GW0001
    }

    private static string ColumnName(string path) => path switch
    {
        DefinitionIdField => "definition_id",
        DraftIdField => "draft_id",
        DefinitionVersionIdField => "definition_version_id",
        HeadVersionIdField => "head_version_id",
        OwnerVersionIdField => "owner_version_id",
        DependencyVersionIdField => "dependency_version_id",
        CollectionField => "collection",
        ActivityForkCandidateRetentionField => "retention_key",
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
        _ => path.Replace('.', '_')
    };

    private static ActivityIndex Keyword(
        string identity,
        string field,
        params PortableQueryOperation[] operations) => new(
        identity,
        [field],
        false);

    private sealed record ActivityIndex(string Identity, string[] Fields, bool IsUnique);

    private sealed record ActivityQuery(
        string Identity,
        string IndexIdentity,
        IReadOnlySet<PortableQueryOperation> Operations,
        QuerySortSupport SortSupport,
        QueryPagingSupport PagingSupport);
}
