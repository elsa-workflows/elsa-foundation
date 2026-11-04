using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Migrations.IdentityIam.MySql;

/// <inheritdoc />
public partial class IssuerQualifiedExternalLogins : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "Issuer", table: "identity_external_logins", type: "longtext", nullable: true, collation: "utf8mb4_0900_bin");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Issuer", table: "identity_external_logins");
}
