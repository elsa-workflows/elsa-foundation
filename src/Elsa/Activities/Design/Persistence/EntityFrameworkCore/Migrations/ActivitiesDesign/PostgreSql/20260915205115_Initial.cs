using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Migrations.ActivitiesDesign.PostgreSql
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
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    ScopeIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Scope = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Rules = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_availability_settings", x => new { x.TenantScopeKey, x.ScopeIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_authoring",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DefinitionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ContentAuthority = table.Column<string>(type: "text", nullable: false),
                    ForkedFrom = table.Column<string>(type: "text", nullable: true),
                    HeadVersionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RecommendedVersionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HeadVersionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RecommendedVersionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_authoring", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_draft_layouts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DraftId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Records = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    DraftIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_draft_layouts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_drafts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DefinitionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SourceVersionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    PresentationLabel = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    PublishedVersionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PublishedVersionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_drafts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definition_versions_v2",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    SemVerSortKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DefinitionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderSchemaVersion = table.Column<string>(type: "text", nullable: false),
                    ConsumerKey = table.Column<string>(type: "text", nullable: false),
                    ConsumerSchemaVersion = table.Column<string>(type: "text", nullable: false),
                    DescriptorType = table.Column<string>(type: "text", nullable: false),
                    DescriptorPayloadSource = table.Column<string>(type: "text", nullable: true),
                    InputsSource = table.Column<string>(type: "text", nullable: true),
                    OutputsSource = table.Column<string>(type: "text", nullable: true),
                    DesignFacetsSource = table.Column<string>(type: "text", nullable: true),
                    ExecutionType = table.Column<int>(type: "integer", nullable: false),
                    SourceKind = table.Column<string>(type: "text", nullable: false),
                    SourceId = table.Column<string>(type: "text", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definition_versions_v2", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_definitions",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActivityTypeKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    TenantKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_definitions", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_dependency_edges",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerVersionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OwnerTemplateHash = table.Column<string>(type: "text", nullable: false),
                    DependencyVersionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    DependencyTemplateHash = table.Column<string>(type: "text", nullable: false),
                    OccurrenceId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ParentOccurrenceId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ChildSlotName = table.Column<string>(type: "text", nullable: false),
                    ChildIndex = table.Column<int>(type: "integer", nullable: false),
                    NodeOrigin = table.Column<string>(type: "text", nullable: false),
                    MemberUsage = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    DependencyVersionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurrenceIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerVersionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ParentOccurrenceIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_dependency_edges", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_dependency_projection",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RebuildId = table.Column<string>(type: "text", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Items = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_dependency_projection", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_design_operations",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true),
                    OperationKind = table.Column<string>(type: "text", nullable: false),
                    OperationKey = table.Column<string>(type: "text", nullable: false),
                    OperationKindIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OperationKeyIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CanonicalRequestFingerprint = table.Column<string>(type: "text", nullable: false),
                    AuthoritativeResultFingerprint = table.Column<string>(type: "text", nullable: false),
                    AuthoritativeResultJson = table.Column<string>(type: "text", nullable: false),
                    MutatedUnitsJson = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_design_operations", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_draft_validations",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DraftId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Diagnostics = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    DraftIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_draft_validations", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_fork_candidates",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CandidateId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CandidateIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviewIdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "text", nullable: false),
                    AccessBindingFingerprint = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    AuthorizationProfile = table.Column<string>(type: "text", nullable: false),
                    SourceDefinitionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    SourceVersionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    SourceVersion = table.Column<string>(type: "text", nullable: false),
                    SourceLifecycle = table.Column<int>(type: "integer", nullable: false),
                    SourceProviderFingerprint = table.Column<string>(type: "text", nullable: false),
                    TargetProviderFingerprint = table.Column<string>(type: "text", nullable: false),
                    ReservedDefinition = table.Column<string>(type: "text", nullable: false),
                    ReservedAuthoringState = table.Column<string>(type: "text", nullable: false),
                    ReservedDraft = table.Column<string>(type: "text", nullable: false),
                    ReservedLayout = table.Column<string>(type: "text", nullable: false),
                    MigrationDiagnostics = table.Column<string>(type: "text", nullable: false),
                    SourceContractFingerprint = table.Column<string>(type: "text", nullable: false),
                    TargetContractFingerprint = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetentionKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AppliedIdempotencyKey = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    AppliedIdempotencyKeyIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    SourceDefinitionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_fork_candidates", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_fork_receipts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    CandidateId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PublicCandidateId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "text", nullable: false),
                    AccessBindingFingerprint = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    AuthorizationProfile = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DefinitionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ActivityTypeKey = table.Column<string>(type: "text", nullable: false),
                    DraftId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    DefinitionMaterialJson = table.Column<string>(type: "text", nullable: false),
                    AuthoringState = table.Column<string>(type: "text", nullable: false),
                    Draft = table.Column<string>(type: "text", nullable: false),
                    Layout = table.Column<string>(type: "text", nullable: false),
                    MigrationDiagnostics = table.Column<string>(type: "text", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CandidateIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DraftIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PublicCandidateIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_fork_receipts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_definitions",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActivityTypeKey = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ContentAuthority = table.Column<string>(type: "text", nullable: true),
                    ContentAuthorityIsValid = table.Column<bool>(type: "boolean", nullable: false, computedColumnSql: "CASE WHEN \"ContentAuthority\" IS NOT NULL AND \"ContentAuthority\" IS JSON OBJECT THEN CASE WHEN \"ContentAuthorityKind\" IN (0, 1) AND jsonb_typeof(\"ContentAuthority\"::jsonb -> 'kind') = 'number' AND (\"ContentAuthority\"::jsonb ->> 'kind') ~ '^[01]$' AND (\"ContentAuthority\"::jsonb ->> 'kind')::integer = \"ContentAuthorityKind\" AND jsonb_typeof(\"ContentAuthority\"::jsonb -> 'authorityKey') = 'string' AND \"ContentAuthorityAuthorityKey\" = \"ContentAuthority\"::jsonb ->> 'authorityKey' AND jsonb_typeof(\"ContentAuthorityAuthorityKeyJson\"::jsonb) = 'string' AND (\"ContentAuthorityAuthorityKeyJson\"::jsonb #>> '{}') = \"ContentAuthorityAuthorityKey\" AND btrim(\"ContentAuthority\"::jsonb ->> 'authorityKey', ' ' || chr(9) || chr(10) || chr(11) || chr(12) || chr(13) || chr(133) || chr(160) || chr(5760) || chr(8192) || chr(8193) || chr(8194) || chr(8195) || chr(8196) || chr(8197) || chr(8198) || chr(8199) || chr(8200) || chr(8201) || chr(8202) || chr(8232) || chr(8233) || chr(8239) || chr(8287) || chr(12288)) <> '' AND (((NOT (\"ContentAuthority\"::jsonb ? 'sourceId') OR jsonb_typeof(\"ContentAuthority\"::jsonb -> 'sourceId') = 'null') AND \"ContentAuthoritySourceId\" IS NULL AND \"ContentAuthoritySourceIdJson\" = 'null') OR (jsonb_typeof(\"ContentAuthority\"::jsonb -> 'sourceId') = 'string' AND \"ContentAuthoritySourceId\" = \"ContentAuthority\"::jsonb ->> 'sourceId' AND jsonb_typeof(\"ContentAuthoritySourceIdJson\"::jsonb) = 'string' AND (\"ContentAuthoritySourceIdJson\"::jsonb #>> '{}') = \"ContentAuthoritySourceId\" AND btrim(\"ContentAuthority\"::jsonb ->> 'sourceId', ' ' || chr(9) || chr(10) || chr(11) || chr(12) || chr(13) || chr(133) || chr(160) || chr(5760) || chr(8192) || chr(8193) || chr(8194) || chr(8195) || chr(8196) || chr(8197) || chr(8198) || chr(8199) || chr(8200) || chr(8201) || chr(8202) || chr(8232) || chr(8233) || chr(8239) || chr(8287) || chr(12288)) <> '')) AND jsonb_typeof(\"ContentAuthority\"::jsonb -> 'integrityHash') = 'string' AND \"ContentAuthorityIntegrityHash\" = \"ContentAuthority\"::jsonb ->> 'integrityHash' AND (\"ContentAuthorityKind\" = 1 OR NOT (\"ContentAuthority\"::jsonb ? 'sourceId') OR jsonb_typeof(\"ContentAuthority\"::jsonb -> 'sourceId') = 'null') THEN true ELSE false END ELSE false END", stored: true),
                    ContentAuthorityCanonicalJson = table.Column<string>(type: "text", nullable: true),
                    ContentAuthorityAuthorityKeyJson = table.Column<string>(type: "text", nullable: true),
                    ContentAuthoritySourceIdJson = table.Column<string>(type: "text", nullable: true),
                    ContentAuthorityAuthorityKey = table.Column<string>(type: "text", nullable: true),
                    ContentAuthoritySourceId = table.Column<string>(type: "text", nullable: true),
                    ContentAuthorityIntegrityHash = table.Column<string>(type: "text", nullable: true),
                    ContentAuthorityKind = table.Column<int>(type: "integer", nullable: false),
                    HeadVersionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RecommendedVersionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Head = table.Column<string>(type: "text", nullable: true),
                    Recommendation = table.Column<string>(type: "text", nullable: true),
                    HeadProviderKey = table.Column<string>(type: "text", nullable: true),
                    RecommendationProviderKey = table.Column<string>(type: "text", nullable: true),
                    DraftCount = table.Column<long>(type: "bigint", nullable: false),
                    VersionCount = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HeadVersionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RecommendedVersionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResourceIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResourceId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    DefinitionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ValidFromSequence = table.Column<long>(type: "bigint", nullable: false),
                    ValidToSequenceExclusive = table.Column<long>(type: "bigint", nullable: false),
                    ValidFromKey = table.Column<string>(type: "text", nullable: false),
                    ValidToKey = table.Column<string>(type: "text", nullable: false),
                    VisibilityKey = table.Column<string>(type: "text", nullable: false),
                    SortKey = table.Column<string>(type: "text", nullable: false),
                    SearchText = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_definitions", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_drafts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DraftId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SourceVersionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderSchemaVersion = table.Column<string>(type: "text", nullable: false),
                    PresentationLabel = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DraftIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResourceIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResourceId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    DefinitionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ValidFromSequence = table.Column<long>(type: "bigint", nullable: false),
                    ValidToSequenceExclusive = table.Column<long>(type: "bigint", nullable: false),
                    ValidFromKey = table.Column<string>(type: "text", nullable: false),
                    ValidToKey = table.Column<string>(type: "text", nullable: false),
                    VisibilityKey = table.Column<string>(type: "text", nullable: false),
                    SortKey = table.Column<string>(type: "text", nullable: false),
                    SearchText = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_drafts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_snapshots",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_snapshots", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_versions",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DefinitionVersionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Lifecycle = table.Column<int>(type: "integer", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderSchemaVersion = table.Column<string>(type: "text", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DefinitionVersionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResourceIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResourceId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    DefinitionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ValidFromSequence = table.Column<long>(type: "bigint", nullable: false),
                    ValidToSequenceExclusive = table.Column<long>(type: "bigint", nullable: false),
                    ValidFromKey = table.Column<string>(type: "text", nullable: false),
                    ValidToKey = table.Column<string>(type: "text", nullable: false),
                    VisibilityKey = table.Column<string>(type: "text", nullable: false),
                    SortKey = table.Column<string>(type: "text", nullable: false),
                    SearchText = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_versions", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_management_watermarks",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    RetainedFromSequence = table.Column<long>(type: "bigint", nullable: false),
                    AdvancedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_management_watermarks", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_upgrade_apply_receipts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true),
                    ReceiptId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PlanId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ReceiptJson = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    PlanIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReceiptIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_upgrade_apply_receipts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_upgrade_plans",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true),
                    PlanId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PlanJson = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    PlanIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_upgrade_plans", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_version_layouts",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DefinitionVersionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Records = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    DefinitionVersionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_version_layouts", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateTable(
                name: "elsa_activity_version_publications",
                columns: table => new
                {
                    TenantScopeKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    IdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DefinitionVersionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    DefinitionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    ActivityTypeKey = table.Column<string>(type: "text", nullable: false),
                    ResolutionKind = table.Column<int>(type: "integer", nullable: false),
                    SourceDraftId = table.Column<string>(type: "text", nullable: true),
                    SourceVersionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Contract = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    TemplateId = table.Column<string>(type: "text", nullable: false),
                    TemplateHash = table.Column<string>(type: "text", nullable: false),
                    SourceReferenceId = table.Column<string>(type: "text", nullable: false),
                    ProviderFingerprint = table.Column<string>(type: "text", nullable: false),
                    DirectDependencyCount = table.Column<int>(type: "integer", nullable: false),
                    ClosedTemplateCount = table.Column<int>(type: "integer", nullable: false),
                    RuntimeRequirements = table.Column<string>(type: "text", nullable: false),
                    ResourceMeasurements = table.Column<string>(type: "text", nullable: false),
                    ResumeTargetCount = table.Column<int>(type: "integer", nullable: false),
                    Lifecycle = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    DefinitionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DefinitionVersionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceVersionIdIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_activity_version_publications", x => new { x.TenantScopeKey, x.IdIdentityHash });
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_authoring_TenantScopeKey_Definitio~",
                table: "elsa_activity_definition_authoring",
                columns: new[] { "TenantScopeKey", "DefinitionIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_authoring_TenantScopeKey_HeadVersi~",
                table: "elsa_activity_definition_authoring",
                columns: new[] { "TenantScopeKey", "HeadVersionIdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_draft_layouts_TenantScopeKey_Draft~",
                table: "elsa_activity_definition_draft_layouts",
                columns: new[] { "TenantScopeKey", "DraftIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_drafts_TenantScopeKey_DefinitionId~",
                table: "elsa_activity_definition_drafts",
                columns: new[] { "TenantScopeKey", "DefinitionIdIdentityHash", "IdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definition_versions_v2_TenantScopeKey_Definit~",
                table: "elsa_activity_definition_versions_v2",
                columns: new[] { "TenantScopeKey", "DefinitionIdIdentityHash", "SemVerSortKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_definitions_TenantKey_ActivityTypeKey",
                table: "elsa_activity_definitions",
                columns: new[] { "TenantKey", "ActivityTypeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_dependency_edges_TenantScopeKey_DependencyVer~",
                table: "elsa_activity_dependency_edges",
                columns: new[] { "TenantScopeKey", "DependencyVersionIdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_dependency_edges_TenantScopeKey_OwnerVersion~1",
                table: "elsa_activity_dependency_edges",
                columns: new[] { "TenantScopeKey", "OwnerVersionIdIdentityHash", "OccurrenceIdIdentityHash", "DependencyVersionIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_dependency_edges_TenantScopeKey_OwnerVersionI~",
                table: "elsa_activity_dependency_edges",
                columns: new[] { "TenantScopeKey", "OwnerVersionIdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_design_operations_TenantScopeKey_OperationKin~",
                table: "elsa_activity_design_operations",
                columns: new[] { "TenantScopeKey", "OperationKindIdentityHash", "OperationKeyIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_draft_validations_TenantScopeKey_DraftIdIdent~",
                table: "elsa_activity_draft_validations",
                columns: new[] { "TenantScopeKey", "DraftIdIdentityHash", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_fork_candidates_TenantScopeKey_ActorIdentityH~",
                table: "elsa_activity_fork_candidates",
                columns: new[] { "TenantScopeKey", "ActorIdentityHash", "CandidateIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_fork_candidates_TenantScopeKey_RetentionKey_I~",
                table: "elsa_activity_fork_candidates",
                columns: new[] { "TenantScopeKey", "RetentionKey", "IdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_fork_receipts_TenantScopeKey_ActorIdentityHas~",
                table: "elsa_activity_fork_receipts",
                columns: new[] { "TenantScopeKey", "ActorIdentityHash", "IdempotencyIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_definitions_TenantId_ValidFromSequ~",
                table: "elsa_activity_management_definitions",
                columns: new[] { "TenantId", "ValidFromSequence", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_definitions_TenantScopeKey_Resourc~",
                table: "elsa_activity_management_definitions",
                columns: new[] { "TenantScopeKey", "ResourceIdIdentityHash", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_drafts_TenantId_ValidFromSequence_~",
                table: "elsa_activity_management_drafts",
                columns: new[] { "TenantId", "ValidFromSequence", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_drafts_TenantScopeKey_ResourceIdId~",
                table: "elsa_activity_management_drafts",
                columns: new[] { "TenantScopeKey", "ResourceIdIdentityHash", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_snapshots_Sequence",
                table: "elsa_activity_management_snapshots",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_versions_TenantId_ValidFromSequenc~",
                table: "elsa_activity_management_versions",
                columns: new[] { "TenantId", "ValidFromSequence", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_management_versions_TenantScopeKey_ResourceId~",
                table: "elsa_activity_management_versions",
                columns: new[] { "TenantScopeKey", "ResourceIdIdentityHash", "ValidToSequenceExclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_upgrade_apply_receipts_TenantScopeKey_PlanIdI~",
                table: "elsa_activity_upgrade_apply_receipts",
                columns: new[] { "TenantScopeKey", "PlanIdIdentityHash", "IdempotencyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_version_layouts_TenantScopeKey_DefinitionVers~",
                table: "elsa_activity_version_layouts",
                columns: new[] { "TenantScopeKey", "DefinitionVersionIdIdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_version_publications_TenantScopeKey_Definiti~1",
                table: "elsa_activity_version_publications",
                columns: new[] { "TenantScopeKey", "DefinitionIdIdentityHash", "IdIdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_activity_version_publications_TenantScopeKey_Definitio~",
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
