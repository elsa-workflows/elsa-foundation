using System.Runtime.CompilerServices;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed partial class SqliteRootAdmission
{
    internal async Task<Contract.Admission> AdmitKeyedAsync(string tenant, Contract.Target.Key target, string id, string kind,
        string? payload, long? due, TimeSpan? keepFor, string actorType, CancellationToken token) =>
        (await AdmitKeyedCoreAsync(tenant, target, id, kind, payload, due, keepFor, actorType, null, token))!;

    internal Task<Contract.Admission?> AdmitKeyedRequestAsync(HeldRootRequest caller, Contract.Target.Key target, string kind,
        string? payload, string actorType, CancellationToken token, RootAdmissionPressure.Attempt? reserved) =>
        AdmitKeyedCoreAsync(caller.Tenant, target, caller.Caller.Id, kind, payload, null, caller.KeepFor, actorType, caller, token, reserved);

    private async Task<Contract.Admission?> AdmitKeyedCoreAsync(string tenant, Contract.Target.Key target, string id, string kind,
        string? payload, long? due, TimeSpan? keepFor, string actorType, HeldRootRequest? caller, CancellationToken token,
        RootAdmissionPressure.Attempt? reserved = null)
    {
        using (var profile = create())
            if (!profile.UsesRootKeyedStarts) throw new NotSupportedException("Caller keys require their SQLite qualification profile.");
        var window = ReceiptWindow(deduplicationWindow, keepFor);
        if (RootKeyedAdmission.Prepare(tenant, target, id, kind, payload, due, (long)window.TotalMilliseconds, actorType) is not { } request)
            return new(Contract.AdmissionOutcome.Refused);

        using var localAttempt = reserved is null ? pressure?.Begin(tenant, request.Start is null ? RootAdmissionKind.Input : RootAdmissionKind.Start) : null;
        var attempt = reserved ?? localAttempt;
        return await WriteAsync(async (db, ct) =>
        {
            var issuer = await ReadIssuerAsync(db, ct);
            return await RootKeyedAdmission.ApplyAsync(db, request, issuer.Key, issuer.Partitions, caller, Fence,
                async (life, readToken) =>
                {
                    var selected = await ReadTargetAsync(db, tenant, life.Reference, readToken);
                    return selected is not null && selected.Actor == life.Address.Id && selected.Partition == life.Address.Partition &&
                        (selected.Status == (int)InstanceStatus.Running || caller is null && selected is { HasCheckpoint: false, Pending: true });
                }, attempt, ct);
        }, token, attempt);

        static FormattableString Fence(FormattableString sql) => FormattableStringFactory.Create(
            sql.Format.Replace(RootKeyedAdmission.Source, """SELECT true AS "SourceReady" """, StringComparison.Ordinal),
            sql.GetArguments());
    }
}
