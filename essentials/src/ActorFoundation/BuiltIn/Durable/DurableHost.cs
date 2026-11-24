using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// Layer 2's side of the seam for one process. Activating a scope takes its partition's fence, or declines when the
/// store refuses. It does not trust the runtime: if a runtime activates a scope on two nodes at once, the fence lets one
/// of them write. Two shapes:
/// <list type="bullet">
/// <item>Per node (<see cref="ForNode"/>, K1 D1): one worker serves every scope the node activates, as one owned set.
/// Activating a scope adds its partition to the set (a take of that one fence row). A balance round can give up several
/// of the set's partitions together. The set may be scattered. The worker polls the set in one query and flushes its partitions in one
/// batch. The node's passes, activations and give-ups run one at a time, so the set changes only between passes, and
/// one pass serves every scope: a scope that another scope's pass served since its own last call gets that answer.</item>
/// <item>Fixed (the constructor): one worker per scope, given up front and activated by its lowest partition. The models
/// and the simulation use it, and so does a node on a store without owned sets (<c>commit=ef</c>: one partition per worker).</item>
/// </list>
/// </summary>
public sealed class DurableHost : IScopeHost, IScopeCount, INodeLeaseHost, IScopeSetRelease, IScopeSetGain
{
    private NodeHeartbeat? _beat;       // K1b: the node's heartbeat over this host's Workers, made at the first heartbeat
    private (IKernelStore Store, int Placed, bool Part)? _counted;     // a node's host (ForNode): what its start checks
    private string? _name;              // ForNode and the constructor are the only writers; the node, for the heartbeat before any Worker exists
    private TimeSpan? _lease;           // ForNode and the constructor are the only writers; independent of Workers
    private readonly Dictionary<int, Worker> _workers = [];
    private readonly Func<int, Worker?> _fixed;
    private readonly Worker? _node;
    private readonly Dictionary<int, NodeScope> _scopes = [];
    private readonly SemaphoreSlim _turn = new(1, 1);     // a pass, a gain and a loss of the node's worker never overlap
    private readonly object _attachmentGate = new();
    private RuntimeRun? _runtimeRun;
    private volatile bool _stopping;
    private long _passes, _lastAt, _calls;
    private TimeSpan? _last;
    private Dictionary<int, TimeSpan> _lastAges = [];

    public DurableHost(IEnumerable<Worker> workers)
    {
        _workers = workers.ToDictionary(w => w.Partition);
        _fixed = scope => _workers.GetValueOrDefault(scope);
        if (_workers.Values.FirstOrDefault() is { } first) (_name, _lease) = (first.Node, first.Lease);
    }

    private DurableHost(Worker node) => (_node, _fixed) = (node, _ => null);

    private DurableHost(Func<int, Worker> create) => _fixed = scope =>
    {
        lock (_workers)
        {
            if (_workers.TryGetValue(scope, out var worker)) return worker;
            _workers[scope] = worker = create(scope);
            _beat?.Add(worker);      // a Worker created after the first heartbeat joins its gate and its answers
            return worker;
        }
    };

    /// <summary>
    /// The host of one node (D1): one worker for the node's owned set. On a store without owned sets, a worker per scope,
    /// created when the scope is first activated. With a <paramref name="projection"/>, the node's commits and takes nudge
    /// its rounds, per virtual partition.
    /// </summary>
    /// <param name="partitions">The count the node places, explicit: the runtime's scopes, the store's partitions and
    /// <see cref="WorkerOptions.Partitions"/>, which spread placement hashes over. Options that say another count are refused.</param>
    /// <param name="placesPart">The simulation's worlds only: the node places the first of the store's partitions, fewer
    /// than it has, and the rest are left unserved on purpose. A database-backed node places every one.</param>
    public static DurableHost ForNode(string node, IKernelStore store, IActorType type, int partitions, WorkerOptions? options = null, NodeProjection? projection = null, bool placesPart = false)
    {
        if (partitions <= 0) throw new ArgumentException("A node's host needs the partition count it places.", nameof(partitions));
        if (options is not null && options.Partitions != partitions)
            throw new ArgumentException($"WorkerOptions.Partitions is {options.Partitions} and the node places {partitions}: spread placement must hash over the node's count.", nameof(options));
        options = (options ?? new()) with { Partitions = partitions };
        if (projection is not null)
            options = options with { OnJournal = options.OnJournal is { } earlier ? moved => { earlier(moved); projection.Nudge(moved); } : projection.Nudge };
        var host = store.OwnedSetsAvailable ? new DurableHost(Worker.ForNode(node, store, type, options)) : new DurableHost(scope => new Worker(node, scope, store, type, options));
        host._counted = (store, partitions, placesPart);
        host._name = node;
        host._lease = options.Lease;
        return host;
    }

