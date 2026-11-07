using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.PostgreSql
{
    /// <inheritdoc />
    public partial class RuntimeDeliverySources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_runtime_delivery_work",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "text", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceFamily = table.Column<string>(type: "text", nullable: false),
                    SourceFamilyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceId = table.Column<string>(type: "text", nullable: false),
                    SourceIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true),
                    TenantIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RunKind = table.Column<int>(type: "integer", nullable: false),
                    TestScopeId = table.Column<string>(type: "text", nullable: true),
                    TestScopeIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AuthorityPartitionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FullDiscoveryIdentity = table.Column<string>(type: "text", nullable: false),
                    FullDiscoveryIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceHeaderRevision = table.Column<long>(type: "bigint", nullable: false),
                    SourceHeaderFingerprint = table.Column<string>(type: "text", nullable: false),
                    WorkId = table.Column<string>(type: "text", nullable: false),
                    WorkIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WorkKind = table.Column<string>(type: "text", nullable: false),
                    WorkKindHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    RequiredParticipantVersion = table.Column<int>(type: "integer", nullable: false),
                    ContentFingerprint = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RecordedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ClaimableIsEligible = table.Column<bool>(type: "boolean", nullable: false),
                    ClaimableAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: true),
                    FencingToken = table.Column<long>(type: "bigint", nullable: false),
                    ClaimedAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    VisibleAfterUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    CompletionOperationId = table.Column<string>(type: "text", nullable: true),
                    CompletionOperationFingerprint = table.Column<string>(type: "text", nullable: true),
                    OperationProofJson = table.Column<string>(type: "text", nullable: true),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_delivery_work", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_ingress_source",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "text", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceFamily = table.Column<string>(type: "text", nullable: false),
                    SourceFamilyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceId = table.Column<string>(type: "text", nullable: false),
                    SourceIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true),
                    TenantIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RunKind = table.Column<int>(type: "integer", nullable: false),
                    TestScopeId = table.Column<string>(type: "text", nullable: true),
                    TestScopeIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AuthorityPartitionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FullDiscoveryIdentity = table.Column<string>(type: "text", nullable: false),
                    FullDiscoveryIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HeaderVersion = table.Column<int>(type: "integer", nullable: false),
                    HeaderRevision = table.Column<long>(type: "bigint", nullable: false),
                    RequiredParticipantVersion = table.Column<int>(type: "integer", nullable: false),
                    HeaderFingerprint = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RecordedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    AcceptanceOperationId = table.Column<string>(type: "text", nullable: false),
                    AcceptanceOperationFingerprint = table.Column<string>(type: "text", nullable: false),
                    OperationProofJson = table.Column<string>(type: "text", nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_ingress_source", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_delivery_work_FullDiscoveryIdentityHash_Source~",
                table: "elsa_runtime_delivery_work",
                columns: new[] { "FullDiscoveryIdentityHash", "SourceFamilyHash", "WorkKindHash", "Role", "ClaimableIsEligible", "ClaimableAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_delivery_work_SchemaVersion",
                table: "elsa_runtime_delivery_work",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_delivery_work_ScopeKeyHash_SourceFamilyHash_So~",
                table: "elsa_runtime_delivery_work",
                columns: new[] { "ScopeKeyHash", "SourceFamilyHash", "SourceIdHash", "WorkIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_ingress_source_FullDiscoveryIdentityHash_Sourc~",
                table: "elsa_runtime_ingress_source",
                columns: new[] { "FullDiscoveryIdentityHash", "SourceFamilyHash", "Status", "RecordedAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_ingress_source_SchemaVersion",
                table: "elsa_runtime_ingress_source",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_ingress_source_ScopeKeyHash_SourceFamilyHash_S~",
                table: "elsa_runtime_ingress_source",
                columns: new[] { "ScopeKeyHash", "SourceFamilyHash", "SourceIdHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_runtime_delivery_work");

            migrationBuilder.DropTable(
                name: "elsa_runtime_ingress_source");
        }
    }
}
