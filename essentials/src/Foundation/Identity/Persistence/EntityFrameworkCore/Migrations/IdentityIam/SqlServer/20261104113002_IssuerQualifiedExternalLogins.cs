using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Migrations.IdentityIam.SqlServer;

/// <inheritdoc />
public partial class IssuerQualifiedExternalLogins : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "Issuer", table: "identity_external_logins", type: "nvarchar(max)", nullable: true, collation: "Latin1_General_100_BIN2");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Issuer", table: "identity_external_logins");
}
