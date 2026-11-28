using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.PostgreSql
{
    /// <inheritdoc />
    public partial class KernelEventPublicationOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "InsertionOrdinal",
                table: "elsa_runtime_kernel_event_publication",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KernelEventPublication_ApplyLegacy",
                table: "elsa_runtime_kernel_event_publication",
                columns: new[] { "ScopeHash", "TenantHash", "InsertionOrdinal", "Current", "Applied", "SlotHash", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_KernelEventPublication_ApplyScan",
                table: "elsa_runtime_kernel_event_publication",
                columns: new[] { "ScopeHash", "TenantHash", "Current", "Applied", "InsertionOrdinal" });

            migrationBuilder.CreateIndex(
                name: "IX_KernelEventPublication_RetainLegacy",
                table: "elsa_runtime_kernel_event_publication",
                columns: new[] { "ScopeHash", "TenantHash", "InsertionOrdinal", "Current", "SlotHash", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_KernelEventPublication_RetainScan",
                table: "elsa_runtime_kernel_event_publication",
                columns: new[] { "ScopeHash", "TenantHash", "Current", "InsertionOrdinal" });

            migrationBuilder.CreateIndex(
                name: "UX_KernelEventPublication_InsertionOrdinal",
                table: "elsa_runtime_kernel_event_publication",
                columns: new[] { "ScopeHash", "TenantHash", "InsertionOrdinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KernelEventPublication_ApplyLegacy",
                table: "elsa_runtime_kernel_event_publication");

            migrationBuilder.DropIndex(
                name: "IX_KernelEventPublication_ApplyScan",
                table: "elsa_runtime_kernel_event_publication");

            migrationBuilder.DropIndex(
                name: "IX_KernelEventPublication_RetainLegacy",
                table: "elsa_runtime_kernel_event_publication");

            migrationBuilder.DropIndex(
                name: "IX_KernelEventPublication_RetainScan",
                table: "elsa_runtime_kernel_event_publication");

            migrationBuilder.DropIndex(
                name: "UX_KernelEventPublication_InsertionOrdinal",
                table: "elsa_runtime_kernel_event_publication");

            migrationBuilder.DropColumn(
                name: "InsertionOrdinal",
                table: "elsa_runtime_kernel_event_publication");
        }
    }
}
