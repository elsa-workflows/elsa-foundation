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
                name: "identity_global_provider_configurations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: true),
                    TenantLookupKey = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: true),
                    Provider = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: false),
                    ProviderLookupKey = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: false),
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
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_global_provider_configurations", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateTable(
                name: "identity_provider_configurations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: false),
                    TenantLookupKey = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: false),
                    Provider = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: false),
                    ProviderLookupKey = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: false),
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
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_provider_configurations", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_global_provider_configurations");

            migrationBuilder.DropTable(
                name: "identity_provider_configurations");
        }
    }
}
