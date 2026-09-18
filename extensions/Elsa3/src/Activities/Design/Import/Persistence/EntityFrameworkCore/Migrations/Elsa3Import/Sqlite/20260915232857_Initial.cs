using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Migrations.Elsa3Import.Sqlite
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa3_reusable_import_collections",
                columns: table => new
                {
                    TenantKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false, collation: "BINARY"),
                    UserIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                    HandleHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                    Handle = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false, collation: "BINARY"),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: true, collation: "BINARY"),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false, collation: "BINARY"),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, collation: "BINARY"),
                    CreatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentLength = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa3_reusable_import_collections", x => new { x.TenantKey, x.UserIdHash, x.HandleHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa3_reusable_import_definition_bindings",
                columns: table => new
                {
                    TenantKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false, collation: "BINARY"),
                    BindingIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                    BindingId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false, collation: "BINARY"),
                    TargetDocumentKind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                    TargetDefinitionIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                    TargetDefinitionId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false, collation: "BINARY"),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                    SourceDefinitionId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false, collation: "BINARY"),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: true, collation: "BINARY"),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, collation: "BINARY"),
                    CreatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtOffsetMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa3_reusable_import_definition_bindings", x => new { x.TenantKey, x.BindingIdHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa3_reusable_import_receipts",
                columns: table => new
                {
                    TenantKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false, collation: "BINARY"),
                    UserIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                    ReceiptIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                    ReceiptId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false, collation: "BINARY"),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false, collation: "BINARY"),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: true, collation: "BINARY"),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false, collation: "BINARY"),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, collation: "BINARY"),
                    CompletedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    CommitAttemptId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, collation: "BINARY"),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa3_reusable_import_receipts", x => new { x.TenantKey, x.UserIdHash, x.ReceiptIdHash });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa3_reusable_import_collections");

            migrationBuilder.DropTable(
                name: "elsa3_reusable_import_definition_bindings");

            migrationBuilder.DropTable(
                name: "elsa3_reusable_import_receipts");
        }
    }
}
