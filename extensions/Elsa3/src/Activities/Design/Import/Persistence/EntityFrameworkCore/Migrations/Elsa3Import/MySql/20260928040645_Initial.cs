using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Migrations.Elsa3Import.MySql
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
                name: "__ElsaDatabaseIdentity_Elsa3ReusableActivityImport",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_Elsa3ReusableActivityImport", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_Elsa3ReusableActivityImport",
                columns: table => new
                {
                    Family = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    DatabaseIdentity = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    FinalizedVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    IntentJson = table.Column<string>(type: "longtext", nullable: true),
                    HoldsJson = table.Column<string>(type: "longtext", nullable: false),
                    HistoryJson = table.Column<string>(type: "longtext", nullable: false),
                    FinishJson = table.Column<string>(type: "longtext", nullable: true),
                    FinishHistoryJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaSchemaFinalization_Elsa3ReusableActivityImport", x => x.Family);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa3_reusable_import_collections",
                columns: table => new
                {
                    TenantKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false, collation: "utf8mb4_0900_bin"),
                    UserIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    HandleHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    Handle = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    TenantId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: true, collation: "utf8mb4_0900_bin"),
                    UserId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ContentLength = table.Column<long>(type: "bigint", nullable: false),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa3_reusable_import_collections", x => new { x.TenantKey, x.UserIdHash, x.HandleHash });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa3_reusable_import_definition_bindings",
                columns: table => new
                {
                    TenantKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false, collation: "utf8mb4_0900_bin"),
                    BindingIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    BindingId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    TargetDocumentKind = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    TargetDefinitionIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    TargetDefinitionId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    SourceKind = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    SourceDefinitionId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    TenantId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: true, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa3_reusable_import_definition_bindings", x => new { x.TenantKey, x.BindingIdHash });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa3_reusable_import_receipts",
                columns: table => new
                {
                    TenantKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false, collation: "utf8mb4_0900_bin"),
                    UserIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ReceiptIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ReceiptId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    IdempotencyKey = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    TenantId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: true, collation: "utf8mb4_0900_bin"),
                    UserId = table.Column<string>(type: "varchar(1200)", maxLength: 1200, nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    CompletedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CommitAttemptId = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa3_reusable_import_receipts", x => new { x.TenantKey, x.UserIdHash, x.ReceiptIdHash });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_Elsa3ReusableActivityImport_SchemaVer~",
                table: "__ElsaDatabaseIdentity_Elsa3ReusableActivityImport",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_Elsa3ReusableActivityImport_SchemaV~",
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
