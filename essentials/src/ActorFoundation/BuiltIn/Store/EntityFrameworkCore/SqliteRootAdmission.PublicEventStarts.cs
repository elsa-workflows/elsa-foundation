using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed partial class SqliteRootAdmission
{
    internal async Task<Contract.Admission> PublishPublicEventAsync(string tenant, string key, string id, string? payload,
        Contract.AddOptions? options, CancellationToken token)
    {
        if (RootPublicEventPublications.Validate(key, id, payload, options) is { } refusal) return refusal;
        using var attempt = pressure?.Begin(tenant, RootAdmissionKind.Input);
        return await WriteAsync((db, now, ct) => RootEventAdmission.PublishOutsideAsync(db, tenant, key, id, payload, now,
            options?.KeepFor ?? TimeSpan.Zero, deduplicationWindow, attempt, bind: null, ct), token, attempt);
    }

    internal Task SetPublicEventStartAsync(string tenant, string key, string wait, Contract.StartSpec? spec, CancellationToken token) =>
        WriteAsync(async (db, ct) =>
        {
            if (!db.UsesRootPublicEventStarts) throw new NotSupportedException("The public event start profile is unavailable.");
            await RootPublicEventStarts.SetAsync(db, tenant, tenant + "_" + key, wait, spec, bind: null, ct);
            return true;
        }, token);
}
