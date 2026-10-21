using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite.Runtime;

/// <summary>Adds retained HTTP source pages to connector runtime state.</summary>
[DbContext(typeof(EtlRuntimeSqliteDbContext))]
[Migration("20261021100000_ConnectorHttpPages")]
internal sealed class ConnectorHttpPages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "elsa_etl_connector_http_pages",
            columns: table => new
            {
                ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RunKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                PartitionOrdinal = table.Column<int>(type: "INTEGER", nullable: false),
                PageSequence = table.Column<long>(type: "INTEGER", nullable: false),
                RequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                PageJson = table.Column<string>(type: "TEXT", nullable: false),
                ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_elsa_etl_connector_http_pages",
                    x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal, x.PageSequence });
                table.ForeignKey(
                    name: "FK_elsa_etl_connector_http_pages_elsa_etl_connector_partitions_ScopeKey_RunKey_PartitionOrdinal",
                    columns: x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal },
                    principalTable: "elsa_etl_connector_partitions",
                    principalColumns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_http_pages_SchemaVersion",
            table: "elsa_etl_connector_http_pages",
            column: "SchemaVersion");
        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_http_pages_ScopeKey_RunKey_PartitionOrdinal_RequestHash",
            table: "elsa_etl_connector_http_pages",
            columns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "RequestHash" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retained connector HTTP pages cannot be downgraded safely. Restore a verified database backup instead.");
}
