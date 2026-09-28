using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Cluster.EntityFrameworkCore.Migrations.ClusterMembership.SqlServer
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_cluster_members",
                columns: table => new
                {
                    HostId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Incarnation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CurrentHostId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    HeartbeatAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ExpiryPeriodTicks = table.Column<long>(type: "bigint", nullable: false),
                    LeftAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    ReportJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReportRevision = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_cluster_members", x => new { x.HostId, x.Incarnation });
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_cluster_members_CurrentHostId",
                table: "elsa_cluster_members",
                column: "CurrentHostId",
                unique: true,
                filter: "[CurrentHostId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_cluster_members_SchemaVersion",
                table: "elsa_cluster_members",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_cluster_members");
        }
    }
}
