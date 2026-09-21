namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;

/// <summary>
/// Provider-safe names and sizes for the P01 publication-record, P05 activity-publication receipt and P06
/// activity draft test-run tables. Identities use the shared 450-code-unit bound and the encoded, hashed
/// projections of <see cref="PublishingPolicyProjectionEfModule"/>.
/// </summary>
public static class PublishingLedgerEfModule
{
    public const string PublicationRecordTableName = "elsa_publication_records";
    public const string ActivityPublicationReceiptTableName = "elsa_activity_publication_receipts";
    public const string ActivityDraftTestRunTableName = "elsa_activity_draft_test_runs";

    public const string PublicationRecordByIdentityIndexName = "IX_elsa_publication_records_scope_publicationId";
    public const string PublicationRecordBySlotIndexName = "IX_elsa_publication_records_scope_slot_publication";
    public const string ActivityPublicationReceiptByIdentityIndexName = "IX_elsa_activity_publication_receipts_scope_receiptKey";
    public const string ActivityDraftTestRunByIdentityIndexName = "IX_elsa_activity_draft_test_runs_scope_testRunId";
    public const string ActivityDraftTestRunByExpiryIndexName = "IX_elsa_activity_draft_test_runs_scope_expiry_testRunId";

    /// <summary>Rows read per keyset page when a slot's publication records are listed.</summary>
    public const int SlotListPageSize = 256;

    /// <summary>Upper bound on keyset pages, so a provider that never advances cannot loop forever.</summary>
    public const int MaximumSlotListPages = 1_000_000;

    public const int TestRunIdOrderKeyMaximumLength = (PublishingPolicyProjectionEfModule.IdentityMaximumLength + 1) * sizeof(char);

    /// <summary>The schema version stamped on the lossless JSON material of receipts and test-run receipts.</summary>
    public const string ContentSchemaVersion = "1";
}
