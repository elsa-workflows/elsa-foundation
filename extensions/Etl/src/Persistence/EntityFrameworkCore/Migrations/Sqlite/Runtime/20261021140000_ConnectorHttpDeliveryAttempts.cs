using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite.Runtime;

/// <summary>Adds retained HTTP delivery state and retry eligibility to connector batches.</summary>
[DbContext(typeof(EtlRuntimeSqliteDbContext))]
[Migration("20261021140000_ConnectorHttpDeliveryAttempts")]
internal sealed class ConnectorHttpDeliveryAttempts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Nullable additions preserve legacy batch and partition rows in place, including the
        // existing foreign-key graph and any retained output written by the prior migration.
        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_batches"
            ADD COLUMN "HttpDeliveryJson" TEXT NULL;
            """);

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_batches"
            ADD COLUMN "HttpDeliveryDigest" TEXT NULL
            CONSTRAINT "CK_elsa_etl_connector_batches_HttpDeliveryPlan"
            CHECK (("HttpDeliveryJson" IS NULL AND "HttpDeliveryDigest" IS NULL) OR
                   ("HttpDeliveryJson" IS NOT NULL AND "HttpDeliveryDigest" IS NOT NULL AND
                    length("HttpDeliveryDigest") = 64 AND "OutputJson" IS NOT NULL));
            """);

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_partitions"
            ADD COLUMN "NotBeforeUtcTicks" INTEGER NULL
            CONSTRAINT "CK_elsa_etl_connector_partitions_NotBeforeUtcTicks"
            CHECK ("NotBeforeUtcTicks" IS NULL OR "NotBeforeUtcTicks" > 0);
            """);

        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_partitions_ScopeKey_RunKey_Completed_NotBeforeUtcTicks_ExpiresAtUtcTicks",
            table: "elsa_etl_connector_partitions",
            columns: new[] { "ScopeKey", "RunKey", "Completed", "NotBeforeUtcTicks", "ExpiresAtUtcTicks" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retained connector HTTP delivery state cannot be downgraded safely. Restore a verified database backup instead.");
}
