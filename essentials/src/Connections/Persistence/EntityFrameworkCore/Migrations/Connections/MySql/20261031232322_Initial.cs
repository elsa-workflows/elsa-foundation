using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Connections.Persistence.EntityFrameworkCore.Migrations.Connections.MySql
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
                name: "__ElsaDatabaseIdentity_ElsaConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaConnections", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaConnections",
                columns: table => new
                {
                    Family = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin"),
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
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaConnections", x => x.Family);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_connections",
                columns: table => new
                {
                    TenantLookupKey = table.Column<string>(type: "varchar(257)", maxLength: 257, nullable: false, collation: "utf8mb4_0900_bin"),
                    NormalizedName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    TenantId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_0900_bin"),
                    NameSearchKey = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_0900_bin"),
                    DisplayNameSearchKey = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_0900_bin"),
                    TypeLookupKey = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemeLookupKey = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_0900_bin"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Payload = table.Column<string>(type: "json", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_connections", x => new { x.TenantLookupKey, x.NormalizedName });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_connection_secret_references",
                columns: table => new
                {
                    TenantLookupKey = table.Column<string>(type: "varchar(257)", maxLength: 257, nullable: false, collation: "utf8mb4_0900_bin"),
                    NormalizedName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin"),
                    SecretLookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_0900_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_connection_secret_references", x => new { x.TenantLookupKey, x.NormalizedName, x.SecretLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_connection_secret_references_elsa_connections_TenantLoo~",
                        columns: x => new { x.TenantLookupKey, x.NormalizedName },
                        principalTable: "elsa_connections",
                        principalColumns: new[] { "TenantLookupKey", "NormalizedName" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_ElsaConnections_SchemaVersion",
                table: "__ElsaDatabaseIdentity_ElsaConnections",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaConnections_SchemaVersion",
                table: "__ElsaSchemaFinalization_ElsaConnections",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_connection_secret_references_SchemaVersion",
                table: "elsa_connection_secret_references",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_connection_secret_references_TenantLookupKey_SecretLook~",
                table: "elsa_connection_secret_references",
                columns: new[] { "TenantLookupKey", "SecretLookupHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_connections_SchemaVersion",
                table: "elsa_connections",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__ElsaDatabaseIdentity_ElsaConnections");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_ElsaConnections");

            migrationBuilder.DropTable(
                name: "elsa_connection_secret_references");

            migrationBuilder.DropTable(
                name: "elsa_connections");
        }
    }
}
