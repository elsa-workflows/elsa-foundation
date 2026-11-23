using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.MySql
{
    /// <inheritdoc />
    public partial class OutboxClaimDeliveredAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ClaimDeliveredAtUtcTicks",
                table: "elsa_runtime_post_commit_outbox",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimDeliveredAtUtcTicks",
                table: "elsa_runtime_post_commit_outbox");
        }
    }
}
