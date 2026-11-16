using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite.Runtime;

/// <summary>Adds retained HTTP source-fetch journals and bounded input reservations.</summary>
[DbContext(typeof(EtlRuntimeSqliteDbContext))]
[Migration("20261021160000_ConnectorSourceFetch")]
internal sealed class ConnectorSourceFetch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Each addition preserves existing rows in place. Legacy connectors begin with no source reservation,
        // and legacy HTTP requests have no fetch journal or reservation metadata.
        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_runs"
            ADD COLUMN "SourceInputReservedBytes" INTEGER NOT NULL DEFAULT 0
            CONSTRAINT "CK_elsa_etl_connector_runs_SourceInputReservedBytes"
            CHECK ("SourceInputReservedBytes" >= 0);
            """);

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_partitions"
            ADD COLUMN "SourceInputReservedBytes" INTEGER NOT NULL DEFAULT 0
            CONSTRAINT "CK_elsa_etl_connector_partitions_SourceInputReservedBytes"
            CHECK ("SourceInputReservedBytes" >= 0);
            """);

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_http_requests"
            ADD COLUMN "FetchJson" TEXT NULL;
            """);

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_http_requests"
            ADD COLUMN "FetchDigest" TEXT NULL;
            """);

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_http_requests"
            ADD COLUMN "FetchMetadataBytes" INTEGER NOT NULL DEFAULT 0;
            """);

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_http_requests"
            ADD COLUMN "SourceMetadataReserveBytes" INTEGER NOT NULL DEFAULT 0;
            """);

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_http_requests"
            ADD COLUMN "InputReservationBytes" INTEGER NOT NULL DEFAULT 0
            CONSTRAINT "CK_elsa_etl_connector_http_requests_SourceFetchMetadata"
            CHECK ("FetchMetadataBytes" >= 0 AND "SourceMetadataReserveBytes" >= 0 AND "InputReservationBytes" >= 0 AND
                   (("FetchJson" IS NULL AND "FetchDigest" IS NULL AND "FetchMetadataBytes" = 0 AND
                     "SourceMetadataReserveBytes" = 0 AND "InputReservationBytes" = 0) OR
                    ("FetchJson" IS NOT NULL AND "FetchDigest" IS NOT NULL AND length("FetchDigest") = 64)));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retained connector HTTP source-fetch state cannot be downgraded safely. Restore a verified database backup instead.");
}
