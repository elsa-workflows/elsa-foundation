using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

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
    public const string DocumentIdField = PhysicalDocumentFieldPaths.Id;
    public static IReadOnlyList<DocumentQueryOrder> DeterministicDocumentOrder { get; } =
        [new(DocumentIdField, PhysicalSortDirection.Ascending)];
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
    public const string ActivityDefinitionIdField = "entity.id";
    public const string ActivityDefinitionTypeKeyField = "entity.activityTypeKey";
    public const string ActivityDefinitionCategoryField = "entity.category";
    public const string ActivityDefinitionDisplayNameField = "entity.displayName";
    public const string ActivityDefinitionDescriptionField = "entity.description";
    public const string ActivityDefinitionVersionIdField = "entity.id";
    public const string ActivityDefinitionVersionDefinitionIdField = "entity.definitionId";
    public const string ActivityDefinitionVersionSemVerSortKeyField = "entity.semVerSortKey";
    public const string FindActivityDefinitionByIdQuery = "find-activity-definition-by-id";
    public const string ListActivityDefinitionsByIdQuery = "list-activity-definitions-by-id";
    public const string ListActivityDefinitionsByTypeKeyQuery = "list-activity-definitions-by-type-key";
    public const string ListActivityDefinitionsByCategoryQuery = "list-activity-definitions-by-category";
    public const string ListActivityDefinitionsByDisplayNameQuery = "list-activity-definitions-by-display-name";
    public const string ListActivityDefinitionsByDescriptionQuery = "list-activity-definitions-by-description";
    public const string SearchActivityDefinitionsQuery = "search-activity-definitions";
    public const string FindActivityDefinitionVersionByIdQuery = "find-activity-definition-version-by-id";
    public const string ListActivityDefinitionVersionsByDefinitionQuery = "list-activity-definition-versions-by-definition";
    public const string FindActivityDefinitionVersionByDefinitionAndSortKeyQuery = "find-activity-definition-version-by-definition-and-sort-key";

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
            ActivityDefinitionUnit(),
            ActivityDefinitionVersionUnit(),
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

    private static StorageUnit ActivityDefinitionUnit()
    {
        var logicalIndexes = new[]
        {
            LogicalIndex(ByCollectionIndex, [CollectionField, DocumentIdField]),
            LogicalIndex("activity-definition-by-id-point", [DocumentIdField], unique: true),
            LogicalIndex("activity-definition-by-id", [ActivityDefinitionIdField]),
            LogicalIndex("activity-definition-by-type-key", [ActivityDefinitionTypeKeyField, ActivityDefinitionIdField]),
            LogicalIndex("activity-definition-by-category", [ActivityDefinitionCategoryField, ActivityDefinitionIdField]),
            LogicalIndex("activity-definition-by-display-name", [ActivityDefinitionDisplayNameField, ActivityDefinitionIdField]),
            LogicalIndex("activity-definition-by-description", [ActivityDefinitionDescriptionField, ActivityDefinitionIdField]),
            LogicalIndex("activity-definition-by-search", [ActivityDefinitionDisplayNameField, ActivityDefinitionIdField])
        };
        var physicalIndexes = new[]
        {
            PhysicalIndex(ByCollectionIndex, "collection", "id_comparison_key"),
            PointLookupIndex("activity-definition-by-id-point"),
            PhysicalIndex("activity-definition-by-id", "activity_definition_id"),
            PhysicalIndex("activity-definition-by-type-key", "activity_type_key", "activity_definition_id"),
            PhysicalIndex("activity-definition-by-category", "category", "activity_definition_id"),
            PhysicalIndex("activity-definition-by-display-name", "display_name", "activity_definition_id"),
            PhysicalIndex("activity-definition-by-description", "description", "activity_definition_id"),
            PhysicalIndex("activity-definition-by-search", "display_name", "activity_definition_id")
        };
        var documentResults = new[]
        {
            BoundedQueryResultOperation.Documents,
            BoundedQueryResultOperation.Count,
            BoundedQueryResultOperation.First,
            BoundedQueryResultOperation.Any
        };
        var queries = new[]
        {
            BoundedQuery(
                ListAllQuery,
                ByCollectionIndex,
                [Predicate(CollectionField, PortableQueryOperation.Equal)],
                QueryPagingSupport.Offset,
                QuerySortSupport.Ascending,
                [BoundedQueryResultOperation.Documents],
                sortFields: [new BoundedQuerySortField(DocumentIdField, PhysicalSortDirection.Ascending)]),
            BoundedQuery(
                FindActivityDefinitionByIdQuery,
                "activity-definition-by-id-point",
                [Predicate(DocumentIdField, PortableQueryOperation.Equal)],
                QueryPagingSupport.None,
                QuerySortSupport.None,
                [BoundedQueryResultOperation.First, BoundedQueryResultOperation.Any]),
            BoundedQuery(
                ListActivityDefinitionsByIdQuery,
                "activity-definition-by-id",
                [
                    Predicate(
                        ActivityDefinitionIdField,
                        PortableQueryOperation.Equal,
                        PortableQueryOperation.In,
                        PortableQueryOperation.Contains)
                ],
                QueryPagingSupport.Offset,
                QuerySortSupport.Ascending,
                documentResults,
                supportsDisjunction: true,
                sortFields: DefinitionSort(ActivityDefinitionIdField),
                residualPredicateFields: DefinitionResiduals(ActivityDefinitionIdField)),
            BoundedQuery(
                ListActivityDefinitionsByTypeKeyQuery,
                "activity-definition-by-type-key",
                [
                    Predicate(
                        ActivityDefinitionTypeKeyField,
                        PortableQueryOperation.Equal,
                        PortableQueryOperation.In,
                        PortableQueryOperation.Contains)
                ],
                QueryPagingSupport.Offset,
                QuerySortSupport.Ascending,
                documentResults,
                supportsDisjunction: true,
                sortFields: DefinitionSort(ActivityDefinitionTypeKeyField),
                residualPredicateFields: DefinitionResiduals(ActivityDefinitionTypeKeyField)),
            BoundedQuery(
                ListActivityDefinitionsByCategoryQuery,
                "activity-definition-by-category",
                [
                    Predicate(
                        ActivityDefinitionCategoryField,
                        PortableQueryOperation.Equal,
                        PortableQueryOperation.Contains)
                ],
                QueryPagingSupport.Offset,
                QuerySortSupport.Ascending,
                documentResults,
                supportsDisjunction: true,
                sortFields: DefinitionSort(ActivityDefinitionCategoryField),
                residualPredicateFields: DefinitionResiduals(ActivityDefinitionCategoryField)),
            BoundedQuery(
                ListActivityDefinitionsByDisplayNameQuery,
                "activity-definition-by-display-name",
                [
                    Predicate(
                        ActivityDefinitionDisplayNameField,
                        PortableQueryOperation.Equal,
                        PortableQueryOperation.Contains)
                ],
                QueryPagingSupport.Offset,
                QuerySortSupport.Ascending,
                documentResults,
                supportsDisjunction: true,
                sortFields: DefinitionSort(ActivityDefinitionDisplayNameField),
                residualPredicateFields: DefinitionResiduals(ActivityDefinitionDisplayNameField)),
            BoundedQuery(
                ListActivityDefinitionsByDescriptionQuery,
                "activity-definition-by-description",
                [
                    Predicate(
                        ActivityDefinitionDescriptionField,
                        PortableQueryOperation.Equal,
                        PortableQueryOperation.Contains)
                ],
                QueryPagingSupport.Offset,
                QuerySortSupport.Ascending,
                documentResults,
                supportsDisjunction: true,
                sortFields: DefinitionSort(ActivityDefinitionDescriptionField),
                residualPredicateFields: DefinitionResiduals(ActivityDefinitionDescriptionField)),
            BoundedQuery(
                SearchActivityDefinitionsQuery,
                "activity-definition-by-search",
                [
                    Predicate(
                        ActivityDefinitionDisplayNameField,
                        PortableQueryOperation.Equal,
                        PortableQueryOperation.Contains)
                ],
                QueryPagingSupport.Offset,
                QuerySortSupport.Ascending,
                documentResults,
                supportsDisjunction: true,
                sortFields: DefinitionSort(ActivityDefinitionDisplayNameField),
                residualPredicateFields: DefinitionResiduals(ActivityDefinitionDisplayNameField))
        };

        return ExplicitPhysicalUnit(
            ActivityDefinitionDocumentKind,
            "Activity definition",
            [
                Column("collection", CollectionField, false),
                Column("activity_definition_id", ActivityDefinitionIdField, false),
                Column("activity_type_key", ActivityDefinitionTypeKeyField, false),
                Column("category", ActivityDefinitionCategoryField, false),
                Column("display_name", ActivityDefinitionDisplayNameField),
                Column("description", ActivityDefinitionDescriptionField)
            ],
            logicalIndexes,
            physicalIndexes,
            queries);
    }

    private static StorageUnit ActivityDefinitionVersionUnit()
    {
        var logicalIndexes = new[]
        {
            LogicalIndex(ByCollectionIndex, [CollectionField, DocumentIdField]),
            LogicalIndex("activity-definition-version-by-id-point", [DocumentIdField], unique: true),
            LogicalIndex(
                "activity-definition-versions-by-definition",
                [
                    ActivityDefinitionVersionDefinitionIdField,
                    ActivityDefinitionVersionSemVerSortKeyField,
                    ActivityDefinitionVersionIdField
                ]),
            LogicalIndex(
                "activity-definition-version-by-definition-and-sort-key",
                [ActivityDefinitionVersionDefinitionIdField, ActivityDefinitionVersionSemVerSortKeyField])
        };
        var physicalIndexes = new[]
        {
            PhysicalIndex(ByCollectionIndex, "collection", "id_comparison_key"),
            PointLookupIndex("activity-definition-version-by-id-point"),
            PhysicalIndex(
                "activity-definition-versions-by-definition",
                "definition_id",
                "sem_ver_sort_key",
                "version_id"),
            PhysicalIndex(
                "activity-definition-version-by-definition-and-sort-key",
                "definition_id",
                "sem_ver_sort_key")
        };
        var queries = new[]
        {
            BoundedQuery(
                ListAllQuery,
                ByCollectionIndex,
                [Predicate(CollectionField, PortableQueryOperation.Equal)],
                QueryPagingSupport.Offset,
                QuerySortSupport.Ascending,
                [BoundedQueryResultOperation.Documents],
                sortFields: [new BoundedQuerySortField(DocumentIdField, PhysicalSortDirection.Ascending)]),
            BoundedQuery(
                FindActivityDefinitionVersionByIdQuery,
                "activity-definition-version-by-id-point",
                [Predicate(DocumentIdField, PortableQueryOperation.Equal)],
                QueryPagingSupport.None,
                QuerySortSupport.None,
                [BoundedQueryResultOperation.First, BoundedQueryResultOperation.Any]),
            BoundedQuery(
                ListActivityDefinitionVersionsByDefinitionQuery,
                "activity-definition-versions-by-definition",
                [
                    Predicate(
                        ActivityDefinitionVersionDefinitionIdField,
                        PortableQueryOperation.Equal,
                        PortableQueryOperation.In)
                ],
                QueryPagingSupport.Offset,
                QuerySortSupport.Ascending,
                [BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count],
                sortFields: ActivityDefinitionVersionSort()),
            BoundedQuery(
                FindActivityDefinitionVersionByDefinitionAndSortKeyQuery,
                "activity-definition-version-by-definition-and-sort-key",
                [
                    Predicate(ActivityDefinitionVersionDefinitionIdField, PortableQueryOperation.Equal),
                    Predicate(ActivityDefinitionVersionSemVerSortKeyField, PortableQueryOperation.Equal)
                ],
                QueryPagingSupport.None,
                QuerySortSupport.None,
                [BoundedQueryResultOperation.First, BoundedQueryResultOperation.Any])
        };

        return ExplicitPhysicalUnit(
            ActivityDefinitionVersionDocumentKind,
            "Activity definition version",
            [
                Column("collection", CollectionField, false),
                Column("version_id", ActivityDefinitionVersionIdField, false),
                Column("definition_id", ActivityDefinitionVersionDefinitionIdField, false),
                Column("sem_ver_sort_key", ActivityDefinitionVersionSemVerSortKeyField, false)
            ],
            logicalIndexes,
            physicalIndexes,
            queries);
    }

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
        QuerySortSupport.Ascending,
        QueryPagingSupport.Offset,
        [new BoundedQuerySortField(DocumentIdField, PhysicalSortDirection.Ascending)]);

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

    private static StorageUnit ExplicitPhysicalUnit(
        string documentKind,
        string label,
        ProjectedColumnDefinition[] columns,
        LogicalIndexDeclaration[] logicalIndexes,
        PhysicalIndexDefinition[] physicalIndexes,
        BoundedQueryDeclaration[] boundedQueries)
    {
        var unit = BaseUnit(documentKind, label, LifecyclePolicy.Mutable);
        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(
                    PhysicalTableDefinition.PhysicalEntityTable(documentKind, columns, indexes: physicalIndexes)),
                logicalIndexes,
                boundedQueries)
        };
    }

    private static LogicalIndexDeclaration LogicalIndex(
        string identity,
        string[] fields,
        bool unique = false) =>
        new(
            identity,
            fields.Select(field => new IndexField(field, IndexValueKind.Keyword)).ToArray(),
            IndexValueKind.Keyword,
            unique,
            MissingValueBehavior.Excluded);

    private static PhysicalIndexDefinition PhysicalIndex(string identity, params string[] columns) =>
        new(
            identity,
            [
                new PhysicalIndexColumnDefinition("storage_scope", 0),
                .. columns.Select((column, index) => new PhysicalIndexColumnDefinition(column, index + 1))
            ],
            missingValueBehavior: MissingValueBehavior.Excluded);

    private static PhysicalIndexDefinition PointLookupIndex(string identity) =>
        new(
            identity,
            [
                new PhysicalIndexColumnDefinition("storage_scope", 0),
                new PhysicalIndexColumnDefinition("id_lookup_key", 1),
                new PhysicalIndexColumnDefinition("id_comparison_key", 2)
            ],
            isUnique: true,
            missingValueBehavior: MissingValueBehavior.Excluded);

    private static BoundedQueryDeclaration BoundedQuery(
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

    private static BoundedQueryResidualPredicateField[] DefinitionResiduals(params string[] excludedPaths)
    {
        var fields = new[]
        {
            (
                Path: ActivityDefinitionIdField,
                Operations: new[] { PortableQueryOperation.Equal, PortableQueryOperation.In, PortableQueryOperation.Contains }),
            (
                Path: ActivityDefinitionTypeKeyField,
                Operations: new[] { PortableQueryOperation.Equal, PortableQueryOperation.In, PortableQueryOperation.Contains }),
            (
                Path: ActivityDefinitionCategoryField,
                Operations: new[] { PortableQueryOperation.Equal, PortableQueryOperation.Contains }),
            (
                Path: ActivityDefinitionDisplayNameField,
                Operations: new[] { PortableQueryOperation.Equal, PortableQueryOperation.Contains }),
            (
                Path: ActivityDefinitionDescriptionField,
                Operations: new[] { PortableQueryOperation.Equal, PortableQueryOperation.Contains })
        };

        return fields
            .Where(field => !excludedPaths.Contains(field.Path, StringComparer.Ordinal))
            .Select(field => ResidualPredicate(field.Path, field.Operations))
            .ToArray();
    }

    public static IReadOnlyList<DocumentQueryOrder> ActivityDefinitionIdOrder { get; } =
        [new(ActivityDefinitionIdField, PhysicalSortDirection.Ascending)];

    public static IReadOnlyList<DocumentQueryOrder> ActivityDefinitionTypeKeyOrder { get; } =
    [
        new(ActivityDefinitionTypeKeyField, PhysicalSortDirection.Ascending),
        new(ActivityDefinitionIdField, PhysicalSortDirection.Ascending)
    ];

    public static IReadOnlyList<DocumentQueryOrder> ActivityDefinitionCategoryOrder { get; } =
    [
        new(ActivityDefinitionCategoryField, PhysicalSortDirection.Ascending),
        new(ActivityDefinitionIdField, PhysicalSortDirection.Ascending)
    ];

    public static IReadOnlyList<DocumentQueryOrder> ActivityDefinitionDisplayNameOrder { get; } =
    [
        new(ActivityDefinitionDisplayNameField, PhysicalSortDirection.Ascending),
        new(ActivityDefinitionIdField, PhysicalSortDirection.Ascending)
    ];

    public static IReadOnlyList<DocumentQueryOrder> ActivityDefinitionDescriptionOrder { get; } =
    [
        new(ActivityDefinitionDescriptionField, PhysicalSortDirection.Ascending),
        new(ActivityDefinitionIdField, PhysicalSortDirection.Ascending)
    ];

    public static IReadOnlyList<DocumentQueryOrder> ActivityDefinitionSearchOrder { get; } =
        ActivityDefinitionDisplayNameOrder;

    public static IReadOnlyList<DocumentQueryOrder> ActivityDefinitionVersionOrder { get; } =
    [
        new(ActivityDefinitionVersionDefinitionIdField, PhysicalSortDirection.Ascending),
        new(ActivityDefinitionVersionSemVerSortKeyField, PhysicalSortDirection.Ascending),
        new(ActivityDefinitionVersionIdField, PhysicalSortDirection.Ascending)
    ];

    private static BoundedQuerySortField[] DefinitionSort(string primaryField) =>
        primaryField == ActivityDefinitionIdField
            ? [new BoundedQuerySortField(ActivityDefinitionIdField, PhysicalSortDirection.Ascending)]
            :
            [
                new BoundedQuerySortField(primaryField, PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(ActivityDefinitionIdField, PhysicalSortDirection.Ascending)
            ];

    private static BoundedQuerySortField[] ActivityDefinitionVersionSort() =>
    [
        new(ActivityDefinitionVersionDefinitionIdField, PhysicalSortDirection.Ascending),
        new(ActivityDefinitionVersionSemVerSortKeyField, PhysicalSortDirection.Ascending),
        new(ActivityDefinitionVersionIdField, PhysicalSortDirection.Ascending)
    ];

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
        var documentIdOrderedIndexes = queries
            .Where(query => query.SortFields?.Any(field =>
                field.Path == DocumentIdField && field.Direction == PhysicalSortDirection.Ascending) == true)
            .Select(query => query.IndexIdentity)
            .ToHashSet(StringComparer.Ordinal);
        if (indexes.Any(index => index.IsUnique && documentIdOrderedIndexes.Contains(index.Identity)))
        {
            throw new InvalidOperationException(
                "A unique activity-design index cannot be used by an id-sorted exhaustive route; declare a separate non-unique ordered route.");
        }
        var logicalIndexes = indexes
            .Select(index => new LogicalIndexDeclaration(
                index.Identity,
                [
                    .. index.Fields.Select(field => new IndexField(field, IndexValueKind.Keyword)),
                    .. (!index.IsUnique && documentIdOrderedIndexes.Contains(index.Identity)
                        ? new[] { new IndexField(DocumentIdField, IndexValueKind.Keyword) }
                        : Array.Empty<IndexField>())
                ],
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
                    .. index.Fields.Select((field, order) => new PhysicalIndexColumnDefinition(ColumnName(field), order + 1)),
                    .. (!index.IsUnique && documentIdOrderedIndexes.Contains(index.Identity)
                        ? new[] { new PhysicalIndexColumnDefinition("id_comparison_key", index.Fields.Length + 1, PhysicalSortDirection.Ascending) }
                        : Array.Empty<PhysicalIndexColumnDefinition>())
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
                sortFields: query.SortFields,
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
        QueryPagingSupport PagingSupport,
        IReadOnlyList<BoundedQuerySortField>? SortFields = null);
}
