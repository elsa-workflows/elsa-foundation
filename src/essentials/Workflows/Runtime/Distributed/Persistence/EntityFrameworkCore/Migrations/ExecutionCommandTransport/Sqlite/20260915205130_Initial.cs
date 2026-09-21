using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Migrations.ExecutionCommandTransport.Sqlite
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_distributed_command_stream_head",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<byte[]>(type: "BLOB", maxLength: 258, nullable: false),
                    LastSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    PendingCount = table.Column<long>(type: "INTEGER", nullable: false),
                    PendingVisibleAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    PendingSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_distributed_command_stream_head", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_distributed_command_transport",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TransportItemId = table.Column<string>(type: "TEXT", maxLength: 414, nullable: false),
                    TransportItemIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    EnqueuedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    EnqueuedAtOffsetMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    VisibleAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    LeaseOwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LeaseToken = table.Column<long>(type: "INTEGER", nullable: false),
                    LeaseExpiresAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    LeaseExpiresAtOffsetMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_distributed_command_transport", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_distributed_command_stream_head_ScopeKeyHash_PendingVisibleAtUtcTicks_WorkflowExecutionIdOrderKey_Id",
                table: "elsa_distributed_command_stream_head",
                columns: new[] { "ScopeKeyHash", "PendingVisibleAtUtcTicks", "WorkflowExecutionIdOrderKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_distributed_command_stream_head_ScopeKeyHash_WorkflowExecutionIdHash",
                table: "elsa_distributed_command_stream_head",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_distributed_command_stream_head_ScopeKeyHash_WorkflowExecutionIdHash_PendingCount",
                table: "elsa_distributed_command_stream_head",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "PendingCount" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_distributed_command_transport_ScopeKeyHash_WorkflowExecutionIdHash_Sequence",
                table: "elsa_distributed_command_transport",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_distributed_command_transport_ScopeKeyHash_WorkflowExecutionIdHash_VisibleAtUtcTicks_Sequence_TransportItemIdHash",
                table: "elsa_distributed_command_transport",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "VisibleAtUtcTicks", "Sequence", "TransportItemIdHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_distributed_command_stream_head");

            migrationBuilder.DropTable(
                name: "elsa_distributed_command_transport");
        }
    }
}
