using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore.Migrations.ExternalAuthentication.PostgreSql
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "__ElsaDatabaseIdentity_ElsaIdentityExternalAuthentication",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaIdentityExternalAuthentication", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaIdentityExternalAuthentication",
                columns: table => new
                {
                    Family = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaIdentityExternalAuthentication", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "identity_authentication_artifacts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    TenantKey = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    BrowserBinding = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    AdministratorBinding = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: true, collation: "C"),
                    CreatedAtMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAtMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    ProtectedPayload = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_authentication_artifacts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_ElsaIdentityExternalAuthentication_S~",
                table: "__ElsaDatabaseIdentity_ElsaIdentityExternalAuthentication",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaIdentityExternalAuthentication~",
                table: "__ElsaSchemaFinalization_ElsaIdentityExternalAuthentication",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "ix_identity_auth_artifacts_expiry",
                table: "identity_authentication_artifacts",
                columns: new[] { "TenantKey", "ExpiresAtMilliseconds" });

            migrationBuilder.CreateIndex(
                name: "IX_identity_authentication_artifacts_SchemaVersion",
                table: "identity_authentication_artifacts",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__ElsaDatabaseIdentity_ElsaIdentityExternalAuthentication");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_ElsaIdentityExternalAuthentication");

            migrationBuilder.DropTable(
                name: "identity_authentication_artifacts");
        }
    }
}