    /// <summary>
    /// K1 slice 7, a node's host when the runtime starts it: the runtime's scopes, the workers' placement count
    /// (<see cref="WorkerOptions.Partitions"/>, which spread placement hashes over) and the store's partitions must be
    /// equal. Fewer would leave partitions with rows, and perhaps work, unserved; more would address partitions with no
    /// row. A mismatch refuses the start, naming the three counts. Only a node built with <c>placesPart</c> (the
    /// simulation's worlds) may place fewer than the store has. The fixed host is not checked.
    /// </summary>
    public async ValueTask CheckScopesAsync(int scopes, CancellationToken cancellationToken = default)
    {
        if (_stopping)
            throw new InvalidOperationException("This durable host cannot start while its previous shutdown is incomplete.");
        if (_counted is not { } counted) return;
        var stored = await counted.Store.PartitionCountAsync(cancellationToken);
        // A store that does not say its count is refused too, unless the node places part of it (the simulation's).
        if (counted.Placed != scopes || (counted.Part ? stored < scopes : stored != scopes))
            throw new InvalidOperationException($"The partition counts disagree: the runtime places {scopes} scopes, the workers place over {counted.Placed} partitions, and the store has {stored?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "an unknown number of"} partitions. Configure one count, the one the database was created with.");
    }

    /// <summary>Calls to a scope's pass, and the worker passes they ran; the rest were answered by the one-pass rule.</summary>
    public (long Calls, long Passes) PassCounts => (Interlocked.Read(ref _calls), Interlocked.Read(ref _passes));

    /// <summary>The node's one worker, or null for a host of a worker per scope.</summary>
    public Worker? NodeWorker => _node;

