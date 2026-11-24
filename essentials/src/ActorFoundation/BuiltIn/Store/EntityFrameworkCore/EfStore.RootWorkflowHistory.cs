using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private const int WorkflowHistoryVersion = 1;
    private static readonly string[] RootListSourceTriggers = ["kernel_require_root_checkpoint_writer", "kernel_project_root_actor_list"];
    private static readonly string[] RootListWithParkTriggers = [..RootListSourceTriggers, "kernel_project_root_park_metadata"];
    private static readonly string[] RootListWithMarkTriggers = ["kernel_require_root_checkpoint_writer", "kernel_require_root_actor_list_v13", "kernel_project_root_actor_list_v13"];
    private static readonly string[] WorkflowHistorySourceTriggers = [..RootListSourceTriggers, "kernel_project_root_workflow_history_v1"];

    internal sealed record RootWorkflowHistoryItem(string Reference, InstanceStatus Status, string? Definition, long UpdatedAt);
    internal sealed record RootWorkflowHistoryPage(IReadOnlyList<RootWorkflowHistoryItem> Items, long AsOf);
    private sealed record RootWorkflowHistoryRow(string? Reference, int? Status, string? Header, long? UpdatedAt,
        int? Version, bool TriggerEnabled, int? StoreSchema, int? MessageProtocol, int? JournalCodec, long AsOf);

    internal async Task InstallRootWorkflowHistoryAsync(CancellationToken token = default)
    {
        if (!postgres || Engine is not null) throw new NotSupportedException("Qualified workflow history requires PostgreSQL.");
        await using var db = create();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await RequireStoreVersionsAsync(db, token, FormatLock.Shared);
        await RequirePreviewInstallSchemaAsync(db, token);
        await db.Database.ExecuteSqlRawAsync("""
            SELECT pg_advisory_xact_lock(48602345, 1);
            CREATE TABLE IF NOT EXISTS "FoundationWorkflowHistoryFormat" (
                "Singleton" boolean PRIMARY KEY CHECK ("Singleton"), "Version" integer NOT NULL);
            """, token);
        var versions = await db.Database.SqlQueryRaw<int>("""
            SELECT "Version" AS "Value" FROM "FoundationWorkflowHistoryFormat" WHERE "Singleton"
            """).ToListAsync(token);
        var installed = versions.Count == 1;
        if (installed) RequireHistoryVersion(versions[0]);
        var required = installed ? WorkflowHistorySourceTriggers : RootListSourceTriggers;
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT to_regclass('"FoundationActorList"') IS NOT NULL
                AND (NOT {installed} OR to_regclass('"FoundationWorkflowHistory"') IS NOT NULL)
                AND EXISTS (
                    SELECT 1 FROM pg_catalog.pg_trigger source
                    JOIN pg_catalog.pg_class relation ON relation.oid = source.tgrelid
                    JOIN pg_catalog.pg_proc installed ON installed.oid = source.tgfoid
                        AND installed.pronamespace = relation.relnamespace AND installed.proname = source.tgname
                    WHERE source.tgrelid = to_regclass('"Instances"')
                        AND source.tgname::text = ANY({required}) AND source.tgenabled IN ('O', 'A')
                    GROUP BY source.tgrelid HAVING count(*) = {required.Length}
                ) AS "Value"
            """).SingleAsync(token))
            throw new StoreFormatException("The installed workflow history source or one of its projection triggers is incomplete.");
        if (!installed)
        {
            // Hold the same root installation lock and publish the format, table and trigger together.
            // Legacy roots have no update time until a qualified checkpoint is observed.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "FoundationWorkflowHistory" (
                    "Tenant" text COLLATE "C" NOT NULL, "Reference" text COLLATE "C" NOT NULL,
                    "Revision" bigint NOT NULL CHECK ("Revision" > 0), "UpdatedAt" bigint NOT NULL,
                    PRIMARY KEY ("Tenant", "Reference"));
                CREATE INDEX "IX_FoundationWorkflowHistory_Chronology"
                    ON "FoundationWorkflowHistory" ("Tenant", "UpdatedAt" DESC, "Reference");
                CREATE FUNCTION kernel_project_root_workflow_history_v1() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM 1 FROM "FoundationWorkflowHistoryFormat" WHERE "Singleton" AND "Version" = 1 FOR SHARE;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'Unsupported workflow history projection version' USING ERRCODE = '55000';
                    END IF;
                    INSERT INTO "FoundationWorkflowHistory" AS history ("Tenant", "Reference", "Revision", "UpdatedAt")
                    SELECT birth."Tenant", birth."Reference", NEW."Revision", CASE
                        WHEN NEW."FoundationTerminalRevision" = NEW."Revision" THEN NEW."FoundationTerminalAt"
                        ELSE floor(extract(epoch from clock_timestamp()) * 1000)::bigint END
                    FROM "FoundationRootBirths" birth
                    WHERE birth."Tenant" = NEW."FoundationTenant" AND birth."Reference" = NEW."FoundationReference"
                        AND birth."Actor" = NEW."Id" AND birth."Partition" = NEW."Partition" AND birth."Creation" = 1
                        AND (OLD."FoundationReference" IS NULL AND NEW."Revision" = 1 OR
                            OLD."FoundationReference" = NEW."FoundationReference" AND NEW."Revision" = OLD."Revision" + 1
                            AND NEW."FoundationWriteRevision" = NEW."Revision")
                    ON CONFLICT ("Tenant", "Reference") DO UPDATE SET "Revision" = excluded."Revision", "UpdatedAt" = excluded."UpdatedAt"
                    WHERE history."Revision" < excluded."Revision";
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'Workflow history requires the next qualified root checkpoint' USING ERRCODE = '23514';
                    END IF;
                    RETURN NULL;
                END
                $fn$;
                CREATE TRIGGER kernel_project_root_workflow_history_v1
                AFTER UPDATE OF "FoundationReference", "Revision" ON "Instances"
                FOR EACH ROW WHEN (NEW."FoundationReference" IS NOT NULL AND NOT NEW."Retired" AND (
                    OLD."FoundationReference" IS DISTINCT FROM NEW."FoundationReference" OR
                    OLD."Revision" IS DISTINCT FROM NEW."Revision"))
                EXECUTE FUNCTION kernel_project_root_workflow_history_v1();
                INSERT INTO "FoundationWorkflowHistoryFormat" ("Singleton", "Version") VALUES (true, 1);
                """, token);
        }
        await transaction.CommitAsync(token);
    }

    internal async Task<RootWorkflowHistoryPage> ReadRootWorkflowHistoryAsync(string tenant, string type, string? definition,
        int? status, long? from, long? to, long? afterTime, string? afterRef, int limit, CancellationToken token)
    {
        if (!postgres || Engine is not null) throw new NotSupportedException("Qualified workflow history requires PostgreSQL.");
        await using var db = create();
        var expected = db.RequiredFormat;
        var encodedType = DurableKeyJson.Write(type);
        var encodedDefinition = definition is null ? null : DurableKeyJson.Write(new KeyValuePair<string, string>("definition", definition));
        List<RootWorkflowHistoryRow> rows;
        try
        {
            rows = await db.Database.SqlQuery<RootWorkflowHistoryRow>($"""
                WITH kernel AS (
                    SELECT "StoreSchema", "MessageProtocol", "JournalCodec" FROM "KernelStoreVersions" WHERE "Id" = {StoreVersionEntity.MarkerId}
                ), format AS (
                    SELECT "Version" FROM "FoundationWorkflowHistoryFormat" WHERE "Singleton"
                ), readiness AS (
                    -- A renamed source can still serve its projection until a canonical source replaces it.
                    SELECT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_trigger source
                        JOIN pg_catalog.pg_class relation ON relation.oid = source.tgrelid
                        JOIN pg_catalog.pg_proc installed ON installed.oid = source.tgfoid
                            AND installed.pronamespace = relation.relnamespace AND installed.proname = source.tgname
                        WHERE relation.relnamespace = (SELECT relnamespace FROM pg_catalog.pg_class
                                WHERE oid = to_regclass('"FoundationWorkflowHistory"'))
                            AND (to_regclass('"Instances"') IS NULL OR source.tgrelid = to_regclass('"Instances"'))
                            AND source.tgname::text = ANY({WorkflowHistorySourceTriggers}) AND source.tgenabled IN ('O', 'A')
                        GROUP BY source.tgrelid HAVING count(*) = {WorkflowHistorySourceTriggers.Length}
                    ) AS "TriggerEnabled"
                ), page AS (
                    SELECT l."Reference", l."Status", l."Header", h."UpdatedAt"
                    FROM "FoundationWorkflowHistory" h JOIN "FoundationActorList" l USING ("Tenant", "Reference")
                    WHERE h."Tenant" = {tenant} AND (SELECT "TriggerEnabled" FROM readiness)
                        AND EXISTS (SELECT 1 FROM format WHERE "Version" = {WorkflowHistoryVersion})
                        AND EXISTS (SELECT 1 FROM kernel WHERE "StoreSchema" = {expected.StoreSchema}
                            AND "MessageProtocol" = {expected.MessageProtocol} AND "JournalCodec" = {expected.JournalCodec})
                        AND l."Type" = kernel_actor_list_filter({encodedType})
                        AND ({encodedDefinition}::text IS NULL OR EXISTS (SELECT 1 FROM jsonb_array_elements(l."Labels") label(entry)
                            WHERE label.entry = kernel_actor_list_filter({encodedDefinition}::text)))
                        AND ({status}::integer IS NULL OR l."Status" = {status})
                        AND ({from}::bigint IS NULL OR h."UpdatedAt" >= {from})
                        AND ({to}::bigint IS NULL OR h."UpdatedAt" <= {to})
                        AND ({afterTime}::bigint IS NULL OR h."UpdatedAt" < {afterTime} OR
                            h."UpdatedAt" = {afterTime} AND h."Reference" > {afterRef})
                    ORDER BY h."UpdatedAt" DESC, h."Reference" LIMIT {limit}
                )
                SELECT page.*, kernel.*, (SELECT "Version" FROM format) AS "Version",
                    (SELECT "TriggerEnabled" FROM readiness) AS "TriggerEnabled",
                    floor(extract(epoch from statement_timestamp()) * 1000)::bigint AS "AsOf"
                FROM (VALUES (1)) stamp(id) LEFT JOIN page ON true LEFT JOIN kernel ON true
                ORDER BY page."UpdatedAt" DESC, page."Reference" COLLATE "C"
                """).ToListAsync(token);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedFunction)
        {
            throw new StoreFormatException("The qualified workflow history projection is missing.", error);
        }
        var first = rows[0];
        if (first.StoreSchema is not { } schema || first.MessageProtocol is not { } protocol || first.JournalCodec is not { } codec)
            throw new StoreFormatException("The kernel store version marker is missing.");
        var stored = new StoreFormat(schema, protocol, codec);
        if (stored != expected) throw new StoreFormatException($"Unsupported kernel store versions. Stored {stored}. This build reads {expected}.");
        RequireHistoryVersion(first.Version);
        if (!first.TriggerEnabled) throw new StoreFormatException("The workflow history source or one of its projection triggers is missing or disabled.");
        var items = rows.Where(row => row.Reference is not null).Select(row =>
        {
            var definition = DurableKeyJson.Read<RootHeader>(row.Header!).Labels.FirstOrDefault(label => label.Key == "definition").Value;
            return new RootWorkflowHistoryItem(row.Reference!, (InstanceStatus)row.Status!.Value, definition, row.UpdatedAt!.Value);
        }).ToArray();
        return new(items, first.AsOf);
    }

    private static void RequireHistoryVersion(int? version)
    {
        if (version != WorkflowHistoryVersion)
            throw new StoreFormatException($"Unsupported workflow history projection version. Stored {version?.ToString() ?? "missing"}. This build reads {WorkflowHistoryVersion}.");
    }
}
