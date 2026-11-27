using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

internal static partial class RootEventStarts
{
    internal const string StagedTargets = "FoundationPagedEventStarts";
    internal const string StagedSlotIndex = "IX_FoundationPagedEventStarts_Slot";
    internal const string StagedSlotColumns = """("Tenant", "Slot", "Revision")""";

    internal static string StagedTargetColumns(bool sqlite)
    {
        var (text, number) = Types(sqlite);
        return $"""
            "Occurrence" {text} NOT NULL, "Tenant" {text} NOT NULL, "Key" {text} NOT NULL,
            "Slot" {text} NOT NULL, "Node" {text} NOT NULL, "Revision" {number} NOT NULL CHECK ("Revision" > 0),
            "Authority" {text} NOT NULL, PRIMARY KEY ("Occurrence", "Slot", "Node")
            """;
    }

    // A protected start's revision belongs to the producer checkpoint. Stage only authority, so its Event
    // body has one durable copy in the initial publication ticket until the owner materializes it.
    internal static async Task<(int Count, long Bytes)> StageMatchesAsync(KernelDb db, RootPublicEventTicket ticket,
        int available, long availableBytes, Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        var author = ticket.Author ?? throw new InvalidOperationException("Protected starts require an authored publication.");
        var matches = await FindMatchesAsync(db, author.Tenant, ticket.QualifiedKey, available + 1, bind, token);
        var bytes = matches.Sum(match => match.Bytes!.Value);
        if (matches.Count > available || bytes > availableBytes)
            throw new StoreException(Failure.Definite, "The author's protected start authorities exceed the checkpoint limit.");
        if (matches.Count == 0) return default;
        var written = await db.Database.ExecuteSqlInterpolatedAsync(Bind($"""
            WITH source AS MATERIALIZED (/*event-start-source*/)
            INSERT INTO "FoundationPagedEventStarts" ("Occurrence", "Tenant", "Key", "Slot", "Node", "Revision", "Authority")
            SELECT {ticket.Occurrence}, r."Tenant", r."Key", r."Slot", r."Node", slots."Revision", r."Authority"
            FROM "FoundationEventStartRegistrations" r
            JOIN "FoundationEventStartSlots" slots ON slots."Tenant" = r."Tenant" AND slots."Slot" = r."Slot"
            WHERE r."Tenant" = {author.Tenant} AND r."Key" = {ticket.QualifiedKey} AND (SELECT "SourceReady" FROM source)
            """, bind), token);
        if (written != matches.Count) throw SourceChanged();
        return (written, bytes);
    }

    private sealed record StagedStatus(bool SourceReady, bool HasMatches);

    internal static async Task<bool> HasStagedMatchesAsync(KernelDb db, string occurrence,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        var status = await db.Database.SqlQuery<StagedStatus>(Bind($"""
            WITH source AS MATERIALIZED (/*event-start-source*/)
            SELECT "SourceReady", "SourceReady" AND EXISTS (
                SELECT 1 FROM "FoundationPagedEventStarts" WHERE "Occurrence" = {occurrence}) AS "HasMatches"
            FROM source
            """, bind)).SingleAsync(token);
        if (!status.SourceReady) throw SourceChanged();
        return status.HasMatches;
    }

    internal static async Task<bool> DispatchStagedPageAsync(KernelDb db, RootPublicEventDispatch.Publication publication,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        RequireTransaction(db, bind);
        var rows = await db.Database.SqlQuery<MatchRow>(Bind($"""
            WITH source AS MATERIALIZED (/*event-start-source*/), matches AS MATERIALIZED (
                SELECT "Slot", "Node", "Revision", CAST(octet_length("Authority") AS BIGINT) AS "Bytes"
                FROM "FoundationPagedEventStarts" WHERE "Occurrence" = {publication.Occurrence} AND (SELECT "SourceReady" FROM source)
                ORDER BY "Slot", "Node" LIMIT {InMemoryStore.ParkPageLimit + 1}
            )
            SELECT source."SourceReady", matches.* FROM source LEFT JOIN matches ON true ORDER BY matches."Slot", matches."Node"
            """, bind)).ToListAsync(token);
        if (!rows[0].SourceReady) throw SourceChanged();
        if (rows is [{ Slot: null }]) return true;

        var body = RootAdmissionWire.DecodePayload(publication.Payload.Text, publication.Payload.Escaped);
        var packed = PackPayload(body);
        var payloadBytes = packed is null ? 0 : Encoding.UTF8.GetByteCount(packed);
        var bytes = 0L;
        var matches = new List<MatchRow>(rows.Count);
        foreach (var row in rows.Take(InMemoryStore.ParkPageLimit))
        {
            var size = row.Bytes!.Value + payloadBytes + 256;
            if (matches.Count > 0 && bytes + size > ParkRecoveryCredit.LimitBytes) break;
            matches.Add(row);
            bytes += size;
        }
        var material = new EventRow(publication.Key, publication.EventId, body, publication.KeepUntil)
            { FoundationTenant = publication.Tenant };
        await InsertMatchesAsync(db, material, publication.KeepUntil, matches, packed, bind, token, publication.Occurrence);
        var last = matches[^1];
        var removed = await db.Database.ExecuteSqlInterpolatedAsync(Bind($"""
            WITH source AS MATERIALIZED (/*event-start-source*/)
            DELETE FROM "FoundationPagedEventStarts"
            WHERE "Occurrence" = {publication.Occurrence} AND ("Slot", "Node") <= ({last.Slot}, {last.Node})
                AND (SELECT "SourceReady" FROM source)
            """, bind), token);
        if (removed != matches.Count) throw SourceChanged();
        return matches.Count == rows.Count;
    }

    internal static Task DiscardStagedMatchesAsync(KernelDb db, string occurrence, Func<FormattableString, FormattableString>? bind,
        CancellationToken token) =>
        db.Database.ExecuteSqlInterpolatedAsync(Bind($"""
            WITH source AS MATERIALIZED (/*event-start-source*/)
            DELETE FROM "FoundationPagedEventStarts" WHERE "Occurrence" = {occurrence} AND (SELECT "SourceReady" FROM source)
            """, bind), token);
}
