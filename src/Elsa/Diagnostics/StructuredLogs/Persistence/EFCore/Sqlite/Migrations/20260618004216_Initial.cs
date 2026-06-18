using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Elsa");

            migrationBuilder.CreateTable(
                name: "StructuredLogEntries",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    EventId = table.Column<int>(type: "INTEGER", nullable: true),
                    EventName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: -1, nullable: false),
                    MessageTemplate = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PropertiesJson = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    ScopesJson = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    ExceptionJson = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StructuredLogEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersistedStructuredLogEntry_Level",
                schema: "Elsa",
                table: "StructuredLogEntries",
                column: "Level");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedStructuredLogEntry_Sequence",
                schema: "Elsa",
                table: "StructuredLogEntries",
                column: "Sequence");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedStructuredLogEntry_Timestamp",
                schema: "Elsa",
                table: "StructuredLogEntries",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StructuredLogEntries",
                schema: "Elsa");
        }
    }
}
