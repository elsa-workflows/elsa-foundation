using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.Sqlite;

/// <inheritdoc />
public partial class Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "elsa_secrets",
            columns: table => new
            {
                TenantId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                NameSearchKey = table.Column<string>(type: "TEXT", nullable: false),
                DisplayNameSearchKey = table.Column<string>(type: "TEXT", nullable: false),
                TypeNameLookupKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                StoreNameLookupKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                ScopeLookupKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                HasNonExpiringActiveVersion = table.Column<bool>(type: "INTEGER", nullable: false),
                MaxActiveVersionExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                Payload = table.Column<string>(type: "TEXT", nullable: false),
                ConcurrencyToken = table.Column<byte[]>(type: "BLOB", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_elsa_secrets", x => new { x.TenantId, x.NormalizedName });
            });

        migrationBuilder.CreateIndex(
            name: "IX_elsa_secrets_tenantId_status_normalizedName",
            table: "elsa_secrets",
            columns: ["TenantId", "Status", "NormalizedName"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "elsa_secrets");
    }
}
