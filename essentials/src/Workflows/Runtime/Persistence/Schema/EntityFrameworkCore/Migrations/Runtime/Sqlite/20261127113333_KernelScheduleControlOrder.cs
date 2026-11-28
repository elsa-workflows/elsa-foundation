using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.Sqlite
{
    /// <inheritdoc />
    public partial class KernelScheduleControlOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "InsertionOrdinal",
                table: "elsa_runtime_kernel_schedule_control",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "elsa_runtime_kernel_schedule_control_scan",
                columns: table => new
                {
                    ScopeKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TenantKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LastOrdinal = table.Column<long>(type: "INTEGER", nullable: false),
                    Pass = table.Column<long>(type: "INTEGER", nullable: false),
                    Through = table.Column<long>(type: "INTEGER", nullable: true),
                    AfterOrdinal = table.Column<long>(type: "INTEGER", nullable: false),
                    AfterId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_kernel_schedule_control_scan", x => new { x.ScopeKeyHash, x.TenantKeyHash });
                });

            migrationBuilder.CreateIndex(
                name: "IX_KernelScheduleControl_Scan",
                table: "elsa_runtime_kernel_schedule_control",
                columns: new[] { "ScopeKeyHash", "TenantKeyHash", "InsertionOrdinal", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_runtime_kernel_schedule_control_scan");

            migrationBuilder.DropIndex(
                name: "IX_KernelScheduleControl_Scan",
                table: "elsa_runtime_kernel_schedule_control");

            migrationBuilder.DropColumn(
                name: "InsertionOrdinal",
                table: "elsa_runtime_kernel_schedule_control");
        }
    }
}
