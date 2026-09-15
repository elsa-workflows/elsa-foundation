using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Migrations.ExecutionCommandTransport.MySql
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_distributed_command_stream_head",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "longtext", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<byte[]>(type: "varbinary(258)", maxLength: 258, nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    PendingCount = table.Column<long>(type: "bigint", nullable: false),
                    PendingVisibleAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    PendingSequence = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_distributed_command_stream_head", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_distributed_command_transport",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "longtext", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TransportItemId = table.Column<string>(type: "varchar(414)", maxLength: 414, nullable: false),
                    TransportItemIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    EnqueuedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    EnqueuedAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    VisibleAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    LeaseOwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    LeaseToken = table.Column<long>(type: "bigint", nullable: false),
                    LeaseExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    LeaseExpiresAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_distributed_command_transport", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_distributed_command_stream_head_ScopeKeyHash_PendingVis~",
                table: "elsa_distributed_command_stream_head",
                columns: new[] { "ScopeKeyHash", "PendingVisibleAtUtcTicks", "WorkflowExecutionIdOrderKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_distributed_command_stream_head_ScopeKeyHash_WorkflowE~1",
                table: "elsa_distributed_command_stream_head",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "PendingCount" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_distributed_command_stream_head_ScopeKeyHash_WorkflowEx~",
                table: "elsa_distributed_command_stream_head",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_distributed_command_transport_ScopeKeyHash_WorkflowExe~1",
                table: "elsa_distributed_command_transport",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "VisibleAtUtcTicks", "Sequence", "TransportItemIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_distributed_command_transport_ScopeKeyHash_WorkflowExec~",
                table: "elsa_distributed_command_transport",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "Sequence" },
                unique: true);
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
