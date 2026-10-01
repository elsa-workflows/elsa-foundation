using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.SqlServer
{
    /// <inheritdoc />
    public partial class RouteTableConvergenceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_trigger_binding_route_convergence",
                table: "elsa_runtime_workflow_trigger_binding",
                columns: new[] { "ScopeKeyHash", "StimulusTypeLookupKey", "IsActive" })
                .Annotation("SqlServer:Include", new[] { "StimulusLookupKey", "StimulusHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_bookmark_state_route_convergence",
                table: "elsa_runtime_bookmark_state",
                columns: new[] { "ScopeKeyHash", "StimulusTypeLookupKey", "ExpiresAtUtcTicks" })
                .Annotation("SqlServer:Include", new[] { "StimulusLookupKey", "StimulusHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_elsa_runtime_trigger_binding_route_convergence",
                table: "elsa_runtime_workflow_trigger_binding");

            migrationBuilder.DropIndex(
                name: "IX_elsa_runtime_bookmark_state_route_convergence",
                table: "elsa_runtime_bookmark_state");
        }
    }
}