    public async ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default)
    {
        if (_stopping) return null;
        if (_node is not { } node)
        {
            if (_fixed(scope) is not { } worker) return null;
            // A Worker that holds its partition already (a pending retake brought it back) is adopted as it is.
            if (!worker.Owns) await worker.TakeAsync(cancellationToken);
            return worker.Owns ? worker : null;
        }
        await _turn.WaitAsync(CancellationToken.None);
        try
        {
            if (_stopping || !await node.GainAsync(scope, cancellationToken)) return null;
            // The same activation each time: layer 1 holds the one it got, and gives up the one it holds.
            return Scope(scope);
        }
        finally { _turn.Release(); }
    }

    // ---- K1b: the node lease (INodeLeaseHost) ---------------------------------------------------------------------

    private IReadOnlyList<Worker> Workers
    {
        get
        {
            if (_node is { } node) return [node];
            lock (_workers) return [.. _workers.Values];
        }
    }

    IScopeHostRun IScopeHost.AttachRuntime()
    {
        lock (_attachmentGate)
        {
            if (_stopping)
                throw new InvalidOperationException("This durable host cannot start while its previous shutdown is incomplete.");
            if (_runtimeRun is not null)
                throw new InvalidOperationException("This durable host is already attached to a runtime.");
            return _runtimeRun = new(this);
        }
    }

    private sealed class RuntimeRun(DurableHost host) : IScopeHostRun
    {
        public async ValueTask PrepareToStopAsync(CancellationToken cancellationToken)
        {
            lock (host._attachmentGate)
            {
                if (host._runtimeRun != this)
                    throw new InvalidOperationException("Only the attached runtime can prepare this durable host for shutdown.");
                host._stopping = true;
            }
            await Task.WhenAll(host.Workers.Select(worker => worker.DrainJobsAsync())).WaitAsync(cancellationToken);
        }

        public void Stopped()
        {
            lock (host._attachmentGate)
            {
                if (host._runtimeRun != this || !host._stopping)
                    throw new InvalidOperationException("Only the attached runtime can complete this durable host's shutdown.");
                foreach (var worker in host.Workers) worker.ReopenJobEntry();
                host._stopping = false;
                host._runtimeRun = null;
            }
        }
    }

    private IKernelStore? Store => _counted?.Store ?? Workers.FirstOrDefault()?.Store;

    /// <summary>The node lease when the host has a store.</summary>
    public TimeSpan? NodeLease => Store is null ? null : _lease;

    /// <summary>
    /// The node's heartbeat over every Worker of this host, its pass gate deciding (<see cref="NodeHeartbeat"/>). It runs
    /// off the host's turn: the answer renews each Worker's guard at once, and its stand-down waits for the next pass.
    /// </summary>
    public async ValueTask<Cluster?> HeartbeatAsync(TimeSpan window, CancellationToken cancellationToken = default)
    {
        if (NodeLease is not { } lease || Store is not { } store || _name is not { } name) return null;
        NodeHeartbeat beat;
        lock (_workers) beat = _beat ??= new NodeHeartbeat(name, store, Workers, lease, window);
        return (await beat.BeatAsync(cancellationToken))?.Cluster;
    }

    /// <summary>The heartbeat, once the first one was sent: its counts of withheld and missed renewals.</summary>
    public NodeHeartbeat? Heartbeat => _beat;

    /// <summary>
    /// The passes no active scope gives: of each Worker that holds nothing while a partition is pending or a heartbeat answer
    /// waits for it. The node's Worker passes under the turn; a fixed host's Workers have no turn, and the runtime's loop
    /// that calls this calls their scopes' passes too, one after another. Returns the partitions the Workers brought back
    /// by themselves since the last call (an answer's pending retake), in this pass or in a scope's.
    /// </summary>
    public async ValueTask<IReadOnlyCollection<int>> PassAsync(CancellationToken cancellationToken = default)
    {
        foreach (var worker in Workers)
            if (!worker.Owns && (worker.Pending.Count > 0 || worker.AnswerWaiting))
                await (worker == _node ? RunNodeAsync(cancellationToken) : worker.RunAsync(cancellationToken));
        return [.. Workers.SelectMany(w => w.TakeBroughtBack()).Distinct()];
    }

    /// <summary>
    /// K1b: one pass of the node's worker under the host's turn, whether or not a scope is active. A Worker whose partitions
    /// are all pending still passes, so that the host's gate reopens and the last answer's retake runs. Null for a host of a
    /// worker per scope.
    /// </summary>
    public async ValueTask<TimeSpan?> RunNodeAsync(CancellationToken cancellationToken = default)
    {
        if (_node is not { } node) return null;
        await _turn.WaitAsync(CancellationToken.None);
        try { return await node.RunAsync(cancellationToken); }
        finally { _turn.Release(); }
    }

    private async ValueTask<ScopePass> PassAsync(NodeScope scope, CancellationToken cancellationToken)
    {
        var node = _node!;
        await _turn.WaitAsync(CancellationToken.None);
        try
        {
            if (!node.Holds(scope.Scope)) return default;
            _calls++;
            // Another scope's pass ran since this scope's last call, and no wake or job result came since: that pass
            // served this scope too. Its answer stands until its delay runs out. Then this call runs the pass,
            // regardless of which scope passed last or whether that scope is still held.
            // A pending heartbeat answer wants a pass (PassWanted) immediately.
            if (scope.Seen < _passes && !node.PassWanted && _last is { } last)
            {
                var left = last - TimeSpan.FromMilliseconds(node.Monotonic - _lastAt);
                if (last == TimeSpan.Zero || left > TimeSpan.Zero)
                {
                    scope.Seen = _passes;
                    var cachedAge = _lastAges.TryGetValue(scope.Scope, out var sampled)
                        ? sampled + TimeSpan.FromMilliseconds(Math.Max(0, node.Monotonic - _lastAt))
                        : (TimeSpan?)null;
                    return new(last == TimeSpan.Zero ? last : left, cachedAge);
                }
            }
            scope.Seen = ++_passes;
            _last = await node.RunAsync(cancellationToken);
            _lastAt = node.Monotonic;
            _lastAges = _last == TimeSpan.Zero && !cancellationToken.IsCancellationRequested ? node.AgeOfReadyWork() : [];
            return node.Holds(scope.Scope)
                ? new(_last, _lastAges.TryGetValue(scope.Scope, out var age) ? age : null)
                : default;
        }
        finally { _turn.Release(); }
    }

    internal Task DrainEnteredTurnsAsync(CancellationToken cancellationToken) =>
        _node?.DrainEnteredTurnsAsync(cancellationToken) ?? Task.CompletedTask;

    private NodeScope Scope(int scope) =>
        _scopes.TryGetValue(scope, out var known) ? known : _scopes[scope] = new NodeScope(this, scope);

    bool IScopeSetGain.SupportsSetGain => _node is not null;

    async ValueTask<IReadOnlyList<IScopeActivation>> IScopeSetGain.ActivateAsync(IReadOnlyList<int> scopes, CancellationToken cancellationToken)
    {
        await _turn.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_stopping) return [];
            await _node!.GainSetAsync(scopes, cancellationToken);
            return scopes.Where(_node.Holds).Select(Scope).ToArray();
        }
        finally { _turn.Release(); }
    }

    bool IScopeSetRelease.SupportsSetRelease => _node is not null;

    ValueTask IScopeSetRelease.ReleaseAsync(IReadOnlyList<IScopeActivation> activations, CancellationToken cancellationToken)
    {
        if (activations.Any(activation => activation is not NodeScope scope || scope.Host != this))
            throw new ArgumentException("Release requires activations of this host.", nameof(activations));
        return LoseAsync(activations.Select(activation => activation.Scope).ToArray(), cancellationToken);
    }

    private async ValueTask LoseAsync(IReadOnlyCollection<int> scopes, CancellationToken cancellationToken = default)
    {
        await _turn.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _node!.LoseAsync(scopes, cancellationToken);
        }
        finally { _turn.Release(); }
    }

    /// <summary>One scope of the node's set: active while the node's worker holds its partition.</summary>
    private sealed class NodeScope(DurableHost host, int scope) : IScopeActivation
    {
        public long Seen = -1;
        public DurableHost Host => host;
        public int Scope => scope;
        public bool Active => host._node!.Holds(scope);
        public ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default) => host._node!.WokenAsync(actor, cancellationToken);
        public async ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default) =>
            (await host.PassAsync(this, cancellationToken)).Delay;
        public ValueTask<ScopePass> RunWithAgeAsync(CancellationToken cancellationToken = default) => host.PassAsync(this, cancellationToken);
        public ValueTask DisposeAsync() => host.LoseAsync([scope]);
    }
}
