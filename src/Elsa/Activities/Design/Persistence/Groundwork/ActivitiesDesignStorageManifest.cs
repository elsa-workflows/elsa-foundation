using Groundwork.Kernel;

namespace Elsa.Activities.Design.Persistence.Groundwork;

/// <summary>Fresh-catalog Groundwork v2 rows owned by activities-design persistence.</summary>
public static class ActivitiesDesignStorageManifest
{
    public const string SchemaVersion = "1.0.0";
    public const int StorageSchemaVersion = 1;
    public const int MaximumIdLength = 450;
    public const int MaximumProjectionLength = 256;

    public const string IdField = "id";
    public const string EntityIdField = IdField;
    public const string SchemaVersionField = "schemaVersion";
    public const string ContentField = "content";
    public const string RevisionField = "revision";
    public const string ScopeField = "scope";
    public const string TenantIdField = "tenantId";
    public const string ByDefinitionIndex = "by_definition";
    public const string ByHeadVersionIndex = "by_head_version";
    public const string ByDraftIndex = "by_draft";
    public const string ByDefinitionVersionIndex = "by_definition_version";
    public const string ByOwnerVersionIndex = "by_owner_version";
    public const string ByDependencyVersionIndex = "by_dependency_version";
    public const string DefinitionIdField = "definitionId";
    public const string HeadVersionIdField = "headVersionId";
    public const string DraftIdField = "draftId";
    public const string DefinitionVersionIdField = "definitionVersionId";
    public const string OwnerVersionIdField = "ownerVersionId";
    public const string DependencyVersionIdField = "dependencyVersionId";
    public const string ManagementResourceIdField = "resourceId";
    public const string ManagementValidFromField = "validFromKey";
    public const string ManagementValidToField = "validToKey";
    public const string ManagementVisibilityField = "visibilityKey";
    public const string ManagementSortField = "sortKey";
    public const string ManagementSearchField = "searchText";
    public const int ManagementSequenceKeyLength = 20;
    public const int ManagementSearchMaximumLength = 4000;
    public const string ManagementAuthorityField = "authorityKind";
    public const string ManagementProviderField = "providerKey";
    public const string ManagementHeadProviderField = "headProviderKey";
    public const string ManagementRecommendationProviderField = "recommendationProviderKey";
    public const string ManagementDraftStatusField = "status";
    public const string ManagementVersionLifecycleField = "lifecycle";
    public const string ActivityDefinitionIdField = IdField;
    public const string ActivityDefinitionTypeKeyField = "activityTypeKey";
    public const string ActivityDefinitionCategoryField = "category";
    public const string ActivityDefinitionDisplayNameField = "displayName";
    public const string ActivityDefinitionDescriptionField = "description";
    public const string ActivityDefinitionVersionIdField = IdField;
    public const string ActivityDefinitionVersionDefinitionIdField = DefinitionIdField;
    public const string ActivityDefinitionVersionSemVerSortKeyField = "semVerSortKey";

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
    public const string ActivityDefinitionCollection = ActivityDefinitionDocumentKind;
    public const string ActivityDefinitionVersionDocumentKind = "activityDefinitionVersion";
    public const string ActivityDefinitionVersionCollection = ActivityDefinitionVersionDocumentKind;
    public const string ActivityAvailabilitySettingsDocumentKind = "activityAvailabilitySettings";
    public const string ActivityAvailabilitySettingsCollection = ActivityAvailabilitySettingsDocumentKind;
    public const string ActivityDefinitionAuthoringStateDocumentKind = "activityDefinitionAuthoringState";
    public const string ActivityDefinitionAuthoringStateCollection = ActivityDefinitionAuthoringStateDocumentKind;
    public const string ActivityDefinitionDraftDocumentKind = "activityDefinitionDraft";
    public const string ActivityDefinitionDraftCollection = ActivityDefinitionDraftDocumentKind;
    public const string ActivityDefinitionDraftLayoutDocumentKind = "activityDefinitionDraftLayout";
    public const string ActivityDefinitionDraftLayoutCollection = ActivityDefinitionDraftLayoutDocumentKind;
    public const string ActivityDraftValidationDocumentKind = "activityDraftValidation";
    public const string ActivityDraftValidationCollection = ActivityDraftValidationDocumentKind;
    public const string ActivityDefinitionVersionPublicationDocumentKind = "activityDefinitionVersionPublication";
    public const string ActivityDefinitionVersionPublicationCollection = ActivityDefinitionVersionPublicationDocumentKind;
    public const string ActivityDefinitionVersionLayoutDocumentKind = "activityDefinitionVersionLayout";
    public const string ActivityDefinitionVersionLayoutCollection = ActivityDefinitionVersionLayoutDocumentKind;
    public const string ActivityDependencyEdgeDocumentKind = "activityDependencyEdge";
    public const string ActivityDependencyEdgeCollection = ActivityDependencyEdgeDocumentKind;
    public const string ActivityDependencyProjectionDocumentKind = "activityDependencyProjection";
    public const string ActivityDependencyProjectionCollection = ActivityDependencyProjectionDocumentKind;
    public const string ActivityUpgradePlanDocumentKind = "activityUpgradePlan";
    public const string ActivityUpgradePlanCollection = ActivityUpgradePlanDocumentKind;
    public const string ActivityUpgradeApplyReceiptDocumentKind = "activityUpgradeApplyReceipt";
    public const string ActivityUpgradeApplyReceiptCollection = ActivityUpgradeApplyReceiptDocumentKind;
    public const string ActivityForkCandidateDocumentKind = "activityForkCandidate";
    public const string ActivityForkCandidateCollection = ActivityForkCandidateDocumentKind;
    public const string ActivityForkCandidateRetentionField = "retentionKey";
    public const string ActivityForkCandidateRetentionIndex = "fork_candidate_by_retention";
    public const string ActivityForkCandidateExpiredQuery = "fork-candidate-expired";
    public const string ActivityForkReceiptDocumentKind = "activityForkReceipt";
    public const string ActivityForkReceiptCollection = ActivityForkReceiptDocumentKind;
    public const string ActivityDefinitionManagementProjectionDocumentKind = "activityDefinitionManagementProjection";
    public const string ActivityDefinitionManagementProjectionCollection = ActivityDefinitionManagementProjectionDocumentKind;
    public const string ActivityDraftManagementProjectionDocumentKind = "activityDraftManagementProjection";
    public const string ActivityDraftManagementProjectionCollection = ActivityDraftManagementProjectionDocumentKind;
    public const string ActivityVersionManagementProjectionDocumentKind = "activityVersionManagementProjection";
    public const string ActivityVersionManagementProjectionCollection = ActivityVersionManagementProjectionDocumentKind;
    public const string ActivityManagementProjectionWatermarkDocumentKind = "activityManagementProjectionWatermark";
    public const string ActivityManagementProjectionWatermarkCollection = ActivityManagementProjectionWatermarkDocumentKind;
    public const string ActivityManagementProjectionSnapshotDocumentKind = "activityManagementProjectionSnapshot";
    public const string ActivityManagementProjectionSnapshotCollection = ActivityManagementProjectionSnapshotDocumentKind;
    public const string ManagementDefinitionCurrentQuery = "management-definition-current";
    public const string ManagementDraftCurrentQuery = "management-draft-current";
    public const string ManagementVersionCurrentQuery = "management-version-current";
    public const string ManagementDefinitionsQuery = "management-definitions-identity-asc";
    public const string ManagementDraftsQuery = "management-drafts-identity-asc";
    public const string ManagementVersionsQuery = "management-versions-identity-asc";
    public const string ManagementExpiredQuery = "management-expired";
    public const string DesignOperationDocumentKind = "designOperation";
    public const string DesignOperationCollection = DesignOperationDocumentKind;

