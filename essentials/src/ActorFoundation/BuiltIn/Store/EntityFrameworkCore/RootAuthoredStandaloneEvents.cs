using Kernel.Durable;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal static class RootAuthoredStandaloneEvents
{
    internal const string Host = "FoundationAuthoredStandaloneEventHost";
    internal const string Transfers = "FoundationAuthoredStandaloneEventTransfers";
    internal const string PageIndex = "IX_FoundationAuthoredStandaloneEventTransfers_Page";
    internal const string PageColumns = """("CommittedAt", "Occurrence")""";
    private const int PageSize = 32;

    internal static string HostColumns(bool sqlite)
    {
        var singleton = sqlite ? "INTEGER" : "boolean";
        var text = sqlite ? "TEXT COLLATE BINARY" : """text COLLATE "C" """;
        return $"""
            "Singleton" {singleton} PRIMARY KEY CHECK ("Singleton" = true) REFERENCES "FoundationRootIssuer" ("Singleton"),
            "Tenant" {text} NOT NULL CHECK (length("Tenant") BETWEEN 1 AND 64)
            """;
    }

    internal static string TransferColumns(bool sqlite)
    {
        var text = sqlite ? "TEXT COLLATE BINARY" : """text COLLATE "C" """;
        var flag = sqlite ? "INTEGER" : "boolean";
        var clock = sqlite ? "INTEGER" : "bigint";
        return FormattableString.Invariant($"""
            "Occurrence" {text} PRIMARY KEY NOT NULL CHECK (length("Occurrence") = 64),
            "Publisher" {text} NOT NULL REFERENCES "FoundationRootBirths" ("Reference"),
            "Partition" integer NOT NULL CHECK ("Partition" >= 0),
            "EventName" {text} NOT NULL CHECK (octet_length("EventName") BETWEEN 1 AND {FoundationMetadataLimits.NameBytes}),
            "Payload" {text} CHECK (octet_length("Payload") <= {FoundationMetadataLimits.EventContentBytes}),
            "PayloadEscaped" {flag} NOT NULL CHECK ("PayloadEscaped" IN (false, true) AND (NOT "PayloadEscaped" OR "Payload" IS NOT NULL)),
            "CommittedAt" {clock} NOT NULL CHECK ("CommittedAt" > 0),
            "Fingerprint" {text} NOT NULL CHECK (length("Fingerprint") = 64)
            """);
    }

    internal static async Task InstallAsync(KernelDb db, bool create, CancellationToken token)
    {
        var expected = db.PublicEventHost?.AuthoredStandaloneTenant;
        if (create)
        {
            if (expected is null) throw new StoreFormatException("Select an ordinary publisher scope for the authored standalone Event profile.");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationAuthoredStandaloneEventHost" ("Singleton", "Tenant") VALUES (true, {expected})
                """, token);
        }
        await ReadTenantAsync(db, bind: null, token);
    }

    private sealed record HostSelection(bool SourceReady, string? Tenant);

    internal static async Task<string> ReadTenantAsync(KernelDb db, Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        if (!db.UsesAuthoredStandaloneEvents || db.Database.CurrentTransaction is null)
            throw new NotSupportedException("Authored standalone Events require the selected provider transaction.");
        var selected = await db.Database.SqlQuery<HostSelection>(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            SELECT source."SourceReady", host."Tenant" FROM source
            LEFT JOIN "FoundationAuthoredStandaloneEventHost" host ON source."SourceReady" AND host."Singleton" = true
            """, bind)).SingleAsync(token);
        if (!selected.SourceReady || selected.Tenant is not { Length: >= 1 and <= 64 } tenant ||
            tenant.Any(value => value is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')) ||
            db.PublicEventHost?.AuthoredStandaloneTenant is { } expected && expected != tenant)
            throw new StoreFormatException("The selected authored standalone Event publisher scope does not belong to the installed store.");
        return tenant;
    }

    internal static async Task CaptureAsync(KernelDb db, EfStore.WorkerRoot root, RootPublicEventTicket ticket, string tenant,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        if (root.Tenant != tenant) return;
        var name = ticket.QualifiedKey[(tenant.Length + 1)..];
        var committed = ticket.Author!.AcceptedAt!.Value;
        var (payload, escaped) = ticket.Payload;
        var fingerprint = Fingerprint(tenant, ticket.Occurrence, root.Reference, root.Partition, name, payload, escaped, committed);
        var count = await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            INSERT INTO "FoundationAuthoredStandaloneEventTransfers"
                ("Occurrence", "Publisher", "Partition", "EventName", "Payload", "PayloadEscaped", "CommittedAt", "Fingerprint")
            SELECT {ticket.Occurrence}, b."Reference", b."Partition", {name}, {payload}, {escaped}, {committed}, {fingerprint}
            FROM source, "FoundationRootBirths" b, "FoundationAuthoredStandaloneEventHost" host
            WHERE source."SourceReady" AND host."Singleton" = true AND host."Tenant" = {tenant}
                AND b."Tenant" = {tenant} AND b."Reference" = {root.Reference} AND b."Actor" = {root.Actor}
                AND b."Partition" = {root.Partition} AND b."Creation" = 1 AND b."Fingerprint" = {root.Fingerprint}
            """, bind), token);
        if (count != 1) throw new StoreFormatException("The authored Event transfer could not bind its exact committed publisher.");
    }

    private sealed record Transfer(string Occurrence, string Publisher, int Partition, string EventName,
        string? Payload, bool PayloadEscaped, long CommittedAt, string Fingerprint);

    internal static async Task<IReadOnlyList<Contract.AuthoredStandaloneEvent>> ReadAsync(KernelDb db, Contract.BoundScope scope,
        Contract.AuthoredStandaloneEventCursor? after, Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        await RequireScopeAsync(db, scope, bind, token);
        if (after is not null && (after.CommittedAt <= 0 || !FoundationRootCreator.IsGeneratedReference(after.Occurrence)))
            throw new ArgumentException("The standalone Event cursor is invalid.", nameof(after));
        var rows = await db.Database.SqlQuery<Transfer>(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), page AS MATERIALIZED (
                SELECT "Occurrence", "CommittedAt", CAST(coalesce(octet_length("Payload"), 0) + 2048 AS BIGINT) AS "Bytes"
                FROM "FoundationAuthoredStandaloneEventTransfers" WHERE (SELECT "SourceReady" FROM source)
                    AND ("CommittedAt", "Occurrence") > ({after?.CommittedAt ?? 0}, {after?.Occurrence ?? ""})
                ORDER BY "CommittedAt", "Occurrence" LIMIT {PageSize}
            ), budget AS (
                SELECT "Occurrence", row_number() OVER ordered AS "Position", sum("Bytes") OVER ordered AS "Bytes"
                FROM page WINDOW ordered AS (ORDER BY "CommittedAt", "Occurrence" ROWS UNBOUNDED PRECEDING)
            )
            SELECT e.* FROM budget JOIN "FoundationAuthoredStandaloneEventTransfers" e USING ("Occurrence")
            WHERE budget."Position" = 1 OR budget."Bytes" <= {FoundationMetadataLimits.MessageContentBytes}
            ORDER BY e."CommittedAt", e."Occurrence"
            """, bind)).ToListAsync(token);
        return rows.Select(row =>
        {
            if (row.Fingerprint != Fingerprint(scope.Tenant, row.Occurrence, row.Publisher, row.Partition, row.EventName,
                    row.Payload, row.PayloadEscaped, row.CommittedAt))
                throw new InvalidDataException("The pending standalone Event does not match its captured publication.");
            return new Contract.AuthoredStandaloneEvent(row.Occurrence, new(row.Publisher), row.Partition, row.EventName,
                RootAdmissionWire.DecodePayload(row.Payload, row.PayloadEscaped), row.CommittedAt, row.Fingerprint);
        }).ToArray();
    }

    internal static async Task<bool> AcknowledgeAsync(KernelDb db, Contract.BoundScope scope, string occurrence, string fingerprint,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        await RequireScopeAsync(db, scope, bind, token);
        if (!FoundationRootCreator.IsGeneratedReference(occurrence) || !FoundationRootCreator.IsGeneratedReference(fingerprint))
            throw new ArgumentException("The standalone Event acknowledgement is invalid.");
        return await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            DELETE FROM "FoundationAuthoredStandaloneEventTransfers"
            WHERE "Occurrence" = {occurrence} AND "Fingerprint" = {fingerprint} AND (SELECT "SourceReady" FROM source)
                AND EXISTS (SELECT 1 FROM "FoundationAuthoredStandaloneEventHost"
                    WHERE "Singleton" = true AND "Tenant" = {scope.Tenant})
            """, bind), token) == 1;
    }

    private static async Task RequireScopeAsync(KernelDb db, Contract.BoundScope scope,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        if (scope.IsHostIsolated || await ReadTenantAsync(db, bind, token) != scope.Tenant)
            throw new NotSupportedException("This actor scope cannot transfer authored standalone Events.");
    }

    private static string Fingerprint(string tenant, string occurrence, string publisher, int partition, string name,
        string? payload, bool escaped, long committedAt) => FoundationRootCreator.OperationDigest(DurableKeyJson.Write(
            new { Kind = "standalone-event-transfer/1", tenant, occurrence, publisher, partition, name, payload, escaped, committedAt }));
}
