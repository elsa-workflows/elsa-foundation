using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
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