    public static IReadOnlyList<ActivityDesignQueryOrder> ByDefinitionDocumentOrder =>
        [new(DefinitionIdField), new(EntityIdField)];
    public static IReadOnlyList<ActivityDesignQueryOrder> ByOwnerVersionDocumentOrder =>
        [new(OwnerVersionIdField), new(EntityIdField)];
    public static IReadOnlyList<ActivityDesignQueryOrder> ActivityDefinitionTypeKeyOrder =>
        [new(ActivityDefinitionTypeKeyField), new(EntityIdField)];
    public static IReadOnlyList<ActivityDesignQueryOrder> ActivityDefinitionCategoryOrder =>
        [new(ActivityDefinitionCategoryField), new(EntityIdField)];
    public static IReadOnlyList<ActivityDesignQueryOrder> ActivityDefinitionVersionOrder =>
        [new(ActivityDefinitionVersionDefinitionIdField), new(ActivityDefinitionVersionSemVerSortKeyField), new(EntityIdField)];


    public static IReadOnlyList<StorageUnit> CreateUnits() => UnitNames.Select(pair => CreateUnit(pair.Id, pair.Name)).ToArray();

    public static StorageUnit Require(string unitId) => CreateUnits().Single(unit => unit.Id.Value == unitId);

    private static StorageUnit CreateUnit(string id, string name)
    {
        var declaration = StorageUnit.Declare(id, name)
            .String(IdField, MaximumIdLength, column => column.Required())
            .String(SchemaVersionField, 32, column => column.Required())
            .Json(ContentField, column => column.Required())
            .Int64(RevisionField, column => column.Required())
            .String(ScopeField, MaximumProjectionLength)
            .String(TenantIdField, 256)
            .Key(IdField)
            .OptimisticConcurrency();
        foreach (var field in ProjectionFields)
        {
            if (field is IdField or SchemaVersionField or ContentField or RevisionField or ScopeField or TenantIdField)
                continue;
            declaration.String(field, ProjectionLength(field));
        }
        foreach (var index in IndexesFor(id))
            declaration.Index(index.Name, index.Columns.ToArray());
        return declaration.Scoped().Build() with { SchemaVersion = StorageSchemaVersion };
    }

