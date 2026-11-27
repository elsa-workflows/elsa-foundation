using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed partial class SqliteRootAdmission
{
    internal Task SetPublicEventStartAsync(string tenant, string key, string wait, Contract.StartSpec? spec, CancellationToken token) =>
        WriteAsync(async (db, ct) =>
        {
            if (!db.UsesRootPublicEventStarts) throw new NotSupportedException("The public event start profile is unavailable.");
            await RootPublicEventStarts.SetAsync(db, tenant, tenant + "_" + key, wait, spec, bind: null, ct);
            return true;
        }, token);
}
