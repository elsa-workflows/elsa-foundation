using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.MySql
{
    /// <inheritdoc />
    public partial class RecurringTriggerOccurrenceClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaimOwnerId",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "varchar(344)",
                maxLength: 344,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ClaimToken",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "ClaimedAtOffsetMinutes",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ClaimedAtUtcTicks",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureCount",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VisibleAfterOffsetMinutes",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "VisibleAfterUtcTicks",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimOwnerId",
                table: "elsa_runtime_recurring_trigger_schedule");

            migrationBuilder.DropColumn(
                name: "ClaimToken",
                table: "elsa_runtime_recurring_trigger_schedule");

            migrationBuilder.DropColumn(
                name: "ClaimedAtOffsetMinutes",
                table: "elsa_runtime_recurring_trigger_schedule");

            migrationBuilder.DropColumn(
                name: "ClaimedAtUtcTicks",
                table: "elsa_runtime_recurring_trigger_schedule");

            migrationBuilder.DropColumn(
                name: "FailureCount",
                table: "elsa_runtime_recurring_trigger_schedule");

            migrationBuilder.DropColumn(
                name: "VisibleAfterOffsetMinutes",
                table: "elsa_runtime_recurring_trigger_schedule");

            migrationBuilder.DropColumn(
                name: "VisibleAfterUtcTicks",
                table: "elsa_runtime_recurring_trigger_schedule");
        }
    }
}
