using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite.Runtime;

/// <summary>Adds the request ledger and retained HTTP pages to connector runtime state.</summary>
[DbContext(typeof(EtlRuntimeSqliteDbContext))]
[Migration("20261021100000_ConnectorHttpPages")]
internal sealed class ConnectorHttpPages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "elsa_etl_connector_http_requests",
            columns: table => new
            {
                ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RunKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                PartitionOrdinal = table.Column<int>(type: "INTEGER", nullable: false),
                PageSequence = table.Column<long>(type: "INTEGER", nullable: false),
                RequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RequestJson = table.Column<string>(type: "TEXT", nullable: false),
                MetadataBytes = table.Column<long>(type: "INTEGER", nullable: false),
                PreviousPageSequence = table.Column<long>(type: "INTEGER", nullable: true),
                PreviousHasContinuation = table.Column<bool>(type: "INTEGER", nullable: true),
                SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_elsa_etl_connector_http_requests",
                    x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal, x.PageSequence });
                table.UniqueConstraint(
                    "AK_elsa_etl_connector_http_requests_ScopeKey_RunKey_PartitionOrdinal_PageSequence_RequestHash",
                    x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal, x.PageSequence, x.RequestHash });
                table.CheckConstraint(
                    "CK_elsa_etl_connector_http_requests_NonNegativeMetadataBytes",
                    "MetadataBytes >= 0");
                table.CheckConstraint(
                    "CK_elsa_etl_connector_http_requests_SequencePredecessor",
                    "(PageSequence = 0 AND PreviousPageSequence IS NULL AND PreviousHasContinuation IS NULL) OR (PageSequence > 0 AND PreviousPageSequence IS NOT NULL AND PreviousHasContinuation IS NOT NULL AND PreviousPageSequence = PageSequence - 1 AND PreviousHasContinuation = 1)");
                table.ForeignKey(
                    name: "FK_elsa_etl_connector_http_requests_elsa_etl_connector_partitions_ScopeKey_RunKey_PartitionOrdinal",
                    columns: x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal },
                    principalTable: "elsa_etl_connector_partitions",
                    principalColumns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_elsa_etl_connector_http_requests_elsa_etl_connector_http_pages_ScopeKey_RunKey_PartitionOrdinal_PreviousPageSequence_RequestHash_PreviousHasContinuation",
                    columns: x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal, x.PreviousPageSequence, x.RequestHash, x.PreviousHasContinuation },
                    principalTable: "elsa_etl_connector_http_pages",
                    principalColumns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "PageSequence", "NextRequestHash", "HasContinuation" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "elsa_etl_connector_http_pages",
            columns: table => new
            {
                ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RunKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                PartitionOrdinal = table.Column<int>(type: "INTEGER", nullable: false),
                PageSequence = table.Column<long>(type: "INTEGER", nullable: false),
                RequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                NextRequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                HasContinuation = table.Column<bool>(type: "INTEGER", nullable: false),
                PageJson = table.Column<string>(type: "TEXT", nullable: false),
                ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                PageDigest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                MetadataBytes = table.Column<long>(type: "INTEGER", nullable: false),
                SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_elsa_etl_connector_http_pages",
                    x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal, x.PageSequence });
                table.UniqueConstraint(
                    "AK_elsa_etl_connector_http_pages_ScopeKey_RunKey_PartitionOrdinal_PageSequence_NextRequestHash_HasContinuation",
                    x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal, x.PageSequence, x.NextRequestHash, x.HasContinuation });
                table.CheckConstraint(
                    "CK_elsa_etl_connector_http_pages_NonNegativeByteCounts",
                    "ByteLength >= 0 AND MetadataBytes >= 0");
                table.ForeignKey(
                    name: "FK_elsa_etl_connector_http_pages_elsa_etl_connector_partitions_ScopeKey_RunKey_PartitionOrdinal",
                    columns: x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal },
                    principalTable: "elsa_etl_connector_partitions",
                    principalColumns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_elsa_etl_connector_http_pages_elsa_etl_connector_http_requests_ScopeKey_RunKey_PartitionOrdinal_PageSequence_RequestHash",
                    columns: x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal, x.PageSequence, x.RequestHash },
                    principalTable: "elsa_etl_connector_http_requests",
                    principalColumns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "PageSequence", "RequestHash" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_http_requests_SchemaVersion",
            table: "elsa_etl_connector_http_requests",
            column: "SchemaVersion");
        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_http_requests_ScopeKey_RunKey_PartitionOrdinal_RequestHash",
            table: "elsa_etl_connector_http_requests",
            columns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "RequestHash" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_http_requests_ScopeKey_RunKey_PartitionOrdinal_PreviousPageSequence_RequestHash_PreviousHasContinuation",
            table: "elsa_etl_connector_http_requests",
            columns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "PreviousPageSequence", "RequestHash", "PreviousHasContinuation" });
        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_http_pages_SchemaVersion",
            table: "elsa_etl_connector_http_pages",
            column: "SchemaVersion");
        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_http_pages_ScopeKey_RunKey_PartitionOrdinal_RequestHash",
            table: "elsa_etl_connector_http_pages",
            columns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "RequestHash" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_http_pages_ScopeKey_RunKey_PartitionOrdinal_PageSequence_RequestHash",
            table: "elsa_etl_connector_http_pages",
            columns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "PageSequence", "RequestHash" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retained connector request and page state cannot be downgraded safely. Restore a verified database backup instead.");
}
