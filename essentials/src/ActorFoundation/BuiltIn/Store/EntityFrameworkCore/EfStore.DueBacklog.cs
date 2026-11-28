using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal RootAdmissionPressure? AdmissionPressure { get; init; }

    internal static async Task<RootAdmissionPressure> OpenStandalonePostgresPressureAsync(Func<KernelDb> create, CancellationToken token)
    {
        var pressure = RootAdmissionPressure.CreateDefault();
        var store = new EfStore(create, postgres: true) { AdmissionPressure = pressure };
        await store.PrimeAdmissionPressureAsync(token);
        pressure.RefreshOnDemand(store.PrimeAdmissionPressureAsync);
        return pressure;
    }

    internal async Task PrimeAdmissionPressureAsync(CancellationToken cancellationToken)
    {
        if (AdmissionPressure is null) return;
        await using var db = create();
        await using var pinned = await BeginPinnedRootOperationAsync(db, cancellationToken);
        var nodes = postgres ? await db.Database.SqlQueryRaw<int>("""
            SELECT CAST(count(*) AS integer) AS "Value" FROM "Nodes"
            WHERE "LeaseUntil" >= floor(extract(epoch from statement_timestamp()) * 1000)::bigint
            """).SingleAsync(cancellationToken) : 1;
        await RefreshAdmissionPressureAsync(db, nodes, cancellationToken);
    }

    private async Task RefreshAdmissionPressureAsync(KernelDb db, int nodes, CancellationToken cancellationToken)
    {
        if (AdmissionPressure is not { } pressure) return;
        if (!db.UsesRootDueBacklog)
            throw new StoreFormatException("Admission pressure requires the due-backlog store profile.");
        var depths = await db.Partitions.Where(partition => partition.Backlog >= pressure.Backlog.LowWater)
            .ToDictionaryAsync(partition => partition.Id, partition => partition.Backlog, cancellationToken);
        pressure.Refresh(depths, nodes);
    }

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
        var now = DueBacklogClock(dialect);
        return await db.Database.SqlQueryRaw<int>($$"""
            SELECT CAST(count(*) AS integer) AS "Value" FROM (
                SELECT 1 FROM "Messages"
                WHERE "Partition" = {0} AND "Due" <= {{now}} AND "Done" = {{dialect.False}}
                LIMIT {1}
            ) AS due
            """, partition, cap).SingleAsync(cancellationToken);
    }

    async ValueTask<IReadOnlyList<DueBacklogSample>> IDueBacklogStore.SampleDueBacklogAsync(IReadOnlyList<DueBacklogRead> reads,
        int cap, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cap);
        if (reads.Count == 0) return [];
        await using var db = create();
        RequireDueBacklog(db);
        await using var pinned = await BeginPinnedRootOperationAsync(db, cancellationToken);
        var dialect = RequireDialect();
        var ids = reads.Select(read => read.Partition).ToArray();
        var seeds = reads.Where(read => read.ReadPublished).Select(read => read.Partition).ToArray();
        var set = postgres ? """p."Id" = ANY({0})""" : """p."Id" IN (SELECT value FROM json_each({0}))""";
        var seed = postgres ? """p."Id" = ANY({1})""" : """p."Id" IN (SELECT value FROM json_each({1}))""";
        // The root poll only covers eligible roots and can omit held work. The partial index counts physical
        // due work in one provider read, with an independently limited seek for each partition.
        var sql = $$"""
            SELECT p."Id" AS "Partition", CASE WHEN {{seed}} THEN p."Backlog" END AS "Published",
                CAST((SELECT count(*) FROM (
                    SELECT 1 FROM "Messages" m
                    WHERE m."Partition" = p."Id" AND m."Due" <= {{DueBacklogClock(dialect)}} AND m."Done" = {{dialect.False}}
                    LIMIT {2}
                ) AS due) AS integer) AS "Estimate"
            FROM "Partitions" p WHERE {{set}}
            """;
        return await db.Database.SqlQuery<DueBacklogSample>(FormattableStringFactory.Create(sql,
            postgres ? ids : (object)Json.Write(ids), postgres ? seeds : (object)Json.Write(seeds), cap)).ToListAsync(cancellationToken);
    }

    // A statement-stable cutoff lets PostgreSQL seek on Due as well as Partition.
    private string DueBacklogClock(CommitDialect dialect) =>
        postgres ? "floor(extract(epoch from statement_timestamp()) * 1000)::bigint" : dialect.Now;

    private void RequireDueBacklog(KernelDb db)
    {
        if (!SupportsDueBacklog || !db.UsesRootDueBacklog)
            throw new NotSupportedException("The root owner is not configured to sample due backlog.");
    }
}
