using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.Sqlite
{
    /// <inheritdoc />
    public partial class KernelExecutablePins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RuntimeKernelExecutablePins",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ArtifactId = table.Column<string>(type: "TEXT", nullable: true),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: true),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    ReleasedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeKernelExecutablePins", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KernelExecutablePin_Open",
                table: "RuntimeKernelExecutablePins",
                columns: new[] { "ScopeKeyHash", "ReleasedAtUtcTicks", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeKernelExecutablePins_SchemaVersion",
                table: "RuntimeKernelExecutablePins",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuntimeKernelExecutablePins");
        }
    }
}