    private static IEnumerable<IndexSpec> IndexesFor(string id) => id switch
    {
        ActivityDefinitionDocumentKind =>
        [
            Index("activity_definition_by_type_key", ActivityDefinitionTypeKeyField, IdField),
            Index("activity_definition_by_category", ActivityDefinitionCategoryField, IdField),
            Index("activity_definition_by_display_name", ActivityDefinitionDisplayNameField, IdField),
            Index("activity_definition_by_description", ActivityDefinitionDescriptionField, IdField),
            Index("activity_definition_by_search", ManagementSearchField, IdField)
        ],
        ActivityDefinitionAuthoringStateDocumentKind => [Index(ByDefinitionIndex, DefinitionIdField, IdField), Index(ByHeadVersionIndex, HeadVersionIdField, IdField)],
        ActivityDefinitionDraftDocumentKind => [Index(ByDefinitionIndex, DefinitionIdField, IdField)],
        ActivityDefinitionDraftLayoutDocumentKind or ActivityDraftValidationDocumentKind => [Index(ByDraftIndex, DraftIdField, IdField)],
        ActivityDefinitionVersionPublicationDocumentKind => [Index(ByDefinitionIndex, DefinitionIdField, IdField), Index(ByDefinitionVersionIndex, DefinitionVersionIdField, IdField)],
        ActivityDefinitionVersionLayoutDocumentKind => [Index(ByDefinitionVersionIndex, DefinitionVersionIdField, IdField)],
        ActivityDependencyEdgeDocumentKind => [Index(ByOwnerVersionIndex, OwnerVersionIdField, IdField), Index(ByDependencyVersionIndex, DependencyVersionIdField, IdField)],
        ActivityForkCandidateDocumentKind => [Index(ActivityForkCandidateRetentionIndex, ActivityForkCandidateRetentionField, IdField)],
        ActivityDefinitionManagementProjectionDocumentKind => [Index("management_definitions_identity_asc", ManagementSortField, ManagementValidFromField, IdField)],
        ActivityDraftManagementProjectionDocumentKind => [Index("management_drafts_identity_asc", ManagementSortField, ManagementValidFromField, IdField)],
        ActivityVersionManagementProjectionDocumentKind => [Index("management_versions_identity_asc", ManagementSortField, ManagementValidFromField, IdField)],
        _ => []
    };

