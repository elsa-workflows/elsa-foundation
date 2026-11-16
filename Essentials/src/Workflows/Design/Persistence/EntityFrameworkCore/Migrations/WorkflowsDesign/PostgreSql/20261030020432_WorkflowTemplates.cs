using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Migrations.WorkflowsDesign.PostgreSql
{
    /// <inheritdoc />
    [Elsa.Persistence.EntityFramework.EfMigrationIncludes("20260928040711_Initial")]
    public partial class WorkflowTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_workflow_templates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    Revision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false, collation: "C"),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true, collation: "C"),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_templates", x => new { x.ScopeKey, x.Id });
                });

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
