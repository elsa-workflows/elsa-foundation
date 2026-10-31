using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Connections.Persistence.EntityFrameworkCore.Migrations.Connections.PostgreSql
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
                    Id = table.Column<int>(type: "integer", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaConnections",
                columns: table => new
                {
                    Family = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    DatabaseIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    FinalizedVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IntentJson = table.Column<string>(type: "text", nullable: true),
                    HoldsJson = table.Column<string>(type: "text", nullable: false),
                    HistoryJson = table.Column<string>(type: "text", nullable: false),
                    FinishJson = table.Column<string>(type: "text", nullable: true),
                    FinishHistoryJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaConnections", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "elsa_connections",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    NameSearchKey = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    DisplayNameSearchKey = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    TypeLookupKey = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    SchemeLookupKey = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_connections", x => new { x.TenantId, x.NormalizedName });
                });

            migrationBuilder.CreateTable(
                name: "elsa_connection_secret_references",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    SecretLookupHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_connection_secret_references", x => new { x.TenantId, x.NormalizedName, x.SecretLookupHash });
                    table.ForeignKey(
                        name: "FK_elsa_connection_secret_references_elsa_connections_TenantId~",
                        columns: x => new { x.TenantId, x.NormalizedName },
                        principalTable: "elsa_connections",
                        principalColumns: new[] { "TenantId", "NormalizedName" },
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
                name: "IX_elsa_connection_secret_references_TenantId_SecretLookupHash",
                table: "elsa_connection_secret_references",
                columns: new[] { "TenantId", "SecretLookupHash" });

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
