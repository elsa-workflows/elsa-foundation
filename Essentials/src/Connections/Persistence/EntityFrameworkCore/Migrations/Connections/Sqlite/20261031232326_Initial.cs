using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Connections.Persistence.EntityFrameworkCore.Migrations.Connections.Sqlite
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "__ElsaDatabaseIdentity_ElsaConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaConnections",
                columns: table => new
                {
                    Family = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    FinalizedVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IntentJson = table.Column<string>(type: "TEXT", nullable: true),
                    HoldsJson = table.Column<string>(type: "TEXT", nullable: false),
                    HistoryJson = table.Column<string>(type: "TEXT", nullable: false),
                    FinishJson = table.Column<string>(type: "TEXT", nullable: true),
                    FinishHistoryJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaConnections", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "elsa_connections",
                columns: table => new
                {
                    TenantLookupKey = table.Column<string>(type: "TEXT", maxLength: 257, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    NameSearchKey = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayNameSearchKey = table.Column<string>(type: "TEXT", nullable: false),
                    TypeLookupKey = table.Column<string>(type: "TEXT", nullable: false),
                    SchemeLookupKey = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_connections", x => new { x.TenantLookupKey, x.NormalizedName });
                });

            migrationBuilder.CreateTable(
                name: "elsa_connection_secret_references",
                columns: table => new
                {
                    TenantLookupKey = table.Column<string>(type: "TEXT", maxLength: 257, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SecretLookupHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_connection_secret_references", x => new { x.TenantLookupKey, x.NormalizedName, x.SecretLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_connection_secret_references_elsa_connections_TenantLookupKey_NormalizedName",
                        columns: x => new { x.TenantLookupKey, x.NormalizedName },
                        principalTable: "elsa_connections",
                        principalColumns: new[] { "TenantLookupKey", "NormalizedName" },
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_elsa_connection_secret_references_TenantLookupKey_SecretLookupHash",
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
