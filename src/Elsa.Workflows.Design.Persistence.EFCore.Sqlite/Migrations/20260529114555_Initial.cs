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
                name: "WorkflowDefinitions",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowDefinitionDrafts",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkflowDefinitionId = table.Column<string>(type: "TEXT", nullable: false),
                    StateSource = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinitionDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowDefinitionDrafts_WorkflowDefinitions_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalSchema: "Elsa",
                        principalTable: "WorkflowDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "WorkflowDefinitionDraftLayouts",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkflowDefinitionDraftId = table.Column<string>(type: "TEXT", nullable: false),
                    Records = table.Column<string>(type: "TEXT", maxLength: -1, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinitionDraftLayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowDefinitionDraftLayouts_WorkflowDefinitionDrafts_WorkflowDefinitionDraftId",
                        column: x => x.WorkflowDefinitionDraftId,
                        principalSchema: "Elsa",
                        principalTable: "WorkflowDefinitionDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowDefinitionDraftValidations",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkflowDefinitionDraftId = table.Column<string>(type: "TEXT", nullable: false),
                    Errors = table.Column<string>(type: "TEXT", maxLength: -1, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "WorkflowDefinitionVersionLayouts",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkflowDefinitionVersionId = table.Column<string>(type: "TEXT", nullable: false),
                    Records = table.Column<string>(type: "TEXT", maxLength: -1, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinitionVersionLayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowDefinitionVersionLayouts_WorkflowDefinitionVersions_WorkflowDefinitionVersionId",
                        column: x => x.WorkflowDefinitionVersionId,
                        principalSchema: "Elsa",
                        principalTable: "WorkflowDefinitionVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitionDraftLayouts_TenantId",
                schema: "Elsa",
                table: "WorkflowDefinitionDraftLayouts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitionDraftLayouts_WorkflowDefinitionDraftId",
                schema: "Elsa",
                table: "WorkflowDefinitionDraftLayouts",
                column: "WorkflowDefinitionDraftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitionDrafts_TenantId",
                schema: "Elsa",
                table: "WorkflowDefinitionDrafts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitionDrafts_WorkflowDefinitionId",
                schema: "Elsa",
                table: "WorkflowDefinitionDrafts",
                column: "WorkflowDefinitionId");

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

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinition_Name",
                schema: "Elsa",
                table: "WorkflowDefinitions",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_TenantId",
                schema: "Elsa",
                table: "WorkflowDefinitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitionVersionLayouts_TenantId",
                schema: "Elsa",
                table: "WorkflowDefinitionVersionLayouts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitionVersionLayouts_WorkflowDefinitionVersionId",
                schema: "Elsa",
                table: "WorkflowDefinitionVersionLayouts",
                column: "WorkflowDefinitionVersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitionVersions_TenantId",
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
                name: "WorkflowDefinitionDraftLayouts",
                schema: "Elsa");

            migrationBuilder.DropTable(
                name: "WorkflowDefinitionDraftValidations",
                schema: "Elsa");

            migrationBuilder.DropTable(
                name: "WorkflowDefinitionVersionLayouts",
                schema: "Elsa");

            migrationBuilder.DropTable(
                name: "WorkflowDefinitionDrafts",
                schema: "Elsa");

            migrationBuilder.DropTable(
                name: "WorkflowDefinitionVersions",
                schema: "Elsa");

            migrationBuilder.DropTable(
                name: "WorkflowDefinitions",
                schema: "Elsa");
        }
    }
}
