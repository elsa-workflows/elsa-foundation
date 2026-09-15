using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Migrations.ActivitiesDesign.MySql
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
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    ScopeIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Scope = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Rules = table.Column<string>(type: "longtext", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_availability_settings", x => new { x.TenantScopeKey, x.ScopeIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_authoring",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DefinitionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    ContentAuthority = table.Column<string>(type: "longtext", nullable: false),
                    ForkedFrom = table.Column<string>(type: "longtext", nullable: true),
                    HeadVersionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    RecommendedVersionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    HeadVersionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    RecommendedVersionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_authoring", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_draft_layouts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DraftId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Records = table.Column<string>(type: "longtext", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DraftIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_draft_layouts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_drafts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DefinitionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SourceVersionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    PresentationLabel = table.Column<string>(type: "longtext", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "longtext", nullable: false),
                    PublishedVersionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    PublishedVersionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_drafts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_versions_v2",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "longtext", nullable: false),
                    SemVerSortKey = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    DefinitionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    ProviderKey = table.Column<string>(type: "longtext", nullable: false),
                    ProviderSchemaVersion = table.Column<string>(type: "longtext", nullable: false),
                    ConsumerKey = table.Column<string>(type: "longtext", nullable: false),
                    ConsumerSchemaVersion = table.Column<string>(type: "longtext", nullable: false),
                    DescriptorType = table.Column<string>(type: "longtext", nullable: false),
                    DescriptorPayloadSource = table.Column<string>(type: "longtext", nullable: true),
                    InputsSource = table.Column<string>(type: "longtext", nullable: true),
                    OutputsSource = table.Column<string>(type: "longtext", nullable: true),
                    DesignFacetsSource = table.Column<string>(type: "longtext", nullable: true),
                    ExecutionType = table.Column<int>(type: "int", nullable: false),
                    SourceKind = table.Column<string>(type: "longtext", nullable: false),
                    SourceId = table.Column<string>(type: "longtext", nullable: false),
                    Hash = table.Column<string>(type: "longtext", nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_versions_v2", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_definitions",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ActivityTypeKey = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "longtext", nullable: false),
                    DisplayName = table.Column<string>(type: "longtext", nullable: true),
                    Description = table.Column<string>(type: "longtext", nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    TenantKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definitions", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_dependency_edges",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    OwnerVersionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    OwnerTemplateHash = table.Column<string>(type: "longtext", nullable: false),
                    DependencyVersionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    DependencyTemplateHash = table.Column<string>(type: "longtext", nullable: false),
                    OccurrenceId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    ParentOccurrenceId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    ChildSlotName = table.Column<string>(type: "longtext", nullable: false),
                    ChildIndex = table.Column<int>(type: "int", nullable: false),
                    NodeOrigin = table.Column<string>(type: "longtext", nullable: false),
                    MemberUsage = table.Column<string>(type: "longtext", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DependencyVersionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    OccurrenceIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    OwnerVersionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ParentOccurrenceIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_dependency_edges", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_dependency_projection",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    RebuildId = table.Column<string>(type: "longtext", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    Items = table.Column<string>(type: "longtext", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_dependency_projection", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_design_operations",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true),
                    OperationKind = table.Column<string>(type: "longtext", nullable: false),
                    OperationKey = table.Column<string>(type: "longtext", nullable: false),
                    OperationKindIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    OperationKeyIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    CanonicalRequestFingerprint = table.Column<string>(type: "longtext", nullable: false),
                    AuthoritativeResultFingerprint = table.Column<string>(type: "longtext", nullable: false),
                    AuthoritativeResultJson = table.Column<string>(type: "longtext", nullable: false),
                    MutatedUnitsJson = table.Column<string>(type: "longtext", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_design_operations", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_draft_validations",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DraftId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    Diagnostics = table.Column<string>(type: "longtext", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DraftIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_draft_validations", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_fork_candidates",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    CandidateId = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    CandidateIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ActorIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PreviewIdempotencyKey = table.Column<string>(type: "longtext", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "longtext", nullable: false),
                    AccessBindingFingerprint = table.Column<string>(type: "longtext", nullable: false),
                    ActorId = table.Column<string>(type: "longtext", nullable: false),
                    AuthorizationProfile = table.Column<string>(type: "longtext", nullable: false),
                    SourceDefinitionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    SourceVersionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    SourceVersion = table.Column<string>(type: "longtext", nullable: false),
                    SourceLifecycle = table.Column<int>(type: "int", nullable: false),
                    SourceProviderFingerprint = table.Column<string>(type: "longtext", nullable: false),
                    TargetProviderFingerprint = table.Column<string>(type: "longtext", nullable: false),
                    ReservedDefinition = table.Column<string>(type: "longtext", nullable: false),
                    ReservedAuthoringState = table.Column<string>(type: "longtext", nullable: false),
                    ReservedDraft = table.Column<string>(type: "longtext", nullable: false),
                    ReservedLayout = table.Column<string>(type: "longtext", nullable: false),
                    MigrationDiagnostics = table.Column<string>(type: "longtext", nullable: false),
                    SourceContractFingerprint = table.Column<string>(type: "longtext", nullable: false),
                    TargetContractFingerprint = table.Column<string>(type: "longtext", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    RetentionKey = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AppliedIdempotencyKey = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    AppliedIdempotencyKeyIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    SourceDefinitionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_fork_candidates", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_fork_receipts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ActorIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    IdempotencyIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "longtext", nullable: false),
                    CandidateId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    PublicCandidateId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "longtext", nullable: false),
                    AccessBindingFingerprint = table.Column<string>(type: "longtext", nullable: false),
                    ActorId = table.Column<string>(type: "longtext", nullable: false),
                    AuthorizationProfile = table.Column<string>(type: "longtext", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DefinitionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    ActivityTypeKey = table.Column<string>(type: "longtext", nullable: false),
                    DraftId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    DefinitionMaterialJson = table.Column<string>(type: "longtext", nullable: false),
                    AuthoringState = table.Column<string>(type: "longtext", nullable: false),
                    Draft = table.Column<string>(type: "longtext", nullable: false),
                    Layout = table.Column<string>(type: "longtext", nullable: false),
                    MigrationDiagnostics = table.Column<string>(type: "longtext", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    CandidateIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    DraftIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    PublicCandidateIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_fork_receipts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_definitions",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ActivityTypeKey = table.Column<string>(type: "longtext", nullable: false),
                    Category = table.Column<string>(type: "longtext", nullable: false),
                    DisplayName = table.Column<string>(type: "longtext", nullable: true),
                    Description = table.Column<string>(type: "longtext", nullable: true),
                    ContentAuthority = table.Column<string>(type: "longtext", nullable: true),
                    ContentAuthorityIsValid = table.Column<bool>(type: "tinyint(1)", nullable: false, computedColumnSql: "CASE WHEN JSON_VALID(`ContentAuthority`) = 1 THEN CASE WHEN `ContentAuthorityKind` IN (0, 1) AND JSON_TYPE(`ContentAuthority`) = 'OBJECT' AND JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.kind')) = 'INTEGER' AND CAST(JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthority`, '$.kind')) AS SIGNED) = `ContentAuthorityKind` AND JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.authorityKey')) = 'STRING' AND `ContentAuthorityAuthorityKey` = JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthority`, '$.authorityKey')) AND JSON_VALID(`ContentAuthorityAuthorityKeyJson`) = 1 AND JSON_TYPE(JSON_EXTRACT(`ContentAuthorityAuthorityKeyJson`, '$')) = 'STRING' AND JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthorityAuthorityKeyJson`, '$')) = `ContentAuthorityAuthorityKey` AND CHAR_LENGTH(REGEXP_REPLACE(JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthority`, '$.authorityKey')), '[[:space:]]', '')) > 0 AND (((JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.sourceId')) IS NULL OR JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.sourceId')) = 'NULL') AND `ContentAuthoritySourceId` IS NULL AND `ContentAuthoritySourceIdJson` = 'null') OR (JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.sourceId')) = 'STRING' AND `ContentAuthoritySourceId` = JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthority`, '$.sourceId')) AND JSON_VALID(`ContentAuthoritySourceIdJson`) = 1 AND JSON_TYPE(JSON_EXTRACT(`ContentAuthoritySourceIdJson`, '$')) = 'STRING' AND JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthoritySourceIdJson`, '$')) = `ContentAuthoritySourceId` AND CHAR_LENGTH(REGEXP_REPLACE(JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthority`, '$.sourceId')), '[[:space:]]', '')) > 0)) AND JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.integrityHash')) = 'STRING' AND `ContentAuthorityIntegrityHash` = JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthority`, '$.integrityHash')) AND (`ContentAuthorityKind` = 1 OR JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.sourceId')) IS NULL OR JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.sourceId')) = 'NULL') THEN 1 ELSE 0 END ELSE 0 END", stored: false),
                    ContentAuthorityCanonicalJson = table.Column<string>(type: "longtext", nullable: true),
                    ContentAuthorityAuthorityKeyJson = table.Column<string>(type: "longtext", nullable: true),
                    ContentAuthoritySourceIdJson = table.Column<string>(type: "longtext", nullable: true),
                    ContentAuthorityAuthorityKey = table.Column<string>(type: "longtext", nullable: true),
                    ContentAuthoritySourceId = table.Column<string>(type: "longtext", nullable: true),
                    ContentAuthorityIntegrityHash = table.Column<string>(type: "longtext", nullable: true),
                    ContentAuthorityKind = table.Column<int>(type: "int", nullable: false),
                    HeadVersionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    RecommendedVersionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    Head = table.Column<string>(type: "longtext", nullable: true),
                    Recommendation = table.Column<string>(type: "longtext", nullable: true),
                    HeadProviderKey = table.Column<string>(type: "longtext", nullable: true),
                    RecommendationProviderKey = table.Column<string>(type: "longtext", nullable: true),
                    DraftCount = table.Column<long>(type: "bigint", nullable: false),
                    VersionCount = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    HeadVersionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    RecommendedVersionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    ResourceIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ResourceId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    DefinitionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    ValidFromSequence = table.Column<long>(type: "bigint", nullable: false),
                    ValidToSequenceExclusive = table.Column<long>(type: "bigint", nullable: false),
                    ValidFromKey = table.Column<string>(type: "longtext", nullable: false),
                    ValidToKey = table.Column<string>(type: "longtext", nullable: false),
                    VisibilityKey = table.Column<string>(type: "longtext", nullable: false),
                    SortKey = table.Column<string>(type: "longtext", nullable: false),
                    SearchText = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_definitions", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_drafts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DraftId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SourceVersionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProviderKey = table.Column<string>(type: "longtext", nullable: false),
                    ProviderSchemaVersion = table.Column<string>(type: "longtext", nullable: false),
                    PresentationLabel = table.Column<string>(type: "longtext", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    DraftIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    ResourceIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ResourceId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    DefinitionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    ValidFromSequence = table.Column<long>(type: "bigint", nullable: false),
                    ValidToSequenceExclusive = table.Column<long>(type: "bigint", nullable: false),
                    ValidFromKey = table.Column<string>(type: "longtext", nullable: false),
                    ValidToKey = table.Column<string>(type: "longtext", nullable: false),
                    VisibilityKey = table.Column<string>(type: "longtext", nullable: false),
                    SortKey = table.Column<string>(type: "longtext", nullable: false),
                    SearchText = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_drafts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_snapshots",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_snapshots", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_versions",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DefinitionVersionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    Version = table.Column<string>(type: "longtext", nullable: false),
                    Lifecycle = table.Column<int>(type: "int", nullable: false),
                    ProviderKey = table.Column<string>(type: "longtext", nullable: false),
                    ProviderSchemaVersion = table.Column<string>(type: "longtext", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    DefinitionVersionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    ResourceIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ResourceId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    DefinitionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    ValidFromSequence = table.Column<long>(type: "bigint", nullable: false),
                    ValidToSequenceExclusive = table.Column<long>(type: "bigint", nullable: false),
                    ValidFromKey = table.Column<string>(type: "longtext", nullable: false),
                    ValidToKey = table.Column<string>(type: "longtext", nullable: false),
                    VisibilityKey = table.Column<string>(type: "longtext", nullable: false),
                    SortKey = table.Column<string>(type: "longtext", nullable: false),
                    SearchText = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_versions", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_watermarks",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    RetainedFromSequence = table.Column<long>(type: "bigint", nullable: false),
                    AdvancedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_watermarks", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_upgrade_apply_receipts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true),
                    ReceiptId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    PlanId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    ReceiptJson = table.Column<string>(type: "longtext", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    PlanIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ReceiptIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_upgrade_apply_receipts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_upgrade_plans",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true),
                    PlanId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    PlanJson = table.Column<string>(type: "longtext", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    PlanIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_upgrade_plans", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_version_layouts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DefinitionVersionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    Records = table.Column<string>(type: "longtext", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionVersionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_version_layouts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "elsa_activity_version_publications",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "varchar(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DefinitionVersionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    DefinitionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                    Version = table.Column<string>(type: "longtext", nullable: false),
                    ActivityTypeKey = table.Column<string>(type: "longtext", nullable: false),
                    ResolutionKind = table.Column<int>(type: "int", nullable: false),
                    SourceDraftId = table.Column<string>(type: "longtext", nullable: true),
                    SourceVersionId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    Contract = table.Column<string>(type: "longtext", nullable: false),
                    Provider = table.Column<string>(type: "longtext", nullable: false),
                    TemplateId = table.Column<string>(type: "longtext", nullable: false),
                    TemplateHash = table.Column<string>(type: "longtext", nullable: false),
                    SourceReferenceId = table.Column<string>(type: "longtext", nullable: false),
                    ProviderFingerprint = table.Column<string>(type: "longtext", nullable: false),
                    DirectDependencyCount = table.Column<int>(type: "int", nullable: false),
                    ClosedTemplateCount = table.Column<int>(type: "int", nullable: false),
                    RuntimeRequirements = table.Column<string>(type: "longtext", nullable: false),
                    ResourceMeasurements = table.Column<string>(type: "longtext", nullable: false),
                    ResumeTargetCount = table.Column<int>(type: "int", nullable: false),
                    Lifecycle = table.Column<int>(type: "int", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DefinitionVersionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_version_publications", x => new { x.TenantScopeKey, x.IdIdentityHash });
                })
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_authoring_TenantScopeKey_Definition~",
                table: "elsa_activity_definition_authoring",
                columns: new[] { "TenantScopeKey", "DefinitionIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_authoring_TenantScopeKey_HeadVersio~",
                table: "elsa_activity_definition_authoring",
                columns: new[] { "TenantScopeKey", "HeadVersionIdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_draft_layouts_TenantScopeKey_DraftI~",
                table: "elsa_activity_definition_draft_layouts",
                columns: new[] { "TenantScopeKey", "DraftIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_drafts_TenantScopeKey_DefinitionIdI~",
                table: "elsa_activity_definition_drafts",
                columns: new[] { "TenantScopeKey", "DefinitionIdIdentityHash", "IdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_versions_v2_TenantScopeKey_Definiti~",
                table: "elsa_activity_definition_versions_v2",
                columns: new[] { "TenantScopeKey", "DefinitionIdIdentityHash", "SemVerSortKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definitions_TenantKey_ActivityTypeKey",
                table: "elsa_activity_definitions",
                columns: new[] { "TenantKey", "ActivityTypeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_dependency_edges_TenantScopeKey_DependencyVers~",
                table: "elsa_activity_dependency_edges",
                columns: new[] { "TenantScopeKey", "DependencyVersionIdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_dependency_edges_TenantScopeKey_OwnerVersionI~1",
                table: "elsa_activity_dependency_edges",
                columns: new[] { "TenantScopeKey", "OwnerVersionIdIdentityHash", "OccurrenceIdIdentityHash", "DependencyVersionIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_dependency_edges_TenantScopeKey_OwnerVersionId~",
                table: "elsa_activity_dependency_edges",
                columns: new[] { "TenantScopeKey", "OwnerVersionIdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_design_operations_TenantScopeKey_OperationKind~",
                table: "elsa_activity_design_operations",
                columns: new[] { "TenantScopeKey", "OperationKindIdentityHash", "OperationKeyIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_draft_validations_TenantScopeKey_DraftIdIdenti~",
                table: "elsa_activity_draft_validations",
                columns: new[] { "TenantScopeKey", "DraftIdIdentityHash", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_fork_candidates_TenantScopeKey_ActorIdentityHa~",
                table: "elsa_activity_fork_candidates",
                columns: new[] { "TenantScopeKey", "ActorIdentityHash", "CandidateIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_fork_candidates_TenantScopeKey_RetentionKey_Id~",
                table: "elsa_activity_fork_candidates",
                columns: new[] { "TenantScopeKey", "RetentionKey", "IdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_fork_receipts_TenantScopeKey_ActorIdentityHash~",
                table: "elsa_activity_fork_receipts",
                columns: new[] { "TenantScopeKey", "ActorIdentityHash", "IdempotencyIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_definitions_TenantId_ValidFromSeque~",
                table: "elsa_activity_management_definitions",
                columns: new[] { "TenantId", "ValidFromSequence", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_definitions_TenantScopeKey_Resource~",
                table: "elsa_activity_management_definitions",
                columns: new[] { "TenantScopeKey", "ResourceIdIdentityHash", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_drafts_TenantId_ValidFromSequence_V~",
                table: "elsa_activity_management_drafts",
                columns: new[] { "TenantId", "ValidFromSequence", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_drafts_TenantScopeKey_ResourceIdIde~",
                table: "elsa_activity_management_drafts",
                columns: new[] { "TenantScopeKey", "ResourceIdIdentityHash", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_snapshots_Sequence",
                table: "elsa_activity_management_snapshots",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_versions_TenantId_ValidFromSequence~",
                table: "elsa_activity_management_versions",
                columns: new[] { "TenantId", "ValidFromSequence", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_versions_TenantScopeKey_ResourceIdI~",
                table: "elsa_activity_management_versions",
                columns: new[] { "TenantScopeKey", "ResourceIdIdentityHash", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_upgrade_apply_receipts_TenantScopeKey_PlanIdId~",
                table: "elsa_activity_upgrade_apply_receipts",
                columns: new[] { "TenantScopeKey", "PlanIdIdentityHash", "IdempotencyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_version_layouts_TenantScopeKey_DefinitionVersi~",
                table: "elsa_activity_version_layouts",
                columns: new[] { "TenantScopeKey", "DefinitionVersionIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_version_publications_TenantScopeKey_Definitio~1",
                table: "elsa_activity_version_publications",
                columns: new[] { "TenantScopeKey", "DefinitionIdIdentityHash", "IdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_version_publications_TenantScopeKey_Definition~",
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
