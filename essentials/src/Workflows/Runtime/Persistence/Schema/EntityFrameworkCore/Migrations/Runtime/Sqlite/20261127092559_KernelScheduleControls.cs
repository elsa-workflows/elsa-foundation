using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.Sqlite
{
    /// <inheritdoc />
    public partial class KernelScheduleControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_runtime_kernel_schedule_control",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TenantKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OwnerSlotHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_kernel_schedule_control", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_kernel_schedule_control_SchemaVersion",
                table: "elsa_runtime_kernel_schedule_control",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_KernelScheduleControl_Owner",
                table: "elsa_runtime_kernel_schedule_control",
                columns: new[] { "ScopeKeyHash", "OwnerSlotHash" });

            migrationBuilder.CreateIndex(
                name: "IX_KernelScheduleControl_Tenant",
                table: "elsa_runtime_kernel_schedule_control",
                columns: new[] { "ScopeKeyHash", "TenantKeyHash", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_runtime_kernel_schedule_control");
        }
    }
}
