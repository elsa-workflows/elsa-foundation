using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Migrations.StructuredLogs.MySql
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
                name: "elsa_structured_log_append_operations",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    BatchId = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScopeId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    StreamId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    IssuedAtTicks = table.Column<long>(type: "bigint", nullable: false),
                    Fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    OutcomeJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_structured_log_append_operations", x => new { x.ScopeKey, x.BatchId });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_structured_log_records",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Position = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScopeId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    StreamId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TimestampTicks = table.Column<long>(type: "bigint", nullable: false),
                    TimestampOffsetMinutes = table.Column<short>(type: "smallint", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    CategoryKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SourceKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ReplayToken = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_structured_log_records", x => new { x.ScopeKey, x.Position });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_structured_log_stream_states",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScopeId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    StreamId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    HighWater = table.Column<long>(type: "bigint", nullable: false),
                    AppendOperationCutoffTicks = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    UpdatedAtTicks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_structured_log_stream_states", x => x.ScopeKey);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_structured_log_append_operations_ScopeKey_IssuedAtTicks",
                table: "elsa_structured_log_append_operations",
                columns: new[] { "ScopeKey", "IssuedAtTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_structured_log_records_scope_category_position",
                table: "elsa_structured_log_records",
                columns: new[] { "ScopeKey", "CategoryKey", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_structured_log_records_scope_level_position",
                table: "elsa_structured_log_records",
                columns: new[] { "ScopeKey", "Level", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_structured_log_records_scope_replay",
                table: "elsa_structured_log_records",
                columns: new[] { "ScopeKey", "ReplayToken" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_structured_log_records_scope_source_position",
                table: "elsa_structured_log_records",
                columns: new[] { "ScopeKey", "SourceKey", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_structured_log_append_operations");

            migrationBuilder.DropTable(
                name: "elsa_structured_log_records");

            migrationBuilder.DropTable(
                name: "elsa_structured_log_stream_states");
        }
    }
}
