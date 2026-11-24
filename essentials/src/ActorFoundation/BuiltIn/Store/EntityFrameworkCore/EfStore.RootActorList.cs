using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal sealed record RootListItem(string Reference, string Type, InstanceStatus Status, long? EndedAt, long? TerminalRevision,
        string TypeData, IReadOnlyDictionary<string, string> Labels);
    internal sealed record RootListPage(IReadOnlyList<RootListItem> Items, long AsOf);

    private sealed record RootListRow(string? Reference, int? Status, long? EndedAt, long? TerminalRevision, string? Header, long AsOf, bool SourceReady,
        int? StoreSchema, int? MessageProtocol, int? JournalCodec);
    private sealed record RootPointRow(string? Reference, int? Status, long? EndedAt, long? TerminalRevision, string? Header, long AsOf, bool SourceReady,
        int? StoreSchema, int? MessageProtocol, int? JournalCodec);

    internal static async Task RequirePreviewInstallSchemaAsync(KernelDb db, CancellationToken token)
    {
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT to_regclass('"Instances"') IS NOT NULL AND NOT EXISTS (
                SELECT 1 FROM (VALUES ('"Instances"'), ('"Journal"'), ('"Partitions"'), ('"KernelStoreVersions"'),
                    ('"FoundationRootIssuer"'), ('"FoundationRootBirths"'), ('"FoundationRootReceipts"'),
                    ('"FoundationActorList"'), ('"FoundationWorkflowHistory"'), ('"FoundationWorkflowHistoryFormat"')) expected(name)
                JOIN pg_catalog.pg_class relation ON relation.oid = to_regclass(expected.name)
                JOIN pg_catalog.pg_namespace ns ON ns.oid = relation.relnamespace
                WHERE ns.nspname IS DISTINCT FROM pg_catalog.current_schema()
            ) AS "Value"
            """).SingleAsync(token))
            throw new StoreFormatException("PostgreSQL preview installation requires the store relations in the active schema.");
    }

    internal static Task InstallRootActorListAsync(KernelDb db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync("""
            -- Keep the original header for decoding in .NET. PostgreSQL jsonb cannot represent a NUL.
            -- Escape both NUL and literal backslashes injectively before using jsonb for comparisons.
            -- The scanner consumes complete JSON escapes, including a backslash paired with another.
            CREATE OR REPLACE FUNCTION kernel_actor_list_filter(source_json text) RETURNS jsonb LANGUAGE sql IMMUTABLE STRICT AS $fn$
                SELECT string_agg(CASE token[1]
                    WHEN $s$\\$s$ THEN $s$\\\\$s$
                    WHEN $s$\u0000$s$ THEN $s$\\u0000$s$
                    ELSE token[1] END, '' ORDER BY ordinality)::jsonb
                FROM regexp_matches(source_json, $pattern$[^\\]+|\\(?:u0000|.)$pattern$, 'g') WITH ORDINALITY AS parts(token, ordinality);
            $fn$;

            -- The root checkpoint is already protected by the owning transaction's fence. Materialize
            -- only changed public metadata, including silent turns, before that transaction commits.
            CREATE OR REPLACE FUNCTION kernel_project_root_actor_list() RETURNS trigger LANGUAGE plpgsql AS $fn$
            DECLARE filter jsonb := kernel_actor_list_filter(NEW."FoundationHeader");
            BEGIN
                INSERT INTO "FoundationActorList" ("Tenant", "Reference", "Status", "EndedAt", "Header", "Type", "Labels")
                VALUES (NEW."FoundationTenant", NEW."FoundationReference", NEW."Status", NEW."FoundationTerminalAt",
                    NEW."FoundationHeader", filter->'Model', filter->'Labels')
                ON CONFLICT ("Tenant", "Reference") DO UPDATE SET "Status" = excluded."Status", "EndedAt" = excluded."EndedAt",
                    "Header" = excluded."Header", "Type" = excluded."Type", "Labels" = excluded."Labels";
                RETURN NEW;
            END
            $fn$;
            DO $install$
            BEGIN
                IF to_regclass('"FoundationActorList"') IS NOT NULL THEN RETURN; END IF;

                CREATE TABLE "FoundationActorList" (
                    "Tenant" text COLLATE "C" NOT NULL, "Reference" text COLLATE "C" NOT NULL,
                    "Status" integer NOT NULL, "EndedAt" bigint, "Header" text NOT NULL,
                    "Type" jsonb NOT NULL, "Labels" jsonb NOT NULL,
                    PRIMARY KEY ("Tenant", "Reference"));

                CREATE TRIGGER kernel_project_root_actor_list AFTER UPDATE OF "FoundationHeader", "Status", "FoundationTerminalAt"
                ON "Instances" FOR EACH ROW WHEN (NEW."FoundationReference" IS NOT NULL AND (
                    OLD."FoundationReference" IS DISTINCT FROM NEW."FoundationReference" OR
                    OLD."FoundationHeader" IS DISTINCT FROM NEW."FoundationHeader" OR
                    OLD."Status" IS DISTINCT FROM NEW."Status" OR OLD."FoundationTerminalAt" IS DISTINCT FROM NEW."FoundationTerminalAt"))
                EXECUTE FUNCTION kernel_project_root_actor_list();

                -- Trigger installation excludes concurrent checkpoint writes until installation commits.
                -- Backfill earlier previews from public metadata alone, once per database.
                INSERT INTO "FoundationActorList" ("Tenant", "Reference", "Status", "EndedAt", "Header", "Type", "Labels")
                SELECT i."FoundationTenant", i."FoundationReference", i."Status", i."FoundationTerminalAt", i."FoundationHeader",
                    f.header->'Model', f.header->'Labels'
                FROM "Instances" i CROSS JOIN LATERAL (SELECT kernel_actor_list_filter(i."FoundationHeader") AS header) f
                WHERE i."FoundationReference" IS NOT NULL AND NOT i."Retired";
            END
            $install$;
            """, cancellationToken);

    internal static Task InstallRootTerminalMarksAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "FoundationActorList" ADD COLUMN IF NOT EXISTS "TerminalRevision" bigint;
            CREATE OR REPLACE FUNCTION kernel_project_root_terminal_mark() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                UPDATE "FoundationActorList" SET "TerminalRevision" = NEW."FoundationTerminalRevision",
                    "EndedAt" = NEW."FoundationTerminalAt"
                WHERE "Tenant" = NEW."FoundationTenant" AND "Reference" = NEW."FoundationReference";
                RETURN NEW;
            END
            $fn$;
            CREATE OR REPLACE TRIGGER kernel_project_root_terminal_mark
            AFTER UPDATE OF "FoundationTerminalRevision" ON "Instances"
            FOR EACH ROW WHEN (NEW."FoundationReference" IS NOT NULL AND
                OLD."FoundationTerminalRevision" IS DISTINCT FROM NEW."FoundationTerminalRevision")
            EXECUTE FUNCTION kernel_project_root_terminal_mark();

            UPDATE "FoundationActorList" p SET "TerminalRevision" = i."FoundationTerminalRevision",
                "EndedAt" = i."FoundationTerminalAt"
            FROM "Instances" i WHERE i."FoundationTenant" = p."Tenant" AND i."FoundationReference" = p."Reference"
                AND NOT i."Retired" AND
                (p."TerminalRevision", p."EndedAt") IS DISTINCT FROM (i."FoundationTerminalRevision", i."FoundationTerminalAt");
            """, token);

    private static FormattableString ActorListQuery(KernelDb db, FormattableString query) =>
        FormattableStringFactory.Create(query.Format.Replace("/*terminal-mark*/",
            db.UsesRootStableBody ? """l."TerminalRevision" """ : "NULL::bigint", StringComparison.Ordinal), query.GetArguments());

    internal async Task<(RootListItem Item, long AsOf)?> ReadRootActorAsync(string tenant, string reference,
        CancellationToken cancellationToken)
    {
        if (!postgres || Engine is not null) throw new NotSupportedException("Qualified root actor reads require PostgreSQL.");
        await using var db = create();
        var expected = db.RequiredFormat;
        var triggers = db.UsesRootStableBody ? RootListWithMarkTriggers : RootListSourceTriggers;
        RootPointRow row;
        try
        {
            row = await db.Database.SqlQuery<RootPointRow>(ActorListQuery(db, $"""
                SELECT l."Reference", l."Status", l."EndedAt", /*terminal-mark*/ AS "TerminalRevision", l."Header",
                    kernel."StoreSchema", kernel."MessageProtocol", kernel."JournalCodec",
                    floor(extract(epoch from statement_timestamp()) * 1000)::bigint AS "AsOf",
                    EXISTS (
                        SELECT 1 FROM pg_catalog.pg_trigger source
                        JOIN pg_catalog.pg_class relation ON relation.oid = source.tgrelid
                        JOIN pg_catalog.pg_proc installed ON installed.oid = source.tgfoid
                            AND installed.pronamespace = relation.relnamespace AND installed.proname = source.tgname
                        WHERE source.tgrelid = to_regclass('"Instances"')
                            AND source.tgname::text = ANY({triggers}) AND source.tgenabled IN ('O', 'A')
                        GROUP BY source.tgrelid HAVING count(*) = {triggers.Length}
                    ) AS "SourceReady"
                FROM (VALUES (1)) stamp(id)
                LEFT JOIN "KernelStoreVersions" kernel ON kernel."Id" = {StoreVersionEntity.MarkerId}
                LEFT JOIN "FoundationActorList" l ON l."Tenant" = {tenant} AND l."Reference" = {reference}
                    AND kernel."StoreSchema" = {expected.StoreSchema} AND kernel."MessageProtocol" = {expected.MessageProtocol}
                    AND kernel."JournalCodec" = {expected.JournalCodec}
                """)).SingleAsync(cancellationToken);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            throw new StoreFormatException("The root actor projection has an unreadable schema.", error);
        }
        catch (PostgresException error) when (error.SqlState.StartsWith("42", StringComparison.Ordinal) &&
            error.SqlState != PostgresErrorCodes.InsufficientPrivilege)
        {
            // PostgreSQL may reject a changed schema during query analysis, before evaluating the marker join.
            await RequireStoreVersionsAsync(db, cancellationToken);
            throw;
        }
        RequireSnapshotFormat(expected, row.StoreSchema, row.MessageProtocol, row.JournalCodec);
        RequireRootActorListSource(row.SourceReady);
        return row.Reference is null ? null : (ToRootListItem(row.Reference, row.Status!.Value, row.EndedAt, row.TerminalRevision, row.Header!), row.AsOf);
    }

    internal async Task<RootListPage> ReadRootActorListAsync(string tenant, string after, string? type, int? status,
        KeyValuePair<string, string>[] labels, long? endedBefore, bool? parked, int limit, CancellationToken cancellationToken)
    {
        if (!postgres || Engine is not null) throw new NotSupportedException("Qualified root actor lists require PostgreSQL.");
        await using var db = create();
        var expected = db.RequiredFormat;
        var triggers = db.UsesRootStableBody ? RootListWithMarkTriggers : RootListSourceTriggers;
        var encodedType = type is null ? null : DurableKeyJson.Write(type);
        var encodedLabels = DurableKeyJson.Write(labels);
        List<RootListRow> rows;
        try
        {
            rows = await db.Database.SqlQuery<RootListRow>(ActorListQuery(db, $"""
                WITH marker AS (
                    SELECT "StoreSchema", "MessageProtocol", "JournalCodec"
                    FROM "KernelStoreVersions" WHERE "Id" = {StoreVersionEntity.MarkerId}
                ), readiness AS (
                    SELECT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_trigger source
                        JOIN pg_catalog.pg_class relation ON relation.oid = source.tgrelid
                        JOIN pg_catalog.pg_proc installed ON installed.oid = source.tgfoid
                            AND installed.pronamespace = relation.relnamespace AND installed.proname = source.tgname
                        WHERE source.tgrelid = to_regclass('"Instances"')
                            AND source.tgname::text = ANY({triggers}) AND source.tgenabled IN ('O', 'A')
                        GROUP BY source.tgrelid HAVING count(*) = {triggers.Length}
                    ) AS "SourceReady"
                ), page AS (
                    SELECT l."Reference", l."Status", l."EndedAt", /*terminal-mark*/ AS "TerminalRevision", l."Header"
                    FROM "FoundationActorList" l
                    WHERE l."Tenant" = {tenant} AND l."Reference" > {after}
                        AND (SELECT "SourceReady" FROM readiness)
                        AND EXISTS (SELECT 1 FROM marker WHERE "StoreSchema" = {expected.StoreSchema}
                            AND "MessageProtocol" = {expected.MessageProtocol} AND "JournalCodec" = {expected.JournalCodec})
                        AND ({encodedType}::text IS NULL OR l."Type" = kernel_actor_list_filter({encodedType}::text))
                        AND ({status}::integer IS NULL OR l."Status" = {status}::integer)
                        AND ({endedBefore}::bigint IS NULL OR l."EndedAt" < {endedBefore}::bigint)
                        AND {parked}::boolean IS NOT TRUE
                        AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(kernel_actor_list_filter({encodedLabels})) criterion(entry)
                            WHERE NOT EXISTS (SELECT 1 FROM jsonb_array_elements(l."Labels") label(entry) WHERE label.entry = criterion.entry))
                    ORDER BY l."Reference" LIMIT {limit}
                )
                SELECT page."Reference", page."Status", page."EndedAt", page."TerminalRevision", page."Header",
                    marker."StoreSchema", marker."MessageProtocol", marker."JournalCodec",
                    (SELECT "SourceReady" FROM readiness) AS "SourceReady",
                    floor(extract(epoch from statement_timestamp()) * 1000)::bigint AS "AsOf"
                FROM (VALUES (1)) stamp(id) LEFT JOIN page ON true LEFT JOIN marker ON true
                ORDER BY page."Reference" COLLATE "C"
                """)).ToListAsync(cancellationToken);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            throw new StoreFormatException("The root list has an unreadable schema.", error);
        }
        catch (PostgresException error) when (error.SqlState.StartsWith("42", StringComparison.Ordinal) &&
            error.SqlState != PostgresErrorCodes.InsufficientPrivilege)
        {
            await RequireStoreVersionsAsync(db, cancellationToken);
            throw;
        }
        var first = rows[0];
        RequireSnapshotFormat(expected, first.StoreSchema, first.MessageProtocol, first.JournalCodec);
        RequireRootActorListSource(first.SourceReady);
        var items = rows.Where(row => row.Reference is not null).Select(row => ToRootListItem(row.Reference!, row.Status!.Value, row.EndedAt, row.TerminalRevision, row.Header!)).ToArray();
        return new(items, first.AsOf);
    }

    private static void RequireRootActorListSource(bool ready)
    {
        if (!ready) throw new StoreFormatException("The root actor projection source or a required trigger is missing or disabled.");
    }

    private static RootListItem ToRootListItem(string reference, int status, long? endedAt, long? terminalRevision, string encodedHeader)
    {
        var header = DurableKeyJson.Read<RootHeader>(encodedHeader);
        return new(reference, header.Model, (InstanceStatus)status, endedAt, terminalRevision, header.TypeData, header.Labels.ToDictionary(StringComparer.Ordinal));
    }
}
