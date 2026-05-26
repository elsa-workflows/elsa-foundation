using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Design.Persistence.EFCore.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Elsa");

            migrationBuilder.CreateTable(
                name: "WorkflowDefinitionDrafts",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    StateSource = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinitionDrafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowDefinitions",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    MetaData_Provider = table.Column<string>(type: "TEXT", nullable: true),
                    MetaData_Materializer = table.Column<string>(type: "TEXT", nullable: true),
                    MetaData_MaterializerContext = table.Column<string>(type: "TEXT", nullable: true),
                    MetaData_IsSystem = table.Column<bool>(type: "INTEGER", nullable: true),
                    MetaData_ToolVersion = table.Column<string>(type: "TEXT", nullable: true),
                    DraftId = table.Column<string>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowDefinitions_WorkflowDefinitionDrafts_DraftId",
                        column: x => x.DraftId,
                        principalSchema: "Elsa",
                        principalTable: "WorkflowDefinitionDrafts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkflowDefinitionVersions",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", nullable: false),
                    StateSource = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    SourceCreatedAt = table.Column<string>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinitionVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowDefinitionVersions_WorkflowDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalSchema: "Elsa",
                        principalTable: "WorkflowDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitionDraft_TenantId",
                schema: "Elsa",
                table: "WorkflowDefinitionDrafts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinition_Name",
                schema: "Elsa",
                table: "WorkflowDefinitions",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinition_TenantId",
                schema: "Elsa",
                table: "WorkflowDefinitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_DraftId",
                schema: "Elsa",
                table: "WorkflowDefinitions",
                column: "DraftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitionVersion_TenantId",
                schema: "Elsa",
                table: "WorkflowDefinitionVersions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowDefinitionVersion_DefinitionId_Version",
                schema: "Elsa",
                table: "WorkflowDefinitionVersions",
                columns: new[] { "DefinitionId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowDefinitionVersions",
                schema: "Elsa");

            migrationBuilder.DropTable(
                name: "WorkflowDefinitions",
                schema: "Elsa");

            migrationBuilder.DropTable(
                name: "WorkflowDefinitionDrafts",
                schema: "Elsa");
        }
    }
}
