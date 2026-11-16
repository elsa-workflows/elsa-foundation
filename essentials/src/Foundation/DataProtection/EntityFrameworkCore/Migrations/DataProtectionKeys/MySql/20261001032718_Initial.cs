using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore.Migrations.DataProtectionKeys.MySql
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
                name: "__ElsaDatabaseIdentity_ElsaDataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaDataProtectionKeys", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaDataProtectionKeys",
                columns: table => new
                {
                    Family = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
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
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaDataProtectionKeys", x => x.Family);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_data_protection_keys",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    FriendlyName = table.Column<string>(type: "longtext", nullable: true),
                    Xml = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_data_protection_keys", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_ElsaDataProtectionKeys_SchemaVersion",
                table: "__ElsaDatabaseIdentity_ElsaDataProtectionKeys",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaDataProtectionKeys_SchemaVersion",
                table: "__ElsaSchemaFinalization_ElsaDataProtectionKeys",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_data_protection_keys_SchemaVersion",
                table: "elsa_data_protection_keys",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__ElsaDatabaseIdentity_ElsaDataProtectionKeys");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_ElsaDataProtectionKeys");

            migrationBuilder.DropTable(
                name: "elsa_data_protection_keys");
        }
    }
}
