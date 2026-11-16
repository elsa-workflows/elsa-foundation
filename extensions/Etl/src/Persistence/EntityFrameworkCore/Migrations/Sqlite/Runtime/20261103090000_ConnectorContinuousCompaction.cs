using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite.Runtime;

/// <summary>Adds a continuous shard's poll generation and compacted summary, and lets a compacted request be a base.</summary>
/// <remarks>
/// <para>Prior writers never compacted, so every existing partition keeps zero summaries and generation, and every existing
/// request keeps its predecessor.</para>
/// <para>SQLite cannot alter a CHECK, so the request table is rebuilt. Foreign keys are deferred instead of disabled, which
/// keeps the rebuild inside this migration's transaction: rows that reference the dropped table become deferred violations,
/// and re-inserting their parent rows clears them before commit. The alternate key is restored as a UNIQUE constraint
/// because pages and partitions reference it, and every index keeps its EF name.</para>
/// </remarks>
[DbContext(typeof(EtlRuntimeSqliteDbContext))]
[Migration("20261103090000_ConnectorContinuousCompaction")]
internal sealed class ConnectorContinuousCompaction : Migration
{
    private static readonly string[] PartitionCounters =
    [
        "PollGeneration", "CompactedPageCount", "CompactedBatchCount", "CompactedAcceptedRecords",
        "CompactedRejectedRecords", "CompactedFilteredRecords", "CompactedMainEffects", "CompactedErrorEffects"
    ];

    private const string RequestColumns = """
        "ScopeKey", "RunKey", "PartitionOrdinal", "PageSequence", "RequestHash", "RequestJson", "MetadataBytes",
        "PreviousPageSequence", "PreviousHasContinuation", "SchemaVersion", "FetchJson", "FetchDigest",
        "FetchMetadataBytes", "SourceMetadataReserveBytes", "InputReservationBytes"
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var column in PartitionCounters)
            migrationBuilder.Sql($"""
                ALTER TABLE "elsa_etl_connector_partitions"
                ADD COLUMN "{column}" INTEGER NOT NULL DEFAULT 0
                CONSTRAINT "CK_elsa_etl_connector_partitions_{column}" CHECK ("{column}" >= 0);
                """);

        migrationBuilder.Sql("PRAGMA defer_foreign_keys = ON;");
        migrationBuilder.Sql($"""
            CREATE TABLE "ef_temp_elsa_etl_connector_http_requests" AS
            SELECT {RequestColumns} FROM "elsa_etl_connector_http_requests";
            """);
        migrationBuilder.Sql("""DROP TABLE "elsa_etl_connector_http_requests";""");
        migrationBuilder.Sql("""
            CREATE TABLE "elsa_etl_connector_http_requests" (
                "ScopeKey" TEXT NOT NULL,
                "RunKey" TEXT NOT NULL,
                "PartitionOrdinal" INTEGER NOT NULL,
                "PageSequence" INTEGER NOT NULL,
                "RequestHash" TEXT NOT NULL,
                "RequestJson" TEXT NOT NULL,
                "MetadataBytes" INTEGER NOT NULL,
                "PreviousPageSequence" INTEGER NULL,
                "PreviousHasContinuation" INTEGER NULL,
                "SchemaVersion" TEXT NOT NULL,
                "FetchJson" TEXT NULL,
                "FetchDigest" TEXT NULL,
                "FetchMetadataBytes" INTEGER NOT NULL DEFAULT 0,
                "SourceMetadataReserveBytes" INTEGER NOT NULL DEFAULT 0,
                "InputReservationBytes" INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "PK_elsa_etl_connector_http_requests" PRIMARY KEY ("ScopeKey", "RunKey", "PartitionOrdinal", "PageSequence"),
                CONSTRAINT "AK_elsa_etl_connector_http_requests_ScopeKey_RunKey_PartitionOrdinal_PageSequence_RequestHash" UNIQUE ("ScopeKey", "RunKey", "PartitionOrdinal", "PageSequence", "RequestHash"),
                CONSTRAINT "CK_elsa_etl_connector_http_requests_NonNegativeMetadataBytes" CHECK (MetadataBytes >= 0),
                CONSTRAINT "CK_elsa_etl_connector_http_requests_SequencePredecessor" CHECK ((PreviousPageSequence IS NULL AND PreviousHasContinuation IS NULL) OR (PageSequence > 0 AND PreviousPageSequence IS NOT NULL AND PreviousHasContinuation IS NOT NULL AND PreviousPageSequence = PageSequence - 1 AND PreviousHasContinuation = 1)),
                CONSTRAINT "CK_elsa_etl_connector_http_requests_SourceFetchMetadata" CHECK (FetchMetadataBytes >= 0 AND SourceMetadataReserveBytes >= 0 AND InputReservationBytes >= 0 AND ((FetchJson IS NULL AND FetchDigest IS NULL AND FetchMetadataBytes = 0 AND SourceMetadataReserveBytes = 0 AND InputReservationBytes = 0) OR (FetchJson IS NOT NULL AND FetchDigest IS NOT NULL AND length(FetchDigest) = 64))),
                CONSTRAINT "FK_elsa_etl_connector_http_requests_elsa_etl_connector_partitions_ScopeKey_RunKey_PartitionOrdinal" FOREIGN KEY ("ScopeKey", "RunKey", "PartitionOrdinal") REFERENCES "elsa_etl_connector_partitions" ("ScopeKey", "RunKey", "PartitionOrdinal") ON DELETE RESTRICT,
                CONSTRAINT "FK_elsa_etl_connector_http_requests_elsa_etl_connector_http_pages_ScopeKey_RunKey_PartitionOrdinal_PreviousPageSequence_RequestHash_PreviousHasContinuation" FOREIGN KEY ("ScopeKey", "RunKey", "PartitionOrdinal", "PreviousPageSequence", "RequestHash", "PreviousHasContinuation") REFERENCES "elsa_etl_connector_http_pages" ("ScopeKey", "RunKey", "PartitionOrdinal", "PageSequence", "NextRequestHash", "HasContinuation") ON DELETE RESTRICT
            );
            """);
        migrationBuilder.Sql($"""
            INSERT INTO "elsa_etl_connector_http_requests" ({RequestColumns})
            SELECT {RequestColumns} FROM "ef_temp_elsa_etl_connector_http_requests";
            """);
        migrationBuilder.Sql("""DROP TABLE "ef_temp_elsa_etl_connector_http_requests";""");
        migrationBuilder.Sql("""CREATE INDEX "IX_elsa_etl_connector_http_requests_SchemaVersion" ON "elsa_etl_connector_http_requests" ("SchemaVersion");""");
        migrationBuilder.Sql("""CREATE UNIQUE INDEX "IX_elsa_etl_connector_http_requests_ScopeKey_RunKey_PartitionOrdinal_RequestHash" ON "elsa_etl_connector_http_requests" ("ScopeKey", "RunKey", "PartitionOrdinal", "RequestHash");""");
        migrationBuilder.Sql("""CREATE INDEX "IX_elsa_etl_connector_http_requests_ScopeKey_RunKey_PartitionOrdinal_PreviousPageSequence_RequestHash_PreviousHasContinuation" ON "elsa_etl_connector_http_requests" ("ScopeKey", "RunKey", "PartitionOrdinal", "PreviousPageSequence", "RequestHash", "PreviousHasContinuation");""");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Compacted connector history cannot be downgraded safely. Restore a verified database backup instead.");
}
