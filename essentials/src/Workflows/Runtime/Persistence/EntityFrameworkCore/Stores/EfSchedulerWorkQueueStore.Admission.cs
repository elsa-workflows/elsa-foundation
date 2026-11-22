using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfSchedulerWorkQueueStore
{
    internal RuntimeDbContext Context => context;
    internal PersistenceAccessContext AccessContext => accessContextAccessor.Current;

    internal async ValueTask<RuntimeSchedulerWorkItem?> FindAdmissionAsync(string executionId, string workId, CancellationToken cancellationToken)
    {
        var scope = CurrentScope().Value;
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, executionId, workId);
        var row = await context.SchedulerWorkItems.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return row is null ? null : ReadChecked(row, scope, executionId, workId);
    }
}
