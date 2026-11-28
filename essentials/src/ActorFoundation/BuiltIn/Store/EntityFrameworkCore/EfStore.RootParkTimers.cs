using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // The timer wire lets the quote seek only live or Park-retained timers, including a timer with a zero due time.
    private static readonly string ParkTimerRowPredicate = $"""
        "InboxScope" = {RootSelfScope} AND "EventMatch" IS NULL AND "Kind" = '{RootSelfTimerWire}'
            AND (NOT "Done" OR "FoundationKeptBy" IS NOT NULL)
        """;

    private static readonly string ParkTimerIndex = $"""("Partition", "Target", "Id") WHERE {ParkTimerRowPredicate}""";

    private sealed record QuotedParkTimer(bool Fits, string? Id, long? Seq, short? KeptBy, long? Bytes, string? Kind, string? Payload,
        string? Sender, string? Start, long? Due, long? KeepUntil, string? ReplyTo, long? JobGeneration);

    private static async Task<string?> ReadRootParkTimersAsync(KernelDb db, string actor, int partition,
        FoundationSelfIssuer? issuer, bool lockRows, CancellationToken token)
    {
        if (!db.UsesRootPrivateParkInputs || issuer is null) return null;
        var source = db.UsesSqliteRootRuntime ? "\"Messages\"" : PostgresPrivateInboxView;
        var rowLock = lockRows && !db.UsesSqliteRootRuntime ? "FOR UPDATE OF p" : "";
        var rows = await db.Database.SqlQueryRaw<QuotedParkTimer>($"""
            WITH selected AS MATERIALIZED (
                SELECT k."Id", k."Seq", k."FoundationKeptBy" AS "KeptBy", {KeptBytes(db)} AS "Bytes"
                FROM "Messages" k WHERE "Target" = {"{0}"} AND "Partition" = {"{1}"} AND {ParkTimerRowPredicate}
                ORDER BY "Id" LIMIT {InMemoryStore.MaxRestoreWaits + 1}
            ), quote AS MATERIALIZED (
                SELECT count(*) <= {InMemoryStore.MaxRestoreWaits}
                    AND coalesce(sum("Bytes" + {InMemoryStore.RestoreWaitAllowance}), 0) <= {InMemoryStore.MaxRestoreBytes} AS "Fits"
                FROM selected
            ), bodies AS MATERIALIZED (
                SELECT q."Id", q."Seq", q."KeptBy", q."Bytes", p."Kind", p."Payload", p."Sender", p."Start", p."Due",
                    p."KeepUntil", p."ReplyTo", p."JobGeneration"
                FROM quote f CROSS JOIN selected q JOIN {source} p ON p."Id" = q."Id"
                    AND p."InboxScope" = {RootSelfScope} AND p."EventMatch" IS NULL
                    AND p."Target" = {"{0}"} AND p."Partition" = {"{1}"}
                WHERE f."Fits" {rowLock}
            )
            SELECT f."Fits", p.* FROM quote f LEFT JOIN bodies p ON true ORDER BY p."Id"
            """, actor, partition).ToListAsync(token);
        if (!rows[0].Fits) return null;
        return DurableKeyJson.Write(rows.Where(row => row.Id is not null).Select(row =>
        {
            var timer = DecodeRootSelf(new MessageEntity
            {
                Id = row.Id!, InboxScope = RootSelfScope, Target = actor, Kind = row.Kind!,
                Sender = row.Sender, Payload = row.Payload, Start = row.Start, Due = row.Due!.Value, KeepUntil = row.KeepUntil,
                ReplyTo = row.ReplyTo, JobGeneration = row.JobGeneration!.Value
            }, issuer);
            return new FoundationRootParkTimer(timer.Id, row.Seq!.Value, (ParkKeepReason?)row.KeptBy, row.Bytes!.Value,
                FoundationRootParkTimer.Digest(timer));
        }).ToArray());
    }
}
