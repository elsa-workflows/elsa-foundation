using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Migrations.WorkflowsDesign.Sqlite
{
    /// <inheritdoc />
    [Elsa.Persistence.EntityFramework.EfMigrationIncludes("20261030020434_Initial")]
    public partial class WorkflowLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LibraryModifiedTicks",
                table: "elsa_workflow_definitions_v2",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "LibraryCreatedTicks",
                table: "elsa_workflow_definition_drafts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "LibraryIdentitySortKey",
                table: "elsa_workflow_definition_drafts",
                type: "TEXT",
                unicode: false,
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "LibraryModifiedTicks",
                table: "elsa_workflow_definition_drafts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "elsa_workflow_favorites",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 65, nullable: false),
                    DefinitionHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SubjectId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SubjectHash = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_favorites", x => new { x.ScopeKey, x.Id });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_favorites_elsa_workflow_definitions_v2_ScopeKey_DefinitionHash",
                        columns: x => new { x.ScopeKey, x.DefinitionHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_folders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 65, nullable: false),
                    ParentId = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: true),
                    ParentKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NameHash = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    Path = table.Column<string>(type: "TEXT", unicode: false, maxLength: 641, nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_folders", x => new { x.ScopeKey, x.Id });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_folders_elsa_workflow_folders_ScopeKey_ParentId",
                        columns: x => new { x.ScopeKey, x.ParentId },
                        principalTable: "elsa_workflow_folders",
                        principalColumns: new[] { "ScopeKey", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_library_audit",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 65, nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Operation = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    OperationKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: false),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_library_audit", x => new { x.ScopeKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_library_fence",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 65, nullable: false),
                    Revision = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_library_fence", x => new { x.ScopeKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_library_views",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 65, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    OwnerHash = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    Shared = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_library_views", x => new { x.ScopeKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_tags",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 65, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NameHash = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Mode = table.Column<string>(type: "TEXT", unicode: false, maxLength: 16, nullable: false),
                    Cardinality = table.Column<string>(type: "TEXT", unicode: false, maxLength: 16, nullable: false),
                    Deprecated = table.Column<bool>(type: "INTEGER", nullable: false),
                    EverAssigned = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: false),
                    ValuesJson = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_tags", x => new { x.ScopeKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_organization",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 65, nullable: false),
                    DefinitionHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    FolderId = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: true),
                    Published = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_organization", x => new { x.ScopeKey, x.Id });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_organization_elsa_workflow_definitions_v2_ScopeKey_DefinitionHash",
                        columns: x => new { x.ScopeKey, x.DefinitionHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_elsa_workflow_organization_elsa_workflow_folders_ScopeKey_FolderId",
                        columns: x => new { x.ScopeKey, x.FolderId },
                        principalTable: "elsa_workflow_folders",
                        principalColumns: new[] { "ScopeKey", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_tag_assignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 65, nullable: false),
                    DefinitionHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TagId = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    ValueKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_tag_assignments", x => new { x.ScopeKey, x.Id });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_tag_assignments_elsa_workflow_definitions_v2_ScopeKey_DefinitionHash",
                        columns: x => new { x.ScopeKey, x.DefinitionHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_elsa_workflow_tag_assignments_elsa_workflow_tags_ScopeKey_TagId",
                        columns: x => new { x.ScopeKey, x.TagId },
                        principalTable: "elsa_workflow_tags",
                        principalColumns: new[] { "ScopeKey", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definitions_v2_ScopeKey_LibraryModifiedTicks_IdLookupHash",
                table: "elsa_workflow_definitions_v2",
                columns: new[] { "ScopeKey", "LibraryModifiedTicks", "IdLookupHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_drafts_ScopeKey_WorkflowDefinitionIdLookupHash_LibraryModifiedTicks_LibraryCreatedTicks_LibraryIdentitySortKey",
                table: "elsa_workflow_definition_drafts",
                columns: new[] { "ScopeKey", "WorkflowDefinitionIdLookupHash", "LibraryModifiedTicks", "LibraryCreatedTicks", "LibraryIdentitySortKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_favorites_SchemaVersion",
                table: "elsa_workflow_favorites",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_favorites_ScopeKey_DefinitionHash",
                table: "elsa_workflow_favorites",
                columns: new[] { "ScopeKey", "DefinitionHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_favorites_ScopeKey_SubjectHash_DefinitionHash",
                table: "elsa_workflow_favorites",
                columns: new[] { "ScopeKey", "SubjectHash", "DefinitionHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_folders_SchemaVersion",
                table: "elsa_workflow_folders",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_folders_ScopeKey_ParentId",
                table: "elsa_workflow_folders",
                columns: new[] { "ScopeKey", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_folders_ScopeKey_ParentKey_NameHash",
                table: "elsa_workflow_folders",
                columns: new[] { "ScopeKey", "ParentKey", "NameHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_folders_ScopeKey_Path",
                table: "elsa_workflow_folders",
                columns: new[] { "ScopeKey", "Path" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_library_audit_SchemaVersion",
                table: "elsa_workflow_library_audit",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_library_audit_ScopeKey_TargetId",
                table: "elsa_workflow_library_audit",
                columns: new[] { "ScopeKey", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_library_fence_SchemaVersion",
                table: "elsa_workflow_library_fence",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_library_views_SchemaVersion",
                table: "elsa_workflow_library_views",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_library_views_ScopeKey_OwnerHash",
                table: "elsa_workflow_library_views",
                columns: new[] { "ScopeKey", "OwnerHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_library_views_ScopeKey_Shared",
                table: "elsa_workflow_library_views",
                columns: new[] { "ScopeKey", "Shared" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_organization_SchemaVersion",
                table: "elsa_workflow_organization",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_organization_ScopeKey_DefinitionHash",
                table: "elsa_workflow_organization",
                columns: new[] { "ScopeKey", "DefinitionHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_organization_ScopeKey_FolderId",
                table: "elsa_workflow_organization",
                columns: new[] { "ScopeKey", "FolderId" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_organization_ScopeKey_Published_DefinitionHash",
                table: "elsa_workflow_organization",
                columns: new[] { "ScopeKey", "Published", "DefinitionHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_tag_assignments_SchemaVersion",
                table: "elsa_workflow_tag_assignments",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_tag_assignments_ScopeKey_DefinitionHash_TagId_ValueKey",
                table: "elsa_workflow_tag_assignments",
                columns: new[] { "ScopeKey", "DefinitionHash", "TagId", "ValueKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_tag_assignments_ScopeKey_TagId_ValueKey_DefinitionHash",
                table: "elsa_workflow_tag_assignments",
                columns: new[] { "ScopeKey", "TagId", "ValueKey", "DefinitionHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_tags_SchemaVersion",
                table: "elsa_workflow_tags",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_tags_ScopeKey_NameHash",
                table: "elsa_workflow_tags",
                columns: new[] { "ScopeKey", "NameHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_workflow_favorites");

            migrationBuilder.DropTable(
                name: "elsa_workflow_library_audit");

            migrationBuilder.DropTable(
                name: "elsa_workflow_library_fence");

            migrationBuilder.DropTable(
                name: "elsa_workflow_library_views");

            migrationBuilder.DropTable(
                name: "elsa_workflow_organization");

            migrationBuilder.DropTable(
                name: "elsa_workflow_tag_assignments");

            migrationBuilder.DropTable(
                name: "elsa_workflow_folders");

            migrationBuilder.DropTable(
                name: "elsa_workflow_tags");

            migrationBuilder.DropIndex(
                name: "IX_elsa_workflow_definitions_v2_ScopeKey_LibraryModifiedTicks_IdLookupHash",
                table: "elsa_workflow_definitions_v2");

            migrationBuilder.DropIndex(
                name: "IX_elsa_workflow_definition_drafts_ScopeKey_WorkflowDefinitionIdLookupHash_LibraryModifiedTicks_LibraryCreatedTicks_LibraryIdentitySortKey",
                table: "elsa_workflow_definition_drafts");

            migrationBuilder.DropColumn(
                name: "LibraryModifiedTicks",
                table: "elsa_workflow_definitions_v2");

            migrationBuilder.DropColumn(
                name: "LibraryCreatedTicks",
                table: "elsa_workflow_definition_drafts");

            migrationBuilder.DropColumn(
                name: "LibraryIdentitySortKey",
                table: "elsa_workflow_definition_drafts");

            migrationBuilder.DropColumn(
                name: "LibraryModifiedTicks",
                table: "elsa_workflow_definition_drafts");
        }
    }
}
