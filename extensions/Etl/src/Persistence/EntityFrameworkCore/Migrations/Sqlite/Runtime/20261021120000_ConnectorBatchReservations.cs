using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite.Runtime;

/// <summary>Adds durable connector batch reservations and partition owed-batch pointers.</summary>
[DbContext(typeof(EtlRuntimeSqliteDbContext))]
[Migration("20261021120000_ConnectorBatchReservations")]
internal sealed class ConnectorBatchReservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQLite cannot add a foreign key to an existing table. Rebuild the partition table
        // while preserving its rows and all inbound references from the HTTP ledger tables.
        migrationBuilder.Sql("PRAGMA foreign_keys = OFF;", suppressTransaction: true);

        migrationBuilder.CreateTable(
            name: "elsa_etl_connector_batches",
            columns: table => new
            {
                ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RunKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                PartitionOrdinal = table.Column<int>(type: "INTEGER", nullable: false),
                BatchId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                StartKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                DescriptorJson = table.Column<string>(type: "TEXT", nullable: false),
                DescriptorDigest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RecordCount = table.Column<long>(type: "INTEGER", nullable: false),
                InputBytes = table.Column<long>(type: "INTEGER", nullable: false),
                MetadataBytes = table.Column<long>(type: "INTEGER", nullable: false),
                Settled = table.Column<bool>(type: "INTEGER", nullable: false),
                SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_elsa_etl_connector_batches",
                    x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal, x.BatchId });
                table.ForeignKey(
                    name: "FK_elsa_etl_connector_batches_elsa_etl_connector_partitions_ScopeKey_RunKey_PartitionOrdinal",
                    columns: x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal },
                    principalTable: "elsa_etl_connector_partitions",
                    principalColumns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "elsa_etl_connector_partitions_replacement",
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
                SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                OwedBatchId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                BatchMetadataBytes = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_elsa_etl_connector_partitions",
                    x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal });
                table.CheckConstraint(
                    "CK_elsa_etl_connector_partitions_NonNegativeCounters",
                    "PageCount >= 0 AND PageBytes >= 0 AND MetadataBytes >= 0 AND BatchMetadataBytes >= 0");
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
                table.ForeignKey(
                    name: "FK_elsa_etl_connector_partitions_elsa_etl_connector_batches_ScopeKey_RunKey_PartitionOrdinal_OwedBatchId",
                    columns: x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal, x.OwedBatchId },
                    principalTable: "elsa_etl_connector_batches",
                    principalColumns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "BatchId" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.Sql(
            "INSERT INTO \"elsa_etl_connector_partitions_replacement\" (\"ScopeKey\", \"RunKey\", \"PartitionOrdinal\", \"PositionJson\", \"Completed\", \"Owner\", \"Fence\", \"ExpiresAtUtcTicks\", \"PageCount\", \"PageBytes\", \"MetadataBytes\", \"CurrentRequestSequence\", \"CurrentRequestHash\", \"Revision\", \"SchemaVersion\", \"OwedBatchId\", \"BatchMetadataBytes\") " +
            "SELECT \"ScopeKey\", \"RunKey\", \"PartitionOrdinal\", \"PositionJson\", \"Completed\", \"Owner\", \"Fence\", \"ExpiresAtUtcTicks\", \"PageCount\", \"PageBytes\", \"MetadataBytes\", \"CurrentRequestSequence\", \"CurrentRequestHash\", \"Revision\", \"SchemaVersion\", NULL, 0 FROM \"elsa_etl_connector_partitions\";");
        migrationBuilder.DropTable(name: "elsa_etl_connector_partitions");
        migrationBuilder.RenameTable(
            name: "elsa_etl_connector_partitions_replacement",
            newName: "elsa_etl_connector_partitions");

        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_batches_SchemaVersion",
            table: "elsa_etl_connector_batches",
            column: "SchemaVersion");
        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_batches_ScopeKey_RunKey_PartitionOrdinal_StartKey",
            table: "elsa_etl_connector_batches",
            columns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "StartKey" },
            unique: true);
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
            name: "IX_elsa_etl_connector_partitions_ScopeKey_RunKey_PartitionOrdinal_OwedBatchId",
            table: "elsa_etl_connector_partitions",
            columns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "OwedBatchId" });

        migrationBuilder.Sql("PRAGMA foreign_keys = ON;", suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Durable connector batches cannot be downgraded safely. Restore a verified database backup instead.");
}
