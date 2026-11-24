namespace Kernel.Runtime;

/// <summary>The owner's defaults of 12 November. A test or the simulation may choose its own.</summary>
public static class Defaults
{
    /// <summary>A node's lease.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Virtual partitions of a database that serves several nodes: PostgreSQL, SQL Server, MySQL, MongoDB. K1 slice 7:
    /// the owner's 1,024, each with its own fence row and journal (it was 64 physical partitions before K1).
    /// </summary>
    public const int ServerPartitions = VirtualPartitions.Provisional;

    /// <summary>
    /// Virtual partitions of SQLite: the same 1,024. It has one writer, so one node owns them all, as one set with one
    /// poll per pass and a flush that fences the partitions it touches. A store without owned sets (<c>commit=ef</c>)
    /// gets a worker per partition, so a deployment that chooses it passes a smaller count. Before K1 it was 1.
    /// </summary>
    public const int SqlitePartitions = VirtualPartitions.Provisional;
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
    private readonly bool[] _releasing = new bool[partitions];
    private IScopeHost? _host;
    private bool _balancing;
    private Task? _stopSet;
    private Cluster? _cluster;      // K1b: what the node's last renewing heartbeat saw, for the balancing round

    public string Node => node;
    public int Partitions => partitions;
    public IWakeSignal Wake { get; } = wake ?? new InProcessWakeSignal();

    public int ScopeOf(InstanceRef actor) => actor.Partition;

