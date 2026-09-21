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
                    Revision = table.Column<long>(type: "bigint", nullable: false)
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
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_provider_configurations", x => x.Id);
                });
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
