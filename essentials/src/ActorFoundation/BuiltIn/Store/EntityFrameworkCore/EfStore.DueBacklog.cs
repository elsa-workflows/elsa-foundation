using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private bool SupportsDueBacklog => _ordinaryRoots is { MeasuresDueBacklog: true } && OwnedCommit;

    bool IDueBacklogStore.MeasuresDueBacklog => SupportsDueBacklog;

    async ValueTask<int> IDueBacklogStore.ReadPublishedDueBacklogAsync(int partition, CancellationToken cancellationToken)
    {
        await using var db = create();
        RequireDueBacklog(db);
        await using var pinned = await BeginPinnedRootOperationAsync(db, cancellationToken);
        return await db.Partitions.Where(row => row.Id == partition).Select(row => row.Backlog).SingleAsync(cancellationToken);
    }

    async ValueTask<int> IDueBacklogStore.CountDueBacklogAsync(int partition, int cap, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(partition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cap);
        await using var db = create();
        RequireDueBacklog(db);
        await using var pinned = await BeginPinnedRootOperationAsync(db, cancellationToken);
        var dialect = RequireDialect();
        // A statement-stable cutoff lets PostgreSQL seek on Due as well as Partition.
        var now = postgres ? "(extract(epoch from statement_timestamp()) * 1000)::bigint" : dialect.Now;
        return await db.Database.SqlQueryRaw<int>($$"""
            SELECT CAST(count(*) AS integer) AS "Value" FROM (
                SELECT 1 FROM "Messages"
                WHERE "Partition" = {0} AND "Due" <= {{now}} AND "Done" = {{dialect.False}}
                LIMIT {1}
            ) AS due
            """, partition, cap).SingleAsync(cancellationToken);
    }

    private void RequireDueBacklog(KernelDb db)
    {
        if (!SupportsDueBacklog || !db.UsesRootDueBacklog)
            throw new NotSupportedException("The root owner is not configured to sample due backlog.");
    }
}
