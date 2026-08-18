using Groundwork.Kernel;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork;

/// <summary>
/// The provider-neutral Groundwork v2 catalog for publishing persistence.
/// Projection values are written as first-class row values; the JSON payload is retained only for
/// aggregate materialization. Every named route is backed by an explicit public-v2 index.
/// </summary>
public static class PublishingGroundworkStorageManifest
{
    public const string SchemaVersion = "1.0.0";

    /// <summary>
    /// Publishing identities are composite — a slot is keyed by definition and slot name, a policy by
    /// definition or the host sentinel — so the key column is bounded above the plain identity bound.
    /// A two-column index over two such values is 1,024 bytes, inside the 1,700-byte portable budget.
    /// </summary>
    public const int IdMaximumLength = 256;

    public const int IdentityMaximumLength = 256;
    public const int SchemaVersionMaximumLength = 32;

    public const string PublicationSlotDocumentKind = "publishingPublicationSlot";
    public const string PublicationRecordDocumentKind = "publishingPublicationRecord";
    public const string PublicationPolicyDocumentKind = "publishingPublicationPolicy";
    public const string ProjectionIntentDocumentKind = "publishingProjectionIntent";
    public const string SnapshotReviewDocumentKind = "publishingSnapshotReview";
    public const string ActivityPublicationReceiptDocumentKind = "publishingActivityPublicationReceipt";
    public const string ActivityDraftTestRunDocumentKind = "publishingActivityDraftTestRun";

    public const string IdField = "id";
    public const string SchemaVersionField = "schemaVersion";
    public const string ContentField = "content";
    public const string TenantIdField = "tenantId";
    public const string ConcurrencyTokenField = "rowVersion";

    public const string WorkflowDefinitionIdField = "workflowDefinitionId";
    public const string SlotIdField = "slotId";
    public const string PublicationIdField = "publicationId";
    public const string ActivePublicationIdField = "activePublicationId";
    public const string ExpiresAtField = "expiresAt";
    public const string ReceiptExpiresAtField = "receiptExpiresAt";

    public const string SlotByDefinitionIndex = "publication_slot_by_definition";
    public const string SlotByActivePublicationIndex = "publication_slot_by_active_publication";
    public const string RecordBySlotIndex = "publication_record_by_slot";
    public const string IntentByPublicationIndex = "publication_intent_by_publication";
    public const string SnapshotReviewByExpiryIndex = "publication_snapshot_review_by_expiry";
    public const string DraftTestRunByExpiryIndex = "activity_draft_test_run_by_expiry";

    public static IReadOnlyList<StorageUnit> CreateUnits() =>
    [
        SlotUnit(),
        RecordUnit(),
        PolicyUnit(),
        ProjectionIntentUnit(),
        SnapshotReviewUnit(),
        ActivityPublicationReceiptUnit(),
        ActivityDraftTestRunUnit()
    ];

    public static StorageUnit Require(string unitId) =>
        CreateUnits().Single(unit => StringComparer.Ordinal.Equals(unit.Id.Value, unitId));

    private static StorageDeclarationBuilder Document(string kind, string table) =>
        StorageUnit.Declare(kind, table)
            .String(IdField, IdMaximumLength, column => column.Required())
            .String(SchemaVersionField, SchemaVersionMaximumLength, column => column.Required())
            .Json(ContentField, column => column.Required())
            .String(TenantIdField, IdentityMaximumLength);

    private static StorageUnit SlotUnit() =>
        Document(PublicationSlotDocumentKind, "elsa_publication_slots")
            .String(WorkflowDefinitionIdField, IdentityMaximumLength, column => column.Required())
            // Null until a publication occupies the slot, which is what makes an unpublished slot
            // invisible to the active-publication route rather than a row it has to filter out.
            .String(ActivePublicationIdField, IdentityMaximumLength)
            .Key(IdField)
            .OptimisticConcurrency(ConcurrencyTokenField)
            .Index(SlotByDefinitionIndex, WorkflowDefinitionIdField, IdField)
            .Index(SlotByActivePublicationIndex, ActivePublicationIdField, IdField)
            .Scoped()
            .Build();

    private static StorageUnit RecordUnit() =>
        Document(PublicationRecordDocumentKind, "elsa_publication_records")
            .String(SlotIdField, IdentityMaximumLength, column => column.Required())
            .Key(IdField)
            .OptimisticConcurrency(ConcurrencyTokenField)
            .Index(RecordBySlotIndex, SlotIdField, IdField)
            .Scoped()
            .Build();

    private static StorageUnit PolicyUnit() =>
        Document(PublicationPolicyDocumentKind, "elsa_publication_policies")
            // Null for the host-wide policy, which owns the sentinel key rather than a definition.
            .String(WorkflowDefinitionIdField, IdentityMaximumLength)
            .Key(IdField)
            .OptimisticConcurrency(ConcurrencyTokenField)
            .Scoped()
            .Build();

    private static StorageUnit ProjectionIntentUnit() =>
        Document(ProjectionIntentDocumentKind, "elsa_publication_projection_intents")
            .String(PublicationIdField, IdentityMaximumLength, column => column.Required())
            .Key(IdField)
            .OptimisticConcurrency(ConcurrencyTokenField)
            .Index(IntentByPublicationIndex, PublicationIdField, IdField)
            .Scoped()
            .Build();

    private static StorageUnit SnapshotReviewUnit() =>
        Document(SnapshotReviewDocumentKind, "elsa_publication_snapshot_reviews")
            .Timestamp(ExpiresAtField, column => column.Required())
            .Key(IdField)
            .OptimisticConcurrency(ConcurrencyTokenField)
            .Index(SnapshotReviewByExpiryIndex, ExpiresAtField, IdField)
            .Scoped()
            .Build();

    private static StorageUnit ActivityPublicationReceiptUnit() =>
        Document(ActivityPublicationReceiptDocumentKind, "elsa_activity_publication_receipts")
            .Key(IdField)
            .OptimisticConcurrency(ConcurrencyTokenField)
            .Scoped()
            .Build();

    private static StorageUnit ActivityDraftTestRunUnit() =>
        Document(ActivityDraftTestRunDocumentKind, "elsa_activity_draft_test_runs")
            .Timestamp(ReceiptExpiresAtField, column => column.Required())
            .Key(IdField)
            .OptimisticConcurrency(ConcurrencyTokenField)
            .Index(DraftTestRunByExpiryIndex, ReceiptExpiresAtField, IdField)
            .Scoped()
            .Build();
}
