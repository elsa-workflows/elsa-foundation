namespace Kernel.Sim;

/// <summary>
/// K1 slice 7: the host of a node in a set world, the node's <see cref="DurableHost.ForNode"/> host with its activations,
/// passes and give-ups queued in the order layer 1 asks for them. The host runs them one at a time under its lock, and a
/// contended lock resumes its waiter on the thread pool; the simulation is one thread, whose calls complete when it
/// answers them. The queue keeps every continuation on that thread and in order: an operation starts when the one before
/// it has ended, inline. Nothing else changes: the host and its worker are the runtime's.
/// </summary>
public sealed class SerialHost(DurableHost host) : IScopeHost
{
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
