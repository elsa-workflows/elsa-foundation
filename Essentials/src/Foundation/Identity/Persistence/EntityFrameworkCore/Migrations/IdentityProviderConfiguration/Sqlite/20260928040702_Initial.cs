using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Migrations.IdentityProviderConfiguration.Sqlite
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaIdentityProviderConfiguration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaIdentityProviderConfiguration",
                columns: table => new
                {
                    Family = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    FinalizedVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IntentJson = table.Column<string>(type: "TEXT", nullable: true),
                    HoldsJson = table.Column<string>(type: "TEXT", nullable: false),
                    HistoryJson = table.Column<string>(type: "TEXT", nullable: false),
                    FinishJson = table.Column<string>(type: "TEXT", nullable: true),
                    FinishHistoryJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaIdentityProviderConfiguration", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "identity_global_provider_configurations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 1600, nullable: true),
                    TenantLookupKey = table.Column<string>(type: "TEXT", maxLength: 1600, nullable: true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 1600, nullable: false),
                    ProviderLookupKey = table.Column<string>(type: "TEXT", maxLength: 1600, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsLocalUserManagement = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsLocalRoleManagement = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsApplicationManagement = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsGroupSync = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsTokenIssuance = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsRefresh = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsRevocation = table.Column<bool>(type: "INTEGER", nullable: false),
                    PermissionPropagation = table.Column<int>(type: "INTEGER", nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_global_provider_configurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_provider_configurations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 1600, nullable: false),
                    TenantLookupKey = table.Column<string>(type: "TEXT", maxLength: 1600, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 1600, nullable: false),
                    ProviderLookupKey = table.Column<string>(type: "TEXT", maxLength: 1600, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsLocalUserManagement = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsLocalRoleManagement = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsApplicationManagement = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsGroupSync = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsTokenIssuance = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsRefresh = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsRevocation = table.Column<bool>(type: "INTEGER", nullable: false),
                    PermissionPropagation = table.Column<int>(type: "INTEGER", nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_provider_configurations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_ElsaIdentityProviderConfiguration_SchemaVersion",
                table: "__ElsaDatabaseIdentity_ElsaIdentityProviderConfiguration",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaIdentityProviderConfiguration_SchemaVersion",
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
