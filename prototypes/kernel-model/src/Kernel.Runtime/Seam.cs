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
/// K1 slice 7: a host that checks, when the runtime starts it, that the runtime's scopes, its workers' placement count
/// and its store's partitions agree. The count is fixed when the database is created; a runtime or a worker at another
/// count would place work on partitions with no row, or leave some unserved. A mismatch refuses the start.
/// </summary>
public interface IScopeCount
{
    ValueTask CheckScopesAsync(int scopes, CancellationToken cancellationToken = default);
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
    /// work, or after a delay for the next timer it holds; null once it no longer holds the scope. A cancelled token stops
    /// the pass at a turn boundary, before its commit: a process that goes away.
    /// <para>
    /// R1, the lease. Under the node lease (<see cref="INodeLeaseHost"/>) a pass renews nothing: the node's heartbeat
    /// renews one lease for every scope, and only while each pass completes within the lease (the pass gate). A runtime
    /// that calls again when asked, at least once per poll interval, keeps its node live; one whose passes stop completing
    /// stops its renewal, and another node takes its scopes.
    /// </para>
    /// </summary>
    ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// K1b: a host whose node keeps one lease for every scope it holds (ownership-and-fencing, "The node's heartbeat" and
/// "Liveness follows the Worker's passes"). The runtime's node sends the heartbeat on a cadence of its own, at least three
/// times per lease and at most half the membership window apart, apart from its balancing rounds; the host's pass gate
/// decides whether it renews. The runtime also runs the passes no active scope gives, and adopts what they take back.
/// </summary>
public interface INodeLeaseHost
{
    /// <summary>The node's lease; null while the store keeps a lease per partition, which the passes renew.</summary>
    TimeSpan? NodeLease { get; }

    /// <summary>
    /// One heartbeat, if the pass gate lets it: the node record's write, then the membership reads balancing needs. The
    /// cluster it saw when it renewed; null when it did not, a failure of any kind being a missed renewal.
    /// </summary>
    ValueTask<Cluster?> HeartbeatAsync(TimeSpan window, CancellationToken cancellationToken = default);

    /// <summary>
    /// The passes no active scope gives: of a Worker whose only partitions are pending, or that holds none while a
    /// heartbeat answer waits for it. Returns the scopes the host's Workers brought back by themselves since the last call
    /// (a pending retake); the runtime activates the ones it does not know, as a scope already held. A scope the runtime
    /// gave up is never among them: a give-up ends its pending entry, and a retake in flight then releases.
    /// </summary>
    ValueTask<IReadOnlyCollection<int>> PassAsync(CancellationToken cancellationToken = default);
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

/// <summary>The wake signal of one process: a call to every listener, at once, on the sender's thread.</summary>
public sealed class InProcessWakeSignal : IWakeSignal
{
    private readonly List<Action<InstanceRef>> _listeners = [];

    public void Send(InstanceRef actor)
    {
        Action<InstanceRef>[] listeners;
        lock (_listeners) listeners = [.. _listeners];
        foreach (var listener in listeners) listener(actor);
    }

    public void Listen(Action<InstanceRef> woken)
    {
        lock (_listeners) _listeners.Add(woken);
    }
}

/// <summary>
/// What a heartbeat sees: how many nodes were heard from within the window, and which partitions nobody holds.
/// <paramref name="Rank"/> is this node's place among them in name order, where the membership knows it (0 otherwise);
/// only the <see cref="Flaw.ContiguousOnlyBalancer"/> mutation reads it.
/// </summary>
public sealed record Cluster(int Nodes, IReadOnlyList<int> Free, long Now, int Rank = 0);

/// <summary>The built-in runtime's view of the cluster, kept by the store: a node's heartbeat and the partitions nobody holds.</summary>
public interface IMembership
{
    /// <summary>
    /// Say that this node is alive (the <c>node</c> row) and look at the cluster: the nodes heard from within
    /// <paramref name="window"/>, and the partitions that have no owner or whose lease has expired. Liveness only.
    /// Lease renewal runs through the node heartbeat. </summary>
    ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default);
}
