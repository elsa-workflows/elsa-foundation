using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Migrations.StudioPreferences.MySql
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
                name: "__ElsaDatabaseIdentity_ElsaStudioPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaStudioPreferences", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaStudioPreferences",
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
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaStudioPreferences", x => x.Family);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_studio_preferences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SubjectId = table.Column<string>(type: "longtext", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: false),
                    StudioHostId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Namespace = table.Column<string>(type: "longtext", nullable: false),
                    PreferenceSchemaVersion = table.Column<int>(type: "int", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ValueJson = table.Column<string>(type: "longtext", nullable: false),
                    UpdatedAt = table.Column<string>(type: "varchar(64)", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_studio_preferences", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

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
