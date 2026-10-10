using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.Sqlite
{
    /// <inheritdoc />
    public partial class WorkflowExecutableRootWriteLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_runtime_workflow_executable_root_write_lease",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ArtifactId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ArtifactIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LeaseId = table.Column<string>(type: "TEXT", nullable: false),
                    Token = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    IncarnationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_workflow_executable_root_write_lease", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_executable_root_write_lease_SchemaVersion",
                table: "elsa_runtime_workflow_executable_root_write_lease",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_executable_root_write_lease_ScopeKeyHash_ArtifactIdHash_ArtifactId_ExpiresAtUtcTicks",
                table: "elsa_runtime_workflow_executable_root_write_lease",
                columns: new[] { "ScopeKeyHash", "ArtifactIdHash", "ArtifactId", "ExpiresAtUtcTicks" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_runtime_workflow_executable_root_write_lease");
        }
    }
}
