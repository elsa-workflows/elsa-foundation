using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Migrations.Elsa3Import.PostgreSql
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "__ElsaDatabaseIdentity_Elsa3ReusableActivityImport",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_Elsa3ReusableActivityImport", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_Elsa3ReusableActivityImport",
                columns: table => new
                {
                    Family = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    DatabaseIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    FinalizedVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IntentJson = table.Column<string>(type: "text", nullable: true),
                    HoldsJson = table.Column<string>(type: "text", nullable: false),
                    HistoryJson = table.Column<string>(type: "text", nullable: false),
                    FinishJson = table.Column<string>(type: "text", nullable: true),
                    FinishHistoryJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaSchemaFinalization_Elsa3ReusableActivityImport", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "elsa3_reusable_import_collections",
                columns: table => new
                {
                    TenantKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false, collation: "C"),
                    UserIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    HandleHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    Handle = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    TenantId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true, collation: "C"),
                    UserId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ContentLength = table.Column<long>(type: "bigint", nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa3_reusable_import_collections", x => new { x.TenantKey, x.UserIdHash, x.HandleHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa3_reusable_import_definition_bindings",
                columns: table => new
                {
                    TenantKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false, collation: "C"),
                    BindingIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    BindingId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    TargetDocumentKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    TargetDefinitionIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    TargetDefinitionId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    SourceKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    SourceDefinitionId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    TenantId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtOffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa3_reusable_import_definition_bindings", x => new { x.TenantKey, x.BindingIdHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa3_reusable_import_receipts",
                columns: table => new
                {
                    TenantKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false, collation: "C"),
                    UserIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ReceiptIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ReceiptId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    IdempotencyKey = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    TenantId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true, collation: "C"),
                    UserId = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    CompletedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CommitAttemptId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa3_reusable_import_receipts", x => new { x.TenantKey, x.UserIdHash, x.ReceiptIdHash });
                });

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_Elsa3ReusableActivityImport_SchemaVe~",
                table: "__ElsaDatabaseIdentity_Elsa3ReusableActivityImport",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_Elsa3ReusableActivityImport_Schema~",
                table: "__ElsaSchemaFinalization_Elsa3ReusableActivityImport",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa3_reusable_import_collections_SchemaVersion",
                table: "elsa3_reusable_import_collections",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa3_reusable_import_definition_bindings_SchemaVersion",
                table: "elsa3_reusable_import_definition_bindings",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa3_reusable_import_receipts_SchemaVersion",
                table: "elsa3_reusable_import_receipts",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__ElsaDatabaseIdentity_Elsa3ReusableActivityImport");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_Elsa3ReusableActivityImport");

            migrationBuilder.DropTable(
                name: "elsa3_reusable_import_collections");

            migrationBuilder.DropTable(
                name: "elsa3_reusable_import_definition_bindings");

            migrationBuilder.DropTable(
                name: "elsa3_reusable_import_receipts");
        }
    }
}
