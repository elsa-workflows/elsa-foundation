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
                name: "__ElsaDatabaseIdentity_ElsaClusterMembership",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaClusterMembership", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaClusterMembership",
                columns: table => new
                {
                    Family = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    FinalizedVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IntentJson = table.Column<string>(type: "TEXT", nullable: true),
                    HoldsJson = table.Column<string>(type: "TEXT", nullable: false),
                    HistoryJson = table.Column<string>(type: "TEXT", nullable: false),
                    FinishJson = table.Column<string>(type: "TEXT", nullable: true),
                    FinishHistoryJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaClusterMembership", x => x.Family);
                });

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
                name: "IX___ElsaDatabaseIdentity_ElsaClusterMembership_SchemaVersion",
                table: "__ElsaDatabaseIdentity_ElsaClusterMembership",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaClusterMembership_SchemaVersion",
                table: "__ElsaSchemaFinalization_ElsaClusterMembership",
                column: "SchemaVersion");

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
                name: "__ElsaDatabaseIdentity_ElsaClusterMembership");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_ElsaClusterMembership");

            migrationBuilder.DropTable(
                name: "elsa_cluster_members");
        }
    }
}
