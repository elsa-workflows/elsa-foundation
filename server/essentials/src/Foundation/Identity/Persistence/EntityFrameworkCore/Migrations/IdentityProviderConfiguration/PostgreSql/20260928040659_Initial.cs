using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Migrations.IdentityProviderConfiguration.PostgreSql
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "__ElsaDatabaseIdentity_ElsaIdentityProviderConfiguration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaIdentityProviderConfiguration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaIdentityProviderConfiguration",
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
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaIdentityProviderConfiguration", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "identity_global_provider_configurations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    TenantId = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: true, collation: "C"),
                    TenantLookupKey = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: true, collation: "C"),
                    Provider = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: false, collation: "C"),
                    ProviderLookupKey = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: false, collation: "C"),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsLocalUserManagement = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsLocalRoleManagement = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsApplicationManagement = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsGroupSync = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsTokenIssuance = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsRefresh = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsRevocation = table.Column<bool>(type: "boolean", nullable: false),
                    PermissionPropagation = table.Column<int>(type: "integer", nullable: false),
                    SettingsJson = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_global_provider_configurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_provider_configurations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    TenantId = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: false, collation: "C"),
                    TenantLookupKey = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: false, collation: "C"),
                    Provider = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: false, collation: "C"),
                    ProviderLookupKey = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: false, collation: "C"),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsLocalUserManagement = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsLocalRoleManagement = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsApplicationManagement = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsGroupSync = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsTokenIssuance = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsRefresh = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsRevocation = table.Column<bool>(type: "boolean", nullable: false),
                    PermissionPropagation = table.Column<int>(type: "integer", nullable: false),
                    SettingsJson = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_provider_configurations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_ElsaIdentityProviderConfiguration_Sc~",
                table: "__ElsaDatabaseIdentity_ElsaIdentityProviderConfiguration",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaIdentityProviderConfiguration_~",
                table: "__ElsaSchemaFinalization_ElsaIdentityProviderConfiguration",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_identity_global_provider_configurations_SchemaVersion",
                table: "identity_global_provider_configurations",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_identity_provider_configurations_SchemaVersion",
                table: "identity_provider_configurations",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__ElsaDatabaseIdentity_ElsaIdentityProviderConfiguration");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_ElsaIdentityProviderConfiguration");

            migrationBuilder.DropTable(
                name: "identity_global_provider_configurations");

            migrationBuilder.DropTable(
                name: "identity_provider_configurations");
        }
    }
}
