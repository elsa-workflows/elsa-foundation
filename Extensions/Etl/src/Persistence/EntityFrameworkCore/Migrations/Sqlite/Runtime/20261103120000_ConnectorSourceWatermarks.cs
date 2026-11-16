using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite.Runtime;

/// <summary>Adds the SQL source watermark checkpoints (spec 1199).</summary>
/// <remarks>Prior writers never read a watermark, so the table starts empty and existing runs are unaffected.</remarks>
[DbContext(typeof(EtlRuntimeSqliteDbContext))]
[Migration("20261103120000_ConnectorSourceWatermarks")]
internal sealed class ConnectorSourceWatermarks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "elsa_etl_connector_source_watermarks",
            columns: table => new
            {
                ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                CheckpointKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Watermark = table.Column<long>(type: "INTEGER", nullable: false),
                RunKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                UpdatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_elsa_etl_connector_source_watermarks", x => new { x.ScopeKey, x.CheckpointKey }));
        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_source_watermarks_SchemaVersion",
            table: "elsa_etl_connector_source_watermarks",
            column: "SchemaVersion");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("SQL source watermark checkpoints cannot be downgraded safely. Restore a verified database backup instead.");
}
