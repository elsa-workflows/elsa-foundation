using Groundwork.Kernel;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>
/// The provider-neutral Groundwork v2 catalog for workflow-design persistence.
/// Projection values are written as first-class row values; the JSON payload is retained only for
/// aggregate materialization. Provider routes use only indexes whose declared key widths fit the
/// public portable budget; exact name/description filtering uses fixed-width lookup hashes and
/// substring filtering uses the bounded identity candidate probe.
/// </summary>
public static class WorkflowsDesignStorageManifest
{
    public const string SchemaVersion = "1.0.0";
    // The definition projection changed incompatibly when the portable ID lookup hash was added.
    // Keep the envelope version stable, but provision a new physical table for this clean
    // pre-GA baseline so Groundwork never attempts to add a required, non-canonical projection
    // column or retain the removed wide indexes in place.
    public const int DefinitionStorageSchemaVersion = 2;
    public const int IdentityMaximumLength = 128;
    public const int TextMaximumLength = 256;
    public const int SchemaVersionMaximumLength = 32;
    // The provider-independent Unicode ordinal-ignore-case key uses six hexadecimal characters
    // plus a boundary marker per UTF-16 code unit. It is retained for substring matching, but is
    // deliberately not indexed: its maximum width is larger than the strict portable index cap.
    public const int DefinitionIdSearchKeyMaximumLength = IdentityMaximumLength * 7;
    public const int DefinitionIdLookupHashMaximumLength = 64;
    public const int DefinitionTextLookupHashMaximumLength = 64;

    public const string WorkflowDefinitionDocumentKind = "workflowDefinition";
    public const string WorkflowDefinitionVersionDocumentKind = "workflowDefinitionVersion";
    public const string WorkflowDefinitionDraftDocumentKind = "workflowDefinitionDraft";
    public const string WorkflowDefinitionVersionLayoutDocumentKind = "workflowDefinitionVersionLayout";

    public const string WorkflowDefinitionCollection = WorkflowDefinitionDocumentKind;
    public const string WorkflowDefinitionVersionCollection = WorkflowDefinitionVersionDocumentKind;
    public const string WorkflowDefinitionDraftCollection = WorkflowDefinitionDraftDocumentKind;
    public const string WorkflowDefinitionVersionLayoutCollection = WorkflowDefinitionVersionLayoutDocumentKind;
    public const string DesignOperationDocumentKind = "workflowDesignOperation";

    public const string IdField = "id";
    public const string SchemaVersionField = "schemaVersion";
    public const string ContentField = "content";
    public const string TenantIdField = "tenantId";

    public const string DefinitionIdField = "definitionId";
    public const string DefinitionNameField = "name";
    public const string DefinitionDescriptionField = "description";
    public const string DefinitionIdSearchKeyField = "definitionIdSearchKey";
    public const string DefinitionIdLookupHashField = "definitionIdLookupHash";
    public const string DefinitionNameLookupHashField = "definitionNameLookupHash";
    public const string DefinitionDescriptionLookupHashField = "definitionDescriptionLookupHash";
    public const string DefinitionDeletedAtField = "deletedAt";
    public const string DefinitionDeletedReasonField = "deletedReason";
    public const string DefinitionIsSourceOwnedField = "isSourceOwned";

    public const string VersionIdField = "versionId";
    public const string VersionDefinitionIdField = "definitionId";
    public const string VersionField = "version";
    public const string ConcurrencyTokenField = "rowVersion";
    public const string VersionSemVerSortKeyField = "semVerSortKey";
    public const string VersionSourceDraftField = "sourceDraftId";

    public const string DraftIdField = "draftId";
    public const string DraftDefinitionIdField = "definitionId";
    public const string DraftSourceVersionField = "sourceVersionId";
    public const string DraftLastModifiedAtField = "lastModifiedAt";
    public const string DraftCreatedAtField = "createdAt";

    public const string LayoutVersionIdField = "versionId";
    public const string OperationIdField = "operationId";
    public const string OperationKindField = "operationKind";
    public const string OperationKeyField = "operationKey";
    public const string OperationRequestFingerprintField = "requestFingerprint";
    public const string OperationResultFingerprintField = "resultFingerprint";
    public const string OperationResultJsonField = "resultJson";

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

