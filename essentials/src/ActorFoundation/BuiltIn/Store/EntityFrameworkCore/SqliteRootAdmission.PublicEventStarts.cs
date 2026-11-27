using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed partial class SqliteRootAdmission
{
    internal async Task<bool> MaintainPublicEventsAsync(RootPublicEventStarts.MaintenanceWork work, CancellationToken token)
    {
        // An empty maintenance pass takes a read snapshot. Only a candidate needs the SQLite writer.
        if (work == RootPublicEventStarts.MaintenanceWork.Cleanup)
        {
            var candidate = await InAdmissionTransactionAsync(async (db, ct) =>
                await RootPublicEventPublications.FindCleanupAsync(db, await ReadClockAsync(db, ct), bind: null, ct), token, readOnly: true);
            return candidate is not null &&
                await WriteAsync((db, now, ct) => RootPublicEventPublications.CleanupAsync(db, candidate, now, bind: null, ct), token);
        }

        if (work == RootPublicEventStarts.MaintenanceWork.Live)
        {
            var candidate = await InAdmissionTransactionAsync((db, ct) => RootPublicEventLiveWaits.FindAsync(db, bind: null, ct),
                token, readOnly: true);
            return candidate is not null &&
                await WriteAsync((db, ct) => RootPublicEventLiveWaits.CatchUpAsync(db, candidate, bind: null, ct), token);
        }

        var pending = await InAdmissionTransactionAsync((db, ct) => RootPublicEventStarts.FindCatchUpAsync(db, bind: null, ct),
            token, readOnly: true);
        return pending is not null && await WriteAsync((db, ct) => RootPublicEventStarts.CatchUpAsync(db, pending, bind: null, ct), token);
    }

    internal async Task<Contract.Admission> PublishPublicEventAsync(string tenant, string key, string id, string? payload,
        Contract.AddOptions? options, CancellationToken token)
    {
        if (RootPublicEventPublications.Validate(key, id, payload, options) is { } refusal) return refusal;
        using var attempt = pressure?.Begin(tenant, RootAdmissionKind.Input);
        return await WriteAsync((db, now, ct) => RootEventAdmission.PublishOutsideAsync(db, tenant, key, id, payload, now,
            options?.KeepFor ?? TimeSpan.Zero, deduplicationWindow, attempt, bind: null, ct), token, attempt);
    }

    internal Task SetPublicEventStartAsync(string tenant, string key, string wait, Contract.StartSpec? spec, CancellationToken token) =>
        WriteAsync(async (db, now, ct) =>
        {
            if (!db.UsesRootPublicEventStarts) throw new NotSupportedException("The public event start profile is unavailable.");
            await RootPublicEventStarts.SetAsync(db, tenant, tenant + "_" + key, wait, spec, now, bind: null, ct);
            return true;
        }, token);
}
