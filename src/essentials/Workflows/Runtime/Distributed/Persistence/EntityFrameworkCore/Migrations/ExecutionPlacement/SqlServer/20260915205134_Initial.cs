using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Migrations.ExecutionPlacement.SqlServer
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_distributed_execution_placement",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<byte[]>(type: "varbinary(258)", maxLength: 258, nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OwnerIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PlacementToken = table.Column<long>(type: "bigint", nullable: false),
                    AcquiredAt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    IsReleased = table.Column<bool>(type: "bit", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_distributed_execution_placement", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_distributed_execution_placement_ScopeKeyHash_OwnerIdHash_IsReleased_ExpiresAtUtcTicks_WorkflowExecutionIdOrderKey",
                table: "elsa_distributed_execution_placement",
                columns: new[] { "ScopeKeyHash", "OwnerIdHash", "IsReleased", "ExpiresAtUtcTicks", "WorkflowExecutionIdOrderKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_distributed_execution_placement");
        }
    }
}
