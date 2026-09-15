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
                name: "elsa_studio_preferences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SubjectId = table.Column<string>(type: "longtext", nullable: false),
                    TenantId = table.Column<string>(type: "longtext", nullable: false),
                    StudioHostId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Namespace = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    ValueJson = table.Column<string>(type: "longtext", nullable: false),
                    UpdatedAt = table.Column<string>(type: "varchar(64)", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_studio_preferences", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_studio_preferences");
        }
    }
}
