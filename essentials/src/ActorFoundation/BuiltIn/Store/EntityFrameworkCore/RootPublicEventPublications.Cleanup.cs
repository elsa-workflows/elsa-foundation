using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

internal static partial class RootPublicEventPublications
{
    private const string Uncovered = """
        AND NOT EXISTS (SELECT 1 FROM "FoundationPublicEventStarts" r
            WHERE r."Tenant" = p."Tenant" AND r."Key" = p."Key" AND p."KeepUntil" > r."RetainAfter"
                AND p."CleanupAt" = p."KeepUntil" AND p."Ordinal" > r."CatchAfter" AND p."Ordinal" <= r."CatchThrough")
        AND NOT EXISTS (SELECT 1 FROM "FoundationPublicEventTargets" t
            WHERE t."Tenant" = p."Tenant" AND t."Publication" = p."Occurrence" AND t."Reference" IS NULL)
        """;

    internal sealed record CleanupKey(bool SourceReady, string? Tenant, string? Key, long? Ordinal);
    private sealed record CleanupPublication(bool SourceReady, string? Occurrence, long? ReceiptUntil, bool? ClosesRetention);

    internal static async Task<CleanupKey?> FindCleanupAsync(KernelDb db, long now, Func<FormattableString, FormattableString>? bind,
        CancellationToken token)
    {
        var next = await db.Database.SqlQuery<CleanupKey>(CleanupSql($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), expired AS (
                SELECT p."Tenant", p."Key", p."Ordinal" FROM "FoundationPublicEventPublications" p
                WHERE p."CleanupAt" < {now} AND (SELECT "SourceReady" FROM source) /*uncovered*/
                ORDER BY p."CleanupAt", p."Ordinal" LIMIT 1
            )
            SELECT source."SourceReady", expired.* FROM source LEFT JOIN expired ON true
            """, bind)).SingleAsync(token);
        RequireCleanupSource(next.SourceReady);
        return next.Ordinal is null ? null : next;
    }

    // The key lock or the SQLite writer serializes this expiry with registration. A registration
    // entitled to an event before its expiry keeps that payload until delivery or removal.
    internal static async Task<bool> CleanupAsync(KernelDb db, CleanupKey target, long now,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        var expired = await db.Database.SqlQuery<CleanupPublication>(CleanupSql($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), expired AS (
                SELECT p."Occurrence", p."ReceiptUntil", p."CleanupAt" = p."KeepUntil" AS "ClosesRetention"
                FROM "FoundationPublicEventPublications" p
                WHERE p."Tenant" = {target.Tenant} AND p."Key" = {target.Key} AND p."Ordinal" = {target.Ordinal}
                    AND p."CleanupAt" < {now} AND (SELECT "SourceReady" FROM source) /*uncovered*/
            )
            SELECT source."SourceReady", expired.* FROM source LEFT JOIN expired ON true
            """, bind)).SingleAsync(token);
        RequireCleanupSource(expired.SourceReady);
        if (expired.Occurrence is null) return false;
        if (expired.ClosesRetention == true) await AdvanceRetentionAsync(db, target.Tenant!, now, bind, token);

        await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            DELETE FROM "FoundationPublicEventTargets" WHERE "Tenant" = {target.Tenant} AND "Publication" = {expired.Occurrence}
                AND "Reference" IS NOT NULL AND (SELECT "SourceReady" FROM source)
            """, bind), token);

        var mutation = expired.ReceiptUntil >= now
            ? (FormattableString)$"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                UPDATE "FoundationPublicEventPublications" SET "Payload" = NULL, "CleanupAt" = "ReceiptUntil"
                WHERE "Tenant" = {target.Tenant} AND "Key" = {target.Key} AND "Ordinal" = {target.Ordinal}
                    AND (SELECT "SourceReady" FROM source)
                """
            : $"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                DELETE FROM "FoundationPublicEventPublications"
                WHERE "Tenant" = {target.Tenant} AND "Key" = {target.Key} AND "Ordinal" = {target.Ordinal}
                    AND (SELECT "SourceReady" FROM source)
                """;
        RequireCleanupSource(await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind(mutation, bind), token) == 1);
        return true;
    }

    private static FormattableString CleanupSql(FormattableString query, Func<FormattableString, FormattableString>? bind) =>
        RootPublicEventStarts.Bind(FormattableStringFactory.Create(query.Format.Replace("/*uncovered*/", Uncovered, StringComparison.Ordinal),
            query.GetArguments()), bind);

    private static void RequireCleanupSource(bool ready)
    {
        if (!ready) throw new StoreFormatException("The pinned public event source changed during cleanup.");
    }
}
