namespace Kernel;

/// <summary>The owner's defaults of 12 November. A test or the simulation may choose its own.</summary>
public static class Defaults
{
    /// <summary>A partition's lease.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(10);

    /// <summary>Partitions of a database that serves several nodes: PostgreSQL, SQL Server, MySQL, MongoDB.</summary>
    public const int ServerPartitions = 64;

    /// <summary>Partitions of SQLite, which has one writer: more would only add polls and fences.</summary>
    public const int SqlitePartitions = 1;
}

/// <summary>
/// A node's fair share of the partitions. A round says "I am alive", counts the nodes heard from, gives up what is
/// above the share and asks for free partitions up to it. Leases decide who should write and the fence who can; this
/// only decides who asks, so a wrong count costs availability, never correctness.
/// </summary>
public sealed class Balancer(string node, IKernelStore store, IReadOnlyList<Worker> workers, TimeSpan window, Flaw flaws = Flaw.None)
{
    private bool _running;

    public async Task RoundAsync(CancellationToken cancellationToken = default)
    {
        if (_running) return;
        _running = true;
        Cluster cluster;
        try { cluster = await store.HeartbeatAsync(node, window, cancellationToken); }
        catch (Exception) { return; }
        finally { _running = false; }
        var share = (workers.Count + cluster.Nodes - 1) / Math.Max(1, cluster.Nodes);    // rounded up, so the shares cover every partition
        if ((flaws & Flaw.GreedyBalancer) != 0) share = workers.Count;
        var held = workers.Count(w => w.Owns || w.Taking);
        foreach (var surplus in workers.Where(w => w.Owns).Reverse().Take(held - share).ToList())
        {
            await surplus.ReleaseAsync(cancellationToken);
            held--;
        }
        var takes = workers.Where(w => !w.Owns && !w.Taking && cluster.Free.Contains(w.Partition)).Take(share - held).Select(w => w.TakeAsync(cancellationToken)).ToList();
        await Task.WhenAll(takes);
    }
}
