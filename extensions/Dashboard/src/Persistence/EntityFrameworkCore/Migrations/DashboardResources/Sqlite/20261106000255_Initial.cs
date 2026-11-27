using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore.Migrations.DashboardResources.Sqlite
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "__ElsaDatabaseIdentity_ElsaStudioDashboard",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaStudioDashboard", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaStudioDashboard",
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
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaStudioDashboard", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "elsa_studio_dashboard_operations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ResourceKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_studio_dashboard_operations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_studio_dashboard_resources",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ScopeId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OwnerKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Revision = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DocumentJson = table.Column<string>(type: "TEXT", nullable: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_studio_dashboard_resources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_studio_dashboard_scopes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Revision = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_studio_dashboard_scopes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_ElsaStudioDashboard_SchemaVersion",
                table: "__ElsaDatabaseIdentity_ElsaStudioDashboard",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaStudioDashboard_SchemaVersion",
                table: "__ElsaSchemaFinalization_ElsaStudioDashboard",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_studio_dashboard_resources_SchemaVersion",
                table: "elsa_studio_dashboard_resources",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_studio_dashboard_resources_ScopeId_Kind_Deleted",
                table: "elsa_studio_dashboard_resources",
                columns: new[] { "ScopeId", "Kind", "Deleted" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_studio_dashboard_resources_ScopeId_OwnerKey_Kind_Deleted",
                table: "elsa_studio_dashboard_resources",
                columns: new[] { "ScopeId", "OwnerKey", "Kind", "Deleted" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_studio_dashboard_operations_SchemaVersion",
                table: "elsa_studio_dashboard_operations",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_studio_dashboard_scopes_SchemaVersion",
                table: "elsa_studio_dashboard_scopes",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__ElsaDatabaseIdentity_ElsaStudioDashboard");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_ElsaStudioDashboard");

            migrationBuilder.DropTable(
                name: "elsa_studio_dashboard_operations");

            migrationBuilder.DropTable(
                name: "elsa_studio_dashboard_resources");

            migrationBuilder.DropTable(
                name: "elsa_studio_dashboard_scopes");
        }
    }
}
