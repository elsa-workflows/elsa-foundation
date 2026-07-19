using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Design.Persistence.EFCore.Sqlite.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(WorkflowsDesignDbContext))]
    [Migration("20260719080000_AddWorkflowDefinitionSourceOwnership")]
    public partial class AddWorkflowDefinitionSourceOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSourceOwned",
                schema: "Elsa",
                table: "WorkflowDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSourceOwned",
                schema: "Elsa",
                table: "WorkflowDefinitions");
        }
    }
}
