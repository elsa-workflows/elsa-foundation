using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Cluster.EntityFrameworkCore.Migrations.ClusterMembership.Sqlite
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
                    HostId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Incarnation = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CurrentHostId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    HeartbeatAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiryPeriodTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    LeftAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    ReportJson = table.Column<string>(type: "TEXT", nullable: false),
                    ReportRevision = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_cluster_members", x => new { x.HostId, x.Incarnation });
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_cluster_members_CurrentHostId",
                table: "elsa_cluster_members",
                column: "CurrentHostId",
                unique: true);

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
