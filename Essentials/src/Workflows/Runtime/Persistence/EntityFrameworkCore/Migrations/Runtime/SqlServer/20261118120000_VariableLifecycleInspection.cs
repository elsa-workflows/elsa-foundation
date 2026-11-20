using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.SqlServer
{
    /// <inheritdoc />
    public partial class VariableLifecycleInspection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FirstCommitSequence",
                table: "elsa_runtime_activity_execution_state",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastCommitSequence",
                table: "elsa_runtime_activity_execution_state",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LostUpdateWarningCount",
                table: "elsa_runtime_activity_execution_state",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_alteration_job_workflow",
                table: "elsa_runtime_workflow_alteration_job",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_activity_execution_state_commit_range",
                table: "elsa_runtime_activity_execution_state",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "FirstCommitSequence", "LastCommitSequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_elsa_runtime_workflow_alteration_job_workflow",
                table: "elsa_runtime_workflow_alteration_job");

            migrationBuilder.DropIndex(
                name: "IX_elsa_runtime_activity_execution_state_commit_range",
                table: "elsa_runtime_activity_execution_state");

            migrationBuilder.DropColumn(
                name: "FirstCommitSequence",
                table: "elsa_runtime_activity_execution_state");

            migrationBuilder.DropColumn(
                name: "LastCommitSequence",
                table: "elsa_runtime_activity_execution_state");

            migrationBuilder.DropColumn(
                name: "LostUpdateWarningCount",
                table: "elsa_runtime_activity_execution_state");
        }
    }
}
