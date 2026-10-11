using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Migrations.StructuredLogs.SqlServer
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "__ElsaDatabaseIdentity_ElsaStructuredLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaStructuredLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaStructuredLogs",
                columns: table => new
                {
                    Family = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    FinalizedVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IntentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HoldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HistoryJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FinishJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FinishHistoryJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaStructuredLogs", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "elsa_structured_log_append_operations",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BatchId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StreamId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IssuedAtTicks = table.Column<long>(type: "bigint", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OutcomeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_structured_log_append_operations", x => new { x.ScopeKey, x.BatchId });
                });

            migrationBuilder.CreateTable(
                name: "elsa_structured_log_records",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Position = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StreamId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TimestampTicks = table.Column<long>(type: "bigint", nullable: false),
                    TimestampOffsetMinutes = table.Column<short>(type: "smallint", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    CategoryKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReplayToken = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_structured_log_records", x => new { x.ScopeKey, x.Position });
                });

            migrationBuilder.CreateTable(
                name: "elsa_structured_log_stream_states",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StreamId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    HighWater = table.Column<long>(type: "bigint", nullable: false),
                    AppendOperationCutoffTicks = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UpdatedAtTicks = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_structured_log_stream_states", x => x.ScopeKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_ElsaStructuredLogs_SchemaVersion",
                table: "__ElsaDatabaseIdentity_ElsaStructuredLogs",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaStructuredLogs_SchemaVersion",
                table: "__ElsaSchemaFinalization_ElsaStructuredLogs",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_structured_log_append_operations_SchemaVersion",
                table: "elsa_structured_log_append_operations",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_structured_log_append_operations_ScopeKey_IssuedAtTicks",
                table: "elsa_structured_log_append_operations",
                columns: new[] { "ScopeKey", "IssuedAtTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_structured_log_records_SchemaVersion",
                table: "elsa_structured_log_records",
                column: "SchemaVersion");

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

            migrationBuilder.CreateIndex(
                name: "IX_elsa_structured_log_stream_states_SchemaVersion",
                table: "elsa_structured_log_stream_states",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__ElsaDatabaseIdentity_ElsaStructuredLogs");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_ElsaStructuredLogs");

            migrationBuilder.DropTable(
                name: "elsa_structured_log_append_operations");

            migrationBuilder.DropTable(
                name: "elsa_structured_log_records");

            migrationBuilder.DropTable(
                name: "elsa_structured_log_stream_states");
        }
    }
}
