using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Migrations.WorkflowsDesign.MySql
{
    /// <inheritdoc />
    public partial class WorkflowTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_workflow_templates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    Revision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false, collation: "utf8mb4_0900_bin"),
                    Description = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true, collation: "utf8mb4_0900_bin"),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_templates", x => new { x.ScopeKey, x.Id });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_templates_SchemaVersion",
                table: "elsa_workflow_templates",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_workflow_templates");
        }
    }
}
