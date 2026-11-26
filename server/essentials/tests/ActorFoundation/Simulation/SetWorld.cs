namespace Kernel.Sim;

/// <summary>
/// K1 slice 7: the host of a node in a set world, the node's <see cref="DurableHost.ForNode"/> host with its activations,
/// passes and give-ups queued in the order layer 1 asks for them. The host runs them one at a time under its lock, and a
/// contended lock resumes its waiter on the thread pool; the simulation is one thread, whose calls complete when it
/// answers them. The queue keeps every continuation on that thread and in order: an operation starts when the one before
/// it has ended, inline. Nothing else changes: the host and its worker are the runtime's.
/// </summary>
public sealed class SerialHost(DurableHost host) : IScopeHost, IScopeCount, INodeLeaseHost
{
    // K1b: the node lease. The heartbeat runs off the turn, as the built-in runtime's own loop sends it; the node passes
    // are queued with the host's other operations.
    public TimeSpan? NodeLease => host.NodeLease;
    public ValueTask<Cluster?> HeartbeatAsync(TimeSpan window, CancellationToken cancellationToken = default) => host.HeartbeatAsync(window, cancellationToken);
    public async ValueTask<IReadOnlyCollection<int>> PassAsync(CancellationToken cancellationToken = default) => await Queued(() => host.PassAsync(cancellationToken));

    public ValueTask CheckScopesAsync(int scopes, CancellationToken cancellationToken = default) => host.CheckScopesAsync(scopes, cancellationToken);
    public IScopeHostRun? AttachRuntime() => ((IScopeHost)host).AttachRuntime();

    private Task _tail = Task.CompletedTask;
    private readonly Dictionary<int, SerialScope> _scopes = [];

    private async Task<T> Queued<T>(Func<ValueTask<T>> operation)
    {
        var (before, done) = (_tail, new TaskCompletionSource());
        _tail = done.Task;
        try
        {
            await before;
            return await operation();
        }
        finally { done.SetResult(); }
    }

    /// <summary>K1b: a whole pass of the node's Worker, queued as the host's own operations are (under its turn).</summary>
    public async Task RunNodeAsync(CancellationToken cancellationToken = default) => await Queued(() => host.RunNodeAsync(cancellationToken));

    public async ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default) =>
        await Queued(() => host.ActivateAsync(scope, cancellationToken)) is { } activation
            ? _scopes.TryGetValue(scope, out var known) ? known : _scopes[scope] = new SerialScope(this, activation)
            : null;

    private sealed class SerialScope(SerialHost host, IScopeActivation inner) : IScopeActivation
    {
        public int Scope => inner.Scope;
        public bool Active => inner.Active;
        public ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default) => inner.WokenAsync(actor, cancellationToken);
        public async ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default) => await host.Queued(() => inner.RunAsync(cancellationToken));
        public async ValueTask DisposeAsync() => await host.Queued(async () => { await inner.DisposeAsync(); return true; });
    }
}

/// <summary>
/// K1b review A: the give-ups a node's layer 1 made, as its host sees them. A scope given up (an activation disposed:
/// a <c>LoseAsync</c> of the node's Worker, or a fixed Worker's release) is recorded until the runtime asks for it again.
/// A partition its Worker holds that the runtime does not know is adopted only when the runtime never gave it up since its
/// last activation: the Worker brought it back by itself (a pending retake). One given up is a bug, reported, not adopted.
/// </summary>
public sealed class GiveUps(IScopeHost inner) : IScopeHost, IScopeCount, INodeLeaseHost
{
    public TimeSpan? NodeLease => (inner as INodeLeaseHost)?.NodeLease;
    public ValueTask<Cluster?> HeartbeatAsync(TimeSpan window, CancellationToken cancellationToken = default) =>
        inner is INodeLeaseHost lease ? lease.HeartbeatAsync(window, cancellationToken) : ValueTask.FromResult<Cluster?>(null);
    public ValueTask<IReadOnlyCollection<int>> PassAsync(CancellationToken cancellationToken = default) =>
        inner is INodeLeaseHost lease ? lease.PassAsync(cancellationToken) : ValueTask.FromResult<IReadOnlyCollection<int>>([]);

    private readonly Dictionary<int, (IScopeActivation Inner, Recorded Outer)> _scopes = [];
    public HashSet<int> GivenUp { get; } = [];

    public ValueTask CheckScopesAsync(int scopes, CancellationToken cancellationToken = default) =>
        inner is IScopeCount counted ? counted.CheckScopesAsync(scopes, cancellationToken) : ValueTask.CompletedTask;
    public IScopeHostRun? AttachRuntime() => inner.AttachRuntime();

    public async ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default)
    {
        GivenUp.Remove(scope);      // the runtime asks for it again: what the attempt's take brings back is the runtime's
        if (await inner.ActivateAsync(scope, cancellationToken) is not { } activation) return null;
        if (_scopes.TryGetValue(scope, out var known) && known.Inner == activation) return known.Outer;
        var outer = new Recorded(this, activation);
        _scopes[scope] = (activation, outer);
        return outer;
    }

    private sealed class Recorded(GiveUps host, IScopeActivation inner) : IScopeActivation
    {
        public int Scope => inner.Scope;
        public bool Active => inner.Active;
        public ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default) => inner.WokenAsync(actor, cancellationToken);
        public ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default) => inner.RunAsync(cancellationToken);
        public ValueTask DisposeAsync()
        {
            host.GivenUp.Add(inner.Scope);
            return inner.DisposeAsync();
        }
    }
}

/// <summary>
/// The membership a world's layer 1 sees: the store's, with the free partitions cut to the world's. A world's store has a
/// fence row for each of the count's virtual partitions (1,024 or 4,096); its layer 1 places, and its traffic addresses,
/// the first <c>partitions</c> of them, so the fair share is of those. The rest stay free, and nothing asks for them.
/// </summary>
public sealed class Placed(IMembership store, int partitions) : IMembership
{
    public async ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default)
    {
        var cluster = await store.HeartbeatAsync(node, window, cancellationToken);
        return cluster with { Free = [.. cluster.Free.Where(p => p < partitions)] };
    }
}
