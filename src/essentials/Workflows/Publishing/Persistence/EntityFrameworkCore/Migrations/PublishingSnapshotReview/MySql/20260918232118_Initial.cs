using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Migrations.PublishingSnapshotReview.MySql
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_activity_draft_test_runs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    TestRunId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    TestRunIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    TestRunIdOrderKey = table.Column<byte[]>(type: "varbinary(902)", maxLength: 902, nullable: false),
                    ReceiptExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ReceiptExpiresAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    Content = table.Column<string>(type: "longtext", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: true, collation: "utf8mb4_0900_bin"),
                    TenantIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_draft_test_runs", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_activity_publication_receipts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ReceiptKeyHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    IdempotencyKey = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    ReceiptTenantId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: true, collation: "utf8mb4_0900_bin"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    Content = table.Column<string>(type: "longtext", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: true, collation: "utf8mb4_0900_bin"),
                    TenantIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_publication_receipts", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_publication_policies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    PolicyKey = table.Column<string>(type: "varchar(1244)", maxLength: 1244, nullable: false, collation: "utf8mb4_0900_bin"),
                    PolicyKeyHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    WorkflowDefinitionId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: true, collation: "utf8mb4_0900_bin"),
                    WorkflowDefinitionIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_0900_bin"),
                    TenantId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: true, collation: "utf8mb4_0900_bin"),
                    TenantIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    DefaultAction = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    DefaultSlotName = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtOffsetMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_publication_policies", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_publication_projection_intents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    IntentId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    IntentIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    IntentIdOrderKey = table.Column<byte[]>(type: "varbinary(902)", maxLength: 902, nullable: false),
                    PublicationId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    PublicationIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ProjectionKind = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    ProjectionKindHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    Operation = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    NextAttemptAtOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    LastFailureCode = table.Column<string>(type: "varchar(344)", maxLength: 344, nullable: true),
                    LastFailureMessage = table.Column<string>(type: "varchar(1368)", maxLength: 1368, nullable: true),
                    TenantId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: true, collation: "utf8mb4_0900_bin"),
                    TenantIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_publication_projection_intents", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_publication_records",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    PublicationId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    PublicationIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    SlotId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    SlotIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    SlotName = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    WorkflowDefinitionId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    WorkflowDefinitionVersionId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    ArtifactId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    SourceReferenceId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: true, collation: "utf8mb4_0900_bin"),
                    ExpectedSlotRevision = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    ActivatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    ActivatedAtOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    RetiredAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    RetiredAtOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    FailureCode = table.Column<string>(type: "longtext", nullable: true),
                    FailureMessage = table.Column<string>(type: "longtext", nullable: true),
                    TenantId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: true, collation: "utf8mb4_0900_bin"),
                    TenantIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_publication_records", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_publication_snapshot_reviews",
                columns: table => new
                {
                    PreflightToken = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_0900_bin"),
                    Incarnation = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    CandidateHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    DefinitionId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_0900_bin"),
                    Action = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    SlotName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_0900_bin"),
                    PolicySource = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    PolicyRevision = table.Column<long>(type: "bigint", nullable: true),
                    RequestedAction = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true, collation: "utf8mb4_0900_bin"),
                    RequestedSlotName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true, collation: "utf8mb4_0900_bin"),
                    RequestedExpectedPublicationId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true, collation: "utf8mb4_0900_bin"),
                    SlotRevision = table.Column<long>(type: "bigint", nullable: false),
                    ActivePublicationId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true, collation: "utf8mb4_0900_bin"),
                    TenantId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true, collation: "utf8mb4_0900_bin"),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_publication_snapshot_reviews", x => x.PreflightToken);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

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
