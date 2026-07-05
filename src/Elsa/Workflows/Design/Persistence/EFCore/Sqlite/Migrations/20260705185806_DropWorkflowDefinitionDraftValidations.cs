using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Design.Persistence.EFCore.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class DropWorkflowDefinitionDraftValidations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowDefinitionDraftValidations",
                schema: "Elsa");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowDefinitionDraftValidations",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkflowDefinitionDraftId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    Errors = table.Column<string>(type: "TEXT", maxLength: -1, nullable: false),
                    LastModifiedAt = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinitionDraftValidations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowDefinitionDraftValidations_WorkflowDefinitionDrafts_WorkflowDefinitionDraftId",
                        column: x => x.WorkflowDefinitionDraftId,
                        principalSchema: "Elsa",
                        principalTable: "WorkflowDefinitionDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitionDraftValidations_TenantId",
                schema: "Elsa",
                table: "WorkflowDefinitionDraftValidations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitionDraftValidations_WorkflowDefinitionDraftId",
                schema: "Elsa",
                table: "WorkflowDefinitionDraftValidations",
                column: "WorkflowDefinitionDraftId",
                unique: true);
        }
    }
}
