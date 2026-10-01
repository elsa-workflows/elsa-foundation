using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.MySql
{
    /// <inheritdoc />
    public partial class RouteTableConvergenceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_bookmark_state_route_convergence",
                table: "elsa_runtime_bookmark_state",
                columns: new[] { "ScopeKeyHash", "StimulusTypeLookupKey", "ExpiresAtUtcTicks", "StimulusLookupKey", "StimulusHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_elsa_runtime_bookmark_state_route_convergence",
                table: "elsa_runtime_bookmark_state");
        }
    }
}
