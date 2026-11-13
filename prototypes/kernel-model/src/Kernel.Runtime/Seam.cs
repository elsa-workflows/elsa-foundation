namespace Kernel.Runtime;

/// <summary>
/// Layer 1, the actor runtime: it decides which node hosts each ownership scope and calls the scope one pass at a time.
/// A scope is a partition (the owner's decision of 12 November: runtimes host partitions only), so
/// <see cref="ScopeOf"/> is the actor's partition. The runtime gives no guarantee about messages or state: layer 2
/// does not trust it, and its fence catches a second activation of a scope that a runtime briefly allows.
/// </summary>
public interface IActorRuntime
{
    int ScopeOf(InstanceRef actor);
    ValueTask StartAsync(IScopeHost host, CancellationToken cancellationToken = default);

    /// <summary>A hint that an actor may have work. A lost hint loses nothing: the scope's poll is the guarantee.</summary>
    ValueTask WakeAsync(InstanceRef actor, CancellationToken cancellationToken = default);
    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Implemented by layer 2: activating a scope takes its fence, or declines. A new activation starts from empty memory,
/// so the runtime wakes it (<see cref="IScopeActivation.WokenAsync"/> with null) before its first pass.
/// </summary>
public interface IScopeHost
{
    ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default);
}

/// <summary>
/// One scope while layer 2 holds its fence. The runtime calls <see cref="RunAsync"/> one pass at a time and never on two
/// nodes at once as far as it knows; disposing gives the scope up.
/// </summary>
public interface IScopeActivation : IAsyncDisposable
{
    int Scope { get; }

    /// <summary>False once layer 2 lost the fence or gave the scope up: the runtime activates it again or lets it go.</summary>
    bool Active { get; }

    /// <summary>A hint: an actor of this scope may have work (null: the runtime's poll interval passed). The next pass reads the inbox.</summary>
    ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// One pass: the turns that are due, committed in batches. Returns when to call again: at once (zero) while there is
    /// work, or after a delay for the next timer it holds or the next renewal of its lease, which layer 2 makes itself;
    /// null once it no longer holds the scope. A runtime that calls again when asked keeps an idle scope's lease. A
    /// cancelled token stops the pass at a turn boundary, before its commit: a process that goes away.
    /// </summary>
    ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Hints between nodes, over the runtime's transport: "there may be work for this actor". Pluggable; the built-in
/// runtime's is <see cref="InProcessWakeSignal"/>. Not durable and not needed for correctness.
/// </summary>
public interface IWakeSignal
{
    void Send(InstanceRef actor);
    void Listen(Action<InstanceRef> woken);
}

/// <summary>The wake signal of one process: a call to every listener, at once.</summary>
public sealed class InProcessWakeSignal : IWakeSignal
{
    private readonly List<Action<InstanceRef>> _listeners = [];

    public void Send(InstanceRef actor)
    {
        foreach (var listener in _listeners.ToList()) listener(actor);
    }

    public void Listen(Action<InstanceRef> woken) => _listeners.Add(woken);
}

/// <summary>What a heartbeat sees: how many nodes were heard from within the window, and which partitions nobody holds.</summary>
public sealed record Cluster(int Nodes, IReadOnlyList<int> Free, long Now);

/// <summary>The built-in runtime's view of the cluster, kept by the store: a node's heartbeat and the partitions nobody holds.</summary>
public interface IMembership
{
    /// <summary>
    /// Say that this node is alive (the <c>node</c> row) and look at the cluster: the nodes heard from within
    /// <paramref name="window"/>, and the partitions that have no owner or whose lease has expired. Liveness only.
    /// </summary>
    ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default);
}