    public async ValueTask StartAsync(IScopeHost host, CancellationToken cancellationToken = default)
    {
        if (host is IScopeCount counted) await counted.CheckScopesAsync(partitions, cancellationToken);     // refuses a mismatch
        _host = host;
        Wake.Listen(actor => _ = Woken(actor));
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
        if (cancellationToken.IsCancellationRequested) return;
        if (_stopSet is { } stopping)
        {
            await stopping.WaitAsync(cancellationToken);
            return;
        }
        // The clocked host joins its loops before stopping. Retain the scalar path when a caller stops with work in flight.
        if (_host is IScopeSetRelease { SupportsSetRelease: true } setHost &&
            !_passing.Contains(true) && !_activating.Contains(true) && !_releasing.Contains(true))
        {
            var held = Enumerable.Range(0, partitions).Select(Activation).OfType<IScopeActivation>().ToArray();
            if (held.Length > 1)
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _stopSet = completion.Task;
                try
                {
                    await ReleaseSetAsync(setHost, held, cancellationToken);
                    completion.SetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.SetCanceled(cancellationToken);
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                    _ = completion.Task.Exception;
                    throw;
                }
                finally { _stopSet = null; }
                return;
            }
        }
        // A cancelled token ends it: what is not given up yet runs out by its lease, as after a crash.
        for (var scope = 0; scope < partitions && !cancellationToken.IsCancellationRequested; scope++) await DeactivateAsync(scope, cancellationToken);
    }

    /// <summary>This node holds the scope's fence, as far as layer 2 knows.</summary>
    public bool Owns(int scope) => _active[scope] is { Active: true };

    /// <summary>An activation of the scope is on its way.</summary>
    public bool Activating(int scope) => _activating[scope];

    public IScopeActivation? Activation(int scope) => _active[scope] is { Active: true } activation ? activation : null;

    /// <summary>
    /// Ask layer 2 for the scope: it takes the fence, or declines. A scope already held is taken again, from empty memory,
    /// so the new activation is woken: its first pass reads the inbox.
    /// </summary>
    public async Task<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default)
    {
        if (_activating[scope] || _releasing[scope]) return null;
        _activating[scope] = true;
        IScopeActivation? activation;
        try { activation = await (_host ?? throw new InvalidOperationException("The runtime has not started.")).ActivateAsync(scope, cancellationToken); }
        finally { _activating[scope] = false; }
        if (activation is null) return null;
        _active[scope] = activation;
        await activation.WokenAsync(null, cancellationToken);     // a take starts from empty memory: the first pass reads the inbox
        return activation;
    }

    /// <summary>Give the scope up. Whoever takes it next finds no death.</summary>
    public async Task DeactivateAsync(int scope, CancellationToken cancellationToken = default)
    {
        if (_releasing[scope] || Activation(scope) is not { } activation) return;
        _releasing[scope] = true;
        _active[scope] = null;
        try { await activation.DisposeAsync(); }
        finally { _releasing[scope] = false; }
    }

    /// <summary>
    /// A node's fair share of the partitions. A round says "I am alive", counts the nodes heard from, gives up what is
    /// above the share and asks for free partitions up to it (<see cref="Balancer.Plan"/>). Leases decide who should write
    /// and the fence who can; this only decides who asks, so a wrong count costs availability, never correctness. K1b:
    /// under the node lease the round reads the cluster the node's last renewing heartbeat saw, and none before the first.
    /// </summary>
    public async Task BalanceAsync(CancellationToken cancellationToken = default)
    {
        if (_balancing) return;
        _balancing = true;
        try { await BalanceRoundAsync(cancellationToken); }
        finally { _balancing = false; }
    }

    private async Task BalanceRoundAsync(CancellationToken cancellationToken)
    {
        Cluster cluster;
        if (NodeLease is not null)
        {
            if (Volatile.Read(ref _cluster) is not { } seen) return;
            cluster = seen;
        }
        else
        {
            // A host without a node lease can still refresh membership, as the conformance stubs do.
            try { cluster = await membership.HeartbeatAsync(node, window, cancellationToken); }
            catch (Exception) { return; }
        }
        var scopes = Enumerable.Range(0, partitions).ToList();
        var (release, take) = Balancer.Plan(partitions, cluster, scopes.Where(Owns).ToList(), scopes.Where(Activating).ToList(), flaws);
        if (release.Count > 1 && _host is IScopeSetRelease { SupportsSetRelease: true } setHost)
        {
            if (release.Any(scope => _passing[scope] || _activating[scope] || _releasing[scope])) return;     // hand the set over when every surplus scope is idle
            cancellationToken.ThrowIfCancellationRequested();     // a process that goes away mid-round gives nothing more up
            var activations = release.Select(Activation).OfType<IScopeActivation>().ToArray();
            if (activations.Length > 0) await ReleaseSetAsync(setHost, activations, cancellationToken);
        }
        else
        {
            foreach (var surplus in release)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Earlier give-ups may have awaited the store while a pass started on this worker.
                if (_passing[surplus] || _activating[surplus] || !Owns(surplus)) continue;
                await DeactivateAsync(surplus, cancellationToken);
            }
        }
        if (take.Count > 1 && _host is IScopeSetGain { SupportsSetGain: true } gains)
        {
            // A release may have yielded since the plan was made.
            var requested = take.Where(scope => !Owns(scope) && !_activating[scope] && !_releasing[scope]).ToArray();
            if (requested.Length == 0) return;
            foreach (var scope in requested) _activating[scope] = true;
            IReadOnlyList<IScopeActivation> gained;
            try
            {
                gained = await gains.ActivateAsync(requested, cancellationToken);
                foreach (var activation in gained) _active[activation.Scope] = activation;
            }
            finally
            {
                foreach (var scope in requested) _activating[scope] = false;
            }
            foreach (var activation in gained) await activation.WokenAsync(null, cancellationToken);
        }
        else await Task.WhenAll(take.Select(s => ActivateAsync(s, cancellationToken)));
    }

    private async ValueTask ReleaseSetAsync(IScopeSetRelease host, IScopeActivation[] activations,
        CancellationToken cancellationToken)
    {
        foreach (var activation in activations) _releasing[activation.Scope] = true;
        try
        {
            await host.ReleaseAsync(activations, cancellationToken);
            foreach (var activation in activations)
                if (!activation.Active && ReferenceEquals(_active[activation.Scope], activation)) _active[activation.Scope] = null;
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            foreach (var activation in activations) _releasing[activation.Scope] = false;
        }
    }

    /// <summary>K1b: the node's lease, when the host keeps one (<see cref="INodeLeaseHost"/>); null for a host with no node lease.</summary>
    public TimeSpan? NodeLease => (_host as INodeLeaseHost)?.NodeLease;

    /// <summary>
    /// K1b: the node's heartbeat, through a host that keeps a node lease. False when it did not renew (the pass gate held
    /// it back, or it failed) or the host keeps none. Its host calls it on its own cadence, never behind a round's moves.
    /// The cluster it saw is the next balancing round's: under the node lease the round writes nothing of its own.
    /// </summary>
    public async ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        if (_host is not INodeLeaseHost { NodeLease: not null } host) return false;
        if (await host.HeartbeatAsync(window, cancellationToken) is not { } cluster)
        {
            Volatile.Write(ref _cluster, null);
            return false;
        }
        // The free list is the store's; this runtime places its own scopes only (a world that places part of the store's).
        Volatile.Write(ref _cluster, cluster.Free.All(p => p < partitions) ? cluster : cluster with { Free = [.. cluster.Free.Where(p => p < partitions)] });
        return true;
    }

    /// <summary>
    /// K1b: the passes no active scope gives (<see cref="INodeLeaseHost.PassAsync"/>), then the scopes the host's Workers
    /// brought back by themselves that this runtime does not know are activated, as a scope already held. Its host calls it
    /// with the scopes' passes.
    /// </summary>
    public async Task NodePassAsync(CancellationToken cancellationToken = default)
    {
        if (_host is not INodeLeaseHost { NodeLease: not null } host) return;
        foreach (var scope in await host.PassAsync(cancellationToken))
            if ((uint)scope < (uint)partitions && !Owns(scope) && !_activating[scope]) await ActivateAsync(scope, cancellationToken);
    }

    /// <summary>One pass of a scope this node holds, never two at once. Null when it holds none, or a pass is under way.</summary>
    public async ValueTask<TimeSpan?> PassAsync(int scope, CancellationToken cancellationToken = default) =>
        (await PassWithAgeAsync(scope, cancellationToken)).Delay;

    /// <summary>The serialized pass with the optional fairness hint used by a clocked host.</summary>
    public async ValueTask<ScopePass> PassWithAgeAsync(int scope, CancellationToken cancellationToken = default)
    {
        if (_passing[scope] || _releasing[scope] || Activation(scope) is not { } activation) return default;
        _passing[scope] = true;
        try { return await activation.RunWithAgeAsync(cancellationToken); }
        finally { _passing[scope] = false; }
    }
}

