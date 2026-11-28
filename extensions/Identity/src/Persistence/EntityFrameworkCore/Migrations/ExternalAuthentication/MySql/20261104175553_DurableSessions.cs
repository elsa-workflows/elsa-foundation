using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore.Migrations.ExternalAuthentication.MySql
{
    /// <inheritdoc />
    public partial class DurableSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_authentication_sessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    TenantKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    UserKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    CreatedAtMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAtMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    RevokedAtMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    ProtectedPayload = table.Column<string>(type: "longtext", nullable: false),
                    Version = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin"),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_authentication_sessions", x => new { x.TenantKey, x.Id });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_identity_auth_sessions_expiry",
                table: "identity_authentication_sessions",
                columns: new[] { "TenantKey", "ExpiresAtMilliseconds" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_auth_sessions_user",
                table: "identity_authentication_sessions",
                columns: new[] { "TenantKey", "UserKey", "CreatedAtMilliseconds", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_identity_authentication_sessions_SchemaVersion",
                table: "identity_authentication_sessions",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_authentication_sessions");
        }
    }
}
