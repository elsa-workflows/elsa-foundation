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
                name: "elsa_publication_policies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PolicyKey = table.Column<string>(type: "nvarchar(1244)", maxLength: 1244, nullable: false, collation: "Latin1_General_BIN2"),
                    PolicyKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowDefinitionId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true, collation: "Latin1_General_BIN2"),
                    WorkflowDefinitionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true, collation: "Latin1_General_BIN2"),
                    TenantIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DefaultAction = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DefaultSlotName = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_BIN2"),
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
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IntentId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_BIN2"),
                    IntentIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IntentIdOrderKey = table.Column<byte[]>(type: "varbinary(902)", maxLength: 902, nullable: false),
                    PublicationId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_BIN2"),
                    PublicationIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProjectionKind = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_BIN2"),
                    ProjectionKindHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    NextAttemptAtOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    LastFailureCode = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: true),
                    LastFailureMessage = table.Column<string>(type: "nvarchar(1368)", maxLength: 1368, nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true, collation: "Latin1_General_BIN2"),
                    TenantIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_publication_projection_intents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_publication_snapshot_reviews",
                columns: table => new
                {
                    PreflightToken = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_BIN2"),
                    Incarnation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CandidateHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DefinitionId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_BIN2"),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SlotName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_BIN2"),
                    PolicySource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PolicyRevision = table.Column<long>(type: "bigint", nullable: true),
                    RequestedAction = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RequestedSlotName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RequestedExpectedPublicationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SlotRevision = table.Column<long>(type: "bigint", nullable: false),
                    ActivePublicationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true, collation: "Latin1_General_BIN2"),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_publication_snapshot_reviews", x => x.PreflightToken);
                });

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
                name: "IX_elsa_publication_snapshot_reviews_expiresAt_preflightToken",
                table: "elsa_publication_snapshot_reviews",
                columns: new[] { "ExpiresAt", "PreflightToken" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_publication_policies");

            migrationBuilder.DropTable(
                name: "elsa_publication_projection_intents");

            migrationBuilder.DropTable(
                name: "elsa_publication_snapshot_reviews");
        }
    }
}
