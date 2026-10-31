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
                name: "__ElsaDatabaseIdentity_ElsaWorkflowsDesign",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaWorkflowsDesign", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaWorkflowsDesign",
                columns: table => new
                {
                    Family = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    FinalizedVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IntentJson = table.Column<string>(type: "text", nullable: true),
                    HoldsJson = table.Column<string>(type: "text", nullable: false),
                    HistoryJson = table.Column<string>(type: "text", nullable: false),
                    FinishJson = table.Column<string>(type: "text", nullable: true),
                    FinishHistoryJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaWorkflowsDesign", x => x.Family);
                });

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
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
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
                    LibraryModifiedTicks = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                name: "elsa_workflow_folders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    ParentId = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: true, collation: "C"),
                    ParentKey = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    NormalizedName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, collation: "C"),
                    NameHash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    Path = table.Column<string>(type: "character varying(641)", unicode: false, maxLength: 641, nullable: false, collation: "C"),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_folders", x => new { x.ScopeKey, x.Id });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_folders_elsa_workflow_folders_ScopeKey_Parent~",
                        columns: x => new { x.ScopeKey, x.ParentId },
                        principalTable: "elsa_workflow_folders",
                        principalColumns: new[] { "ScopeKey", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_library_audit",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    TargetId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    ActorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Operation = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    BeforeJson = table.Column<string>(type: "text", nullable: false),
                    AfterJson = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_library_audit", x => new { x.ScopeKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_library_fence",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    Revision = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_library_fence", x => new { x.ScopeKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_library_views",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    OwnerId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    OwnerHash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    Shared = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false, collation: "C"),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_library_views", x => new { x.ScopeKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_tags",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    NormalizedName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, collation: "C"),
                    NameHash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true, collation: "C"),
                    Mode = table.Column<string>(type: "character varying(16)", unicode: false, maxLength: 16, nullable: false),
                    Cardinality = table.Column<string>(type: "character varying(16)", unicode: false, maxLength: 16, nullable: false),
                    Deprecated = table.Column<bool>(type: "boolean", nullable: false),
                    EverAssigned = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false, collation: "C"),
                    ValuesJson = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_tags", x => new { x.ScopeKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_templates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    Revision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    Name = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false, collation: "C"),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true, collation: "C"),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_templates", x => new { x.ScopeKey, x.Id });
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
                    LibraryCreatedTicks = table.Column<long>(type: "bigint", nullable: false),
                    LibraryIdentitySortKey = table.Column<string>(type: "character varying(512)", unicode: false, maxLength: 512, nullable: false, collation: "C"),
                    LibraryModifiedTicks = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                name: "elsa_workflow_favorites",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    DefinitionHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    SubjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    SubjectHash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_favorites", x => new { x.ScopeKey, x.Id });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_favorites_elsa_workflow_definitions_v2_ScopeK~",
                        columns: x => new { x.ScopeKey, x.DefinitionHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_organization",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    DefinitionHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    DefinitionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    FolderId = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: true, collation: "C"),
                    Published = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_organization", x => new { x.ScopeKey, x.Id });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_organization_elsa_workflow_definitions_v2_Sco~",
                        columns: x => new { x.ScopeKey, x.DefinitionHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_elsa_workflow_organization_elsa_workflow_folders_ScopeKey_F~",
                        columns: x => new { x.ScopeKey, x.FolderId },
                        principalTable: "elsa_workflow_folders",
                        principalColumns: new[] { "ScopeKey", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_tag_assignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false, collation: "C"),
                    DefinitionHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    TagId = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    ValueKey = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_tag_assignments", x => new { x.ScopeKey, x.Id });
                    table.ForeignKey(
                        name: "FK_elsa_workflow_tag_assignments_elsa_workflow_definitions_v2_~",
                        columns: x => new { x.ScopeKey, x.DefinitionHash },
                        principalTable: "elsa_workflow_definitions_v2",
                        principalColumns: new[] { "ScopeKey", "IdLookupHash" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_elsa_workflow_tag_assignments_elsa_workflow_tags_ScopeKey_T~",
                        columns: x => new { x.ScopeKey, x.TagId },
                        principalTable: "elsa_workflow_tags",
                        principalColumns: new[] { "ScopeKey", "Id" },
                        onDelete: ReferentialAction.Restrict);
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
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                name: "IX_elsa_workflow_definition_draft_layouts_ScopeKey_WorkflowDef~",
                table: "elsa_workflow_definition_draft_layouts",
                columns: new[] { "ScopeKey", "WorkflowDefinitionDraftIdLookupHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_drafts_SchemaVersion",
                table: "elsa_workflow_definition_drafts",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_drafts_ScopeKey_WorkflowDefinitio~1",
                table: "elsa_workflow_definition_drafts",
                columns: new[] { "ScopeKey", "WorkflowDefinitionIdLookupHash", "LibraryModifiedTicks", "LibraryCreatedTicks", "LibraryIdentitySortKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_drafts_ScopeKey_WorkflowDefinition~",
                table: "elsa_workflow_definition_drafts",
                columns: new[] { "ScopeKey", "WorkflowDefinitionIdLookupHash", "LastModifiedAt", "IdLookupHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_version_layouts_SchemaVersion",
                table: "elsa_workflow_definition_version_layouts",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_version_layouts_ScopeKey_WorkflowD~",
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
                name: "IX_elsa_workflow_definitions_v2_ScopeKey_LibraryModifiedTicks_~",
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
                name: "IX_elsa_workflow_tag_assignments_ScopeKey_DefinitionHash_TagId~",
                table: "elsa_workflow_tag_assignments",
                columns: new[] { "ScopeKey", "DefinitionHash", "TagId", "ValueKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_tag_assignments_ScopeKey_TagId_ValueKey_Defin~",
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
