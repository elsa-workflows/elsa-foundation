using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

/// <summary>Public start subscriptions and the immutable creator intents selected by a committed publication.</summary>
internal static class RootPublicEventStarts
{
    internal const string Registrations = "FoundationPublicEventStarts";
    internal const string Targets = "FoundationPublicEventTargets";
    internal const string Source = "/*public-event-start-source*/";

    internal static string RegistrationColumns(bool sqlite)
    {
        var text = TextType(sqlite);
        return $"""
            "Tenant" {text} NOT NULL, "Key" {text} NOT NULL, "Wait" {text} NOT NULL,
            "Incarnation" {text} NOT NULL, "Start" {text} NOT NULL,
            PRIMARY KEY ("Tenant", "Key", "Wait")
            """;
    }

    internal static string TargetColumns(bool sqlite)
    {
        var text = TextType(sqlite);
        return $"""
            "Tenant" {text} NOT NULL, "Publication" {text} NOT NULL, "Registration" {text} NOT NULL,
            "Key" {text} NOT NULL, "Wait" {text} NOT NULL, "EventId" {text} NOT NULL,
            "Start" {text} NOT NULL, "Payload" {text},
            PRIMARY KEY ("Tenant", "Publication", "Registration")
            """;
    }

    private static string TextType(bool sqlite) => sqlite ? "TEXT COLLATE BINARY" : """text COLLATE "C" """;

    private sealed record CaptureQuote(bool SourceReady, long Count, long Bytes);

    internal static void Validate(string actorType, string key, string wait, Contract.StartSpec? spec)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(wait);
        if (!FoundationMetadataLimits.FitsLiveWaitName(key))
            throw new MetadataLimitExceeded(nameof(key), FoundationMetadataLimits.NameBytes);
        if (!FoundationMetadataLimits.FitsLiveWaitName(wait))
            throw new MetadataLimitExceeded(nameof(wait), FoundationMetadataLimits.NameBytes);
        if (spec is null) return;
        if (spec.Type != actorType || spec.Ephemeral || !Enum.IsDefined(spec.History))
            throw new NotSupportedException("Public event subscriptions require a durable start of the configured actor type.");
        HeaderLimits.ValidateStart(spec.Type, spec.TypeData, spec.Input);
        FoundationMetadataLimits.RequireContent(spec.Input, nameof(spec), FoundationMetadataLimits.EventStartInputBytes);
    }

    internal static async Task SetAsync(KernelDb db, string tenant, string key, string wait, Contract.StartSpec? spec,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        RequireTransaction(db, bind);
        var command = spec is null
            ? (FormattableString)$"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                DELETE FROM "FoundationPublicEventStarts" WHERE "Tenant" = {tenant} AND "Key" = {key} AND "Wait" = {wait}
                    AND (SELECT "SourceReady" FROM source)
                """
            : $"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                INSERT INTO "FoundationPublicEventStarts" ("Tenant", "Key", "Wait", "Incarnation", "Start")
                SELECT {tenant}, {key}, {wait}, {Guid.NewGuid().ToString("N")}, {DurableKeyJson.Write(spec)} FROM source WHERE "SourceReady"
                ON CONFLICT ("Tenant", "Key", "Wait") DO UPDATE
                    SET "Incarnation" = excluded."Incarnation", "Start" = excluded."Start"
                    WHERE "FoundationPublicEventStarts"."Start" <> excluded."Start"
                """;
        if (await db.Database.ExecuteSqlInterpolatedAsync(Bind(command, bind), token) == 0 && bind is not null)
        {
            var ready = await db.Database.SqlQuery<bool>(Bind($"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                SELECT "SourceReady" AS "Value" FROM source
                """, bind)).SingleAsync(token);
            RequireSource(ready);
        }
    }

    // The caller holds the key lock or the SQLite writer through the quote and copy. A replacement changes
    // only the registration; a publication that already selected its creator keeps the original template.
    internal static async Task<(int Count, long Bytes)> CaptureAsync(KernelDb db, string tenant, string key, string eventId,
        string publication, string? payload, int available, long availableBytes, Func<FormattableString, FormattableString>? bind,
        CancellationToken token)
    {
        RequireTransaction(db, bind);
        FormattableString query = $"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), candidates AS MATERIALIZED (
                SELECT /*start-bytes*/ AS "Bytes" FROM "FoundationPublicEventStarts"
                WHERE "Tenant" = {tenant} AND "Key" = {key} AND (SELECT "SourceReady" FROM source)
                ORDER BY "Wait" LIMIT {available + 1}
            )
            SELECT source."SourceReady", (SELECT count(*) FROM candidates) AS "Count",
                (SELECT CAST(coalesce(sum("Bytes"), 0) AS BIGINT) FROM candidates) AS "Bytes"
            FROM source
            """;
        var length = bind is not null ? """octet_length("Start")""" : """length(CAST("Start" AS BLOB))""";
        var quoteSql = FormattableStringFactory.Create(query.Format.Replace("/*start-bytes*/", length, StringComparison.Ordinal),
            query.GetArguments());
        var quote = await db.Database.SqlQuery<CaptureQuote>(Bind(quoteSql, bind)).SingleAsync(token);
        RequireSource(quote.SourceReady);
        if (quote.Count == 0) return default;
        var packed = payload is null ? null : DurableKeyJson.Write(payload);
        var bytes = quote.Bytes + quote.Count * (packed is null ? 0 : Encoding.UTF8.GetByteCount(packed));
        if (quote.Count > available || bytes > availableBytes)
            throw new StoreException(Failure.Definite, "The event's start subscriptions exceed the atomic delivery limit.");

        var captured = await db.Database.ExecuteSqlInterpolatedAsync(Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            INSERT INTO "FoundationPublicEventTargets"
                ("Tenant", "Publication", "Registration", "Key", "Wait", "EventId", "Start", "Payload")
            SELECT "Tenant", {publication}, "Incarnation", "Key", "Wait", {eventId}, "Start", CAST({packed} AS TEXT)
            FROM "FoundationPublicEventStarts"
            WHERE "Tenant" = {tenant} AND "Key" = {key} AND (SELECT "SourceReady" FROM source)
            ORDER BY "Wait"
            """, bind), token);
        RequireSource(captured == quote.Count);
        return (captured, bytes);
    }

    private static void RequireTransaction(KernelDb db, Func<FormattableString, FormattableString>? bind)
    {
        if (!db.UsesRootPublicEventStarts || db.Database.CurrentTransaction is null || db.Database.IsNpgsql() != (bind is not null))
            throw new StoreFormatException("Public event starts require their pinned provider transaction and store format.");
    }

    private static FormattableString Bind(FormattableString query, Func<FormattableString, FormattableString>? bind) =>
        bind?.Invoke(query) ?? FormattableStringFactory.Create(query.Format.Replace(Source, """SELECT true AS "SourceReady" """,
            StringComparison.Ordinal), query.GetArguments());

    private static void RequireSource(bool ready)
    {
        if (!ready) throw new StoreFormatException("The source changed while selecting public event starts.");
    }
}
