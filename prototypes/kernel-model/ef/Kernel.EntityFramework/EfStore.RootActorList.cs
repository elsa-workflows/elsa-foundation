using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal sealed record RootListItem(string Reference, string Type, InstanceStatus Status, long? EndedAt,
        string TypeData, IReadOnlyDictionary<string, string> Labels);
    internal sealed record RootListPage(IReadOnlyList<RootListItem> Items, long AsOf);

    private sealed record RootListRow(string? Reference, int? Status, long? EndedAt, string? Header, long AsOf);

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

    internal async Task<RootListPage> ReadRootActorListAsync(string tenant, string after, string? type, int? status,
        KeyValuePair<string, string>[] labels, long? endedBefore, bool? parked, int limit, CancellationToken cancellationToken)
    {
        if (!postgres || Engine is not null) throw new NotSupportedException("Qualified root actor lists require PostgreSQL.");
        await using var db = create();
        await RequireStoreVersionsAsync(db, cancellationToken);
        var encodedType = type is null ? null : DurableKeyJson.Write(type);
        var encodedLabels = DurableKeyJson.Write(labels);
        var rows = await db.Database.SqlQuery<RootListRow>($"""
            WITH page AS (
                SELECT l."Reference", l."Status", l."EndedAt", l."Header"
                FROM "FoundationActorList" l
                WHERE l."Tenant" = {tenant} AND l."Reference" > {after}
                    AND ({encodedType}::text IS NULL OR l."Type" = kernel_actor_list_filter({encodedType}::text))
                    AND ({status}::integer IS NULL OR l."Status" = {status}::integer)
                    AND ({endedBefore}::bigint IS NULL OR l."EndedAt" < {endedBefore}::bigint)
                    AND {parked}::boolean IS NOT TRUE
                    AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(kernel_actor_list_filter({encodedLabels})) criterion(entry)
                        WHERE NOT EXISTS (SELECT 1 FROM jsonb_array_elements(l."Labels") label(entry) WHERE label.entry = criterion.entry))
                ORDER BY l."Reference" LIMIT {limit}
            )
            SELECT page."Reference", page."Status", page."EndedAt", page."Header",
                floor(extract(epoch from statement_timestamp()) * 1000)::bigint AS "AsOf"
            FROM (VALUES (1)) stamp(id) LEFT JOIN page ON true
            ORDER BY page."Reference" COLLATE "C"
            """).ToListAsync(cancellationToken);
        var items = rows.Where(row => row.Reference is not null).Select(row =>
        {
            var header = DurableKeyJson.Read<RootHeader>(row.Header!);
            return new RootListItem(row.Reference!, header.Model, (InstanceStatus)row.Status!.Value, row.EndedAt,
                header.TypeData, header.Labels.ToDictionary(StringComparer.Ordinal));
        }).ToArray();
        return new(items, rows[0].AsOf);
    }
}
