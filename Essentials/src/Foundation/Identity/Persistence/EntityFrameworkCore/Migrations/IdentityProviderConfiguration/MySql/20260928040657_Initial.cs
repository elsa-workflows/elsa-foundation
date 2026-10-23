using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Migrations.IdentityProviderConfiguration.MySql
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
                name: "__ElsaDatabaseIdentity_ElsaIdentityProviderConfiguration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaIdentityProviderConfiguration", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaIdentityProviderConfiguration",
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
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaIdentityProviderConfiguration", x => x.Family);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "identity_global_provider_configurations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    TenantId = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: true, collation: "utf8mb4_0900_bin"),
                    TenantLookupKey = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: true, collation: "utf8mb4_0900_bin"),
                    Provider = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: false, collation: "utf8mb4_0900_bin"),
                    ProviderLookupKey = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: false, collation: "utf8mb4_0900_bin"),
                    Kind = table.Column<string>(type: "longtext", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupportsLocalUserManagement = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupportsLocalRoleManagement = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupportsApplicationManagement = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupportsGroupSync = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupportsTokenIssuance = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupportsRefresh = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupportsRevocation = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PermissionPropagation = table.Column<int>(type: "int", nullable: false),
                    SettingsJson = table.Column<string>(type: "longtext", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_global_provider_configurations", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "identity_provider_configurations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    TenantId = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: false, collation: "utf8mb4_0900_bin"),
                    TenantLookupKey = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: false, collation: "utf8mb4_0900_bin"),
                    Provider = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: false, collation: "utf8mb4_0900_bin"),
                    ProviderLookupKey = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: false, collation: "utf8mb4_0900_bin"),
                    Kind = table.Column<string>(type: "longtext", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupportsLocalUserManagement = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupportsLocalRoleManagement = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupportsApplicationManagement = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupportsGroupSync = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupportsTokenIssuance = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupportsRefresh = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupportsRevocation = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PermissionPropagation = table.Column<int>(type: "int", nullable: false),
                    SettingsJson = table.Column<string>(type: "longtext", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_provider_configurations", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_ElsaIdentityProviderConfiguration_Sch~",
                table: "__ElsaDatabaseIdentity_ElsaIdentityProviderConfiguration",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaIdentityProviderConfiguration_S~",
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