    // Groundwork physical identifiers are intentionally portable ASCII identifiers. The query
    // identities above retain the public route names; these names are the provider-facing indexes.
    public const string DefinitionByIdIndex = "definition_by_id_list_v2";
    public const string DefinitionByIdSearchIndex = "definition_by_id_search_v2";
    // These route identities use fixed-width exact lookup hashes, keeping the candidate route
    // narrow while the source column remains the authority for ordinal equality.
    public const string DefinitionByNameIndex = "definition_by_name_v2";
    public const string DefinitionByDescriptionIndex = "definition_by_description_v2";
    public const string VersionByDefinitionIndex = "versions_by_definition_v2";
    public const string VersionByDefinitionAndSortKeyIndex = "version_by_definition_and_sort_key";
    public const string LatestVersionByDefinitionIndex = "latest_version_by_definition";
    public const string DraftByDefinitionIndex = "drafts_by_definition_v2";
    public const string LayoutByVersionIndex = "layout_by_version";
    public const string OperationByKeyIndex = "design_operation_by_key";

    public static IReadOnlyList<StorageUnit> CreateUnits() =>
    [
        DefinitionUnit(),
        VersionUnit(),
        DraftUnit(),
        LayoutUnit(),
        OperationUnit()
    ];

    public static StorageUnit Require(string unitId) =>
        CreateUnits().Single(unit => StringComparer.Ordinal.Equals(unit.Id.Value, unitId));

    private static StorageUnit DefinitionUnit() =>
        StorageUnit.Declare(WorkflowDefinitionDocumentKind, "elsa_workflow_definitions_v2")
            .String(IdField, IdentityMaximumLength, column => column.Required())
            .String(SchemaVersionField, SchemaVersionMaximumLength, column => column.Required())
            .Json(ContentField, column => column.Required())
            .String(TenantIdField, IdentityMaximumLength)
            .String(DefinitionIdField, IdentityMaximumLength, column => column.Required())
            .String(DefinitionIdSearchKeyField, DefinitionIdSearchKeyMaximumLength, column => column.Required())
            .String(DefinitionIdLookupHashField, DefinitionIdLookupHashMaximumLength, column => column.Required())
            .String(DefinitionNameLookupHashField, DefinitionTextLookupHashMaximumLength)
            .String(DefinitionDescriptionLookupHashField, DefinitionTextLookupHashMaximumLength)
            .String(DefinitionNameField, TextMaximumLength, column => column.Collation(PortableCollation.UnicodeOrdinalIgnoreCase))
            .String(DefinitionDescriptionField, TextMaximumLength, column => column.Collation(PortableCollation.UnicodeOrdinalIgnoreCase))
            .Timestamp(DefinitionDeletedAtField)
            .String(DefinitionDeletedReasonField, TextMaximumLength)
            .Boolean(DefinitionIsSourceOwnedField)
            .Timestamp("createdAt", column => column.Required())
            .Timestamp("lastModifiedAt", column => column.Required())
            .Key(IdField)
            .OptimisticConcurrency(ConcurrencyTokenField)
            .UniqueIndex(DefinitionByIdIndex, DefinitionIdField)
            .UniqueIndex(DefinitionByIdSearchIndex, DefinitionIdLookupHashField)
            .Index(DefinitionByNameIndex, index => index.Ascending(DefinitionNameLookupHashField).Ascending(DefinitionIdField))
            .Index(DefinitionByDescriptionIndex, index => index.Ascending(DefinitionDescriptionLookupHashField).Ascending(DefinitionIdField))
            .Scoped()
            .Build() with { SchemaVersion = DefinitionStorageSchemaVersion };

