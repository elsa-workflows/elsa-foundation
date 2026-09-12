using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Persistence.EntityFrameworkCore.MySql.FeasibilityTests.Migrations
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
                name: "elsa_secrets",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    NormalizedName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    NameSearchKey = table.Column<string>(type: "longtext", nullable: false),
                    DisplayNameSearchKey = table.Column<string>(type: "longtext", nullable: false),
                    TypeNameLookupKey = table.Column<string>(type: "longtext", nullable: false),
                    StoreNameLookupKey = table.Column<string>(type: "longtext", nullable: false),
                    ScopeLookupKey = table.Column<string>(type: "longtext", nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    HasNonExpiringActiveVersion = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MaxActiveVersionExpiresAt = table.Column<long>(type: "bigint", nullable: true),
                    Payload = table.Column<string>(type: "json", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "varbinary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_secrets", x => new { x.TenantId, x.NormalizedName });
                })
                .Annotation("MySQL:Charset", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_0900_bin");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_secrets_tenantId_status_normalizedName",
                table: "elsa_secrets",
                columns: new[] { "TenantId", "Status", "NormalizedName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_secrets");
        }
    }
}
