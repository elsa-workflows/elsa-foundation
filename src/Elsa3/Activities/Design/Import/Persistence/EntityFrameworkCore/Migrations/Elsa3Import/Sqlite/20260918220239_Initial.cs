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
                    TenantKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    UserIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    HandleHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Handle = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentLength = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa3_reusable_import_collections", x => new { x.TenantKey, x.UserIdHash, x.HandleHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa3_reusable_import_definition_bindings",
                columns: table => new
                {
                    TenantKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    BindingIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BindingId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    TargetDocumentKind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetDefinitionIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetDefinitionId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceDefinitionId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: true),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtOffsetMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa3_reusable_import_definition_bindings", x => new { x.TenantKey, x.BindingIdHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa3_reusable_import_receipts",
                columns: table => new
                {
                    TenantKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    UserIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReceiptIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReceiptId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CompletedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    CommitAttemptId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
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
