using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Migrations.ActivitiesDesign.SqlServer
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_activity_availability_settings",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Scope = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Rules = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_availability_settings", x => new { x.TenantScopeKey, x.ScopeIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_authoring",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ContentAuthority = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ForkedFrom = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HeadVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RecommendedVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    HeadVersionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RecommendedVersionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_authoring", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_draft_layouts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DraftId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Records = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DraftIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_draft_layouts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_drafts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SourceVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    PresentationLabel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    PublishedVersionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_drafts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_versions_v2",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    SemVerSortKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProviderKey = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProviderSchemaVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConsumerKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConsumerSchemaVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DescriptorType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DescriptorPayloadSource = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InputsSource = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutputsSource = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DesignFacetsSource = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExecutionType = table.Column<int>(type: "int", nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_versions_v2", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definitions",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActivityTypeKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    TenantKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definitions", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_dependency_edges",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OwnerVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OwnerTemplateHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DependencyVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DependencyTemplateHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurrenceId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ParentOccurrenceId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ChildSlotName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChildIndex = table.Column<int>(type: "int", nullable: false),
                    NodeOrigin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MemberUsage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DependencyVersionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OccurrenceIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OwnerVersionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ParentOccurrenceIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_dependency_edges", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_dependency_projection",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RebuildId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Items = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_dependency_projection", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_design_operations",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2"),
                    OperationKind = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    OperationKey = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    OperationKindIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OperationKeyIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CanonicalRequestFingerprint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthoritativeResultFingerprint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthoritativeResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MutatedUnitsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_design_operations", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_draft_validations",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DraftId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Diagnostics = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DraftIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_draft_validations", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_fork_candidates",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CandidateId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CandidateIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PreviewIdempotencyKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccessBindingFingerprint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    AuthorizationProfile = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceDefinitionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceLifecycle = table.Column<int>(type: "int", nullable: false),
                    SourceProviderFingerprint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetProviderFingerprint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReservedDefinition = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReservedAuthoringState = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReservedDraft = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReservedLayout = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MigrationDiagnostics = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceContractFingerprint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetContractFingerprint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetentionKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AppliedIdempotencyKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    AppliedIdempotencyKeyIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    SourceDefinitionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_fork_candidates", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_fork_receipts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    CandidateId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PublicCandidateId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccessBindingFingerprint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    AuthorizationProfile = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DefinitionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActivityTypeKey = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    DraftId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionMaterialJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthoringState = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Draft = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Layout = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MigrationDiagnostics = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CandidateIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    DraftIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    PublicCandidateIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_fork_receipts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_definitions",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActivityTypeKey = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentAuthority = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentAuthorityIsValid = table.Column<bool>(type: "bit", nullable: false, computedColumnSql: "CASE WHEN [ContentAuthority] IS NOT NULL AND ISJSON([ContentAuthority]) = 1 THEN CASE WHEN [ContentAuthorityKind] IN (0, 1) AND JSON_QUERY([ContentAuthority]) IS NOT NULL AND [ContentAuthorityAuthorityKey] IS NOT NULL AND [ContentAuthorityAuthorityKeyJson] IS NOT NULL AND LEFT([ContentAuthorityAuthorityKeyJson], 1) = N'\"' AND RIGHT([ContentAuthorityAuthorityKeyJson], 1) = N'\"' AND ISJSON(CONCAT(N'[', [ContentAuthorityAuthorityKeyJson], N']')) = 1 AND (LEN([ContentAuthorityAuthorityKey]) > 4000 OR JSON_VALUE(CONCAT(N'[', [ContentAuthorityAuthorityKeyJson], N']'), '$[0]') = [ContentAuthorityAuthorityKey]) AND ([ContentAuthoritySourceIdJson] IS NULL OR [ContentAuthoritySourceIdJson] = N'null' OR (LEFT([ContentAuthoritySourceIdJson], 1) = N'\"' AND RIGHT([ContentAuthoritySourceIdJson], 1) = N'\"' AND ISJSON(CONCAT(N'[', [ContentAuthoritySourceIdJson], N']')) = 1 AND (LEN([ContentAuthoritySourceId]) > 4000 OR JSON_VALUE(CONCAT(N'[', [ContentAuthoritySourceIdJson], N']'), '$[0]') = [ContentAuthoritySourceId]))) AND (([ContentAuthoritySourceIdJson] = N'null' AND [ContentAuthoritySourceId] IS NULL) OR ([ContentAuthoritySourceIdJson] <> N'null' AND [ContentAuthoritySourceId] IS NOT NULL)) AND JSON_VALUE([ContentAuthority], '$.integrityHash') = [ContentAuthorityIntegrityHash] AND NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE([ContentAuthorityAuthorityKey], NCHAR(9), N''), NCHAR(10), N''), NCHAR(11), N''), NCHAR(12), N''), NCHAR(13), N''), NCHAR(32), N''), NCHAR(133), N''), NCHAR(160), N''), NCHAR(5760), N''), NCHAR(8192), N''), NCHAR(8193), N''), NCHAR(8194), N''), NCHAR(8195), N''), NCHAR(8196), N''), NCHAR(8197), N''), NCHAR(8198), N''), NCHAR(8199), N''), NCHAR(8200), N''), NCHAR(8201), N''), NCHAR(8202), N''), NCHAR(8232), N''), NCHAR(8233), N''), NCHAR(8239), N''), NCHAR(8287), N''), NCHAR(12288), N''))), N'') IS NOT NULL AND ([ContentAuthoritySourceId] IS NULL OR NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE([ContentAuthoritySourceId], NCHAR(9), N''), NCHAR(10), N''), NCHAR(11), N''), NCHAR(12), N''), NCHAR(13), N''), NCHAR(32), N''), NCHAR(133), N''), NCHAR(160), N''), NCHAR(5760), N''), NCHAR(8192), N''), NCHAR(8193), N''), NCHAR(8194), N''), NCHAR(8195), N''), NCHAR(8196), N''), NCHAR(8197), N''), NCHAR(8198), N''), NCHAR(8199), N''), NCHAR(8200), N''), NCHAR(8201), N''), NCHAR(8202), N''), NCHAR(8232), N''), NCHAR(8233), N''), NCHAR(8239), N''), NCHAR(8287), N''), NCHAR(12288), N''))), N'') IS NOT NULL) AND ([ContentAuthorityKind] = 1 OR [ContentAuthoritySourceId] IS NULL) AND [ContentAuthorityIntegrityHash] = CONVERT(varchar(64), HASHBYTES('SHA2_256', CONCAT(CONCAT(CASE WHEN JSON_MODIFY([ContentAuthority], '$.integrityHash', NULL) IS NULL THEN '-1' ELSE CONVERT(varchar(20), LEN(JSON_MODIFY([ContentAuthority], '$.integrityHash', NULL) + N'#') - 1) END, N':', COALESCE(JSON_MODIFY([ContentAuthority], '$.integrityHash', NULL), N'')), N'|', CONCAT(CASE WHEN JSON_MODIFY([ContentAuthorityCanonicalJson], '$.integrityHash', NULL) IS NULL THEN '-1' ELSE CONVERT(varchar(20), LEN(JSON_MODIFY([ContentAuthorityCanonicalJson], '$.integrityHash', NULL) + N'#') - 1) END, N':', COALESCE(JSON_MODIFY([ContentAuthorityCanonicalJson], '$.integrityHash', NULL), N'')), N'|', CONCAT(CASE WHEN [ContentAuthorityAuthorityKeyJson] IS NULL THEN '-1' ELSE CONVERT(varchar(20), LEN([ContentAuthorityAuthorityKeyJson] + N'#') - 1) END, N':', COALESCE([ContentAuthorityAuthorityKeyJson], N'')), N'|', CONCAT(CASE WHEN [ContentAuthoritySourceIdJson] IS NULL THEN '-1' ELSE CONVERT(varchar(20), LEN([ContentAuthoritySourceIdJson] + N'#') - 1) END, N':', COALESCE([ContentAuthoritySourceIdJson], N'')), N'|', CONCAT(CASE WHEN [ContentAuthorityAuthorityKey] IS NULL THEN '-1' ELSE CONVERT(varchar(20), LEN([ContentAuthorityAuthorityKey] + N'#') - 1) END, N':', COALESCE([ContentAuthorityAuthorityKey], N'')), N'|', CONCAT(CASE WHEN [ContentAuthoritySourceId] IS NULL THEN '-1' ELSE CONVERT(varchar(20), LEN([ContentAuthoritySourceId] + N'#') - 1) END, N':', COALESCE([ContentAuthoritySourceId], N'')), N'|', CONCAT(CASE WHEN CONVERT(nvarchar(20), [ContentAuthorityKind]) IS NULL THEN '-1' ELSE CONVERT(varchar(20), LEN(CONVERT(nvarchar(20), [ContentAuthorityKind]) + N'#') - 1) END, N':', COALESCE(CONVERT(nvarchar(20), [ContentAuthorityKind]), N'')))), 2) THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0) END ELSE CONVERT(bit, 0) END", stored: false),
                    ContentAuthorityCanonicalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentAuthorityAuthorityKeyJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentAuthoritySourceIdJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentAuthorityAuthorityKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentAuthoritySourceId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentAuthorityIntegrityHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentAuthorityKind = table.Column<int>(type: "int", nullable: false),
                    HeadVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RecommendedVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Head = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Recommendation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HeadProviderKey = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2"),
                    RecommendationProviderKey = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2"),
                    DraftCount = table.Column<long>(type: "bigint", nullable: false),
                    VersionCount = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    HeadVersionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RecommendedVersionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ResourceIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ResourceId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ValidFromSequence = table.Column<long>(type: "bigint", nullable: false),
                    ValidToSequenceExclusive = table.Column<long>(type: "bigint", nullable: false),
                    ValidFromKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidToKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VisibilityKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortKey = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    SearchText = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_definitions", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_drafts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DraftId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SourceVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProviderSchemaVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PresentationLabel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    DraftIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ResourceIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ResourceId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ValidFromSequence = table.Column<long>(type: "bigint", nullable: false),
                    ValidToSequenceExclusive = table.Column<long>(type: "bigint", nullable: false),
                    ValidFromKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidToKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VisibilityKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortKey = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    SearchText = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_drafts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_snapshots",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_snapshots", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_versions",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Lifecycle = table.Column<int>(type: "int", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProviderSchemaVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    DefinitionVersionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ResourceIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ResourceId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ValidFromSequence = table.Column<long>(type: "bigint", nullable: false),
                    ValidToSequenceExclusive = table.Column<long>(type: "bigint", nullable: false),
                    ValidFromKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidToKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VisibilityKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortKey = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    SearchText = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_versions", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_watermarks",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    RetainedFromSequence = table.Column<long>(type: "bigint", nullable: false),
                    AdvancedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_watermarks", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_upgrade_apply_receipts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2"),
                    ReceiptId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PlanId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ReceiptJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    PlanIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ReceiptIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_upgrade_apply_receipts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_upgrade_plans",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2"),
                    PlanId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PlanJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    PlanIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_upgrade_plans", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_version_layouts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Records = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionVersionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_version_layouts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_version_publications",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActivityTypeKey = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    ResolutionKind = table.Column<int>(type: "int", nullable: false),
                    SourceDraftId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Contract = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TemplateId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TemplateHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceReferenceId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProviderFingerprint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DirectDependencyCount = table.Column<int>(type: "int", nullable: false),
                    ClosedTemplateCount = table.Column<int>(type: "int", nullable: false),
                    RuntimeRequirements = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResourceMeasurements = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResumeTargetCount = table.Column<int>(type: "int", nullable: false),
                    Lifecycle = table.Column<int>(type: "int", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DefinitionVersionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_version_publications", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_authoring_TenantScopeKey_DefinitionIdIdentityHash",
                table: "elsa_activity_definition_authoring",
                columns: new[] { "TenantScopeKey", "DefinitionIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_authoring_TenantScopeKey_HeadVersionIdIdentityHash",
                table: "elsa_activity_definition_authoring",
                columns: new[] { "TenantScopeKey", "HeadVersionIdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_draft_layouts_TenantScopeKey_DraftIdIdentityHash",
                table: "elsa_activity_definition_draft_layouts",
                columns: new[] { "TenantScopeKey", "DraftIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_drafts_TenantScopeKey_DefinitionIdIdentityHash_IdIdentityHash",
                table: "elsa_activity_definition_drafts",
                columns: new[] { "TenantScopeKey", "DefinitionIdIdentityHash", "IdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_versions_v2_TenantScopeKey_DefinitionIdIdentityHash_SemVerSortKey",
                table: "elsa_activity_definition_versions_v2",
                columns: new[] { "TenantScopeKey", "DefinitionIdIdentityHash", "SemVerSortKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definitions_TenantKey_ActivityTypeKey",
                table: "elsa_activity_definitions",
                columns: new[] { "TenantKey", "ActivityTypeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_dependency_edges_TenantScopeKey_DependencyVersionIdIdentityHash",
                table: "elsa_activity_dependency_edges",
                columns: new[] { "TenantScopeKey", "DependencyVersionIdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_dependency_edges_TenantScopeKey_OwnerVersionIdIdentityHash",
                table: "elsa_activity_dependency_edges",
                columns: new[] { "TenantScopeKey", "OwnerVersionIdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_dependency_edges_TenantScopeKey_OwnerVersionIdIdentityHash_OccurrenceIdIdentityHash_DependencyVersionIdIdentit~",
                table: "elsa_activity_dependency_edges",
                columns: new[] { "TenantScopeKey", "OwnerVersionIdIdentityHash", "OccurrenceIdIdentityHash", "DependencyVersionIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_design_operations_TenantScopeKey_OperationKindIdentityHash_OperationKeyIdentityHash",
                table: "elsa_activity_design_operations",
                columns: new[] { "TenantScopeKey", "OperationKindIdentityHash", "OperationKeyIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_draft_validations_TenantScopeKey_DraftIdIdentityHash_Revision",
                table: "elsa_activity_draft_validations",
                columns: new[] { "TenantScopeKey", "DraftIdIdentityHash", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_fork_candidates_TenantScopeKey_ActorIdentityHash_CandidateIdIdentityHash",
                table: "elsa_activity_fork_candidates",
                columns: new[] { "TenantScopeKey", "ActorIdentityHash", "CandidateIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_fork_candidates_TenantScopeKey_RetentionKey_IdIdentityHash",
                table: "elsa_activity_fork_candidates",
                columns: new[] { "TenantScopeKey", "RetentionKey", "IdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_fork_receipts_TenantScopeKey_ActorIdentityHash_IdempotencyIdentityHash",
                table: "elsa_activity_fork_receipts",
                columns: new[] { "TenantScopeKey", "ActorIdentityHash", "IdempotencyIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_definitions_TenantId_ValidFromSequence_ValidToSequenceExclusive",
                table: "elsa_activity_management_definitions",
                columns: new[] { "TenantId", "ValidFromSequence", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_definitions_TenantScopeKey_ResourceIdIdentityHash_ValidToSequenceExclusive",
                table: "elsa_activity_management_definitions",
                columns: new[] { "TenantScopeKey", "ResourceIdIdentityHash", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_drafts_TenantId_ValidFromSequence_ValidToSequenceExclusive",
                table: "elsa_activity_management_drafts",
                columns: new[] { "TenantId", "ValidFromSequence", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_drafts_TenantScopeKey_ResourceIdIdentityHash_ValidToSequenceExclusive",
                table: "elsa_activity_management_drafts",
                columns: new[] { "TenantScopeKey", "ResourceIdIdentityHash", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_snapshots_Sequence",
                table: "elsa_activity_management_snapshots",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_versions_TenantId_ValidFromSequence_ValidToSequenceExclusive",
                table: "elsa_activity_management_versions",
                columns: new[] { "TenantId", "ValidFromSequence", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_versions_TenantScopeKey_ResourceIdIdentityHash_ValidToSequenceExclusive",
                table: "elsa_activity_management_versions",
                columns: new[] { "TenantScopeKey", "ResourceIdIdentityHash", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_upgrade_apply_receipts_TenantScopeKey_PlanIdIdentityHash_IdempotencyKeyHash",
                table: "elsa_activity_upgrade_apply_receipts",
                columns: new[] { "TenantScopeKey", "PlanIdIdentityHash", "IdempotencyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_version_layouts_TenantScopeKey_DefinitionVersionIdIdentityHash",
                table: "elsa_activity_version_layouts",
                columns: new[] { "TenantScopeKey", "DefinitionVersionIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_version_publications_TenantScopeKey_DefinitionIdIdentityHash_IdIdentityHash",
                table: "elsa_activity_version_publications",
                columns: new[] { "TenantScopeKey", "DefinitionIdIdentityHash", "IdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_version_publications_TenantScopeKey_DefinitionVersionIdIdentityHash",
                table: "elsa_activity_version_publications",
                columns: new[] { "TenantScopeKey", "DefinitionVersionIdIdentityHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_activity_availability_settings");

            migrationBuilder.DropTable(
                name: "elsa_activity_definition_authoring");

            migrationBuilder.DropTable(
                name: "elsa_activity_definition_draft_layouts");

            migrationBuilder.DropTable(
                name: "elsa_activity_definition_drafts");

            migrationBuilder.DropTable(
                name: "elsa_activity_definition_versions_v2");

            migrationBuilder.DropTable(
                name: "elsa_activity_definitions");

            migrationBuilder.DropTable(
                name: "elsa_activity_dependency_edges");

            migrationBuilder.DropTable(
                name: "elsa_activity_dependency_projection");

            migrationBuilder.DropTable(
                name: "elsa_activity_design_operations");

            migrationBuilder.DropTable(
                name: "elsa_activity_draft_validations");

            migrationBuilder.DropTable(
                name: "elsa_activity_fork_candidates");

            migrationBuilder.DropTable(
                name: "elsa_activity_fork_receipts");

            migrationBuilder.DropTable(
                name: "elsa_activity_management_definitions");

            migrationBuilder.DropTable(
                name: "elsa_activity_management_drafts");

            migrationBuilder.DropTable(
                name: "elsa_activity_management_snapshots");

            migrationBuilder.DropTable(
                name: "elsa_activity_management_versions");

            migrationBuilder.DropTable(
                name: "elsa_activity_management_watermarks");

            migrationBuilder.DropTable(
                name: "elsa_activity_upgrade_apply_receipts");

            migrationBuilder.DropTable(
                name: "elsa_activity_upgrade_plans");

            migrationBuilder.DropTable(
                name: "elsa_activity_version_layouts");

            migrationBuilder.DropTable(
                name: "elsa_activity_version_publications");
        }
    }
}
