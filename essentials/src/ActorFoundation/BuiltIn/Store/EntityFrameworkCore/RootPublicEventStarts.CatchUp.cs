using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

internal static partial class RootPublicEventStarts
{
    internal sealed record CatchUpKey(bool SourceReady, string? Tenant, string? Key, string? Wait, string? Registration);
    private sealed record CatchUpStep(bool SourceReady, string? Registration, long? After, long? Through,
        long? Ordinal, long? KeepUntil, string? Occurrence);

    internal enum MaintenanceWork { Start, Live, Cleanup }

    internal static async Task RunMaintenanceAsync(Func<MaintenanceWork, CancellationToken, Task<bool>> step, Action<Exception> report,
        CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var progressed = false;
            try
            {
                progressed = await step(MaintenanceWork.Start, stop);
                progressed |= await step(MaintenanceWork.Live, stop);
                progressed |= await step(MaintenanceWork.Cleanup, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception error) { report(error); }
            await Task.Delay(progressed ? TimeSpan.FromMilliseconds(20) : TimeSpan.FromSeconds(1), stop);
        }
    }

    internal static async Task<CatchUpKey?> FindCatchUpAsync(KernelDb db, Func<FormattableString, FormattableString>? bind,
        CancellationToken token)
    {
        var next = await db.Database.SqlQuery<CatchUpKey>(Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), pending AS (
                SELECT "Tenant", "Key", "Wait", "Incarnation" AS "Registration"
                FROM "FoundationPublicEventStarts" WHERE "CatchAfter" < "CatchThrough"
                    AND (SELECT "SourceReady" FROM source)
                ORDER BY "Tenant", "Key", "Wait" LIMIT 1
            )
            SELECT source."SourceReady", pending.* FROM source LEFT JOIN pending ON true
            """, bind)).SingleAsync(token);
        RequireSource(next.SourceReady);
        return next.Registration is null ? null : next;
    }

    // The caller holds the same key lock as publish, registration and cleanup. FindCatchUp only picks a key.
    internal static async Task<bool> CatchUpAsync(KernelDb db, CatchUpKey target, Func<FormattableString, FormattableString>? bind,
        CancellationToken token)
    {
        RequireTransaction(db, bind);
        var step = await db.Database.SqlQuery<CatchUpStep>(Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), registration AS MATERIALIZED (
                SELECT "Incarnation" AS "Registration", "CatchAfter" AS "After", "CatchThrough" AS "Through", "RetainFrom"
                FROM "FoundationPublicEventStarts" WHERE "Tenant" = {target.Tenant} AND "Key" = {target.Key}
                    AND "Wait" = {target.Wait} AND "Incarnation" = {target.Registration}
                    AND "CatchAfter" < "CatchThrough" AND (SELECT "SourceReady" FROM source)
            ), publication AS (
                SELECT p."Ordinal", p."KeepUntil", p."Occurrence"
                FROM "FoundationPublicEventPublications" p, registration r
                WHERE p."Tenant" = {target.Tenant} AND p."Key" = {target.Key} AND p."KeepUntil" >= r."RetainFrom"
                    AND p."CleanupAt" = p."KeepUntil" AND p."Ordinal" > r."After" AND p."Ordinal" <= r."Through"
                ORDER BY p."Ordinal" LIMIT 1
            )
            SELECT source."SourceReady", r."Registration", r."After", r."Through", p.*
            FROM source LEFT JOIN registration r ON true LEFT JOIN publication p ON true
            """, bind)).SingleAsync(token);
        RequireSource(step.SourceReady);
        if (step.Registration is null) return false;
        if (step.Occurrence is { } occurrence)
        {
            var captured = await db.Database.ExecuteSqlInterpolatedAsync(Bind($"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                INSERT INTO "FoundationPublicEventTargets"
                    ("Tenant", "Publication", "Registration", "Key", "Wait", "EventId", "Start", "Payload")
                SELECT p."Tenant", p."Occurrence", r."Incarnation", p."Key", r."Wait", p."EventId", r."Start", p."Payload"
                FROM "FoundationPublicEventPublications" p
                JOIN "FoundationPublicEventStarts" r ON r."Tenant" = p."Tenant" AND r."Key" = p."Key"
                WHERE p."Tenant" = {target.Tenant} AND p."Key" = {target.Key} AND p."Ordinal" = {step.Ordinal}
                    AND r."Wait" = {target.Wait} AND r."Incarnation" = {step.Registration}
                    AND (SELECT "SourceReady" FROM source)
                """, bind), token);
            RequireSource(captured == 1);
            await DispatchAsync(db, target.Tenant!, occurrence, step.KeepUntil!.Value, bind, token, rememberDelivery: true);
        }
        RequireSource(await db.Database.ExecuteSqlInterpolatedAsync(Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            UPDATE "FoundationPublicEventStarts" SET "CatchAfter" = {step.Ordinal ?? step.Through}
            WHERE "Tenant" = {target.Tenant} AND "Key" = {target.Key} AND "Wait" = {target.Wait} AND "Incarnation" = {step.Registration}
                AND "CatchAfter" = {step.After} AND (SELECT "SourceReady" FROM source)
            """, bind), token) == 1);
        return true;
    }
}