    private static readonly string[] ProjectionFields =
    [
        DefinitionIdField, HeadVersionIdField, DraftIdField, DefinitionVersionIdField, OwnerVersionIdField,
        DependencyVersionIdField, ManagementResourceIdField, ManagementValidFromField, ManagementValidToField,
        ManagementVisibilityField, ManagementSortField, ManagementSearchField, ManagementAuthorityField,
        ManagementProviderField, ManagementHeadProviderField, ManagementRecommendationProviderField,
        ManagementDraftStatusField, ManagementVersionLifecycleField, ActivityDefinitionTypeKeyField,
        ActivityDefinitionCategoryField, ActivityDefinitionDisplayNameField, ActivityDefinitionDescriptionField,
        ActivityDefinitionVersionSemVerSortKeyField, ActivityForkCandidateRetentionField
    ];

    private static readonly (string Id, string Name)[] UnitNames =
    [
        (ActivityDefinitionDocumentKind, "elsa_activity_definitions"),
        (ActivityDefinitionVersionDocumentKind, "elsa_activity_definition_versions"),
        (ActivityAvailabilitySettingsDocumentKind, "elsa_activity_availability_settings"),
        (ActivityDefinitionAuthoringStateDocumentKind, "elsa_activity_definition_authoring"),
        (ActivityDefinitionDraftDocumentKind, "elsa_activity_definition_drafts"),
        (ActivityDefinitionDraftLayoutDocumentKind, "elsa_activity_definition_draft_layouts"),
        (ActivityDraftValidationDocumentKind, "elsa_activity_draft_validations"),
        (ActivityDefinitionVersionPublicationDocumentKind, "elsa_activity_version_publications"),
        (ActivityDefinitionVersionLayoutDocumentKind, "elsa_activity_version_layouts"),
        (ActivityDependencyEdgeDocumentKind, "elsa_activity_dependency_edges"),
        (ActivityDependencyProjectionDocumentKind, "elsa_activity_dependency_projection"),
        (ActivityUpgradePlanDocumentKind, "elsa_activity_upgrade_plans"),
        (ActivityUpgradeApplyReceiptDocumentKind, "elsa_activity_upgrade_apply_receipts"),
        (ActivityForkCandidateDocumentKind, "elsa_activity_fork_candidates"),
        (ActivityForkReceiptDocumentKind, "elsa_activity_fork_receipts"),
        (ActivityDefinitionManagementProjectionDocumentKind, "elsa_activity_management_definitions"),
        (ActivityDraftManagementProjectionDocumentKind, "elsa_activity_management_drafts"),
        (ActivityVersionManagementProjectionDocumentKind, "elsa_activity_management_versions"),
        (ActivityManagementProjectionWatermarkDocumentKind, "elsa_activity_management_watermarks"),
        (ActivityManagementProjectionSnapshotDocumentKind, "elsa_activity_management_snapshots"),
        (DesignOperationDocumentKind, "elsa_activity_design_operations")
    ];

    private static IndexSpec Index(string name, params string[] columns) => new(name, columns);

    private static int ProjectionLength(string field) => field switch
    {
        ActivityDefinitionTypeKeyField or ActivityDefinitionCategoryField or
            ActivityDefinitionDisplayNameField or ActivityDefinitionDescriptionField or
            ManagementSearchField or ManagementProviderField or ManagementHeadProviderField or
            ManagementRecommendationProviderField => 256,
        ManagementValidFromField or ManagementValidToField or ManagementVisibilityField or
            ManagementSortField or ManagementAuthorityField or ManagementDraftStatusField or
            ManagementVersionLifecycleField or ActivityForkCandidateRetentionField => 128,
        _ => MaximumProjectionLength
    };

    private sealed record IndexSpec(string Name, IReadOnlyList<string> Columns);
}
