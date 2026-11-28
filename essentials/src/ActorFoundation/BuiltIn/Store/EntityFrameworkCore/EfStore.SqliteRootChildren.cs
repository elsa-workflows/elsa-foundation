using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // The bounded terminal handle is canonical ASCII, so a SQLite trigger can derive its exact occurrence key.
    private const string SqliteChildNoticePath = "/child/notice/";

    private const string SqliteRemovedChild = """
        NOT EXISTS (SELECT 1 FROM json_each(coalesce(NEW."Children", '[]')) kept WHERE kept.value = removed.value)
        """;

    private static string SqliteTerminalChildRegistryWrite(KernelDb db)
    {
        if (!db.UsesRootChildren) return "";
        string[] unchanged =
        [
            "Id", "Partition", "Model", "Definition", "Status", "State", "Jobs", "NextJobGeneration", "CustomHandles",
            "Revision", "Parent", "Fault", "History", "Suspended", "StateVersion", "Retired",
            "FoundationReference", "FoundationTenant", "FoundationHeader", "FoundationWriteRevision", "FoundationTerminalRevision",
            "FoundationTerminalAt", "FoundationStateWire", "FoundationParkAt", "FoundationParkRevision", "FoundationParkClearing",
            "FoundationParkClearingKind", "FoundationParkCleared", "FoundationParkGeneration", "FoundationParkJobResult", "FoundationParkLiveJob"
        ];
        return $"""
            OR (OLD."FoundationTerminalRevision" = OLD."Revision" AND NOT OLD."Retired" AND OLD."Children" IS NOT NULL
                AND ({Row("NEW")}) IS ({Row("OLD")})
                AND json_type(coalesce(NEW."Children", '[]')) = 'array'
                AND json_array_length(coalesce(NEW."Children", '[]')) < json_array_length(OLD."Children")
                AND (SELECT count(DISTINCT kept.value) FROM json_each(coalesce(NEW."Children", '[]')) kept)
                    = json_array_length(coalesce(NEW."Children", '[]'))
                AND NOT EXISTS (SELECT 1 FROM json_each(coalesce(NEW."Children", '[]')) kept
                    WHERE NOT EXISTS (SELECT 1 FROM json_each(OLD."Children") prior WHERE prior.value = kept.value))
                AND NOT EXISTS (SELECT 1 FROM json_each(OLD."Children") removed
                    WHERE {SqliteRemovedChild} AND NOT EXISTS ({SqlitePendingFinalChildNotice})))
            """;

        string Row(string scope) => string.Join(", ", unchanged.Select(column => $"""{scope}."{column}" """));
    }

    // SQLite has no deferred trigger. Couple the only allowed terminal registry mutation to settlement.
    private static SqliteDefinition SqliteTerminalChildSettlement() => new("trigger", "kernel_settle_terminal_child_notice", "Instances", $"""
        CREATE TRIGGER kernel_settle_terminal_child_notice AFTER UPDATE OF "Children" ON "Instances"
        WHEN OLD."FoundationReference" IS NOT NULL AND OLD."FoundationTerminalRevision" = OLD."Revision"
            AND NOT OLD."Retired" AND NEW."Revision" = OLD."Revision" AND NEW."Children" IS NOT OLD."Children"
        BEGIN
            DELETE FROM "Messages"
            WHERE "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "Target" = OLD."Id" AND "Partition" = OLD."Partition"
                AND EXISTS (SELECT 1 FROM json_each(OLD."Children") removed WHERE {SqliteRemovedChild}
                    AND "Messages"."Id" IN ({SqlitePendingFinalChildNotice}));
        END
        """);

    private static readonly string SqlitePendingFinalChildNotice = RenderSqlitePendingFinalChildNotice();

    private static string RenderSqlitePendingFinalChildNotice()
    {
        var raw = $"substr(m.\"Start\", {RootChildNoticePrefix.Length + 1})";
        var wire = $"(CASE WHEN json_valid({raw}) THEN {raw} END)";
        var encoded = $"json_extract({wire}, '$.Payload')";
        var body = $"(CASE WHEN json_type({wire}, '$.Payload') = 'text' AND json_valid({encoded}) THEN {encoded} END)";
        const string handle = """
            ('c' || length(b."Actor") || '_' || b."Actor" || '_' || b."Creation" || '#' || child."FoundationTerminalRevision" || '.end')
            """;
        return $"""
            SELECT m."Id" FROM "Messages" m JOIN "FoundationRootBirths" b ON b."Actor" = m."Sender"
                AND b."Partition" = m."Partition" AND b."Tenant" = OLD."FoundationTenant" AND b."Creation" = 1
            JOIN "Instances" child ON child."Id" = b."Actor" AND child."Partition" = b."Partition"
                AND child."FoundationReference" = b."Reference" AND child."FoundationTenant" = b."Tenant"
            WHERE m."Target" = OLD."Id" AND m."Partition" = OLD."Partition" AND m."InboxScope" = {InboxKey.FoundationOccurrenceScope}
                AND m."Kind" = '{Kinds.ChildFinished}' AND m."Due" = 0 AND m."EventMatch" IS NULL AND NOT m."Done"
                AND m."ReplyTo" IS NULL AND m."KeepUntil" IS NULL AND m."Payload" IS NULL AND m."JobGeneration" = 0
                AND m."Start" GLOB '{RootChildNoticePrefix}*' AND m."Id" = OLD."FoundationReference" || '{SqliteChildNoticePath}' || {handle}
                AND b."Actor" = json_extract(removed.value, '$.Id') AND b."Creation" = json_extract(removed.value, '$.Creation')
                AND child."FoundationTerminalRevision" > 0 AND child."FoundationTerminalAt" IS NOT NULL
                AND (child."Parent" = OLD."Id" OR child."Retired")
                AND json_type({wire}, '$.Final') = 'true' AND json_extract({wire}, '$.Id') = {handle}
                AND json_extract({body}, '$.Child') = b."Actor" AND json_extract({body}, '$.Creation') = b."Creation"
                AND json_type({body}, '$.Creation') = 'integer' AND json_type({body}, '$.Final') = 'true'
                AND json_extract({body}, '$.Status') = child."Status" AND json_type({body}, '$.Status') = 'integer'
                AND coalesce(json_type({body}, '$.Output'), 'null') IN ('text', 'null')
                AND child."Status" IN ({(int)InstanceStatus.Finished}, {(int)InstanceStatus.Faulted}, {(int)InstanceStatus.Cancelled})
            """;
    }

    private sealed record SqliteEndedChildRegistry(long Revision, string? Children);

    private static async Task<string[]?> CloseSqliteEndedChildRegistryAsync(KernelDb db, WorkerRoot root, Message[] selected,
        CancellationToken token)
    {
        var notices = selected.Where(input => input.FoundationRootChildNotice && input.Read<FoundationChildNotice>().Final).ToArray();
        if (notices.Length == 0) return [];
        var actor = await db.Database.SqlQuery<SqliteEndedChildRegistry>($"""
            SELECT "Revision", "Children" FROM "Instances" WHERE "Id" = {root.Actor} AND "Partition" = {root.Partition}
                AND "FoundationTenant" = {root.Tenant} AND "FoundationReference" = {root.Reference}
                AND NOT "Retired" AND "FoundationTerminalRevision" = "Revision"
            """).SingleOrDefaultAsync(token);
        if (actor is null) return null;

        var registry = ChildRegistry.Read(actor.Children);
        var consumed = notices.Where(notice => registry.Remove(new(notice.Sender!, notice.SenderCreation)))
            .Select(notice => notice.InboxKey.Id).ToArray();
        if (consumed.Length == 0) return consumed;
        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "Children" = {ChildRegistry.Write(registry)}
            WHERE "Id" = {root.Actor} AND "Partition" = {root.Partition} AND "Revision" = {actor.Revision}
                AND "FoundationTenant" = {root.Tenant} AND "FoundationReference" = {root.Reference}
                AND "Children" IS {actor.Children} AND NOT "Retired" AND "FoundationTerminalRevision" = "Revision"
            """, token);
        if (updated != 1 || await db.Messages.AnyAsync(m => m.InboxScope == InboxKey.FoundationOccurrenceScope &&
            consumed.Contains(m.Id), token)) return null;
        return consumed;
    }

    private static async Task<bool> DeleteSqliteRootChildInputsAsync(KernelDb db, CreationAddress actor, int home, string[] ids,
        CancellationToken token) => await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "Messages" WHERE "InboxScope" = {InboxKey.FoundationOccurrenceScope}
                AND "Id" IN (SELECT value FROM json_each({Json.Write(ids)})) AND "Target" = {actor.Id}
                AND "Partition" = {home} AND "EventMatch" IS NULL AND "KeepUntil" IS NULL AND NOT "Done"
            """, token) == ids.Length;

    private static async Task<bool> BindSqliteRootChildStartsAsync(KernelDb db, string tenant, string envelopes, int count,
        CancellationToken token)
    {
        // The owned commit includes both writes and the creating parent's checkpoint.
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "FoundationRootBirths" ("Tenant","Reference","Actor","Partition","Creation","Creator","Fingerprint")
            SELECT {tenant}, json_extract(value, '$.Reference'), json_extract(value, '$.Actor'), json_extract(value, '$.Partition'),
                1, json_extract(value, '$.Creator'), json_extract(value, '$.Fingerprint')
            FROM json_each({envelopes})
            """, token) != count) return false;

        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH prior(Seq) AS MATERIALIZED (SELECT coalesce(max("Seq"), 0) FROM "Messages")
            INSERT INTO "Messages" ("Id","InboxScope","Target","Partition","Kind","Due","Payload","Start","Sender","Done","Seen","Attempts","Trial","JobGeneration","Seq")
            SELECT json_extract(value, '$.Creator'), 0, json_extract(value, '$.Actor'), json_extract(value, '$.Partition'),
                {Kinds.Start}, 0, json_extract(value, '$.Payload'), json_extract(value, '$.Header'), json_extract(value, '$.Sender'),
                false, 0, 0, 0, 0, prior.Seq + key + 1
            FROM json_each({envelopes}) CROSS JOIN prior ORDER BY key
            """, token) == count;
    }

    private static Task BindSqliteRootChildInputsAsync(KernelDb db, string tenant, string envelopes, CancellationToken token) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH inputs AS (
                SELECT key AS Ordinal, json_extract(value, '$.Id') AS Id, json_extract(value, '$.Actor') AS Actor,
                    json_extract(value, '$.Partition') AS Partition, json_extract(value, '$.Sender') AS Sender,
                    json_extract(value, '$.TargetCreation') AS TargetCreation, json_extract(value, '$.Kind') AS Kind,
                    json_extract(value, '$.Header') AS Header
                FROM json_each({envelopes})
            ), prior(Seq) AS MATERIALIZED (SELECT coalesce(max("Seq"), 0) FROM "Messages")
            INSERT INTO "Messages" ("Id","InboxScope","Target","Partition","Kind","Due","Start","Sender","Done","Seen","Attempts","Trial","JobGeneration","Seq")
            SELECT input.Id, {InboxKey.FoundationOccurrenceScope}, b."Actor", b."Partition", input.Kind, 0,
                input.Header, input.Sender, false, 0, 0, 0, 0, prior.Seq + input.Ordinal + 1
            FROM inputs input CROSS JOIN prior
            JOIN "FoundationRootBirths" b ON b."Tenant" = {tenant} AND b."Actor" = input.Actor
                AND b."Partition" = input.Partition AND b."Creation" = input.TargetCreation
            LEFT JOIN "Instances" i ON i."Id" = b."Actor" AND i."Partition" = b."Partition" AND i."FoundationReference" = b."Reference"
            WHERE i."Parent" = input.Sender AND i."FoundationTerminalRevision" IS NULL AND NOT i."Retired"
                OR i."Id" IS NULL AND EXISTS (SELECT 1 FROM "Messages" creator
                    WHERE creator."Id" = b."Creator" AND creator."Target" = b."Actor" AND creator."Partition" = b."Partition"
                        AND creator."Sender" = input.Sender AND creator."InboxScope" = 0 AND NOT creator."Done")
            ORDER BY input.Ordinal
            """, token);
}
