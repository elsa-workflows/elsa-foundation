using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.SqlServer;

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
                TenantId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_BIN2"),
                NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_BIN2"),
                NameSearchKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                DisplayNameSearchKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                TypeNameLookupKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                StoreNameLookupKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ScopeLookupKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                HasNonExpiringActiveVersion = table.Column<bool>(type: "bit", nullable: false),
                MaxActiveVersionExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: false)
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
