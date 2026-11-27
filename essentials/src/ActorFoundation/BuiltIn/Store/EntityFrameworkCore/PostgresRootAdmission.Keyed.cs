using System.Data;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    internal bool SupportsKeyedStarts()
    {
        using var db = create();
        return db.UsesRootKeyedStarts;
    }

    internal async Task<Contract.Admission> AdmitKeyedAsync(string tenant, Contract.Target.Key target, string id, string kind,
        string? payload, long? due, TimeSpan? keepFor, string actorType, CancellationToken token) =>
        (await AdmitKeyedCoreAsync(tenant, target, id, kind, payload, due, keepFor, actorType, null, token))!;

    internal Task<Contract.Admission?> AdmitKeyedRequestAsync(HeldRootRequest caller, Contract.Target.Key target, string kind,
        string? payload, string? actorType, CancellationToken token)
    {
        if (!IsValidReplyNode(caller.Node))
            return Task.FromResult<Contract.Admission?>(new(Contract.AdmissionOutcome.Refused));
        return AdmitKeyedCoreAsync(caller.Tenant, target, caller.Caller.Id, kind, payload, null, caller.KeepFor, actorType, caller, token);
    }

    private async Task<Contract.Admission?> AdmitKeyedCoreAsync(string tenant, Contract.Target.Key target, string id, string kind,
        string? payload, long? due, TimeSpan? keepFor, string? actorType, HeldRootRequest? caller, CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesRootKeyedStarts) throw new NotSupportedException("Caller keys require their PostgreSQL qualification profile.");
        RequirePressureProfile(db);
        var window = ReceiptWindow(deduplicationWindow, keepFor);
        if (RootKeyedAdmission.Prepare(tenant, target, id, kind, payload, due, (long)window.TotalMilliseconds, actorType) is not { } request)
            return new(Contract.AdmissionOutcome.Refused);

        using var attempt = pressure?.Begin(tenant, request.Start is null ? RootAdmissionKind.Input : RootAdmissionKind.Start);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var pin = await PinRootNamespaceAsync(db, loadIssuer: true, token)
            ?? throw new StoreFormatException("Caller keys require an exact PostgreSQL source.");
        var issuer = pin.Issuer ?? throw new StoreFormatException("The PostgreSQL root issuer is missing.");
        await db.Database.ExecuteSqlRawAsync("""
            LOCK TABLE "FoundationKeyBindings", "FoundationKeyReceipts", "FoundationRootChildCascades" IN ACCESS SHARE MODE
            """, token);
        var keyLock = tenant + "/" + FoundationRootCreator.OperationDigest(target.CallerKey);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended({keyLock}, 48602345))
            """, token);
        var result = await RootKeyedAdmission.ApplyAsync(db, request, issuer.Key, issuer.Partitions, caller, Fence, CanReceiveAsync, attempt, token);
        await transaction.CommitAsync(token);
        if (result?.Outcome == Contract.AdmissionOutcome.Accepted) attempt?.Committed();
        return result;

        FormattableString Fence(FormattableString sql) => WithAdmissionSource(sql, pin, RootKeyedAdmission.Source, 6,
            """
            ('"FoundationKeyBindings"'), ('"FoundationKeyReceipts"'), ('"FoundationRootBirths"'),
            ('"Messages"'), ('"Instances"'), ('"FoundationRootChildCascades"')
            """,
            requirePermanentDirectRelations: true);

        async Task<bool> CanReceiveAsync(RootKeyedAdmission.Life life, CancellationToken ct)
        {
            var address = await GuardedRootAddressAsync(db, tenant, life.Reference, ct);
            if (address is not { Creation: 1 } || address.Id != life.Address.Id) return false;
            var status = await AdmissionStatusAsync(db, address, tenant, life.Reference, ct, requireCheckpoint: caller is not null);
            return await AcceptsDirectInputAsync(db, address, tenant, life.Reference, status, caller is not null, ct);
        }
    }
}
