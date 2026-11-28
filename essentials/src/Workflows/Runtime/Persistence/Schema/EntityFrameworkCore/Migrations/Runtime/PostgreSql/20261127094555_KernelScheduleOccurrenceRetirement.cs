using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.PostgreSql
{
    /// <inheritdoc />
    public partial class KernelScheduleOccurrenceRetirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_KernelScheduleOccurrence_Open",
                table: "elsa_runtime_kernel_schedule_occurrence",
                columns: new[] { "ScopeKeyHash", "TenantKeyHash", "RetiredAtUtcTicks", "AcceptedAtUtcTicks", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KernelScheduleOccurrence_Open",
                table: "elsa_runtime_kernel_schedule_occurrence");
        }
    }
}
