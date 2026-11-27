using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record PriorTimer(bool Fits, string? Id, long? Seq, string? Kind, string? Payload,
        string? Sender, string? Start, long? Due, long? KeepUntil, string? ReplyTo, long? JobGeneration);

    // A recipe rejected by an ordinary Done timer is an authored plan refusal, rather than a lost commit.
    // This read makes that decision before the owner queues the command. The transactional bind repeats it.
    private async ValueTask<bool> CheckRootTimerRearmsAsync(string actor, int partition, IReadOnlyList<ParkTimerRecipe> recipes,
        CancellationToken token)
    {
        if (RootSelfIssuer is not { RearmsTimers: true } issuer || RootTimerMessages(actor, issuer, recipes) is not { } messages)
            return false;
        await using var db = create();
        if (db.UsesSqliteRootRuntime) await RequireSqliteOperationalFormatAsync(db, token);
        else await RequireStoreVersionsAsync(db, token);
        return db.UsesRootTimerRearm &&
            await MatchPriorRootTimersAsync(db, actor, partition, issuer, messages, lockRows: false, token) is not null;
    }

    private static Dictionary<string, Message>? RootTimerMessages(string actor, FoundationSelfIssuer issuer,
        IReadOnlyList<ParkTimerRecipe> recipes)
    {
        if (recipes.Count is 0 or > InMemoryStore.MaxRestoreWaits ||
            recipes.Sum(FoundationRootParkTimer.RearmBytes) > InMemoryStore.MaxRestoreBytes) return null;
        var origin = CreationProtocol.Origin(actor, 1);
        var messages = new Dictionary<string, Message>(StringComparer.Ordinal);
        foreach (var recipe in recipes)
        {
            if (recipe.Id is not { } id || recipe.Kind is null || !FoundationSelfSend.HasTimerTag(issuer.Handle, origin, id)) return null;
            var timer = FoundationSelfSend.Create(actor, 1, id, recipe.Kind, recipe.Payload, recipe.Due);
            if (!FoundationSelfSend.IsSelfInput(timer, actor, 1) || !FoundationMetadataLimits.FitsComposed(timer) ||
                !messages.TryAdd(timer.InboxKey.Id, timer)) return null;
        }
        return messages;
    }

    private static async Task<PriorTimer[]?> MatchPriorRootTimersAsync(KernelDb db, string actor, int partition,
        FoundationSelfIssuer issuer, Dictionary<string, Message> messages, bool lockRows, CancellationToken token)
    {
        var prior = await ReadDoneRootTimersAsync(db, actor, partition, messages.Keys.ToArray(), lockRows, token);
        if (!prior[0].Fits) return null;
        var done = prior.Where(row => row.Id is not null).ToArray();
        foreach (var previous in done)
        {
            var recipe = messages[previous.Id!];
            if (previous.Kind != RootSelfTimerWire || previous.Sender != actor || previous.Due != recipe.Due ||
                previous.Start is not null || previous.KeepUntil is not null || previous.ReplyTo is not null || previous.JobGeneration != 0)
                return null;
            // A completed private timer can have no retained payload. An extant body must be the same recipe.
            if (previous.Payload is null) continue;
            try
            {
                if (DecodeRootSelf(new()
                    {
                        Id = previous.Id!, InboxScope = RootSelfScope, Target = actor, Kind = previous.Kind, Sender = previous.Sender,
                        Due = previous.Due!.Value, Payload = previous.Payload
                    }, issuer) != recipe) return null;
            }
            catch (Exception error) when (error is NotSupportedException or FormatException or System.Text.Json.JsonException)
            {
                return null;
            }
        }
        return done;
    }

    private static async Task<bool> BindRootTimerRearmsAsync(KernelDb db, WorkerRoot root, long revision, FoundationSelfIssuer? issuer,
        IReadOnlyList<ParkTimerRecipe> recipes, string? restorableTimers, CancellationToken token)
    {
        if (recipes.Count == 0) return true;
        if (!db.UsesRootTimerRearm || issuer is not { RearmsTimers: true } || restorableTimers is null ||
            RootTimerMessages(root.Actor, issuer, recipes) is not { } messages)
            return false;
        var available = DurableKeyJson.Read<FoundationRootParkTimer[]>(restorableTimers)
            .Select(timer => timer.Id).ToHashSet(StringComparer.Ordinal);
        if (recipes.Any(recipe => available.Contains(recipe.Id))) return false;

        var done = await MatchPriorRootTimersAsync(db, root.Actor, root.Partition, issuer, messages, lockRows: true, token);
        if (done is null) return false;
        var sqlite = db.UsesSqliteRootRuntime;
        var origin = CreationProtocol.Origin(root.Actor, 1);
        var ids = recipes.Select(recipe => origin + "#" + recipe.Id).ToArray();
        if (done.Length != 0)
        {
            var matched = done.Select(row => row.Id!).ToArray();
            var count = sqlite
                ? await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM "Messages" WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition}
                        AND "InboxScope" = {RootSelfScope} AND "Done" AND "FoundationKeptBy" IS NULL AND "EventMatch" IS NULL
                        AND "Id" IN (SELECT value FROM json_each({Json.Write(matched)}))
                    """, token)
                : await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM "Messages" WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition}
                        AND "InboxScope" = {RootSelfScope} AND "Done" AND "FoundationKeptBy" IS NULL AND "EventMatch" IS NULL
                        AND "Id" = ANY({matched})
                    """, token);
            if (count != done.Length) return false;
        }

        var wires = ids.Select(id => messages[id]).Select(message => EncodeRootSelfEnvelope(issuer, message,
            new(revision, 0, message.Kind, message.Due, Rearmed: true))).ToArray();
        if (sqlite)
        {
            // Include the removed sequence in the floor, even when deletion removed the former high-water row.
            var priorSequence = done.Length == 0 ? 0 : done.Max(row => row.Seq!.Value);
            var rows = Json.Write(wires.Select(wire => new { wire.Id, wire.Payload, wire.Due }).ToArray());
            return await db.Database.ExecuteSqlInterpolatedAsync($"""
                WITH inputs AS MATERIALIZED (SELECT key, value FROM json_each({rows})),
                    boundary AS MATERIALIZED (
                        SELECT max({priorSequence}, coalesce((SELECT MAX("Seq") FROM "Messages"), 0)) AS seq)
                INSERT INTO "Messages" ("Id", "InboxScope", "Partition", "Target", "Kind", "Payload", "Sender", "Due",
                    "Seq", "Done", "Seen", "Attempts", "Trial", "JobGeneration")
                SELECT json_extract(inputs.value, '$.Id'), {RootSelfScope}, {root.Partition}, {root.Actor}, {RootSelfTimerWire},
                    json_extract(inputs.value, '$.Payload'), {root.Actor}, json_extract(inputs.value, '$.Due'),
                    boundary.seq + inputs.key + 1, 0, 0, 0, 0, 0
                FROM inputs CROSS JOIN boundary WHERE boundary.seq <= {long.MaxValue - wires.Length}
                ORDER BY inputs.key
                """, token) == wires.Length;
        }
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Messages" ("Id", "InboxScope", "Partition", "Target", "Kind", "Payload", "Sender", "Due",
                "Done", "Seen", "Attempts", "Trial", "JobGeneration")
            SELECT input.id, {RootSelfScope}, {root.Partition}, {root.Actor}, {RootSelfTimerWire}, input.payload, {root.Actor}, input.due,
                false, 0, 0, 0, 0
            FROM unnest({ids}::text[], {wires.Select(wire => wire.Payload!).ToArray()}::text[],
                {wires.Select(wire => wire.Due!.Value).ToArray()}::bigint[]) WITH ORDINALITY AS input(id, payload, due, ordinal)
            ORDER BY input.ordinal
            """, token) == wires.Length;
    }

    private static Task<List<PriorTimer>> ReadDoneRootTimersAsync(KernelDb db, string actor, int partition, string[] ids,
        bool lockRows, CancellationToken token)
    {
        var sqlite = db.UsesSqliteRootRuntime;
        var source = sqlite ? "\"Messages\"" : PostgresPrivateInboxView;
        var keys = sqlite ? """ k."Id" IN (SELECT value FROM json_each({2})) """ : """ k."Id" = ANY({2}) """;
        var rowLock = !sqlite && lockRows ? "FOR UPDATE OF p" : "";
        var sql = $$"""
            WITH selected AS MATERIALIZED (
                SELECT k."Id", k."Seq", {{KeptBytes(db)}} AS "Bytes",
                    (k."Kind" = '{{RootSelfTimerWire}}' AND k."Done" AND k."FoundationKeptBy" IS NULL) AS "Replaceable"
                FROM "Messages" k WHERE k."Target" = {0} AND k."Partition" = {1}
                    AND k."InboxScope" = {{RootSelfScope}} AND k."EventMatch" IS NULL AND {{keys}}
            ), quote AS MATERIALIZED (
                SELECT NOT EXISTS (SELECT 1 FROM selected WHERE NOT "Replaceable")
                    AND coalesce(sum("Bytes" + {{InMemoryStore.RestoreWaitAllowance}}), 0) <= {{InMemoryStore.MaxRestoreBytes}} AS "Fits"
                FROM selected
            ), bodies AS MATERIALIZED (
                SELECT k."Id", k."Seq", p."Kind", p."Payload", p."Sender", p."Start", p."Due", p."KeepUntil", p."ReplyTo", p."JobGeneration"
                FROM quote f CROSS JOIN selected k JOIN {{source}} p ON p."Id" = k."Id"
                    AND p."InboxScope" = {{RootSelfScope}} AND p."EventMatch" IS NULL
                    AND p."Target" = {0} AND p."Partition" = {1}
                WHERE f."Fits" {{rowLock}}
            )
            SELECT f."Fits", p.* FROM quote f LEFT JOIN bodies p ON true ORDER BY p."Seq"
            """;
        return db.Database.SqlQuery<PriorTimer>(FormattableStringFactory.Create(sql, actor, partition,
            sqlite ? Json.Write(ids) : (object)ids)).ToListAsync(token);
    }
}
