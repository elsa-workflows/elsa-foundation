using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Migrations.PublishingSnapshotReview.SqlServer
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
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TestRunId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TestRunIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TestRunIdOrderKey = table.Column<byte[]>(type: "varbinary(902)", maxLength: 902, nullable: false),
                    ReceiptExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ReceiptExpiresAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    TenantIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
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
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ReceiptKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ReceiptTenantId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    TenantIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_publication_receipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_publication_policies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PolicyKey = table.Column<string>(type: "nvarchar(1244)", maxLength: 1244, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PolicyKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    WorkflowDefinitionId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    WorkflowDefinitionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    TenantIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefaultAction = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefaultSlotName = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtOffsetMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_publication_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_publication_projection_intents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IntentId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IntentIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IntentIdOrderKey = table.Column<byte[]>(type: "varbinary(902)", maxLength: 902, nullable: false),
                    PublicationId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PublicationIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProjectionKind = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProjectionKindHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Operation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    NextAttemptAtOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    LastFailureCode = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: true),
                    LastFailureMessage = table.Column<string>(type: "nvarchar(1368)", maxLength: 1368, nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    TenantIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
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
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PublicationId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PublicationIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SlotId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SlotIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SlotName = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    WorkflowDefinitionId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    WorkflowDefinitionVersionId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ArtifactId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceReferenceId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ExpectedSlotRevision = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    ActivatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    ActivatedAtOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    RetiredAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    RetiredAtOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    TenantIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
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
                    PreflightToken = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Incarnation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CandidateHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SlotName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PolicySource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PolicyRevision = table.Column<long>(type: "bigint", nullable: true),
                    RequestedAction = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RequestedSlotName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RequestedExpectedPublicationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true, collation: "Latin1_General_100_BIN2"),
                    SlotRevision = table.Column<long>(type: "bigint", nullable: false),
                    ActivePublicationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true, collation: "Latin1_General_100_BIN2"),
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