    private static StorageUnit VersionUnit() =>
        StorageUnit.Declare(WorkflowDefinitionVersionDocumentKind, "elsa_workflow_definition_versions")
            .String(IdField, IdentityMaximumLength, column => column.Required())
            .String(SchemaVersionField, SchemaVersionMaximumLength, column => column.Required())
            .Json(ContentField, column => column.Required())
            .String(TenantIdField, IdentityMaximumLength)
            .String(VersionIdField, IdentityMaximumLength, column => column.Required())
            .String(VersionDefinitionIdField, IdentityMaximumLength, column => column.Required())
            .String(VersionField, IdentityMaximumLength, column => column.Required())
            .String(VersionSemVerSortKeyField, IdentityMaximumLength, column => column.Required())
            .String(VersionSourceDraftField, IdentityMaximumLength)
            .Timestamp("createdAt", column => column.Required())
            .Timestamp("lastModifiedAt", column => column.Required())
            .Key(IdField)
            .OptimisticConcurrency(ConcurrencyTokenField)
            .UniqueIndex(VersionByDefinitionIndex, index => index.Ascending(VersionDefinitionIdField).Ascending(VersionSemVerSortKeyField).Ascending(VersionIdField))
            .UniqueIndex(VersionByDefinitionAndSortKeyIndex, VersionDefinitionIdField, VersionSemVerSortKeyField)
            .Index(LatestVersionByDefinitionIndex, index => index.Ascending(VersionDefinitionIdField).Descending(VersionSemVerSortKeyField).Descending(VersionIdField))
            .Scoped()
            .Build();

    private static StorageUnit DraftUnit() =>
        StorageUnit.Declare(WorkflowDefinitionDraftDocumentKind, "elsa_workflow_definition_drafts")
            .String(IdField, IdentityMaximumLength, column => column.Required())
            .String(SchemaVersionField, SchemaVersionMaximumLength, column => column.Required())
            .Json(ContentField, column => column.Required())
            .String(TenantIdField, IdentityMaximumLength)
            .String(DraftIdField, IdentityMaximumLength, column => column.Required())
            .String(DraftDefinitionIdField, IdentityMaximumLength, column => column.Required())
            .String(DraftSourceVersionField, IdentityMaximumLength)
            .Timestamp(DraftLastModifiedAtField, column => column.Required())
            .Timestamp(DraftCreatedAtField, column => column.Required())
            .Key(IdField)
            .OptimisticConcurrency(ConcurrencyTokenField)
            .UniqueIndex(DraftByDefinitionIndex, index => index.Ascending(DraftDefinitionIdField).Descending(DraftLastModifiedAtField).Descending(DraftCreatedAtField).Descending(DraftIdField))
            .Scoped()
            .Build();

    private static StorageUnit LayoutUnit() =>
        StorageUnit.Declare(WorkflowDefinitionVersionLayoutDocumentKind, "elsa_workflow_definition_version_layouts")
            .String(IdField, IdentityMaximumLength, column => column.Required())
            .String(SchemaVersionField, SchemaVersionMaximumLength, column => column.Required())
            .Json(ContentField, column => column.Required())
            .String(TenantIdField, IdentityMaximumLength)
            .String(LayoutVersionIdField, IdentityMaximumLength, column => column.Required())
            .Timestamp("createdAt", column => column.Required())
            .Timestamp("lastModifiedAt", column => column.Required())
            .Key(IdField)
            .OptimisticConcurrency(ConcurrencyTokenField)
            .UniqueIndex(LayoutByVersionIndex, LayoutVersionIdField)
            .Scoped()
            .Build();

    private static StorageUnit OperationUnit() =>
        StorageUnit.Declare(DesignOperationDocumentKind, "elsa_design_operations")
            .String(IdField, IdentityMaximumLength, column => column.Required())
            .String(SchemaVersionField, SchemaVersionMaximumLength, column => column.Required())
            .Json(ContentField, column => column.Required())
            .String(OperationIdField, TextMaximumLength, column => column.Required())
            .String(OperationKindField, TextMaximumLength, column => column.Required())
            .String(OperationKeyField, TextMaximumLength, column => column.Required())
            .String(OperationRequestFingerprintField, TextMaximumLength, column => column.Required())
            .String(OperationResultFingerprintField, TextMaximumLength, column => column.Required())
            .Json(OperationResultJsonField, column => column.Required())
            .String(TenantIdField, IdentityMaximumLength)
            .Timestamp("createdAt", column => column.Required())
            .Timestamp("lastModifiedAt", column => column.Required())
            .Key(IdField)
            .OptimisticConcurrency(ConcurrencyTokenField)
            .Index(OperationByKeyIndex, OperationKindField, OperationKeyField)
            .Scoped()
            .Build();
}
