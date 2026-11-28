using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.Sqlite
{
    /// <inheritdoc />
    public partial class StampAuthoredEventCohorts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SchemaVersion",
                table: "elsa_runtime_authored_event_cohort",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "1.0.0");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_authored_event_cohort_SchemaVersion",
                table: "elsa_runtime_authored_event_cohort",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_elsa_runtime_authored_event_cohort_SchemaVersion",
                table: "elsa_runtime_authored_event_cohort");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "elsa_runtime_authored_event_cohort");
        }
    }
}
