using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Migrations.WorkflowsDesign.SqlServer
{
    /// <inheritdoc />
    [Elsa.Persistence.EntityFramework.EfMigrationIncludes("20261030020433_Initial")]
    public partial class WorkflowLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Revision",
                table: "elsa_workflow_templates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<long>(
                name: "LibraryModifiedTicks",
                table: "elsa_workflow_definitions_v2",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "LibraryCreatedTicks",
                table: "elsa_workflow_definition_drafts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "LibraryIdentitySortKey",
                table: "elsa_workflow_definition_drafts",
                type: "varchar(512)",
                unicode: false,
                maxLength: 512,
                nullable: false,
                defaultValue: "",
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<long>(
                name: "LibraryModifiedTicks",
                table: "elsa_workflow_definition_drafts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "elsa_workflow_favorites",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SubjectId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SubjectHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
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
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ParentId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ParentKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    NormalizedName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                    NameHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Path = table.Column<string>(type: "varchar(641)", unicode: false, maxLength: 641, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
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
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TargetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Operation = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_library_audit", x => new { x.ScopeKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_library_fence",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Revision = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_library_fence", x => new { x.ScopeKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_library_views",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OwnerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OwnerHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Shared = table.Column<bool>(type: "bit", nullable: false),
                    Revision = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_library_views", x => new { x.ScopeKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_tags",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    NormalizedName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                    NameHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Description = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Mode = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Cardinality = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Deprecated = table.Column<bool>(type: "bit", nullable: false),
                    EverAssigned = table.Column<bool>(type: "bit", nullable: false),
                    Revision = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_tags", x => new { x.ScopeKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_organization",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    FolderId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Published = table.Column<bool>(type: "bit", nullable: false),
                    Revision = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
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
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TagId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ValueKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
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
                name: "IX_elsa_workflow_definition_drafts_ScopeKey_WorkflowDefinitionIdLookupHash_LibraryModifiedTicks_LibraryCreatedTicks_LibraryIden~",
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
                name: "IX_elsa_workflow_definition_drafts_ScopeKey_WorkflowDefinitionIdLookupHash_LibraryModifiedTicks_LibraryCreatedTicks_LibraryIden~",
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

            migrationBuilder.AlterColumn<string>(
                name: "Revision",
                table: "elsa_workflow_templates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldCollation: "Latin1_General_100_BIN2");
        }
    }
}
