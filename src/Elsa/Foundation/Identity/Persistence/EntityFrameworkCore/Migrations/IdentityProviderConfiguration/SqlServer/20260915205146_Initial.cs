using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Migrations.IdentityProviderConfiguration.SqlServer
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
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: true, collation: "Latin1_General_BIN2"),
                    TenantLookupKey = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: true, collation: "Latin1_General_BIN2"),
                    Provider = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: false, collation: "Latin1_General_BIN2"),
                    ProviderLookupKey = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: false, collation: "Latin1_General_BIN2"),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    SupportsLocalUserManagement = table.Column<bool>(type: "bit", nullable: false),
                    SupportsLocalRoleManagement = table.Column<bool>(type: "bit", nullable: false),
                    SupportsApplicationManagement = table.Column<bool>(type: "bit", nullable: false),
                    SupportsGroupSync = table.Column<bool>(type: "bit", nullable: false),
                    SupportsTokenIssuance = table.Column<bool>(type: "bit", nullable: false),
                    SupportsRefresh = table.Column<bool>(type: "bit", nullable: false),
                    SupportsRevocation = table.Column<bool>(type: "bit", nullable: false),
                    PermissionPropagation = table.Column<int>(type: "int", nullable: false),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: false, collation: "Latin1_General_BIN2"),
                    TenantLookupKey = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: false, collation: "Latin1_General_BIN2"),
                    Provider = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: false, collation: "Latin1_General_BIN2"),
                    ProviderLookupKey = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: false, collation: "Latin1_General_BIN2"),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    SupportsLocalUserManagement = table.Column<bool>(type: "bit", nullable: false),
                    SupportsLocalRoleManagement = table.Column<bool>(type: "bit", nullable: false),
                    SupportsApplicationManagement = table.Column<bool>(type: "bit", nullable: false),
                    SupportsGroupSync = table.Column<bool>(type: "bit", nullable: false),
                    SupportsTokenIssuance = table.Column<bool>(type: "bit", nullable: false),
                    SupportsRefresh = table.Column<bool>(type: "bit", nullable: false),
                    SupportsRevocation = table.Column<bool>(type: "bit", nullable: false),
                    PermissionPropagation = table.Column<int>(type: "int", nullable: false),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
