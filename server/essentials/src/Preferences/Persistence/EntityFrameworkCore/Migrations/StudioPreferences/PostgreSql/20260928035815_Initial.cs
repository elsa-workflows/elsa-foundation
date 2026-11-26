using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Migrations.StudioPreferences.PostgreSql
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "__ElsaDatabaseIdentity_ElsaStudioPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaStudioPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaStudioPreferences",
                columns: table => new
                {
                    Family = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
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
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaStudioPreferences", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "elsa_studio_preferences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubjectId = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    StudioHostId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Namespace = table.Column<string>(type: "text", nullable: false),
                    PreferenceSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ValueJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_studio_preferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_ElsaStudioPreferences_SchemaVersion",
                table: "__ElsaDatabaseIdentity_ElsaStudioPreferences",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaStudioPreferences_SchemaVersion",
                table: "__ElsaSchemaFinalization_ElsaStudioPreferences",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_studio_preferences_SchemaVersion",
                table: "elsa_studio_preferences",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__ElsaDatabaseIdentity_ElsaStudioPreferences");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_ElsaStudioPreferences");

            migrationBuilder.DropTable(
                name: "elsa_studio_preferences");
        }
    }
}
