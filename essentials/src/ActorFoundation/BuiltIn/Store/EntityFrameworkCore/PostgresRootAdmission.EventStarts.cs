using System.Data;
using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    internal async Task<EventRegistrationResult> ApplyEventStartsAsync(string tenant, string slot, long revision,
        IReadOnlyList<EventStartRegistration> registrations, CancellationToken token)
    {
        await using var db = create();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var pin = await PinEventStartsAsync(db, token);
        var result = await RootEventStarts.ApplyAsync(db, tenant, slot, revision, registrations, sql => BindEventStarts(sql, pin), token);
        await transaction.CommitAsync(token);
        return result;
    }

    internal Task<IReadOnlyList<EventStartTarget>> ListEventStartsAsync(string tenant, string? after, CancellationToken token) =>
        ReadEventStartsAsync((db, bind) => RootEventStarts.ListAsync(db, tenant, after, bind, token), token);

    internal Task<Contract.Admission> AdmitEventStartAsync(string tenant, string target,
        Func<EventStartTarget, Contract.StartSpec> start, ProtectRootStartAsync protect, CancellationToken caller) =>
        RootEventStarts.RelayAsync(target, start,
            token => ReadEventStartsAsync((db, bind) => RootEventStarts.ReadWorkAsync(db, tenant, target, bind, token), token),
            async (spec, token) =>
            {
                var result = await AdmitProtectedRootAsync(tenant, target, spec, null, null, protect, token, caller, eventTarget: target);
                return result is null ? null : new(result.Duplicate ? Contract.AdmissionOutcome.Duplicate : Contract.AdmissionOutcome.Accepted,
                    new Contract.ActorRef(result.Reference));
            }, caller);

    internal Task<bool> ConfirmEventStartRegistrationDrainedAsync(string tenant, string slot, long revision, CancellationToken token) =>
        ReadEventStartsAsync((db, bind) => RootEventStarts.ConfirmDrainedAsync(db, tenant, slot, revision, bind, token), token);

    internal Task<int> PruneSettledEventStartsAsync(string tenant, CancellationToken token) =>
        ReadEventStartsAsync((db, bind) => RootEventStarts.PruneSettledAsync(db, tenant, bind, token), token);

    private async Task<T> ReadEventStartsAsync<T>(Func<KernelDb, Func<FormattableString, FormattableString>, Task<T>> read,
        CancellationToken token)
    {
        await using var db = create();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var pin = await PinEventStartsAsync(db, token);
        var result = await read(db, sql => BindEventStarts(sql, pin));
        await transaction.CommitAsync(token);
        return result;
    }

    private static async Task<PinnedRootSource> PinEventStartsAsync(KernelDb db, CancellationToken token)
    {
        if (!db.UsesRootEventStarts) throw new StoreFormatException("The event start profile is unavailable.");
        var pin = await PinRootNamespaceAsync(db, loadIssuer: false, token) ??
            throw new StoreFormatException("Event starts require the pinned PostgreSQL store.");
        await LockEventStartTablesAsync(db, token);
        return pin;
    }

    private static Task LockEventStartTablesAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            LOCK TABLE ONLY "FoundationEventStartSlots", ONLY "FoundationEventStartRegistrations",
                ONLY "FoundationEventStartTargets" IN ACCESS SHARE MODE
            """, token);

    private static FormattableString BindEventStarts(FormattableString sql, PinnedRootSource pin) =>
        WithAdmissionSource(sql, pin, RootEventStarts.Source, 4,
            """('"FoundationEventStartSlots"'), ('"FoundationEventStartRegistrations"'), ('"FoundationEventStartTargets"'), ('"FoundationRootBirths"')""",
            requirePermanentDirectRelations: true);
}
