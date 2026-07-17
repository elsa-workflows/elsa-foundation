using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Design.Persistence.EFCore.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowVersionSourceDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceDraftId",
                schema: "Elsa",
                table: "WorkflowDefinitionVersions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceDraftId",
                schema: "Elsa",
                table: "WorkflowDefinitionVersions");
        }
    }
}
