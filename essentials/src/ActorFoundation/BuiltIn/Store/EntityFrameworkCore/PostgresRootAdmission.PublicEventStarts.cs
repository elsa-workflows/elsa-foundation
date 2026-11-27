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
            ? BindPublicEventStarts(query, pin)
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
        await RootPublicEventStarts.SetAsync(db, tenant, qualified, wait, spec, sql => BindPublicEventStarts(sql, pin), token);
        await transaction.CommitAsync(token);
    }

    private static Task LockPublicEventStartTablesAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            LOCK TABLE ONLY "FoundationPublicEventStarts", ONLY "FoundationPublicEventTargets", ONLY "FoundationPublicEventPublications",
                ONLY "FoundationRootIssuer", ONLY "FoundationRootBirths", ONLY "Messages" IN ACCESS SHARE MODE
            """, token);

    private static FormattableString BindPublicEventStarts(FormattableString sql, PinnedRootSource pin) =>
        WithAdmissionSource(sql, pin, RootPublicEventStarts.Source, 6,
            """
            ('"FoundationPublicEventStarts"'), ('"FoundationPublicEventTargets"'), ('"FoundationPublicEventPublications"'),
            ('"FoundationRootIssuer"'), ('"FoundationRootBirths"'), ('"Messages"')
            """, requirePermanentDirectRelations: true);
}
