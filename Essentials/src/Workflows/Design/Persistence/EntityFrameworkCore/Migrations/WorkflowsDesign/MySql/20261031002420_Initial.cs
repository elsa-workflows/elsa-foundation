using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Migrations.WorkflowsDesign.MySql
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "__ElsaDatabaseIdentity_ElsaWorkflowsDesign",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaWorkflowsDesign", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaWorkflowsDesign",
                columns: table => new
                {
                    Family = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    FinalizedVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    IntentJson = table.Column<string>(type: "longtext", nullable: true),
                    HoldsJson = table.Column<string>(type: "longtext", nullable: false),
                    HistoryJson = table.Column<string>(type: "longtext", nullable: false),
                    FinishJson = table.Column<string>(type: "longtext", nullable: true),
                    FinishHistoryJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaWorkflowsDesign", x => x.Family);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_design_operations",
                columns: table => new
                {
                    OperationKindLookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    OperationKeyLookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    OperationKind = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_0900_bin"),
                    OperationKey = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_0900_bin"),
                    RequestFingerprint = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    ResultFingerprint = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    ResultJson = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_design_operations", x => new { x.ScopeKey, x.OperationKindLookupHash, x.OperationKeyLookupHash });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definitions_v2",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    IdSearchKey = table.Column<string>(type: "varchar(896)", maxLength: 896, nullable: false, collation: "utf8mb4_0900_bin"),
                    NameSearchKey = table.Column<string>(type: "varchar(1792)", maxLength: 1792, nullable: true, collation: "utf8mb4_0900_bin"),
                    DescriptionSearchKey = table.Column<string>(type: "varchar(1792)", maxLength: 1792, nullable: true, collation: "utf8mb4_0900_bin"),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_0900_bin"),
                    Description = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true, collation: "utf8mb4_0900_bin"),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedReason = table.Column<string>(type: "longtext", nullable: true),
                    IsSourceOwned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LibraryModifiedTicks = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definitions_v2", x => new { x.ScopeKey, x.IdLookupHash });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_folders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    ParentId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "utf8mb4_0900_bin"),
                    ParentKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_0900_bin"),
                    NormalizedName = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, collation: "utf8mb4_0900_bin"),
                    NameHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    Path = table.Column<string>(type: "varchar(641)", unicode: false, maxLength: 641, nullable: false, collation: "utf8mb4_0900_bin"),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_library_audit",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    TargetId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    ActorId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Operation = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    OperationKey = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_0900_bin"),
                    BeforeJson = table.Column<string>(type: "longtext", nullable: false),
                    AfterJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_library_audit", x => new { x.ScopeKey, x.Id });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_library_fence",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    Revision = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_library_fence", x => new { x.ScopeKey, x.Id });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_library_views",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_0900_bin"),
                    OwnerId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_0900_bin"),
                    OwnerHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    Shared = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Revision = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_library_views", x => new { x.ScopeKey, x.Id });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_tags",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_0900_bin"),
                    NormalizedName = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, collation: "utf8mb4_0900_bin"),
                    NameHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    Description = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true, collation: "utf8mb4_0900_bin"),
                    Mode = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Cardinality = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Deprecated = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EverAssigned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Revision = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    ValuesJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_tags", x => new { x.ScopeKey, x.Id });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_templates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    Revision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    Name = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false, collation: "utf8mb4_0900_bin"),
                    Description = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true, collation: "utf8mb4_0900_bin"),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_templates", x => new { x.ScopeKey, x.Id });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definition_drafts",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    WorkflowDefinitionId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    WorkflowDefinitionIdLookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    SourceVersionId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin"),
                    StateSource = table.Column<string>(type: "longtext", nullable: true),
                    LibraryCreatedTicks = table.Column<long>(type: "bigint", nullable: false),
                    LibraryIdentitySortKey = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false, collation: "utf8mb4_0900_bin"),
                    LibraryModifiedTicks = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definition_drafts", x => new { x.ScopeKey, x.IdLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_definition_drafts_elsa_workflow_definitions_v2~",
                        columns: x => new { x.ScopeKey, x.WorkflowDefinitionIdLookupHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definition_versions",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    Version = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    SemVerSortKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    DefinitionId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    DefinitionIdLookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    SourceDraftId = table.Column<string>(type: "longtext", nullable: true),
                    StateSource = table.Column<string>(type: "longtext", nullable: true),
                    SourceCreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definition_versions", x => new { x.ScopeKey, x.IdLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_definition_versions_elsa_workflow_definitions_~",
                        columns: x => new { x.ScopeKey, x.DefinitionIdLookupHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_favorites",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    DefinitionHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    SubjectId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_0900_bin"),
                    SubjectHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_favorites", x => new { x.ScopeKey, x.Id });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_favorites_elsa_workflow_definitions_v2_ScopeKe~",
                        columns: x => new { x.ScopeKey, x.DefinitionHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_organization",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    DefinitionHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    DefinitionId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    FolderId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "utf8mb4_0900_bin"),
                    Published = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Revision = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_organization", x => new { x.ScopeKey, x.Id });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_organization_elsa_workflow_definitions_v2_Scop~",
                        columns: x => new { x.ScopeKey, x.DefinitionHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_elsa_workflow_organization_elsa_workflow_folders_ScopeKey_Fo~",
                        columns: x => new { x.ScopeKey, x.FolderId },
                        principalTable: "elsa_workflow_folders",
                        principalColumns: new[] { "ScopeKey", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_tag_assignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    DefinitionHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    TagId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ValueKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_tag_assignments", x => new { x.ScopeKey, x.Id });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_tag_assignments_elsa_workflow_definitions_v2_S~",
                        columns: x => new { x.ScopeKey, x.DefinitionHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_elsa_workflow_tag_assignments_elsa_workflow_tags_ScopeKey_Ta~",
                        columns: x => new { x.ScopeKey, x.TagId },
                        principalTable: "elsa_workflow_tags",
                        principalColumns: new[] { "ScopeKey", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definition_draft_layouts",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    WorkflowDefinitionDraftId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    WorkflowDefinitionDraftIdLookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    RecordsJson = table.Column<string>(type: "longtext", nullable: false),
                    ActivityPresentationJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definition_draft_layouts", x => new { x.ScopeKey, x.IdLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_definition_draft_layouts_elsa_workflow_definit~",
                        columns: x => new { x.ScopeKey, x.WorkflowDefinitionDraftIdLookupHash },
                        principalTable: "elsa_workflow_definition_drafts",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_definition_version_layouts",
                columns: table => new
                {
                    IdLookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    ScopeKey = table.Column<string>(type: "varchar(65)", maxLength: 65, nullable: false, collation: "utf8mb4_0900_bin"),
                    WorkflowDefinitionVersionId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    WorkflowDefinitionVersionIdLookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    RecordsJson = table.Column<string>(type: "longtext", nullable: false),
                    ActivityPresentationJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_definition_version_layouts", x => new { x.ScopeKey, x.IdLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_definition_version_layouts_elsa_workflow_defin~",
                        columns: x => new { x.ScopeKey, x.WorkflowDefinitionVersionIdLookupHash },
                        principalTable: "elsa_workflow_definition_versions",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_ElsaWorkflowsDesign_SchemaVersion",
                table: "__ElsaDatabaseIdentity_ElsaWorkflowsDesign",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaWorkflowsDesign_SchemaVersion",
                table: "__ElsaSchemaFinalization_ElsaWorkflowsDesign",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_design_operations_SchemaVersion",
                table: "elsa_design_operations",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_draft_layouts_SchemaVersion",
                table: "elsa_workflow_definition_draft_layouts",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_draft_layouts_ScopeKey_WorkflowDefi~",
                table: "elsa_workflow_definition_draft_layouts",
                columns: new[] { "ScopeKey", "WorkflowDefinitionDraftIdLookupHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_drafts_SchemaVersion",
                table: "elsa_workflow_definition_drafts",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_drafts_ScopeKey_WorkflowDefinition~1",
                table: "elsa_workflow_definition_drafts",
                columns: new[] { "ScopeKey", "WorkflowDefinitionIdLookupHash", "LibraryModifiedTicks", "LibraryCreatedTicks", "LibraryIdentitySortKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_drafts_ScopeKey_WorkflowDefinitionI~",
                table: "elsa_workflow_definition_drafts",
                columns: new[] { "ScopeKey", "WorkflowDefinitionIdLookupHash", "LastModifiedAt", "IdLookupHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_version_layouts_SchemaVersion",
                table: "elsa_workflow_definition_version_layouts",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_version_layouts_ScopeKey_WorkflowDe~",
                table: "elsa_workflow_definition_version_layouts",
                columns: new[] { "ScopeKey", "WorkflowDefinitionVersionIdLookupHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_versions_SchemaVersion",
                table: "elsa_workflow_definition_versions",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "UX_elsa_workflow_definition_versions_semver_identity",
                table: "elsa_workflow_definition_versions",
                columns: new[] { "ScopeKey", "DefinitionIdLookupHash", "SemVerSortKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definitions_v2_SchemaVersion",
                table: "elsa_workflow_definitions_v2",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definitions_v2_ScopeKey_Id",
                table: "elsa_workflow_definitions_v2",
                columns: new[] { "ScopeKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definitions_v2_ScopeKey_LibraryModifiedTicks_I~",
                table: "elsa_workflow_definitions_v2",
                columns: new[] { "ScopeKey", "LibraryModifiedTicks", "IdLookupHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definitions_v2_ScopeKey_Name_Id",
                table: "elsa_workflow_definitions_v2",
                columns: new[] { "ScopeKey", "Name", "Id" });

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
                name: "IX_elsa_workflow_tag_assignments_ScopeKey_DefinitionHash_TagId_~",
                table: "elsa_workflow_tag_assignments",
                columns: new[] { "ScopeKey", "DefinitionHash", "TagId", "ValueKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_tag_assignments_ScopeKey_TagId_ValueKey_Defini~",
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

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_templates_SchemaVersion",
                table: "elsa_workflow_templates",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__ElsaDatabaseIdentity_ElsaWorkflowsDesign");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_ElsaWorkflowsDesign");

            migrationBuilder.DropTable(
                name: "elsa_design_operations");

            migrationBuilder.DropTable(
                name: "elsa_workflow_definition_draft_layouts");

            migrationBuilder.DropTable(
                name: "elsa_workflow_definition_version_layouts");

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
                name: "elsa_workflow_templates");

            migrationBuilder.DropTable(
                name: "elsa_workflow_definition_drafts");

            migrationBuilder.DropTable(
                name: "elsa_workflow_definition_versions");

            migrationBuilder.DropTable(
                name: "elsa_workflow_folders");

            migrationBuilder.DropTable(
                name: "elsa_workflow_tags");

            migrationBuilder.DropTable(
                name: "elsa_workflow_definitions_v2");
        }
    }
}
