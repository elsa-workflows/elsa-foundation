using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.SqlServer
{
    /// <inheritdoc />
    public partial class TimerHistoryOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimerHistoryOwnerJson",
                table: "elsa_runtime_execution_liveness_state",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HistoryPending",
                table: "elsa_runtime_durable_timer",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HistoryRetired",
                table: "elsa_runtime_durable_timer",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "HistoryRevision",
                table: "elsa_runtime_durable_timer",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_runtime_timer_history_pending",
                table: "elsa_runtime_durable_timer",
                columns: new[] { "ScopeKeyHash", "HistoryPending", "WorkflowExecutionIdHash", "TimerIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_timer_live_claim",
                table: "elsa_runtime_durable_timer",
                columns: new[] { "ScopeKeyHash", "HistoryRetired", "ClaimOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_timer_live_due",
                table: "elsa_runtime_durable_timer",
                columns: new[] { "ScopeKeyHash", "HistoryRetired", "DueTimeUtcTicks", "TimerIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_timer_run_history_active",
                table: "elsa_runtime_durable_timer",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "HistoryRetired", "HistoryRevision", "TimerIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_timer_run_history_pending",
                table: "elsa_runtime_durable_timer",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "HistoryPending", "HistoryRetired", "TimerIdHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_runtime_timer_history_pending",
                table: "elsa_runtime_durable_timer");

            migrationBuilder.DropIndex(
                name: "IX_runtime_timer_live_claim",
                table: "elsa_runtime_durable_timer");

            migrationBuilder.DropIndex(
                name: "IX_runtime_timer_live_due",
                table: "elsa_runtime_durable_timer");

            migrationBuilder.DropIndex(
                name: "IX_runtime_timer_run_history_active",
                table: "elsa_runtime_durable_timer");

            migrationBuilder.DropIndex(
                name: "IX_runtime_timer_run_history_pending",
                table: "elsa_runtime_durable_timer");

            migrationBuilder.DropColumn(
                name: "TimerHistoryOwnerJson",
                table: "elsa_runtime_execution_liveness_state");

            migrationBuilder.DropColumn(
                name: "HistoryPending",
                table: "elsa_runtime_durable_timer");

            migrationBuilder.DropColumn(
                name: "HistoryRetired",
                table: "elsa_runtime_durable_timer");

            migrationBuilder.DropColumn(
                name: "HistoryRevision",
                table: "elsa_runtime_durable_timer");
        }
    }
}
