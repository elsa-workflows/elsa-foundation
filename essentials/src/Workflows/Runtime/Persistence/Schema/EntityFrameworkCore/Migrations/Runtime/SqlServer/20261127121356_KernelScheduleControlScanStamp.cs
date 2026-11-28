using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.SqlServer
{
    /// <inheritdoc />
    public partial class KernelScheduleControlScanStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SchemaVersion",
                table: "elsa_runtime_kernel_schedule_control_scan",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "1.0.0");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_kernel_schedule_control_scan_SchemaVersion",
                table: "elsa_runtime_kernel_schedule_control_scan",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_elsa_runtime_kernel_schedule_control_scan_SchemaVersion",
                table: "elsa_runtime_kernel_schedule_control_scan");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "elsa_runtime_kernel_schedule_control_scan");
        }
    }
}
