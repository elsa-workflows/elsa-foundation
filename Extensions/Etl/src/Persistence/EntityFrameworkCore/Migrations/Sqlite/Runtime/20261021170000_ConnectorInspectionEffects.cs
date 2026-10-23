using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite.Runtime;

/// <summary>Adds indexed route-kind counts for bounded connector inspection.</summary>
[DbContext(typeof(EtlRuntimeSqliteDbContext))]
[Migration("20261021170000_ConnectorInspectionEffects")]
internal sealed class ConnectorInspectionEffects : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TEMP TABLE "__elsa_etl_inspection_effect_validation" (
                "IsValid" INTEGER NOT NULL CHECK ("IsValid" = 1)
            );
            """);
        migrationBuilder.Sql("""
            INSERT INTO temp."__elsa_etl_inspection_effect_validation" ("IsValid")
            SELECT CASE
                WHEN "OutputJson" IS NULL THEN 1
                WHEN json_valid("OutputJson") <> 1 OR json_type("OutputJson") <> 'object' THEN 0
                WHEN json_type("OutputJson", '$.main') IS NULL OR
                     json_type("OutputJson", '$.main') NOT IN ('null', 'object') THEN 0
                WHEN json_type("OutputJson", '$.main') = 'object' AND
                     (json_type("OutputJson", '$.main.kind') IS NOT 'text' OR
                      json_extract("OutputJson", '$.main.kind') NOT IN ('http', 'jsonl')) THEN 0
                WHEN json_type("OutputJson", '$.errors') IS NULL OR
                     json_type("OutputJson", '$.errors') NOT IN ('null', 'object') THEN 0
                WHEN json_type("OutputJson", '$.errors') = 'object' AND
                     (json_type("OutputJson", '$.errors.kind') IS NOT 'text' OR
                      json_extract("OutputJson", '$.errors.kind') NOT IN ('http', 'jsonl')) THEN 0
                ELSE 1
            END
            FROM "elsa_etl_connector_batches";
            """);
        migrationBuilder.Sql("DROP TABLE temp.\"__elsa_etl_inspection_effect_validation\";");

        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_batches"
            ADD COLUMN "MainEffectKind" INTEGER NULL
            CONSTRAINT "CK_elsa_etl_connector_batches_MainEffectKind"
            CHECK ("MainEffectKind" IS NULL OR "MainEffectKind" IN (1, 2));
            """);
        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_batches"
            ADD COLUMN "ErrorEffectKind" INTEGER NULL
            CONSTRAINT "CK_elsa_etl_connector_batches_ErrorEffectKind"
            CHECK ("ErrorEffectKind" IS NULL OR "ErrorEffectKind" IN (1, 2));
            """);
        migrationBuilder.Sql("""
            UPDATE "elsa_etl_connector_batches"
            SET "MainEffectKind" = CASE json_extract("OutputJson", '$.main.kind')
                    WHEN 'http' THEN 1 WHEN 'jsonl' THEN 2 ELSE NULL END,
                "ErrorEffectKind" = CASE json_extract("OutputJson", '$.errors.kind')
                    WHEN 'http' THEN 1 WHEN 'jsonl' THEN 2 ELSE NULL END
            WHERE "OutputJson" IS NOT NULL;
            """);
        migrationBuilder.CreateIndex(
            name: "IX_elsa_etl_connector_batches_ScopeKey_RunKey_Settled_PartitionOrdinal_MainEffectKind_ErrorEffectKind",
            table: "elsa_etl_connector_batches",
            columns: new[] { "ScopeKey", "RunKey", "Settled", "PartitionOrdinal", "MainEffectKind", "ErrorEffectKind" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Connector inspection counts cannot be downgraded safely. Restore a verified database backup instead.");
}
