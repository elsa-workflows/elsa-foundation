using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Migrations.ActivitiesDesign.Sqlite
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
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    ScopeIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    Rules = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_availability_settings", x => new { x.TenantScopeKey, x.ScopeIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_authoring",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ContentAuthority = table.Column<string>(type: "TEXT", nullable: false),
                    ForkedFrom = table.Column<string>(type: "TEXT", nullable: true),
                    HeadVersionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    RecommendedVersionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    HeadVersionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RecommendedVersionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_authoring", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_draft_layouts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DraftId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Records = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    DraftIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_draft_layouts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_drafts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceVersionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    PresentationLabel = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedVersionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PublishedVersionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_drafts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_versions_v2",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    SemVerSortKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderSchemaVersion = table.Column<string>(type: "TEXT", nullable: false),
                    ConsumerKey = table.Column<string>(type: "TEXT", nullable: false),
                    ConsumerSchemaVersion = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptorType = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptorPayloadSource = table.Column<string>(type: "TEXT", nullable: true),
                    InputsSource = table.Column<string>(type: "TEXT", nullable: true),
                    OutputsSource = table.Column<string>(type: "TEXT", nullable: true),
                    DesignFacetsSource = table.Column<string>(type: "TEXT", nullable: true),
                    ExecutionType = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_versions_v2", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definitions",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActivityTypeKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    TenantKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definitions", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_dependency_edges",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OwnerVersionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    OwnerTemplateHash = table.Column<string>(type: "TEXT", nullable: false),
                    DependencyVersionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    DependencyTemplateHash = table.Column<string>(type: "TEXT", nullable: false),
                    OccurrenceId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ParentOccurrenceId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    ChildSlotName = table.Column<string>(type: "TEXT", nullable: false),
                    ChildIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    NodeOrigin = table.Column<string>(type: "TEXT", nullable: false),
                    MemberUsage = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    DependencyVersionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OccurrenceIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OwnerVersionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ParentOccurrenceIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_dependency_edges", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_dependency_projection",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RebuildId = table.Column<string>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Items = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_dependency_projection", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_design_operations",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true),
                    OperationKind = table.Column<string>(type: "TEXT", nullable: false),
                    OperationKey = table.Column<string>(type: "TEXT", nullable: false),
                    OperationKindIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OperationKeyIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CanonicalRequestFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    AuthoritativeResultFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    AuthoritativeResultJson = table.Column<string>(type: "TEXT", nullable: false),
                    MutatedUnitsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_design_operations", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_draft_validations",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DraftId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Diagnostics = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    DraftIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_draft_validations", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_fork_candidates",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CandidateId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    CandidateIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PreviewIdempotencyKey = table.Column<string>(type: "TEXT", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    AccessBindingFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorizationProfile = table.Column<string>(type: "TEXT", nullable: false),
                    SourceDefinitionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    SourceVersionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    SourceVersion = table.Column<string>(type: "TEXT", nullable: false),
                    SourceLifecycle = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceProviderFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    TargetProviderFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    ReservedDefinition = table.Column<string>(type: "TEXT", nullable: false),
                    ReservedAuthoringState = table.Column<string>(type: "TEXT", nullable: false),
                    ReservedDraft = table.Column<string>(type: "TEXT", nullable: false),
                    ReservedLayout = table.Column<string>(type: "TEXT", nullable: false),
                    MigrationDiagnostics = table.Column<string>(type: "TEXT", nullable: false),
                    SourceContractFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    TargetContractFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RetentionKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AppliedIdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    AppliedIdempotencyKeyIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    SourceDefinitionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_fork_candidates", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_fork_receipts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IdempotencyIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", nullable: false),
                    CandidateId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    PublicCandidateId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    AccessBindingFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorizationProfile = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ActivityTypeKey = table.Column<string>(type: "TEXT", nullable: false),
                    DraftId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    DefinitionMaterialJson = table.Column<string>(type: "TEXT", nullable: false),
                    AuthoringState = table.Column<string>(type: "TEXT", nullable: false),
                    Draft = table.Column<string>(type: "TEXT", nullable: false),
                    Layout = table.Column<string>(type: "TEXT", nullable: false),
                    MigrationDiagnostics = table.Column<string>(type: "TEXT", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CandidateIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DraftIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PublicCandidateIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_fork_receipts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_definitions",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActivityTypeKey = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ContentAuthority = table.Column<string>(type: "TEXT", nullable: true),
                    ContentAuthorityIsValid = table.Column<bool>(type: "INTEGER", nullable: false, computedColumnSql: "CASE WHEN json_valid(\"ContentAuthority\") = 1 THEN CASE WHEN \"ContentAuthorityKind\" IN (0, 1) AND json_type(\"ContentAuthority\") = 'object' AND json_type(\"ContentAuthority\", '$.kind') = 'integer' AND json_extract(\"ContentAuthority\", '$.kind') = \"ContentAuthorityKind\" AND json_type(\"ContentAuthority\", '$.authorityKey') = 'text' AND \"ContentAuthorityAuthorityKey\" = json_extract(\"ContentAuthority\", '$.authorityKey') AND json_valid(\"ContentAuthorityAuthorityKeyJson\") = 1 AND json_type(\"ContentAuthorityAuthorityKeyJson\") = 'text' AND json_extract(\"ContentAuthorityAuthorityKeyJson\", '$') = \"ContentAuthorityAuthorityKey\" AND length(trim(json_extract(\"ContentAuthority\", '$.authorityKey'), char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288))) > 0 AND (((json_type(\"ContentAuthority\", '$.sourceId') IS NULL OR json_type(\"ContentAuthority\", '$.sourceId') = 'null') AND \"ContentAuthoritySourceId\" IS NULL AND \"ContentAuthoritySourceIdJson\" = 'null') OR (json_type(\"ContentAuthority\", '$.sourceId') = 'text' AND \"ContentAuthoritySourceId\" = json_extract(\"ContentAuthority\", '$.sourceId') AND json_valid(\"ContentAuthoritySourceIdJson\") = 1 AND json_type(\"ContentAuthoritySourceIdJson\") = 'text' AND json_extract(\"ContentAuthoritySourceIdJson\", '$') = \"ContentAuthoritySourceId\" AND length(trim(json_extract(\"ContentAuthority\", '$.sourceId'), char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288))) > 0)) AND json_type(\"ContentAuthority\", '$.integrityHash') = 'text' AND \"ContentAuthorityIntegrityHash\" = json_extract(\"ContentAuthority\", '$.integrityHash') AND (\"ContentAuthorityKind\" = 1 OR json_type(\"ContentAuthority\", '$.sourceId') IS NULL OR json_type(\"ContentAuthority\", '$.sourceId') = 'null') THEN 1 ELSE 0 END ELSE 0 END", stored: false),
                    ContentAuthorityCanonicalJson = table.Column<string>(type: "TEXT", nullable: true),
                    ContentAuthorityAuthorityKeyJson = table.Column<string>(type: "TEXT", nullable: true),
                    ContentAuthoritySourceIdJson = table.Column<string>(type: "TEXT", nullable: true),
                    ContentAuthorityAuthorityKey = table.Column<string>(type: "TEXT", nullable: true),
                    ContentAuthoritySourceId = table.Column<string>(type: "TEXT", nullable: true),
                    ContentAuthorityIntegrityHash = table.Column<string>(type: "TEXT", nullable: true),
                    ContentAuthorityKind = table.Column<int>(type: "INTEGER", nullable: false),
                    HeadVersionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    RecommendedVersionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    Head = table.Column<string>(type: "TEXT", nullable: true),
                    Recommendation = table.Column<string>(type: "TEXT", nullable: true),
                    HeadProviderKey = table.Column<string>(type: "TEXT", nullable: true),
                    RecommendationProviderKey = table.Column<string>(type: "TEXT", nullable: true),
                    DraftCount = table.Column<long>(type: "INTEGER", nullable: false),
                    VersionCount = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    HeadVersionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RecommendedVersionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ResourceIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ValidFromSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    ValidToSequenceExclusive = table.Column<long>(type: "INTEGER", nullable: false),
                    ValidFromKey = table.Column<string>(type: "TEXT", nullable: false),
                    ValidToKey = table.Column<string>(type: "TEXT", nullable: false),
                    VisibilityKey = table.Column<string>(type: "TEXT", nullable: false),
                    SortKey = table.Column<string>(type: "TEXT", nullable: false),
                    SearchText = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_definitions", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_drafts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DraftId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceVersionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderSchemaVersion = table.Column<string>(type: "TEXT", nullable: false),
                    PresentationLabel = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DraftIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ResourceIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ValidFromSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    ValidToSequenceExclusive = table.Column<long>(type: "INTEGER", nullable: false),
                    ValidFromKey = table.Column<string>(type: "TEXT", nullable: false),
                    ValidToKey = table.Column<string>(type: "TEXT", nullable: false),
                    VisibilityKey = table.Column<string>(type: "TEXT", nullable: false),
                    SortKey = table.Column<string>(type: "TEXT", nullable: false),
                    SearchText = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_drafts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_snapshots",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_snapshots", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_versions",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DefinitionVersionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    Lifecycle = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderSchemaVersion = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DefinitionVersionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ResourceIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ValidFromSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    ValidToSequenceExclusive = table.Column<long>(type: "INTEGER", nullable: false),
                    ValidFromKey = table.Column<string>(type: "TEXT", nullable: false),
                    ValidToKey = table.Column<string>(type: "TEXT", nullable: false),
                    VisibilityKey = table.Column<string>(type: "TEXT", nullable: false),
                    SortKey = table.Column<string>(type: "TEXT", nullable: false),
                    SearchText = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_versions", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_watermarks",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    RetainedFromSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    AdvancedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_watermarks", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_upgrade_apply_receipts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true),
                    ReceiptId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    PlanId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ReceiptJson = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    PlanIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReceiptIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_upgrade_apply_receipts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_upgrade_plans",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true),
                    PlanId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    PlanJson = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    PlanIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_upgrade_plans", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_version_layouts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DefinitionVersionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Records = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    DefinitionVersionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_version_layouts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_version_publications",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "TEXT", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DefinitionVersionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    ActivityTypeKey = table.Column<string>(type: "TEXT", nullable: false),
                    ResolutionKind = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceDraftId = table.Column<string>(type: "TEXT", nullable: true),
                    SourceVersionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    Contract = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    TemplateId = table.Column<string>(type: "TEXT", nullable: false),
                    TemplateHash = table.Column<string>(type: "TEXT", nullable: false),
                    SourceReferenceId = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    DirectDependencyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ClosedTemplateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RuntimeRequirements = table.Column<string>(type: "TEXT", nullable: false),
                    ResourceMeasurements = table.Column<string>(type: "TEXT", nullable: false),
                    ResumeTargetCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Lifecycle = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DefinitionVersionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
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
                name: "IX_elsa_activity_dependency_edges_TenantScopeKey_OwnerVersionIdIdentityHash_OccurrenceIdIdentityHash_DependencyVersionIdIdentityHash",
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
