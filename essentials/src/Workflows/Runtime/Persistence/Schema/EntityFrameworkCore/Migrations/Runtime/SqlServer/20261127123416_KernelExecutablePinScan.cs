using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.SqlServer
{
    /// <inheritdoc />
    public partial class KernelExecutablePinScan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "InsertionOrdinal",
                table: "RuntimeKernelExecutablePins",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "elsa_runtime_kernel_executable_pin_scan",
                columns: table => new
                {
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LastOrdinal = table.Column<long>(type: "bigint", nullable: false),
                    Pass = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Through = table.Column<long>(type: "bigint", nullable: true),
                    AfterOrdinal = table.Column<long>(type: "bigint", nullable: false),
                    AfterId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_kernel_executable_pin_scan", x => x.ScopeKeyHash);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KernelExecutablePin_Scan",
                table: "RuntimeKernelExecutablePins",
                columns: new[] { "ScopeKeyHash", "ReleasedAtUtcTicks", "InsertionOrdinal", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_kernel_executable_pin_scan_SchemaVersion",
                table: "elsa_runtime_kernel_executable_pin_scan",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_runtime_kernel_executable_pin_scan");

            migrationBuilder.DropIndex(
                name: "IX_KernelExecutablePin_Scan",
                table: "RuntimeKernelExecutablePins");

            migrationBuilder.DropColumn(
                name: "InsertionOrdinal",
                table: "RuntimeKernelExecutablePins");
        }
    }
}
