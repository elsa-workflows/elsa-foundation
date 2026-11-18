using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// Layer 2's side of the seam for one process. Activating a scope takes its partition's fence, or declines when the
/// store refuses. It does not trust the runtime: if a runtime activates a scope on two nodes at once, the fence lets one
/// of them write. Two shapes:
/// <list type="bullet">
/// <item>Per node (<see cref="ForNode"/>, K1 D1): one worker serves every scope the node activates, as one owned set.
/// Activating a scope adds its partition to the set (a take of that one fence row); giving it up removes it (a release of
/// that one row). The set may be scattered. The worker polls the set in one query and flushes its partitions in one
/// batch. The node's passes, activations and give-ups run one at a time, so the set changes only between passes, and
/// one pass serves every scope: a scope that another scope's pass served since its own last call gets that answer.</item>
/// <item>Fixed (the constructor): one worker per scope, given up front and activated by its lowest partition. The models
/// and the simulation use it, and so does a node on a store without owned sets (<c>commit=ef</c>: one partition per worker).</item>
/// </list>
/// </summary>
public sealed class DurableHost : IScopeHost
{
    private readonly Dictionary<int, Worker> _workers = [];
    private readonly Func<int, Worker?> _fixed;
    private readonly Worker? _node;
    private readonly Dictionary<int, NodeScope> _scopes = [];
    private readonly SemaphoreSlim _turn = new(1, 1);     // a pass, a gain and a loss of the node's worker never overlap
    private long _passes;
    private TimeSpan? _last;

    public DurableHost(IEnumerable<Worker> workers)
    {
        _workers = workers.ToDictionary(w => w.Partition);
        _fixed = scope => _workers.GetValueOrDefault(scope);
    }

    private DurableHost(Worker node) => (_node, _fixed) = (node, _ => null);

    private DurableHost(Func<int, Worker> create) => _fixed = scope =>
    {
        lock (_workers) return _workers.TryGetValue(scope, out var worker) ? worker : _workers[scope] = create(scope);
    };

    /// <summary>
    /// The host of one node (D1): one worker for the node's owned set. On a store without owned sets, a worker per scope,
    /// created when the scope is first activated.
    /// </summary>
    public static DurableHost ForNode(string node, IKernelStore store, IActorType type, WorkerOptions? options = null) =>
        store.OwnedSetsAvailable ? new(Worker.ForNode(node, store, type, options)) : new(scope => new Worker(node, scope, store, type, options));

    /// <summary>The node's one worker, or null for a host of a worker per scope.</summary>
    public Worker? NodeWorker => _node;

    public async ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default)
    {
        if (_node is not { } node)
        {
            if (_fixed(scope) is not { } worker) return null;
            await worker.TakeAsync(cancellationToken);
            return worker.Owns ? worker : null;
        }
        await _turn.WaitAsync(CancellationToken.None);
        try
        {
            if (!await node.GainAsync(scope, cancellationToken)) return null;
            // The same activation each time: layer 1 holds the one it got, and gives up the one it holds.
            return _scopes.TryGetValue(scope, out var known) ? known : _scopes[scope] = new NodeScope(this, scope);
        }
        finally { _turn.Release(); }
    }

    private async ValueTask<TimeSpan?> PassAsync(NodeScope scope, CancellationToken cancellationToken)
    {
        var node = _node!;
        await _turn.WaitAsync(CancellationToken.None);
        try
        {
            if (!node.Holds(scope.Scope)) return null;
            // Another scope's pass ran since this scope's last call, and no wake or job result came since: that pass
            // served this scope too, and its answer stands. The scope whose call ran it keeps the true due time.
            if (scope.Seen < _passes && !node.PassWanted && _last is { } last)
            {
                scope.Seen = _passes;
                return last;
            }
            scope.Seen = ++_passes;
            _last = await node.RunAsync(cancellationToken);
            return node.Holds(scope.Scope) ? _last : null;
        }
        finally { _turn.Release(); }
    }

    private async ValueTask LoseAsync(int scope)
    {
        await _turn.WaitAsync(CancellationToken.None);
        try { await _node!.LoseAsync([scope]); }
        finally { _turn.Release(); }
    }

    /// <summary>One scope of the node's set: active while the node's worker holds its partition.</summary>
    private sealed class NodeScope(DurableHost host, int scope) : IScopeActivation
    {
        public long Seen = -1;
        public int Scope => scope;
        public bool Active => host._node!.Holds(scope);
        public ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default) => host._node!.WokenAsync(actor, cancellationToken);
        public ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default) => host.PassAsync(this, cancellationToken);
        public ValueTask DisposeAsync() => host.LoseAsync(scope);
    }
}
