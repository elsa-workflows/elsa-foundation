using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Migrations.Elsa3Import.SqlServer
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
                    TenantKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    UserIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    HandleHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Handle = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    UserId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ContentLength = table.Column<long>(type: "bigint", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa3_reusable_import_collections", x => new { x.TenantKey, x.UserIdHash, x.HandleHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa3_reusable_import_definition_bindings",
                columns: table => new
                {
                    TenantKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    BindingIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    BindingId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TargetDocumentKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TargetDefinitionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TargetDefinitionId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceDefinitionId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa3_reusable_import_definition_bindings", x => new { x.TenantKey, x.BindingIdHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa3_reusable_import_receipts",
                columns: table => new
                {
                    TenantKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    UserIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ReceiptIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ReceiptId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    UserId = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CompletedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CommitAttemptId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2")
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
