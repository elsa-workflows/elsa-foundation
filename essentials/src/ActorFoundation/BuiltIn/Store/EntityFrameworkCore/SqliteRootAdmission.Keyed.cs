using System.Runtime.CompilerServices;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed partial class SqliteRootAdmission
{
    internal async Task<Contract.Admission> AdmitKeyedAsync(string tenant, Contract.Target.Key target, string id, string kind,
        string? payload, long? due, TimeSpan? keepFor, string actorType, CancellationToken token)
    {
        using (var profile = create())
            if (!profile.UsesRootKeyedStarts) throw new NotSupportedException("Caller keys require their SQLite qualification profile.");
        var window = ReceiptWindow(deduplicationWindow, keepFor);
        if (RootKeyedAdmission.Prepare(tenant, target, id, kind, payload, due, (long)window.TotalMilliseconds, actorType) is not { } request)
            return new(Contract.AdmissionOutcome.Refused);

        using var attempt = pressure?.Begin(tenant, request.Start is null ? RootAdmissionKind.Input : RootAdmissionKind.Start);
        return await WriteAsync(async (db, now, ct) =>
        {
            var issuer = await ReadIssuerAsync(db, ct);
            return await RootKeyedAdmission.ApplyAsync(db, request, issuer.Key, issuer.Partitions, now, Fence,
                async (binding, readToken) =>
                {
                    var selected = await ReadTargetAsync(db, tenant, binding.Reference!, readToken);
                    return selected is not null && selected.Actor == binding.Actor && selected.Partition == binding.Partition &&
                        (selected.Status == (int)InstanceStatus.Running || selected is { HasCheckpoint: false, Pending: true });
                }, attempt, ct);
        }, token, attempt);

        static FormattableString Fence(FormattableString sql) => FormattableStringFactory.Create(
            sql.Format.Replace(RootKeyedAdmission.Source, """SELECT true AS "SourceReady" """, StringComparison.Ordinal),
            sql.GetArguments());
    }
}
