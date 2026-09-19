using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Migrations.WorkflowsDesign.PostgreSql
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
                    OperationKindLookupHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    OperationKeyLookupHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    OperationKind = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    OperationKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    RequestFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    ResultFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_design_operations", x => new { x.ScopeKey, x.OperationKindLookupHash, x.OperationKeyLookupHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definitions_v2",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    IdSearchKey = table.Column<string>(type: "character varying(896)", maxLength: 896, nullable: false, collation: "C"),
                    NameSearchKey = table.Column<string>(type: "character varying(1792)", maxLength: 1792, nullable: true, collation: "C"),
                    DescriptionSearchKey = table.Column<string>(type: "character varying(1792)", maxLength: 1792, nullable: true, collation: "C"),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, collation: "C"),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedReason = table.Column<string>(type: "text", nullable: true),
                    IsSourceOwned = table.Column<bool>(type: "boolean", nullable: false),
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definitions_v2", x => new { x.ScopeKey, x.IdLookupHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definition_drafts",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    WorkflowDefinitionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    WorkflowDefinitionIdLookupHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    SourceVersionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    StateSource = table.Column<string>(type: "text", nullable: true),
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definition_drafts", x => new { x.ScopeKey, x.IdLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_definition_drafts_elsa_workflow_definitions_v~",
                        columns: x => new { x.ScopeKey, x.WorkflowDefinitionIdLookupHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definition_versions",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    Version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    SemVerSortKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    DefinitionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    DefinitionIdLookupHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    SourceDraftId = table.Column<string>(type: "text", nullable: true),
                    StateSource = table.Column<string>(type: "text", nullable: true),
                    SourceCreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definition_versions", x => new { x.ScopeKey, x.IdLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_definition_versions_elsa_workflow_definitions~",
                        columns: x => new { x.ScopeKey, x.DefinitionIdLookupHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definition_draft_layouts",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    WorkflowDefinitionDraftId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    WorkflowDefinitionDraftIdLookupHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    RecordsJson = table.Column<string>(type: "text", nullable: false),
                    ActivityPresentationJson = table.Column<string>(type: "text", nullable: false),
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definition_draft_layouts", x => new { x.ScopeKey, x.IdLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_definition_draft_layouts_elsa_workflow_defini~",
                        columns: x => new { x.ScopeKey, x.WorkflowDefinitionDraftIdLookupHash },
                        principalTable: "elsa_workflow_definition_drafts",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definition_version_layouts",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    WorkflowDefinitionVersionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    WorkflowDefinitionVersionIdLookupHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    RecordsJson = table.Column<string>(type: "text", nullable: false),
                    ActivityPresentationJson = table.Column<string>(type: "text", nullable: false),
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definition_version_layouts", x => new { x.ScopeKey, x.IdLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_definition_version_layouts_elsa_workflow_defi~",
                        columns: x => new { x.ScopeKey, x.WorkflowDefinitionVersionIdLookupHash },
                        principalTable: "elsa_workflow_definition_versions",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_draft_layouts_ScopeKey_WorkflowDef~",
                table: "elsa_workflow_definition_draft_layouts",
                columns: new[] { "ScopeKey", "WorkflowDefinitionDraftIdLookupHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_drafts_ScopeKey_WorkflowDefinition~",
                table: "elsa_workflow_definition_drafts",
                columns: new[] { "ScopeKey", "WorkflowDefinitionIdLookupHash", "LastModifiedAt", "IdLookupHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_version_layouts_ScopeKey_WorkflowD~",
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
