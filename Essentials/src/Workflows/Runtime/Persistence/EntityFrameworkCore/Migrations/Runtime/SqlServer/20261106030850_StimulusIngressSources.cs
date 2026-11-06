using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.SqlServer
{
    /// <inheritdoc />
    public partial class StimulusIngressSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_runtime_stimulus_ingress",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(684)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OriginFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AcceptedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_stimulus_ingress", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_stimulus_ingress_SchemaVersion",
                table: "elsa_runtime_stimulus_ingress",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_stimulus_ingress_ScopeKeyHash_AcceptedAtUtcTicks_Id",
                table: "elsa_runtime_stimulus_ingress",
                columns: new[] { "ScopeKeyHash", "AcceptedAtUtcTicks", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_stimulus_ingress_ScopeKeyHash_SourceIdHash",
                table: "elsa_runtime_stimulus_ingress",
                columns: new[] { "ScopeKeyHash", "SourceIdHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_runtime_stimulus_ingress");
        }
    }
}
