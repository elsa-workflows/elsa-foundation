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
    private static readonly DocumentEnvelopeDefinition Envelope = new();
    private const string AdditiveIndexVersionSuffix = "-v2";

    public const string ByDefinitionIndex = "by-definition";
    public const string ByHeadVersionIndex = "by-head-version";
    public const string ByDraftIndex = "by-draft";
    public const string ByDefinitionVersionIndex = "by-definition-version";
    public const string ByOwnerVersionIndex = "by-owner-version";
    public const string ByDependencyVersionIndex = "by-dependency-version";
    public const string DocumentIdField = PhysicalDocumentFieldPaths.Id;

    /// <summary>
    /// The projected entity-id path used as the workload-visible tie-break for the auxiliary
    /// exhaustive routes. Preview.102 additionally appends Groundwork's provider identity key to
    /// each non-unique paged physical index so equal business keys still have a total physical order.
    /// </summary>
    public const string EntityIdField = "entity.id";
    public const string DefinitionIdField = "entity.definitionId";
    public static IReadOnlyList<DocumentQueryOrder> ByDefinitionDocumentOrder { get; } =
    [
        new(DefinitionIdField, PhysicalSortDirection.Ascending),
        new(EntityIdField, PhysicalSortDirection.Ascending)
    ];
    public static IReadOnlyList<DocumentQueryOrder> ByDefinitionVersionDocumentOrder { get; } =
    [
        new(DefinitionVersionIdField, PhysicalSortDirection.Ascending),
        new(EntityIdField, PhysicalSortDirection.Ascending)
    ];
    public static IReadOnlyList<DocumentQueryOrder> ByOwnerVersionDocumentOrder { get; } =
    [
        new(OwnerVersionIdField, PhysicalSortDirection.Ascending),
        new(EntityIdField, PhysicalSortDirection.Ascending)
    ];
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

    /// <summary>Constant partition value stamped into the canonical JSON of every activity-definition document.</summary>
    public const string ActivityDefinitionCollection = "activityDefinition";

    public const string ActivityDefinitionVersionDocumentKind = "activityDefinitionVersion";

    /// <summary>Constant partition value stamped into the canonical JSON of every activity-definition-version document.</summary>
    public const string ActivityDefinitionVersionCollection = "activityDefinitionVersion";

    public const string ActivityAvailabilitySettingsDocumentKind = "activityAvailabilitySettings";

    /// <summary>Constant partition value stamped into the canonical JSON of every activity-availability-settings document.</summary>
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
                [],
                []),
            Unit(
                ActivityDefinitionAuthoringStateDocumentKind,
                "Activity definition authoring state",
                [
                    Keyword(ByDefinitionIndex, DefinitionIdField),
                    Keyword(ByHeadVersionIndex, HeadVersionIdField)
                ],
                [
                    new ActivityQuery(
                        "list-by-definition",
                        ByDefinitionIndex,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal, PortableQueryOperation.In },
                        QuerySortSupport.Ascending,
                        QueryPagingSupport.Offset,
                        [
                            new BoundedQuerySortField(DefinitionIdField, PhysicalSortDirection.Ascending),
                            new BoundedQuerySortField(EntityIdField, PhysicalSortDirection.Ascending)
                        ]),
                    Query("list-by-head-version", ByHeadVersionIndex, HeadVersionIdField)
                ]),
            Unit(
                ActivityDefinitionDraftDocumentKind,
                "Activity definition draft",
                [Keyword(ByDefinitionIndex, DefinitionIdField)],
                [Query("list-by-definition", ByDefinitionIndex, DefinitionIdField)]),
            Unit(
                ActivityDefinitionDraftLayoutDocumentKind,
                "Activity definition draft layout",
                [Keyword(ByDraftIndex, DraftIdField)],
                [Query("list-by-draft", ByDraftIndex, DraftIdField)]),
            Unit(
                ActivityDraftValidationDocumentKind,
                "Activity draft validation",
                [Keyword(ByDraftIndex, DraftIdField)],
                [Query("list-by-draft", ByDraftIndex, DraftIdField)]),
            Unit(
                ActivityDefinitionVersionPublicationDocumentKind,
                "Activity definition version publication",
                [
                    Keyword(ByDefinitionIndex, DefinitionIdField),
                    Keyword(ByDefinitionVersionIndex, DefinitionVersionIdField)
                ],
                [
                    Query("list-by-definition", ByDefinitionIndex, DefinitionIdField),
                    Query("list-by-definition-version", ByDefinitionVersionIndex, DefinitionVersionIdField)
                ]),
            Unit(
                ActivityDefinitionVersionLayoutDocumentKind,
                "Activity definition version layout",
                [Keyword(ByDefinitionVersionIndex, DefinitionVersionIdField)],
                [Query("list-by-definition-version", ByDefinitionVersionIndex, DefinitionVersionIdField)]),
            Unit(
                ActivityDependencyEdgeDocumentKind,
                "Activity dependency edge",
                [
                    Keyword(ByOwnerVersionIndex, OwnerVersionIdField),
                    Keyword(ByDependencyVersionIndex, DependencyVersionIdField)
                ],
                [
                    Query("list-by-owner-version", ByOwnerVersionIndex, OwnerVersionIdField),
                    Query("list-by-dependency-version", ByDependencyVersionIndex, DependencyVersionIdField)
                ]),
            Unit(
                ActivityDependencyProjectionDocumentKind,
                "Activity dependency projection",
                [],
                []),
            Unit(
                ActivityUpgradePlanDocumentKind,
                "Activity upgrade plan",
                [],
                []),
            Unit(
                ActivityForkCandidateDocumentKind,
                "Activity fork candidate",
                [
                    Keyword(
                        ActivityForkCandidateRetentionIndex,
                        ActivityForkCandidateRetentionField,
                        PortableQueryOperation.LessThanOrEqual)
                ],
                [
                    new ActivityQuery(
                        ActivityForkCandidateExpiredQuery,
                        ActivityForkCandidateRetentionIndex,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
                        QuerySortSupport.Ascending,
                        QueryPagingSupport.Offset,
                        [
                            new BoundedQuerySortField(
                                ActivityForkCandidateRetentionField,
                                PhysicalSortDirection.Ascending),
                            new BoundedQuerySortField(
                                EntityIdField,
                                PhysicalSortDirection.Ascending)
                        ])
                ]),
            Unit(
                ActivityForkReceiptDocumentKind,
                "Activity fork receipt",
                [],
                [],
                LifecyclePolicy.AppendOnly),
            Unit(
                ActivityUpgradeApplyReceiptDocumentKind,
                "Activity upgrade apply receipt",
                [],
                []),
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
            LogicalIndex("activity-definition-by-id-point", [DocumentIdField], unique: true),
            LogicalIndex("activity-definition-by-id", [ActivityDefinitionIdField]),
            LogicalIndex("activity-definition-by-type-key", [ActivityDefinitionTypeKeyField, ActivityDefinitionIdField]),
            LogicalIndex("activity-definition-by-category", [ActivityDefinitionCategoryField, ActivityDefinitionIdField]),
            LogicalIndex("activity-definition-by-display-name", [ActivityDefinitionDisplayNameField, ActivityDefinitionIdField]),
            LogicalIndex("activity-definition-by-description", [ActivityDefinitionDescriptionField, ActivityDefinitionIdField]),
            LogicalIndex(V2("activity-definition-by-id"), [ActivityDefinitionIdField], unique: true),
            LogicalIndex(V2("activity-definition-by-type-key"), [ActivityDefinitionTypeKeyField, ActivityDefinitionIdField], unique: true),
            LogicalIndex(V2("activity-definition-by-category"), [ActivityDefinitionCategoryField, ActivityDefinitionIdField], unique: true),
            LogicalIndex(V2("activity-definition-by-display-name"), [ActivityDefinitionDisplayNameField, ActivityDefinitionIdField], unique: true, missingValues: MissingValueBehavior.IncludedAsNull),
            LogicalIndex(V2("activity-definition-by-description"), [ActivityDefinitionDescriptionField, ActivityDefinitionIdField], unique: true)
        };
        var physicalIndexes = new[]
        {
            PointLookupIndex("activity-definition-by-id-point"),
            PhysicalIndex("activity-definition-by-id", "activity_definition_id"),
            PhysicalIndex("activity-definition-by-type-key", "activity_type_key", "activity_definition_id"),
            PhysicalIndex("activity-definition-by-category", "category", "activity_definition_id"),
            PhysicalIndex("activity-definition-by-display-name", "display_name", "activity_definition_id"),
            PhysicalIndex("activity-definition-by-description", "description", "activity_definition_id"),
            UniquePhysicalIndex(V2("activity-definition-by-id"), "activity_definition_id"),
            UniquePhysicalIndex(V2("activity-definition-by-type-key"), "activity_type_key", "activity_definition_id"),
            UniquePhysicalIndex(V2("activity-definition-by-category"), "category", "activity_definition_id"),
            SearchPhysicalIndex(V2("activity-definition-by-display-name"), "display_name", "activity_definition_id"),
            UniquePhysicalIndex(V2("activity-definition-by-description"), "description", "activity_definition_id")
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
                FindActivityDefinitionByIdQuery,
                "activity-definition-by-id-point",
                [Predicate(DocumentIdField, PortableQueryOperation.Equal)],
                QueryPagingSupport.None,
                QuerySortSupport.None,
                [BoundedQueryResultOperation.First, BoundedQueryResultOperation.Any]),
            BoundedQuery(
                ListActivityDefinitionsByIdQuery,
                V2("activity-definition-by-id"),
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
                V2("activity-definition-by-type-key"),
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
                V2("activity-definition-by-category"),
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
                V2("activity-definition-by-display-name"),
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
                V2("activity-definition-by-description"),
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
            // The search route rides the display-name index: a separate search index would repeat
            // the identical (display_name, activity_definition_id) key pattern, which MongoDB
            // rejects as a duplicate and every provider would pay twice for on writes.
            BoundedQuery(
                SearchActivityDefinitionsQuery,
                V2("activity-definition-by-display-name"),
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
                Column("activity_definition_id", ActivityDefinitionIdField, false, IdentityColumnLength),
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
            LogicalIndex("activity-definition-version-by-id-point", [DocumentIdField], unique: true),
            LogicalIndex(
                "activity-definition-versions-by-definition",
                [
                    ActivityDefinitionVersionDefinitionIdField,
                    ActivityDefinitionVersionSemVerSortKeyField,
                    ActivityDefinitionVersionIdField
                ]),
            LogicalIndex(
                V2("activity-definition-versions-by-definition"),
                [
                    ActivityDefinitionVersionDefinitionIdField,
                    ActivityDefinitionVersionSemVerSortKeyField,
                    ActivityDefinitionVersionIdField
                ],
                unique: true),
            LogicalIndex(
                "activity-definition-version-by-definition-and-sort-key",
                [ActivityDefinitionVersionDefinitionIdField, ActivityDefinitionVersionSemVerSortKeyField])
        };
        var physicalIndexes = new[]
        {
            PointLookupIndex("activity-definition-version-by-id-point"),
            PhysicalIndex(
                "activity-definition-versions-by-definition",
                "definition_id",
                "sem_ver_sort_key",
                "version_id"),
            UniquePhysicalIndex(
                V2("activity-definition-versions-by-definition"),
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
                FindActivityDefinitionVersionByIdQuery,
                "activity-definition-version-by-id-point",
                [Predicate(DocumentIdField, PortableQueryOperation.Equal)],
                QueryPagingSupport.None,
                QuerySortSupport.None,
                [BoundedQueryResultOperation.First, BoundedQueryResultOperation.Any]),
            BoundedQuery(
                ListActivityDefinitionVersionsByDefinitionQuery,
                V2("activity-definition-versions-by-definition"),
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
                Column("version_id", ActivityDefinitionVersionIdField, false, IdentityColumnLength),
                Column("definition_id", ActivityDefinitionVersionDefinitionIdField, false, IdentityColumnLength),
                Column("sem_ver_sort_key", ActivityDefinitionVersionSemVerSortKeyField, false, IdentityColumnLength)
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

    // The declared order leads with the index key field, then the document id. Leading at the index head
    // (rather than the document id alone) is what lets the route admit both a single-value equality read and
    // a deterministic zero-clause full-kind traversal without a mandatory equality prefix.
    private static ActivityQuery Query(string name, string indexName, string keyField) => new(
        name,
        indexName,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.Ascending,
        QueryPagingSupport.Offset,
        [
            new BoundedQuerySortField(keyField, PhysicalSortDirection.Ascending),
            new BoundedQuerySortField(EntityIdField, PhysicalSortDirection.Ascending)
        ]);

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
            Column("resource_id", ManagementResourceIdField, false, IdentityColumnLength),
            Column("definition_id", DefinitionIdField, false, IdentityColumnLength),
            Column("draft_id", DraftIdField, length: IdentityColumnLength),
            Column("definition_version_id", DefinitionVersionIdField, length: IdentityColumnLength),
            Column("valid_from", ManagementValidFromField, false, ManagementSequenceKeyLength),
            Column("valid_to", ManagementValidToField, false, ManagementSequenceKeyLength),
            Column("visibility", ManagementVisibilityField, false),
            Column("sort_key", ManagementSortField, false),
            Column("search_text", ManagementSearchField, false, ManagementSearchMaximumLength),
            NumberColumn("authority", ManagementAuthorityField),
            Column("provider", ManagementProviderField),
            Column("head_provider", ManagementHeadProviderField),
            Column("recommendation_provider", ManagementRecommendationProviderField),
            NumberColumn("draft_status", ManagementDraftStatusField),
            NumberColumn("version_lifecycle", ManagementVersionLifecycleField)
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
                NumericMemberPaths.Contains(predicate.Path) ? IndexValueKind.Number : IndexValueKind.Keyword,
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
                [new IndexField(ManagementSortField, IndexValueKind.Keyword)],
                IndexValueKind.Keyword,
                false,
                MissingValueBehavior.Excluded),
            new LogicalIndexDeclaration(
                "management-by-valid-to",
                [new IndexField(ManagementValidToField, IndexValueKind.Keyword)],
                IndexValueKind.Keyword,
                false,
                MissingValueBehavior.Excluded),
            new LogicalIndexDeclaration(
                V2("management-by-sort"),
                [
                    new IndexField(ManagementSortField, IndexValueKind.Keyword),
                    new IndexField(ManagementValidFromField, IndexValueKind.Keyword)
                ],
                IndexValueKind.Keyword,
                true,
                MissingValueBehavior.Excluded),
            new LogicalIndexDeclaration(
                V2("management-by-valid-to"),
                [
                    new IndexField(ManagementValidToField, IndexValueKind.Keyword),
                    new IndexField(ManagementResourceIdField, IndexValueKind.Keyword),
                    new IndexField(ManagementValidFromField, IndexValueKind.Keyword)
                ],
                IndexValueKind.Keyword,
                true,
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
                V2("management-by-sort"),
                pageOperations,
                QuerySortSupport.Ascending,
                QueryPagingSupport.Offset,
                BoundedQueryExecutionClass.ScaleBearing,
                supportsDisjunction: true,
                supportsTotalCount: true,
                sortFields:
                [
                    new BoundedQuerySortField(ManagementSortField, PhysicalSortDirection.Ascending),
                    new BoundedQuerySortField(ManagementValidFromField, PhysicalSortDirection.Ascending)
                ],
                predicateFields: pageIndexPredicates,
                residualPredicateFields: pageResidualPredicates),
            new BoundedQueryDeclaration(
                ManagementExpiredQuery,
                V2("management-by-valid-to"),
                new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
                QuerySortSupport.Ascending,
                QueryPagingSupport.Offset,
                BoundedQueryExecutionClass.ScaleBearing,
                supportsTotalCount: true,
                sortFields:
                [
                    new BoundedQuerySortField(ManagementValidToField, PhysicalSortDirection.Ascending),
                    new BoundedQuerySortField(ManagementResourceIdField, PhysicalSortDirection.Ascending),
                    new BoundedQuerySortField(ManagementValidFromField, PhysicalSortDirection.Ascending)
                ],
                predicateFields: [Predicate(ManagementValidToField, PortableQueryOperation.LessThanOrEqual)])
        };
        var physical = PhysicalTableDefinition.PhysicalEntityTable(
            documentKind,
            columns,
            Envelope,
            indexes:
            [
                // Each of these restates the Excluded its logical declaration above carries. The two
                // have to agree, and the default is IncludedAsNull, so silence here would mean the
                // physical side quietly widened away from the declaration (GW-PHYSICAL-025).
                new PhysicalIndexDefinition(
                    "management-by-id",
                    [
                        new PhysicalIndexColumnDefinition(Envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(ColumnName(logicalIdField), 1)
                    ],
                    missingValueBehavior: MissingValueBehavior.Excluded),
                new PhysicalIndexDefinition(
                    "management-by-sort",
                    [
                        new PhysicalIndexColumnDefinition(Envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition("sort_key", 1)
                    ],
                    missingValueBehavior: MissingValueBehavior.Excluded),
                new PhysicalIndexDefinition(
                    "management-by-valid-to",
                    [
                        new PhysicalIndexColumnDefinition(Envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition("valid_to", 1)
                    ],
                    missingValueBehavior: MissingValueBehavior.Excluded),
                new PhysicalIndexDefinition(
                    V2("management-by-sort"),
                    [
                        new PhysicalIndexColumnDefinition(Envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition("sort_key", 1),
                        new PhysicalIndexColumnDefinition("valid_from", 2)
                    ],
                    isUnique: true,
                    missingValueBehavior: MissingValueBehavior.Excluded),
                new PhysicalIndexDefinition(
                    V2("management-by-valid-to"),
                    [
                        new PhysicalIndexColumnDefinition(Envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition("valid_to", 1),
                        new PhysicalIndexColumnDefinition("resource_id", 2),
                        new PhysicalIndexColumnDefinition("valid_from", 3)
                    ],
                    isUnique: true,
                    missingValueBehavior: MissingValueBehavior.Excluded)
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
                    PhysicalTableDefinition.PhysicalEntityTable(documentKind, columns, Envelope, physicalIndexes)),
                logicalIndexes,
                boundedQueries)
        };
    }

    private static LogicalIndexDeclaration LogicalIndex(
        string identity,
        string[] fields,
        bool unique = false,
        MissingValueBehavior missingValues = MissingValueBehavior.Excluded) =>
        new(
            identity,
            fields.Select(field => new IndexField(field, IndexValueKind.Keyword)).ToArray(),
            IndexValueKind.Keyword,
            unique,
            missingValues);

    private static PhysicalIndexDefinition PhysicalIndex(string identity, params string[] columns) =>
        new(
            identity,
            [
                new PhysicalIndexColumnDefinition(Envelope.StorageScopeColumn, 0),
                .. columns.Select((column, index) => new PhysicalIndexColumnDefinition(column, index + 1))
            ],
            missingValueBehavior: MissingValueBehavior.Excluded);

    private static PhysicalIndexDefinition UniquePhysicalIndex(string identity, params string[] columns) =>
        new(
            identity,
            [
                new PhysicalIndexColumnDefinition(Envelope.StorageScopeColumn, 0),
                .. columns.Select((column, index) => new PhysicalIndexColumnDefinition(column, index + 1))
            ],
            isUnique: true,
            missingValueBehavior: MissingValueBehavior.Excluded);

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

    private static string V2(string identity) => $"{identity}{AdditiveIndexVersionSuffix}";

    private static PhysicalIndexDefinition PointLookupIndex(string identity) =>
        new(
            identity,
            [
                new PhysicalIndexColumnDefinition(Envelope.StorageScopeColumn, 0),
                new PhysicalIndexColumnDefinition(Envelope.IdLookupKeyColumn, 1),
                new PhysicalIndexColumnDefinition(Envelope.IdComparisonKeyColumn, 2)
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

    // Searchable text columns are bounded to 256 characters and identity/sort-key columns to 128 so
    // every declared compound index key stays under SQL Server's 1700-byte nonclustered limit.
    // Over-limit values fail projection validation rather than truncate, per the ratified data model.
    private const int TextColumnLength = 256;
    private const int IdentityColumnLength = 128;

    private static ProjectedColumnDefinition Column(
        string name,
        string path,
        bool nullable = true,
        int length = TextColumnLength) => new(name, path, PortablePhysicalType.String, length, IsNullable: nullable);

    /// <summary>
    /// A numeric projection for canonical JSON number members (for example enum kinds). MongoDB
    /// resolves projections strictly by JSON type, so a number member must not be declared String.
    /// </summary>
    private static ProjectedColumnDefinition NumberColumn(string name, string path, bool nullable = true) =>
        new(name, path, PortablePhysicalType.Int32, IsNullable: nullable);

    private static BoundedQueryPredicateField Predicate(string path, params PortableQueryOperation[] operations) =>
        new(path, operations.ToHashSet());

    // These members are canonical JSON numbers (enum kinds), so their residual predicates and
    // projected columns must both be numeric; every other residual member is a keyword string.
    private static readonly HashSet<string> NumericMemberPaths =
    [
        ManagementAuthorityField,
        ManagementDraftStatusField,
        ManagementVersionLifecycleField
    ];

    private static BoundedQueryResidualPredicateField ResidualPredicate(
        string path,
        params PortableQueryOperation[] operations) =>
        new(
            path,
            NumericMemberPaths.Contains(path) ? IndexValueKind.Number : IndexValueKind.Keyword,
            operations.ToHashSet());

    private static StorageUnit PhysicalUnit(
        string documentKind,
        string label,
        LifecyclePolicy lifecycle,
        ActivityIndex[] indexes,
        ActivityQuery[] queries)
    {
        var documentIdOrderedIndexes = queries
            .Where(query => query.SortFields?.Any(field =>
                field.Path == EntityIdField && field.Direction == PhysicalSortDirection.Ascending) == true)
            .Select(query => query.IndexIdentity)
            .ToHashSet(StringComparer.Ordinal);
        if (indexes.Any(index => index.IsUnique && documentIdOrderedIndexes.Contains(index.Identity)))
        {
            throw new InvalidOperationException(
                "A unique activity-design index cannot be used by an id-sorted exhaustive route; declare a separate non-unique ordered route.");
        }
        var logicalIndexes = indexes
            .SelectMany(index =>
            {
                var includesDocumentIdentity = documentIdOrderedIndexes.Contains(index.Identity);
                IndexField[] fields =
                [
                    .. index.Fields.Select(field => new IndexField(field, IndexValueKind.Keyword)),
                    .. (!index.IsUnique && includesDocumentIdentity
                        ? new[] { new IndexField(EntityIdField, IndexValueKind.Keyword) }
                        : [])
                ];
                var current = new LogicalIndexDeclaration(
                    includesDocumentIdentity ? V2(index.Identity) : index.Identity,
                    fields,
                    IndexValueKind.Keyword,
                    index.IsUnique || includesDocumentIdentity,
                    MissingValueBehavior.Excluded);
                return includesDocumentIdentity
                    ? new[]
                    {
                        new LogicalIndexDeclaration(
                            index.Identity,
                            index.Identity == ActivityForkCandidateRetentionIndex
                                ? index.Fields.Select(field => new IndexField(field, IndexValueKind.Keyword)).ToArray()
                                : fields,
                            IndexValueKind.Keyword,
                            false,
                            MissingValueBehavior.Excluded),
                        current
                    }
                    : [current];
            })
            .ToArray();
        var indexedColumns = indexes
            .SelectMany(index => index.Fields)
            .Distinct(StringComparer.Ordinal)
            .Select(field => Column(
                ColumnName(field),
                field,
                length: ColumnName(field).EndsWith("_id", StringComparison.Ordinal)
                    ? IdentityColumnLength
                    : TextColumnLength))
            .ToArray();
        ProjectedColumnDefinition[] columns = indexedColumns.Length == 0 || documentIdOrderedIndexes.Count > 0
            ? [.. indexedColumns, Column("entity_id", EntityIdField, false, IdentityColumnLength)]
            : indexedColumns;
        var physicalIndexes = indexes
            .SelectMany(index =>
            {
                var physicalColumns = new List<PhysicalIndexColumnDefinition>
                {
                    new(Envelope.StorageScopeColumn, 0)
                };
                physicalColumns.AddRange(index.Fields.Select((field, order) =>
                    new PhysicalIndexColumnDefinition(ColumnName(field), order + 1)));
                var includesDocumentIdentity = documentIdOrderedIndexes.Contains(index.Identity);
                if (!index.IsUnique && includesDocumentIdentity)
                {
                    physicalColumns.Add(new PhysicalIndexColumnDefinition(
                        "entity_id",
                        physicalColumns.Count,
                        PhysicalSortDirection.Ascending));
                }

                var providerTieBreakPaging = queries
                    .Where(query => query.IndexIdentity == index.Identity)
                    .Select(query => query.PagingSupport)
                    .Where(paging => paging is QueryPagingSupport.Cursor or QueryPagingSupport.Offset)
                    .Distinct()
                    .ToArray();
                if (!index.IsUnique && !includesDocumentIdentity && providerTieBreakPaging.Length == 1)
                {
                    physicalColumns.Add(new PhysicalIndexColumnDefinition(
                        providerTieBreakPaging[0] == QueryPagingSupport.Cursor
                            ? Envelope.IdLookupKeyColumn
                            : Envelope.IdComparisonKeyColumn,
                        physicalColumns.Count));
                }
                else if (!index.IsUnique && !includesDocumentIdentity && providerTieBreakPaging.Length > 1)
                {
                    throw new InvalidOperationException(
                        $"Activity-design index '{index.Identity}' cannot mix cursor and offset provider identity tie-breaks.");
                }

                var current = new PhysicalIndexDefinition(
                    includesDocumentIdentity ? V2(index.Identity) : index.Identity,
                    physicalColumns,
                    isUnique: index.IsUnique || includesDocumentIdentity,
                    missingValueBehavior: MissingValueBehavior.Excluded);
                return includesDocumentIdentity
                    ? new[]
                    {
                        new PhysicalIndexDefinition(
                            index.Identity,
                            index.Identity == ActivityForkCandidateRetentionIndex
                                ? physicalColumns.Where(column =>
                                    column.ColumnLogicalName != "entity_id").ToArray()
                                : physicalColumns,
                            isUnique: false,
                            missingValueBehavior: MissingValueBehavior.Excluded),
                        current
                    }
                    : [current];
            })
            .ToArray();
        var boundedQueries = queries
            .Select(query =>
            {
                var index = indexes.Single(index => index.Identity == query.IndexIdentity);
                return new BoundedQueryDeclaration(
                    query.Identity,
                    documentIdOrderedIndexes.Contains(query.IndexIdentity) ? V2(query.IndexIdentity) : query.IndexIdentity,
                    query.Operations,
                    query.SortSupport,
                    query.PagingSupport,
                    BoundedQueryExecutionClass.ScaleBearing,
                    sortFields: query.SortFields,
                    predicateFields:
                    [
                        new BoundedQueryPredicateField(
                            index.Fields[0],
                            query.Operations)
                    ]);
            })
            .ToArray();

        var unit = BaseUnit(documentKind, label, lifecycle);
        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(PhysicalTableDefinition.PhysicalEntityTable(documentKind, columns, Envelope, physicalIndexes)),
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