/// <summary>
/// The built-in balancer's round as a pure decision (K1: fewest moves). A node's share is the partitions over the nodes
/// heard from, rounded up so the shares cover every partition. It gives up only what is above its share, the highest
/// first, and asks for free partitions up to it, the lowest first. A join therefore moves only the surplus above the new
/// share and a leave only the leaver's partitions, and a node's set may be scattered, which costs nothing with a fence row
/// per partition (PARTITIONS.md).
/// </summary>
public static class Balancer
{
    public static (IReadOnlyList<int> Release, IReadOnlyList<int> Take) Plan(int partitions, Cluster cluster, IReadOnlyCollection<int> owned, IReadOnlyCollection<int> activating, Flaw flaws = Flaw.None)
    {
        var claimed = owned.ToHashSet();
        claimed.UnionWith(activating);     // reactivating a partition already owned occupies one place in the share
        if ((flaws & Flaw.ContiguousOnlyBalancer) != 0)
        {
            // MUTATION: one contiguous range per node, its fair one by rank. Whatever lies outside it moves.
            var range = VirtualPartitions.FairRanges(partitions, Math.Max(1, cluster.Nodes)).ElementAtOrDefault(cluster.Rank);
            return ([.. owned.Where(p => !range.Contains(p)).OrderDescending()], [.. cluster.Free.Where(p => range.Contains(p) && !claimed.Contains(p)).Order()]);
        }
        var share = (flaws & Flaw.GreedyBalancer) != 0 ? partitions : (partitions + cluster.Nodes - 1) / Math.Max(1, cluster.Nodes);
        var release = owned.OrderDescending().Take(Math.Max(0, claimed.Count - share)).ToList();
        var held = claimed.Count - release.Count;
        return (release, [.. cluster.Free.Where(p => !claimed.Contains(p)).Order().Take(Math.Max(0, share - held))]);
    }
}
