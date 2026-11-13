namespace Kernel.Runtime;

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
/// The built-in actor runtime: partitions placed on the nodes of one process each. It activates and gives up scopes
/// through layer 2's <see cref="IScopeHost"/>, holds a node's fair share (<see cref="BalanceAsync"/>), calls each
/// scope one pass at a time (<see cref="PassAsync"/>) and turns wake hints into <see cref="IScopeActivation.WokenAsync"/>.
/// Like the rest of the kernel it has no clock, thread, timer or random number of its own: its host decides when a
/// round or a pass happens.
/// </summary>
public sealed class PartitionRuntime(string node, IMembership membership, int partitions, TimeSpan window, IWakeSignal? wake = null, Flaw flaws = Flaw.None) : IActorRuntime
{
    private readonly IScopeActivation?[] _active = new IScopeActivation?[partitions];
    private readonly bool[] _activating = new bool[partitions], _passing = new bool[partitions];
    private IScopeHost? _host;
    private bool _balancing;

    public string Node => node;
    public int Partitions => partitions;
    public IWakeSignal Wake { get; } = wake ?? new InProcessWakeSignal();

    public int ScopeOf(InstanceRef actor) => actor.Partition;

    public ValueTask StartAsync(IScopeHost host, CancellationToken cancellationToken = default)
    {
        _host = host;
        Wake.Listen(actor => _ = Woken(actor));
        return ValueTask.CompletedTask;
    }

    private async Task Woken(InstanceRef actor)
    {
        if (_active[ScopeOf(actor)] is { Active: true } activation) await activation.WokenAsync(actor);
    }

    public ValueTask WakeAsync(InstanceRef actor, CancellationToken cancellationToken = default)
    {
        Wake.Send(actor);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        for (var scope = 0; scope < partitions; scope++) await DeactivateAsync(scope, cancellationToken);
    }

    /// <summary>This node holds the scope's fence, as far as layer 2 knows.</summary>
    public bool Owns(int scope) => _active[scope] is { Active: true };

    /// <summary>An activation of the scope is on its way.</summary>
    public bool Activating(int scope) => _activating[scope];

    public IScopeActivation? Activation(int scope) => _active[scope] is { Active: true } activation ? activation : null;

    /// <summary>Ask layer 2 for the scope: it takes the fence, or declines. A scope already held is taken again, from empty memory.</summary>
    public async Task<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default)
    {
        if (_activating[scope]) return null;
        _activating[scope] = true;
        IScopeActivation? activation;
        try { activation = await (_host ?? throw new InvalidOperationException("The runtime has not started.")).ActivateAsync(scope, cancellationToken); }
        finally { _activating[scope] = false; }
        if (activation is not null) _active[scope] = activation;
        return activation;
    }

    /// <summary>Give the scope up. Whoever takes it next finds no death.</summary>
    public async Task DeactivateAsync(int scope, CancellationToken cancellationToken = default)
    {
        if (Activation(scope) is not { } activation) return;
        _active[scope] = null;
        await activation.DisposeAsync();
    }

    /// <summary>
    /// A node's fair share of the partitions. A round says "I am alive", counts the nodes heard from, gives up what is
    /// above the share and asks for free partitions up to it. Leases decide who should write and the fence who can;
    /// this only decides who asks, so a wrong count costs availability, never correctness.
    /// </summary>
    public async Task BalanceAsync(CancellationToken cancellationToken = default)
    {
        if (_balancing) return;
        _balancing = true;
        Cluster cluster;
        try { cluster = await membership.HeartbeatAsync(node, window, cancellationToken); }
        catch (Exception) { return; }
        finally { _balancing = false; }
        var share = (partitions + cluster.Nodes - 1) / Math.Max(1, cluster.Nodes);    // rounded up, so the shares cover every partition
        if ((flaws & Flaw.GreedyBalancer) != 0) share = partitions;
        var scopes = Enumerable.Range(0, partitions).ToList();
        var held = scopes.Count(s => Owns(s) || Activating(s));
        foreach (var surplus in scopes.Where(Owns).Reverse().Take(held - share).ToList())
        {
            await DeactivateAsync(surplus, cancellationToken);
            held--;
        }
        var takes = scopes.Where(s => !Owns(s) && !Activating(s) && cluster.Free.Contains(s)).Take(share - held).Select(s => ActivateAsync(s, cancellationToken)).ToList();
        await Task.WhenAll(takes);
    }

    /// <summary>One pass of a scope this node holds, never two at once. Null when it holds none, or a pass is under way.</summary>
    public async ValueTask<TimeSpan?> PassAsync(int scope, CancellationToken cancellationToken = default)
    {
        if (_passing[scope] || Activation(scope) is not { } activation) return null;
        _passing[scope] = true;
        try { return await activation.RunAsync(cancellationToken); }
        finally { _passing[scope] = false; }
    }
}
