using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite.Runtime;

/// <summary>Adds accepted connector progress and file publication evidence.</summary>
[DbContext(typeof(EtlRuntimeSqliteDbContext))]
[Migration("20261021150000_ConnectorBatchAcceptance")]
internal sealed class ConnectorBatchAcceptance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Descriptor strings use the runtime's lossless encoding, but the polymorphic discriminator
        // and numeric page sequence retain their ordinary JSON forms. Refuse any row whose range
        // cannot be classified before deriving the new nullable sequence column.
        migrationBuilder.Sql("""
            CREATE TEMP TABLE "__elsa_etl_acceptance_descriptor_validation" (
                "IsValid" INTEGER NOT NULL CHECK ("IsValid" = 1)
            );
            """);
        migrationBuilder.Sql("""
            INSERT INTO temp."__elsa_etl_acceptance_descriptor_validation" ("IsValid")
            SELECT CASE
                WHEN length(CAST("DescriptorJson" AS BLOB)) > 524288 THEN 0
                WHEN json_valid("DescriptorJson") <> 1 THEN 0
                WHEN json_type("DescriptorJson") <> 'object' THEN 0
                WHEN (SELECT COUNT(*) FROM json_each("DescriptorJson")) <> 4 THEN 0
                WHEN (SELECT COUNT(DISTINCT "key") FROM json_each("DescriptorJson")) <> 4 THEN 0
                WHEN json_type("DescriptorJson", '$.batchId') <> 'text' THEN 0
                WHEN json_type("DescriptorJson", '$.range') <> 'object' THEN 0
                WHEN json_type("DescriptorJson", '$.inputBytes') <> 'integer' THEN 0
                WHEN json_extract("DescriptorJson", '$.inputBytes') < 0 THEN 0
                WHEN json_type("DescriptorJson", '$.inputHash') <> 'text' THEN 0
                WHEN json_type("DescriptorJson", '$.range.kind') <> 'text' THEN 0
                WHEN json_extract("DescriptorJson", '$.range.kind') = 'file'
                    AND json_type("DescriptorJson", '$.range.startOrdinal') = 'integer'
                    AND json_type("DescriptorJson", '$.range.endOrdinalExclusive') = 'integer'
                    AND json_type("DescriptorJson", '$.range.pageSequence') IS NULL
                    AND (SELECT COUNT(*) FROM json_each("DescriptorJson", '$.range')) = 3
                    AND (SELECT COUNT(DISTINCT "key") FROM json_each("DescriptorJson", '$.range')) = 3 THEN 1
                WHEN json_extract("DescriptorJson", '$.range.kind') = 'http'
                    AND json_type("DescriptorJson", '$.range.pageSequence') = 'integer'
                    AND json_extract("DescriptorJson", '$.range.pageSequence') >= 0
                    AND json_type("DescriptorJson", '$.range.requestHash') = 'text'
                    AND json_type("DescriptorJson", '$.range.pageContentHash') = 'text'
                    AND json_type("DescriptorJson", '$.range.startItem') = 'integer'
                    AND json_type("DescriptorJson", '$.range.endItemExclusive') = 'integer'
                    AND (SELECT COUNT(*) FROM json_each("DescriptorJson", '$.range')) = 6
                    AND (SELECT COUNT(DISTINCT "key") FROM json_each("DescriptorJson", '$.range')) = 6 THEN 1
                ELSE 0
            END
            FROM "elsa_etl_connector_batches";
            """);
        migrationBuilder.Sql("DROP TABLE temp.\"__elsa_etl_acceptance_descriptor_validation\";");

        // Additive columns keep the FK graph, existing output plans, and delivery journals in place.
        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_partitions"
            ADD COLUMN "AcceptedBatchCount" INTEGER NOT NULL DEFAULT 0;
            """);
        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_partitions"
            ADD COLUMN "AcceptedRecords" INTEGER NOT NULL DEFAULT 0;
            """);
        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_partitions"
            ADD COLUMN "RejectedRecords" INTEGER NOT NULL DEFAULT 0;
            """);
        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_partitions"
            ADD COLUMN "FilteredRecords" INTEGER NOT NULL DEFAULT 0
            CONSTRAINT "CK_elsa_etl_connector_partitions_NonNegativeAcceptanceCounts"
            CHECK ("AcceptedBatchCount" >= 0 AND "AcceptedRecords" >= 0 AND
                   "RejectedRecords" >= 0 AND "FilteredRecords" >= 0);
            """);

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_batches"
            ADD COLUMN "BatchOrdinal" INTEGER NOT NULL DEFAULT 0
            CONSTRAINT "CK_elsa_etl_connector_batches_BatchOrdinal"
            CHECK ("BatchOrdinal" >= 0);
            """);
        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_batches"
            ADD COLUMN "HttpPageSequence" INTEGER NULL
            CONSTRAINT "CK_elsa_etl_connector_batches_HttpPageSequence"
            CHECK ("HttpPageSequence" IS NULL OR "HttpPageSequence" >= 0);
            """);
        migrationBuilder.Sql("""
            UPDATE "elsa_etl_connector_batches"
            SET "HttpPageSequence" = CASE
                WHEN json_extract("DescriptorJson", '$.range.kind') = 'http'
                    THEN json_extract("DescriptorJson", '$.range.pageSequence')
                ELSE NULL
            END;
            """);
        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_batches"
            ADD COLUMN "MainFilePublished" INTEGER NOT NULL DEFAULT 0
            CONSTRAINT "CK_elsa_etl_connector_batches_MainFilePublishedRequiresOutput"
            CHECK ("MainFilePublished" = 0 OR "OutputJson" IS NOT NULL);
            """);
        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_batches"
            ADD COLUMN "ErrorFilePublished" INTEGER NOT NULL DEFAULT 0
            CONSTRAINT "CK_elsa_etl_connector_batches_ErrorFilePublishedRequiresOutput"
            CHECK ("ErrorFilePublished" = 0 OR "OutputJson" IS NOT NULL);
            """);
        // Existing rows start at zero; new dispatch/acceptance paths allocate their own bounded headroom.
        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_batches"
            ADD COLUMN "AcceptanceMetadataBytes" INTEGER NOT NULL DEFAULT 0
            CONSTRAINT "CK_elsa_etl_connector_batches_AcceptanceMetadataBytes"
            CHECK ("AcceptanceMetadataBytes" >= 0);
            """);

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_http_pages"
            ADD COLUMN "AcceptedItemCount" INTEGER NOT NULL DEFAULT 0
            CONSTRAINT "CK_elsa_etl_connector_http_pages_AcceptedItemCount"
            CHECK ("AcceptedItemCount" >= 0);
            """);

        // Earlier drafts allow at most one owed batch per partition, so each retained batch starts at ordinal zero.
        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_batches_ScopeKey_RunKey_PartitionOrdinal_BatchOrdinal",
            table: "elsa_etl_connector_batches",
            columns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "BatchOrdinal" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_batches_ScopeKey_RunKey_PartitionOrdinal_HttpPageSequence",
            table: "elsa_etl_connector_batches",
            columns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "HttpPageSequence" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Accepted connector progress cannot be downgraded safely. Restore a verified database backup instead.");
}
