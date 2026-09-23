using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Migrations.PublishingSnapshotReview.PostgreSql
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_activity_draft_test_runs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    TestRunId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    TestRunIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    TestRunIdOrderKey = table.Column<byte[]>(type: "bytea", maxLength: 902, nullable: false),
                    ReceiptExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ReceiptExpiresAtOffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    Content = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true, collation: "C"),
                    TenantIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_draft_test_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_publication_receipts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ReceiptKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    IdempotencyKey = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    ReceiptTenantId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true, collation: "C"),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    Content = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true, collation: "C"),
                    TenantIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_publication_receipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_publication_policies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    PolicyKey = table.Column<string>(type: "character varying(1244)", maxLength: 1244, nullable: false, collation: "C"),
                    PolicyKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    WorkflowDefinitionId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true, collation: "C"),
                    WorkflowDefinitionIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, collation: "C"),
                    TenantId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true, collation: "C"),
                    TenantIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    DefaultAction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    DefaultSlotName = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtOffsetMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_publication_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_publication_projection_intents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    IntentId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    IntentIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    IntentIdOrderKey = table.Column<byte[]>(type: "bytea", maxLength: 902, nullable: false),
                    PublicationId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    PublicationIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ProjectionKind = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    ProjectionKindHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    Operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    NextAttemptAtOffsetMinutes = table.Column<int>(type: "integer", nullable: true),
                    LastFailureCode = table.Column<string>(type: "character varying(344)", maxLength: 344, nullable: true),
                    LastFailureMessage = table.Column<string>(type: "character varying(1368)", maxLength: 1368, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true, collation: "C"),
                    TenantIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_publication_projection_intents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_publication_records",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    PublicationId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    PublicationIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    SlotId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    SlotIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    SlotName = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    WorkflowDefinitionId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    WorkflowDefinitionVersionId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    ArtifactId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    SourceReferenceId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true, collation: "C"),
                    ExpectedSlotRevision = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtOffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    ActivatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    ActivatedAtOffsetMinutes = table.Column<int>(type: "integer", nullable: true),
                    RetiredAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    RetiredAtOffsetMinutes = table.Column<int>(type: "integer", nullable: true),
                    FailureCode = table.Column<string>(type: "text", nullable: true),
                    FailureMessage = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true, collation: "C"),
                    TenantIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_publication_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_publication_snapshot_reviews",
                columns: table => new
                {
                    PreflightToken = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    Incarnation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    CandidateHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    DefinitionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    SlotName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    PolicySource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    PolicyRevision = table.Column<long>(type: "bigint", nullable: true),
                    RequestedAction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true, collation: "C"),
                    RequestedSlotName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, collation: "C"),
                    RequestedExpectedPublicationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, collation: "C"),
                    SlotRevision = table.Column<long>(type: "bigint", nullable: false),
                    ActivePublicationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, collation: "C"),
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, collation: "C"),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_publication_snapshot_reviews", x => x.PreflightToken);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_draft_test_runs_scope_expiry_testRunId",
                table: "elsa_activity_draft_test_runs",
                columns: new[] { "TenantIdHash", "ReceiptExpiresAtUtcTicks", "TestRunIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_draft_test_runs_scope_testRunId",
                table: "elsa_activity_draft_test_runs",
                columns: new[] { "TenantIdHash", "TestRunIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_publication_receipts_scope_receiptKey",
                table: "elsa_activity_publication_receipts",
                columns: new[] { "TenantIdHash", "ReceiptKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_publication_policies_scope_policyKey",
                table: "elsa_publication_policies",
                columns: new[] { "TenantIdHash", "PolicyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_publication_projection_intents_scope_intentId",
                table: "elsa_publication_projection_intents",
                columns: new[] { "TenantIdHash", "IntentIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_publication_projection_intents_scope_publication_order",
                table: "elsa_publication_projection_intents",
                columns: new[] { "TenantIdHash", "PublicationIdHash", "IntentIdOrderKey", "IntentIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_publication_records_scope_publicationId",
                table: "elsa_publication_records",
                columns: new[] { "TenantIdHash", "PublicationIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_publication_records_scope_slot_publication",
                table: "elsa_publication_records",
                columns: new[] { "TenantIdHash", "SlotIdHash", "PublicationIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_publication_snapshot_reviews_expiresAt_preflightToken",
                table: "elsa_publication_snapshot_reviews",
                columns: new[] { "ExpiresAt", "PreflightToken" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_activity_draft_test_runs");

            migrationBuilder.DropTable(
                name: "elsa_activity_publication_receipts");

            migrationBuilder.DropTable(
                name: "elsa_publication_policies");

            migrationBuilder.DropTable(
                name: "elsa_publication_projection_intents");

            migrationBuilder.DropTable(
                name: "elsa_publication_records");

            migrationBuilder.DropTable(
                name: "elsa_publication_snapshot_reviews");
        }
    }
}
