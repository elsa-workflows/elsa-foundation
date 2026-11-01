using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Migrations.Specialists.SqlServer
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "__ElsaDatabaseIdentity_ElsaAgentSpecialists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaAgentSpecialists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaAgentSpecialists",
                columns: table => new
                {
                    Family = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
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
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaAgentSpecialists", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "elsa_specialist_conversations",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_specialist_conversations", x => new { x.ScopeKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "elsa_specialist_turns",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Handle = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OperationKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Fence = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    DeadlineUtcTicks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_specialist_turns", x => new { x.ScopeKey, x.Handle });
                });

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_ElsaAgentSpecialists_SchemaVersion",
                table: "__ElsaDatabaseIdentity_ElsaAgentSpecialists",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaAgentSpecialists_SchemaVersion",
                table: "__ElsaSchemaFinalization_ElsaAgentSpecialists",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_specialist_conversations_SchemaVersion",
                table: "elsa_specialist_conversations",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_specialist_turns_SchemaVersion",
                table: "elsa_specialist_turns",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_specialist_turns_ScopeKey_OperationKey",
                table: "elsa_specialist_turns",
                columns: new[] { "ScopeKey", "OperationKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__ElsaDatabaseIdentity_ElsaAgentSpecialists");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_ElsaAgentSpecialists");

            migrationBuilder.DropTable(
                name: "elsa_specialist_conversations");

            migrationBuilder.DropTable(
                name: "elsa_specialist_turns");
        }
    }
}
