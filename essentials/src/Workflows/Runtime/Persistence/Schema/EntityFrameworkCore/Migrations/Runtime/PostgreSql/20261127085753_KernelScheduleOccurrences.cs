using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.PostgreSql
{
    /// <inheritdoc />
    public partial class KernelScheduleOccurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_runtime_kernel_schedule_occurrence",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "text", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantKey = table.Column<string>(type: "text", nullable: false),
                    TenantKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ArtifactId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    AcceptedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ActorReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RetiredAtUtcTicks = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_kernel_schedule_occurrence", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_kernel_schedule_occurrence_SchemaVersion",
                table: "elsa_runtime_kernel_schedule_occurrence",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_KernelScheduleOccurrence_ArtifactPin",
                table: "elsa_runtime_kernel_schedule_occurrence",
                columns: new[] { "ScopeKeyHash", "RetiredAtUtcTicks", "ArtifactId" });

            migrationBuilder.CreateIndex(
                name: "IX_KernelScheduleOccurrence_Pending",
                table: "elsa_runtime_kernel_schedule_occurrence",
                columns: new[] { "ScopeKeyHash", "TenantKeyHash", "ActorReference", "AcceptedAtUtcTicks", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_runtime_kernel_schedule_occurrence");
        }
    }
}
