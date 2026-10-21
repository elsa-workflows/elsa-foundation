using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite.Runtime;

/// <summary>Adds versioned state for connector preparation and partition ownership.</summary>
[DbContext(typeof(EtlRuntimeSqliteDbContext))]
[Migration("20261021070000_ConnectorRuntimePreparation")]
internal sealed class ConnectorRuntimePreparation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "elsa_etl_connector_runs",
            columns: table => new
            {
                ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RunKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                BindingsJson = table.Column<string>(type: "TEXT", nullable: false),
                PreparedJson = table.Column<string>(type: "TEXT", nullable: true),
                RetainedInputBytes = table.Column<long>(type: "INTEGER", nullable: false),
                RetainedMetadataBytes = table.Column<long>(type: "INTEGER", nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_elsa_etl_connector_runs", x => new { x.ScopeKey, x.RunKey });
                table.CheckConstraint(
                    "CK_elsa_etl_connector_runs_RetainedByteCounts",
                    "RetainedInputBytes >= 0 AND RetainedMetadataBytes >= 0");
                table.ForeignKey(
                    name: "FK_elsa_etl_connector_runs_elsa_etl_runtime_runs_ScopeKey_RunKey",
                    columns: x => new { x.ScopeKey, x.RunKey },
                    principalTable: "elsa_etl_runtime_runs",
                    principalColumns: new[] { "ScopeKey", "RunKey" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "elsa_etl_connector_partitions",
            columns: table => new
            {
                ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RunKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                PartitionOrdinal = table.Column<int>(type: "INTEGER", nullable: false),
                PositionJson = table.Column<string>(type: "TEXT", nullable: false),
                Completed = table.Column<bool>(type: "INTEGER", nullable: false),
                Owner = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: true),
                Fence = table.Column<long>(type: "INTEGER", nullable: false),
                ExpiresAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: true),
                PageCount = table.Column<long>(type: "INTEGER", nullable: false),
                PageBytes = table.Column<long>(type: "INTEGER", nullable: false),
                MetadataBytes = table.Column<long>(type: "INTEGER", nullable: false),
                CurrentRequestSequence = table.Column<long>(type: "INTEGER", nullable: true),
                CurrentRequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_elsa_etl_connector_partitions", x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal });
                table.CheckConstraint(
                    "CK_elsa_etl_connector_partitions_NonNegativeCounters",
                    "PageCount >= 0 AND PageBytes >= 0 AND MetadataBytes >= 0");
                table.CheckConstraint(
                    "CK_elsa_etl_connector_partitions_CurrentRequestPointer",
                    "(CurrentRequestSequence IS NULL AND CurrentRequestHash IS NULL) OR (CurrentRequestSequence IS NOT NULL AND CurrentRequestHash IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_elsa_etl_connector_partitions_elsa_etl_connector_runs_ScopeKey_RunKey",
                    columns: x => new { x.ScopeKey, x.RunKey },
                    principalTable: "elsa_etl_connector_runs",
                    principalColumns: new[] { "ScopeKey", "RunKey" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_elsa_etl_connector_partitions_elsa_etl_connector_http_requests_ScopeKey_RunKey_PartitionOrdinal_CurrentRequestSequence_CurrentRequestHash",
                    columns: x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal, x.CurrentRequestSequence, x.CurrentRequestHash },
                    principalTable: "elsa_etl_connector_http_requests",
                    principalColumns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "PageSequence", "RequestHash" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_partitions_SchemaVersion",
            table: "elsa_etl_connector_partitions",
            column: "SchemaVersion");
        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_partitions_ScopeKey_RunKey_Completed_ExpiresAtUtcTicks",
            table: "elsa_etl_connector_partitions",
            columns: new[] { "ScopeKey", "RunKey", "Completed", "ExpiresAtUtcTicks" });
        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_partitions_ScopeKey_RunKey_PartitionOrdinal_CurrentRequestSequence_CurrentRequestHash",
            table: "elsa_etl_connector_partitions",
            columns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "CurrentRequestSequence", "CurrentRequestHash" });
        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_runs_SchemaVersion",
            table: "elsa_etl_connector_runs",
            column: "SchemaVersion");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Connector runtime state cannot be downgraded safely. Restore a verified database backup instead.");
}
