using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using static Kernel.EntityFramework.RootAdmissionWire;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal static class RootEventAdmission
{
    private sealed record WaitTarget(bool SourceReady, string? Actor, string? Reference, int? Partition, string? Wait);
    private sealed record ReceiptState(bool SourceReady, bool Duplicate);

    internal static bool IsValid(HeldRootRequest request, string key, string? payload) =>
        request.IsOpen && FoundationMetadataLimits.IsOutsideMessageId(request.Caller.Id) &&
        FoundationMetadataLimits.FitsLiveWaitName(key) && FoundationMetadataLimits.FitsContent(payload, FoundationMetadataLimits.EventContentBytes);

    // All registrations and publications of a qualified key enter the same transaction order. The sorted lock IDs
    // also give a multi-wait checkpoint one acquisition order even if two keys share a hash.
    internal static Task LockPostgresKeysAsync(KernelDb db, string[] keys, CancellationToken token) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_catalog.pg_advisory_xact_lock(ordered.lock_id)
            FROM (SELECT DISTINCT pg_catalog.hashtextextended(key COLLATE "C", 625728219) AS lock_id
                FROM unnest({keys}::text[]) AS waits(key)) ordered
            ORDER BY ordered.lock_id
            """, token);

    internal static Task<long> ReadPostgresClockAsync(KernelDb db, CancellationToken token) => db.Database.SqlQueryRaw<long>("""
        SELECT floor(extract(epoch from clock_timestamp()) * 1000)::bigint AS "Value"
        """).SingleAsync(token);

    internal static async Task<HeldRootAdmission> PublishAsync(KernelDb db, HeldRootRequest request, string key,
        string? payload, long now, TimeSpan window, RootAdmissionPressure.Attempt? attempt, CancellationToken token,
        Func<FormattableString, FormattableString>? bindPostgresSource = null)
    {
        if (!db.UsesRootLiveEventWaits) throw new NotSupportedException("This root store does not serve live event requests.");
        if (request.Caller.Deadline <= now) return HeldRootAdmission.Expired;
        var qualified = request.Tenant + "_" + key;
        var id = request.Caller.Id;
        var digest = FoundationRootCreator.OperationDigest(id);
        var postgres = bindPostgresSource is not null;
        if ((await ReadReceiptAsync()).Duplicate) return HeldRootAdmission.Duplicate;

        // One snapshot selects the committed wait, state and exact generated actor address. The admission transaction
        // retains that selection until it has reserved the operation and enqueued every matching delivery.
        FormattableString query = $"""
            WITH source AS MATERIALIZED (/*event-source*/), matches AS MATERIALIZED (
                SELECT i."Id" AS "Actor", b."Reference", i."Partition", s."Wait"
                FROM "Subscriptions" s
                JOIN "Instances" i ON i."Id" = s."Instance" AND i."Partition" = s."Partition"
                JOIN "FoundationRootBirths" b ON b."Actor" = i."Id" AND b."Partition" = i."Partition"
                    AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference" AND b."Creation" = 1
                WHERE s."Key" = {qualified} AND s."Start" IS NULL AND i."FoundationTenant" = {request.Tenant}
                    AND i."Status" = {(int)InstanceStatus.Running} AND NOT i."Retired" AND i."FoundationParkAt" IS NULL
                    AND (SELECT "SourceReady" FROM source)
                ORDER BY i."Partition", i."Id", s."Wait" LIMIT {FoundationMetadataLimits.MaxTurnSends + 1}
                /*share-waits*/
            )
            SELECT source."SourceReady", matches.* FROM source LEFT JOIN matches ON true
            ORDER BY matches."Partition", matches."Actor", matches."Wait"
            """;
        query = FormattableStringFactory.Create(query.Format.Replace("/*share-waits*/", postgres ? "FOR SHARE OF i, s" : "", StringComparison.Ordinal),
            query.GetArguments());
        var waits = await db.Database.SqlQuery<WaitTarget>(BindSource(query)).ToListAsync(token);
        RequireSource(waits[0].SourceReady);
        if (waits is [{ Actor: null }]) return HeldRootAdmission.NoWaiter;
        if (waits.Count > FoundationMetadataLimits.MaxTurnSends) throw FanoutFull();
        var messages = new List<MessageEntity>(waits.Count);
        var address = request.Address;
        long bytes = 0;
        foreach (var wait in waits)
        {
            if (!FoundationMetadataLimits.FitsLiveWaitName(wait.Wait!)) throw new StoreFormatException("A committed event wait has an invalid identity.");
            var (header, body, kind) = EncodeDirect(id, 1, Kinds.Event, payload, null, held: true, new(qualified, wait.Wait!));
            if (!FitsEnvelope(header, body, address)) return HeldRootAdmission.Refused;
            bytes += Encoding.UTF8.GetByteCount(header) + Encoding.UTF8.GetByteCount(address) + (body is null ? 0 : Encoding.UTF8.GetByteCount(body));
            if (waits.Count > 1 && bytes > FoundationMetadataLimits.MessageContentBytes) throw FanoutFull();
            messages.Add(new MessageEntity
            {
                Id = $"{wait.Reference}/{digest}/{Guid.NewGuid():N}", InboxScope = InboxKey.FoundationOccurrenceScope,
                Target = wait.Actor!, Partition = wait.Partition!.Value, Kind = kind, Start = header, Payload = body,
                ReplyTo = address
            });
        }
        attempt?.Require(waits.Select(wait => wait.Partition!.Value).Distinct().ToArray());
        if (postgres) now = await ReadPostgresClockAsync(db, token);
        if (request.Caller.Deadline <= now)
        {
            attempt?.RolledBack();
            return HeldRootAdmission.Expired;
        }
        var until = checked(now + (long)window.TotalMilliseconds);

        // A held publication stores an idempotency receipt only. The payload belongs to the matches that exist now.
        var receipt = await db.Database.ExecuteSqlInterpolatedAsync(BindSource($"""
            WITH source AS MATERIALIZED (/*event-source*/)
            INSERT INTO "Events" ("Key", "Id", "KeepUntil") SELECT {qualified}, {digest}, {until} FROM source WHERE "SourceReady"
            ON CONFLICT ("Key", "Id") DO UPDATE SET "KeepUntil" = excluded."KeepUntil"
                WHERE "Events"."KeepUntil" < {now}
            """), token);
        if (receipt == 0)
        {
            _ = await ReadReceiptAsync(); // Distinguish a valid concurrent receipt from an invalid source.
            attempt?.RolledBack();
            return HeldRootAdmission.Duplicate;
        }

        if (postgres)
        {
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync(BindSource($"""
                WITH source AS MATERIALIZED (/*event-source*/)
                INSERT INTO "Messages" ("Id", "InboxScope", "Target", "Partition", "Kind", "Start", "Payload", "Due", "Seen",
                    "Attempts", "Trial", "Done", "KeepUntil", "ReplyTo", "JobGeneration")
                SELECT item.id, {InboxKey.FoundationOccurrenceScope}, item.actor, item.home, {Kinds.Event}, item.header,
                    {messages[0].Payload}::text, 0, 0, 0, 0, false, {until}, {address}, 0
                FROM unnest({messages.Select(m => m.Id).ToArray()}::text[], {messages.Select(m => m.Target).ToArray()}::text[],
                    {messages.Select(m => m.Partition).ToArray()}::integer[], {messages.Select(m => m.Start!).ToArray()}::text[])
                    WITH ORDINALITY AS item(id, actor, home, header, position)
                CROSS JOIN source WHERE "SourceReady" ORDER BY item.position
                """), token);
            if (inserted != messages.Count) throw new StoreFormatException("The pinned live event inbox changed during admission.");
        }
        else
            foreach (var message in messages)
                await SqliteRootAdmission.EnqueueAsync(db, message.Target, message.Partition, message.Id, message.InboxScope,
                    message.Kind, message.Start!, message.Payload, null, until, message.ReplyTo, token);

        return HeldRootAdmission.Accepted;

        FormattableString BindSource(FormattableString sql) => bindPostgresSource?.Invoke(sql) ?? FormattableStringFactory.Create(
            sql.Format.Replace("/*event-source*/", """SELECT true AS "SourceReady" """, StringComparison.Ordinal), sql.GetArguments());

        async Task<ReceiptState> ReadReceiptAsync()
        {
            var result = await db.Database.SqlQuery<ReceiptState>(BindSource($"""
                WITH source AS MATERIALIZED (/*event-source*/)
                SELECT "SourceReady", "SourceReady" AND EXISTS (
                    SELECT 1 FROM "Events" WHERE "Key" = {qualified} AND "Id" = {digest} AND "KeepUntil" >= {now}) AS "Duplicate"
                FROM source
                """)).SingleAsync(token);
            RequireSource(result.SourceReady);
            return result;
        }
    }

    private static void RequireSource(bool sourceReady)
    {
        if (!sourceReady) throw new StoreFormatException("The pinned live event source changed during admission.");
    }

    private static RootAdmissionRefused FanoutFull() => new(Contract.RefusalReason.InboxFull, TimeSpan.FromSeconds(1));
}
