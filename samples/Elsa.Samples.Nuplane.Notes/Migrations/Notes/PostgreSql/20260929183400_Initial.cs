using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Samples.Nuplane.Notes.Migrations.Notes.PostgreSql
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "__ElsaDatabaseIdentity_ElsaSamplesNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaSamplesNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaSamplesNotes",
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
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaSamplesNotes", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "elsa_samples_notes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_samples_notes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_ElsaSamplesNotes_SchemaVersion",
                table: "__ElsaDatabaseIdentity_ElsaSamplesNotes",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaSamplesNotes_SchemaVersion",
                table: "__ElsaSchemaFinalization_ElsaSamplesNotes",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_samples_notes_SchemaVersion",
                table: "elsa_samples_notes",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__ElsaDatabaseIdentity_ElsaSamplesNotes");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_ElsaSamplesNotes");

            migrationBuilder.DropTable(
                name: "elsa_samples_notes");
        }
    }
}
