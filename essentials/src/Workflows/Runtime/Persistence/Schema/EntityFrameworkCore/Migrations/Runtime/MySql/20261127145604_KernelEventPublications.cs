using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.MySql
{
    /// <inheritdoc />
    public partial class KernelEventPublications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_runtime_kernel_event_publication",
                columns: table => new
                {
                    SlotHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ScopeHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TenantHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ArtifactId = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Current = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Applied = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_kernel_event_publication", x => new { x.ScopeHash, x.SlotHash, x.Revision });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_kernel_event_publication_SchemaVersion",
                table: "elsa_runtime_kernel_event_publication",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_kernel_event_publication_ScopeHash_SlotHash_Cur~",
                table: "elsa_runtime_kernel_event_publication",
                columns: new[] { "ScopeHash", "SlotHash", "Current" });

            migrationBuilder.CreateIndex(
                name: "IX_KernelEventPublication_Apply",
                table: "elsa_runtime_kernel_event_publication",
                columns: new[] { "ScopeHash", "TenantHash", "Current", "Applied", "SlotHash", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_KernelEventPublication_Retain",
                table: "elsa_runtime_kernel_event_publication",
                columns: new[] { "ScopeHash", "TenantHash", "Current", "SlotHash", "Revision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_runtime_kernel_event_publication");
        }
    }
}
