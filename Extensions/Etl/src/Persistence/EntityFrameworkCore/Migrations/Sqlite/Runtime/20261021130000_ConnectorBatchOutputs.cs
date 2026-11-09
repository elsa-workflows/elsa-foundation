using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite.Runtime;

/// <summary>Adds retained route output plans and output byte accounting to connector state.</summary>
[DbContext(typeof(EtlRuntimeSqliteDbContext))]
[Migration("20261021130000_ConnectorBatchOutputs")]
internal sealed class ConnectorBatchOutputs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQLite supports adding checked columns in place. Defaults preserve existing run,
        // partition, and reserved-batch rows without rebuilding any FK-connected tables.
        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_runs"
            ADD COLUMN "RetainedOutputBytes" INTEGER NOT NULL DEFAULT 0
            CONSTRAINT "CK_elsa_etl_connector_runs_RetainedOutputBytes"
            CHECK ("RetainedOutputBytes" >= 0);
            """);

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_partitions"
            ADD COLUMN "RetainedOutputBytes" INTEGER NOT NULL DEFAULT 0
            CONSTRAINT "CK_elsa_etl_connector_partitions_RetainedOutputBytes"
            CHECK ("RetainedOutputBytes" >= 0);
            """);

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_batches"
            ADD COLUMN "OutputJson" TEXT NULL;
            """);

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_batches"
            ADD COLUMN "OutputDigest" TEXT NULL
            CONSTRAINT "CK_elsa_etl_connector_batches_OutputPlan"
            CHECK (("OutputJson" IS NULL AND "OutputDigest" IS NULL) OR
                   ("OutputJson" IS NOT NULL AND "OutputDigest" IS NOT NULL AND length("OutputDigest") = 64));
            """);

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_batches"
            ADD COLUMN "OutputBytes" INTEGER NOT NULL DEFAULT 0
            CONSTRAINT "CK_elsa_etl_connector_batches_OutputBytes"
            CHECK ("OutputBytes" >= 0 AND ("OutputJson" IS NOT NULL OR "OutputBytes" = 0));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retained connector output state cannot be downgraded safely. Restore a verified database backup instead.");
}
