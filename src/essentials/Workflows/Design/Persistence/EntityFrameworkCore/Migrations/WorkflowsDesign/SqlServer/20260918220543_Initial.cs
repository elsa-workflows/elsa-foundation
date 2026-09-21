using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Migrations.WorkflowsDesign.SqlServer
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_design_operations",
                columns: table => new
                {
                    OperationKindLookupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OperationKeyLookupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OperationKind = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OperationKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ResultFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_design_operations", x => new { x.ScopeKey, x.OperationKindLookupHash, x.OperationKeyLookupHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definitions_v2",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdSearchKey = table.Column<string>(type: "nvarchar(896)", maxLength: 896, nullable: false, collation: "Latin1_General_100_BIN2"),
                    NameSearchKey = table.Column<string>(type: "nvarchar(1792)", maxLength: 1792, nullable: true, collation: "Latin1_General_100_BIN2"),
                    DescriptionSearchKey = table.Column<string>(type: "nvarchar(1792)", maxLength: 1792, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true, collation: "Latin1_General_100_BIN2"),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeletedReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSourceOwned = table.Column<bool>(type: "bit", nullable: false),
                    Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definitions_v2", x => new { x.ScopeKey, x.IdLookupHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definition_drafts",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false, collation: "Latin1_General_100_BIN2"),
                    WorkflowDefinitionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    WorkflowDefinitionIdLookupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceVersionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    StateSource = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definition_drafts", x => new { x.ScopeKey, x.IdLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_definition_drafts_elsa_workflow_definitions_v2_ScopeKey_WorkflowDefinitionIdLookupHash",
                        columns: x => new { x.ScopeKey, x.WorkflowDefinitionIdLookupHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definition_versions",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SemVerSortKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionIdLookupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceDraftId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StateSource = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceCreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definition_versions", x => new { x.ScopeKey, x.IdLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_definition_versions_elsa_workflow_definitions_v2_ScopeKey_DefinitionIdLookupHash",
                        columns: x => new { x.ScopeKey, x.DefinitionIdLookupHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definition_draft_layouts",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false, collation: "Latin1_General_100_BIN2"),
                    WorkflowDefinitionDraftId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    WorkflowDefinitionDraftIdLookupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActivityPresentationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definition_draft_layouts", x => new { x.ScopeKey, x.IdLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_definition_draft_layouts_elsa_workflow_definition_drafts_ScopeKey_WorkflowDefinitionDraftIdLookupHash",
                        columns: x => new { x.ScopeKey, x.WorkflowDefinitionDraftIdLookupHash },
                        principalTable: "elsa_workflow_definition_drafts",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definition_version_layouts",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false, collation: "Latin1_General_100_BIN2"),
                    WorkflowDefinitionVersionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    WorkflowDefinitionVersionIdLookupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActivityPresentationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definition_version_layouts", x => new { x.ScopeKey, x.IdLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_definition_version_layouts_elsa_workflow_definition_versions_ScopeKey_WorkflowDefinitionVersionIdLookupHash",
                        columns: x => new { x.ScopeKey, x.WorkflowDefinitionVersionIdLookupHash },
                        principalTable: "elsa_workflow_definition_versions",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_draft_layouts_ScopeKey_WorkflowDefinitionDraftIdLookupHash",
                table: "elsa_workflow_definition_draft_layouts",
                columns: new[] { "ScopeKey", "WorkflowDefinitionDraftIdLookupHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_drafts_ScopeKey_WorkflowDefinitionIdLookupHash_LastModifiedAt_IdLookupHash",
                table: "elsa_workflow_definition_drafts",
                columns: new[] { "ScopeKey", "WorkflowDefinitionIdLookupHash", "LastModifiedAt", "IdLookupHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_version_layouts_ScopeKey_WorkflowDefinitionVersionIdLookupHash",
                table: "elsa_workflow_definition_version_layouts",
                columns: new[] { "ScopeKey", "WorkflowDefinitionVersionIdLookupHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_elsa_workflow_definition_versions_semver_identity",
                table: "elsa_workflow_definition_versions",
                columns: new[] { "ScopeKey", "DefinitionIdLookupHash", "SemVerSortKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definitions_v2_ScopeKey_Id",
                table: "elsa_workflow_definitions_v2",
                columns: new[] { "ScopeKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definitions_v2_ScopeKey_Name_Id",
                table: "elsa_workflow_definitions_v2",
                columns: new[] { "ScopeKey", "Name", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_design_operations");

            migrationBuilder.DropTable(
                name: "elsa_workflow_definition_draft_layouts");

            migrationBuilder.DropTable(
                name: "elsa_workflow_definition_version_layouts");

            migrationBuilder.DropTable(
                name: "elsa_workflow_definition_drafts");

            migrationBuilder.DropTable(
                name: "elsa_workflow_definition_versions");

            migrationBuilder.DropTable(
                name: "elsa_workflow_definitions_v2");
        }
    }
}
