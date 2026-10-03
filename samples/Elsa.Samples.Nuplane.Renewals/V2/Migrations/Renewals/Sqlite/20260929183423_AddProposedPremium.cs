using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Samples.Nuplane.Renewals.Migrations.Renewals.Sqlite
{
    /// <inheritdoc />
    public partial class AddProposedPremium : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ProposedPremium",
                table: "elsa_samples_renewals",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProposedPremium",
                table: "elsa_samples_renewals");
        }
    }
}
