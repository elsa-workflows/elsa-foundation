using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.Sqlite
{
    /// <inheritdoc />
    public partial class ModelExecutionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_runtime_model_execution",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExecutionId = table.Column<string>(type: "TEXT", maxLength: 344, nullable: false),
                    ExecutionIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExecutionIdOrderKey = table.Column<string>(type: "TEXT", maxLength: 516, nullable: false),
                    ArtifactIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Lifecycle = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_model_execution", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_model_input",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExecutionId = table.Column<string>(type: "TEXT", maxLength: 344, nullable: false),
                    ExecutionIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InputId = table.Column<string>(type: "TEXT", maxLength: 344, nullable: false),
                    InputIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_model_input", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_model_execution_SchemaVersion",
                table: "elsa_runtime_model_execution",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_model_execution_ScopeKeyHash_ArtifactIdHash",
                table: "elsa_runtime_model_execution",
                columns: new[] { "ScopeKeyHash", "ArtifactIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_model_execution_ScopeKeyHash_ExecutionIdHash",
                table: "elsa_runtime_model_execution",
                columns: new[] { "ScopeKeyHash", "ExecutionIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_model_execution_ScopeKeyHash_ExecutionIdOrderKey",
                table: "elsa_runtime_model_execution",
                columns: new[] { "ScopeKeyHash", "ExecutionIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_model_input_SchemaVersion",
                table: "elsa_runtime_model_input",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_model_input_ScopeKeyHash_ExecutionIdHash_InputIdHash",
                table: "elsa_runtime_model_input",
                columns: new[] { "ScopeKeyHash", "ExecutionIdHash", "InputIdHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_runtime_model_execution");

            migrationBuilder.DropTable(
                name: "elsa_runtime_model_input");
        }
    }
}
