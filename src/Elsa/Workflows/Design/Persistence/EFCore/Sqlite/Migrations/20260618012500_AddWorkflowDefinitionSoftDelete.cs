using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Design.Persistence.EFCore.Sqlite.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(WorkflowsDesignDbContext))]
    [Migration("20260618012500_AddWorkflowDefinitionSoftDelete")]
    public partial class AddWorkflowDefinitionSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeletedAt",
                schema: "Elsa",
                table: "WorkflowDefinitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedReason",
                schema: "Elsa",
                table: "WorkflowDefinitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinition_DeletedAt",
                schema: "Elsa",
                table: "WorkflowDefinitions",
                column: "DeletedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowDefinition_DeletedAt",
                schema: "Elsa",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "Elsa",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "DeletedReason",
                schema: "Elsa",
                table: "WorkflowDefinitions");
        }
    }
}
