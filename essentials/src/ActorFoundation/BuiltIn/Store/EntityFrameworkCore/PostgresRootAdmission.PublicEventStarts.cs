using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    internal bool SupportsPublicEvents()
    {
        using var db = create();
        return db.UsesRootPublicEventStarts;
    }

    internal async Task<bool> MaintainPublicEventsAsync(RootPublicEventStarts.MaintenanceWork work, CancellationToken token)
    {
        await using var db = create();
        db.Database.SetCommandTimeout(TimeSpan.FromSeconds(2));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var pin = await PinRootNamespaceAsync(db, loadIssuer: false, token) ??
            throw new StoreFormatException("Public event maintenance requires the pinned PostgreSQL store.");
        await LockPublicEventStartTablesAsync(db, token);
        bool changed;
        if (work == RootPublicEventStarts.MaintenanceWork.Cleanup)
        {
            var sampled = await RootEventAdmission.ReadPostgresClockAsync(db, token);
            var candidate = await RootPublicEventPublications.FindCleanupAsync(db, sampled, Bind, token);
            if (candidate is null) return false;
            await RootEventAdmission.LockPostgresKeysAsync(db, [candidate.Key!], token);
            var now = await RootEventAdmission.ReadPostgresClockAsync(db, token);
            changed = await RootPublicEventPublications.CleanupAsync(db, candidate, now, Bind, token);
        }
        else if (work == RootPublicEventStarts.MaintenanceWork.Live)
        {
            var candidate = await RootPublicEventLiveWaits.FindAsync(db, Bind, token);
            if (candidate is null) return false;
            await RootEventAdmission.LockPostgresKeysAsync(db, [candidate.Key!], token);
            changed = await RootPublicEventLiveWaits.CatchUpAsync(db, candidate, Bind, token);
        }
        else
        {
            var candidate = await RootPublicEventStarts.FindCatchUpAsync(db, Bind, token);
            if (candidate is null) return false;
            await RootEventAdmission.LockPostgresKeysAsync(db, [candidate.Key!], token);
            changed = await RootPublicEventStarts.CatchUpAsync(db, candidate, Bind, token);
        }
        await transaction.CommitAsync(token);
        return changed;

        FormattableString Bind(FormattableString query) => BindPublicEventStarts(db, query, pin);
    }

    internal async Task<Contract.Admission> PublishPublicEventAsync(string tenant, string key, string id, string? payload,
        Contract.AddOptions? options, CancellationToken token)
    {
        if (RootPublicEventPublications.Validate(key, id, payload, options) is { } refusal) return refusal;
        for (var retry = 0; ; retry++)
        {
            try { return await PublishPublicEventOnceAsync(tenant, key, id, payload, options?.KeepFor ?? TimeSpan.Zero, token); }
            catch (PostgresException error) when (retry < 2 &&
                error.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10 * (retry + 1)), token);
            }
        }
    }

    private async Task<Contract.Admission> PublishPublicEventOnceAsync(string tenant, string key, string id, string? payload,
        TimeSpan retention, CancellationToken token)
    {
        using var attempt = pressure?.Begin(tenant, RootAdmissionKind.Input);
        await using var db = create();
        if (!db.UsesRootPublicEventStarts) throw new NotSupportedException("The public event profile is unavailable.");
        RequirePressureProfile(db);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var pin = await PinRootNamespaceAsync(db, loadIssuer: false, token) ??
            throw new StoreFormatException("Public events require the pinned PostgreSQL store.");
        await LockPublicEventStartTablesAsync(db, token);
        await RootEventAdmission.LockPostgresKeysAsync(db, [tenant + "_" + key], token);
        var now = await RootEventAdmission.ReadPostgresClockAsync(db, token);
        var result = await RootEventAdmission.PublishOutsideAsync(db, tenant, key, id, payload, now, retention, deduplicationWindow,
            attempt, Bind, token);
        await transaction.CommitAsync(token);
        if (result.Outcome == Contract.AdmissionOutcome.Accepted) attempt?.Committed();
        return result;

        FormattableString Bind(FormattableString query) => query.Format.Contains(RootPublicEventStarts.Source, StringComparison.Ordinal)
            ? BindPublicEventStarts(db, query, pin)
            : WithAdmissionSource(query, pin, "/*event-source*/", 5,
                """('"Events"'), ('"Subscriptions"'), ('"FoundationRootBirths"'), ('"Instances"'), ('"Messages"')""");
    }

    internal async Task SetPublicEventStartAsync(string tenant, string key, string wait, Contract.StartSpec? spec, CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesRootPublicEventStarts) throw new NotSupportedException("The public event start profile is unavailable.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var pin = await PinRootNamespaceAsync(db, loadIssuer: false, token) ??
            throw new StoreFormatException("Public event starts require the pinned PostgreSQL store.");
        await LockPublicEventStartTablesAsync(db, token);
        var qualified = tenant + "_" + key;
        await RootEventAdmission.LockPostgresKeysAsync(db, [qualified], token);
        var now = await RootEventAdmission.ReadPostgresClockAsync(db, token);
        await RootPublicEventStarts.SetAsync(db, tenant, qualified, wait, spec, now, sql => BindPublicEventStarts(db, sql, pin), token);
        await transaction.CommitAsync(token);
    }

    internal static async Task RequirePublicEventHostAsync(KernelDb db, RootPublicEventHost host, CancellationToken token)
    {
        if (!db.UsesRootPublicEventHost) return;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var pin = await PinRootNamespaceAsync(db, loadIssuer: false, token) ??
            throw new StoreFormatException("The public Event host requires the pinned PostgreSQL store.");
        await LockPublicEventStartTablesAsync(db, token);
        await RootPublicEventHost.RequireAsync(db, host, sql => BindPublicEventStarts(db, sql, pin), token);
        await transaction.CommitAsync(token);
    }

    private static Task LockPublicEventStartTablesAsync(KernelDb db, CancellationToken token)
    {
        const string tables = """
            LOCK TABLE ONLY "FoundationPublicEventStarts", ONLY "FoundationPublicEventTargets", ONLY "FoundationPublicEventPublications",
                ONLY "FoundationPublicEventHorizons", ONLY "FoundationPublicEventLiveWaits", ONLY "FoundationPublicEventLiveReceipts",
                ONLY "FoundationRootIssuer", ONLY "FoundationRootBirths", ONLY "Instances", ONLY "Subscriptions", ONLY "Messages"
            """;
        var host = db.UsesRootPublicEventHost ? """, ONLY "FoundationPublicEventHost" """ : "";
        var dispatch = db.UsesRootPagedPublicEvents ? """, ONLY "FoundationPublicEventDispatch" """ : "";
        return db.Database.ExecuteSqlRawAsync(tables + host + dispatch + " IN ACCESS SHARE MODE", token);
    }

    private static FormattableString BindPublicEventStarts(KernelDb db, FormattableString sql, PinnedRootSource pin)
    {
        const string tables = """
            ('"FoundationPublicEventStarts"'), ('"FoundationPublicEventTargets"'), ('"FoundationPublicEventPublications"'), ('"FoundationPublicEventHorizons"'),
            ('"FoundationPublicEventLiveWaits"'), ('"FoundationPublicEventLiveReceipts"'), ('"FoundationRootIssuer"'), ('"FoundationRootBirths"'),
            ('"Instances"'), ('"Subscriptions"'), ('"Messages"')
            """;
        var host = db.UsesRootPublicEventHost ? """ , ('"FoundationPublicEventHost"') """ : "";
        var dispatch = db.UsesRootPagedPublicEvents ? """ , ('"FoundationPublicEventDispatch"') """ : "";
        return WithAdmissionSource(sql, pin, RootPublicEventStarts.Source,
            11 + (db.UsesRootPublicEventHost ? 1 : 0) + (db.UsesRootPagedPublicEvents ? 1 : 0),
            tables + host + dispatch, requirePermanentDirectRelations: true);
    }
}
