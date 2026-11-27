using System.Data;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    internal async Task<Contract.Admission> AdmitKeyedAsync(string tenant, Contract.Target.Key target, string id, string kind,
        string? payload, long? due, TimeSpan? keepFor, string actorType, CancellationToken token)
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
            LOCK TABLE "FoundationKeyBindings", "FoundationKeyReceipts" IN ACCESS SHARE MODE
            """, token);
        var keyLock = tenant + "/" + FoundationRootCreator.OperationDigest(target.CallerKey);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended({keyLock}, 48602345))
            """, token);
        var now = await db.Database.SqlQueryRaw<long>("""
            SELECT floor(extract(epoch from clock_timestamp()) * 1000)::bigint AS "Value"
            """).SingleAsync(token);
        var result = await RootKeyedAdmission.ApplyAsync(db, request, issuer.Key, issuer.Partitions, now, Fence, CanReceiveAsync, attempt, token);
        await transaction.CommitAsync(token);
        if (result.Outcome == Contract.AdmissionOutcome.Accepted) attempt?.Committed();
        return result;

        FormattableString Fence(FormattableString sql) => WithAdmissionSource(sql, pin, RootKeyedAdmission.Source, 5,
            """('"FoundationKeyBindings"'), ('"FoundationKeyReceipts"'), ('"FoundationRootBirths"'), ('"Messages"'), ('"Instances"')""",
            requirePermanentDirectRelations: true);

        async Task<bool> CanReceiveAsync(RootKeyedAdmission.Binding binding, CancellationToken ct)
        {
            var address = await GuardedRootAddressAsync(db, tenant, binding.Reference!, ct);
            if (address is not { Creation: 1 } || address.Id != binding.Actor) return false;
            return await AdmissionStatusAsync(db, address, tenant, binding.Reference!, ct) == InstanceStatus.Running;
        }
    }
}
