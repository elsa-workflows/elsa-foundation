namespace Kernel.Runtime.Conformance;

/// <summary>
/// What the suite sees of a runtime through the seam: every activation it asked for, every pass, every scope it gave
/// up. It reports what breaks the seam's contract: two calls into one node's scope at once (an activation, a pass, a
/// release; a worker is a single-threaded state machine), a pass after the activation was given up or after its node
/// stopped or was killed, a stop that did not give its scopes up, and giving up an old activation after taking the
/// scope again. It counts the double activations a runtime allowed.
/// </summary>
public sealed class Watcher
{
    private readonly object _lock = new();
    private readonly List<Watched> _all = [];
    private readonly Dictionary<string, string> _gone = [];             // node: "was killed" or "stopped"
    private readonly HashSet<(Watched A, Watched B)> _overlaps = [];
    private readonly Dictionary<(string, int), string> _busy = [];      // what a node is doing to a scope now
    private readonly Dictionary<(string, int), int> _generations = [];  // a node's successful activations of a scope so far
    private readonly List<string> _violations = [];
    private readonly List<string> _fenced = [];
    private readonly HashSet<string> _killing = [];

    /// <summary>Activations asked for while another node held the scope: a second activation at once.</summary>
    public int Second { get; private set; }

    /// <summary>Activations layer 2 declined: the fence was held by another, or the store refused the take.</summary>
    public int Declined { get; private set; }

    public int Activations { get { lock (_lock) return _all.Count; } }

    /// <summary>Pairs of activations of one scope on two live nodes that both believed they held it at once: a stale owner and its successor.</summary>
    public int Overlaps { get { lock (_lock) return _overlaps.Count; } }

    /// <summary>The pairs held at once of <paramref name="scope"/> that include <paramref name="node"/>.</summary>
    public int OverlapsOn(int scope, string node)
    {
        lock (_lock) return _overlaps.Count(o => o.A.Scope == scope && (o.A.Node == node || o.B.Node == node));
    }

    public IReadOnlyList<string> Violations { get { lock (_lock) return [.. _violations]; } }

    public IScopeHost Wrap(string node, IScopeHost inner) => new Host(this, node, inner);

    /// <summary>The node is being killed: a crash gives nothing up, so it may not give a scope up from now on.</summary>
    public void Killing(string node)
    {
        lock (_lock) _killing.Add(node);
    }

    /// <summary>The node crashed (its kill has returned): its activations are no one's now, whatever their memory says, and none of them may start a pass.</summary>
    public void Dead(string node)
    {
        lock (_lock) _gone[node] = "was killed";
    }

    /// <summary>The node stopped (its stop has returned): it must have given every scope up, and none of its activations may start a pass.</summary>
    public void Stopped(string node)
    {
        lock (_lock)
        {
            foreach (var w in _all.Where(w => w.Node == node && !w.Disposed && w.Inner.Active).GroupBy(w => w.Scope).Select(g => g.Last()))
                _violations.Add($"SEAM {node} stopped without giving up scope {w.Scope}");
            _gone[node] = "stopped";
        }
    }

    /// <summary>For every scope, the live nodes whose activation believes it holds the scope now.</summary>
    public IReadOnlyList<string>[] Holders(int partitions)
    {
        lock (_lock)
            return Enumerable.Range(0, partitions).Select(scope => (IReadOnlyList<string>)Live(scope).Select(w => w.Node).Distinct().ToList()).ToArray();
    }

    /// <summary>Passes that started holding the scope and ended without it: the fence (a refused commit or beat) told a stale owner, or a commit's outcome was unknown.</summary>
    public int Fenced { get { lock (_lock) return _fenced.Count; } }

    public int FencedOn(string node)
    {
        lock (_lock) return _fenced.Count(n => n == node);
    }

    private IEnumerable<Watched> Live(int scope) =>
        _all.Where(w => w.Scope == scope && !w.Disposed && !_gone.ContainsKey(w.Node) && w.Inner.Active).GroupBy(w => w.Inner, ReferenceEqualityComparer.Instance).Select(g => g.Last());

    private void Overlap(int scope)
    {
        lock (_lock)
        {
            var live = Live(scope).ToList();
            foreach (var a in live)
                foreach (var b in live)
                    if (string.CompareOrdinal(a.Node, b.Node) < 0) _overlaps.Add((a, b));
        }
    }

    /// <summary>A call into a node's scope begins: no other may be under way (a worker is single-threaded).</summary>
    private void Enter(string node, int scope, string what)
    {
        lock (_lock)
        {
            if (_busy.TryGetValue((node, scope), out var other)) _violations.Add($"SEAM {node} {what} scope {scope} while it {other} it");
            else _busy[(node, scope)] = what;
            if (_gone.TryGetValue(node, out var gone) && what != "gave up") _violations.Add($"SEAM {node} {what} scope {scope} after it {gone}");
        }
    }

    private void Leave(string node, int scope, string what)
    {
        lock (_lock)
            if (_busy.TryGetValue((node, scope), out var now) && now == what) _busy.Remove((node, scope));
    }

    private sealed class Host(Watcher watcher, string node, IScopeHost inner) : IScopeHost
    {
        public async ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default)
        {
            lock (watcher._lock)
                if (watcher.Live(scope).Any(w => w.Node != node)) watcher.Second++;
            watcher.Enter(node, scope, "activated");
            IScopeActivation? activation;
            try { activation = await inner.ActivateAsync(scope, cancellationToken); }
            finally { watcher.Leave(node, scope, "activated"); }
            if (activation is null)
            {
                lock (watcher._lock) watcher.Declined++;
                return null;
            }
            Watched watched;
            lock (watcher._lock)
            {
                var generation = watcher._generations[(node, scope)] = watcher._generations.GetValueOrDefault((node, scope)) + 1;
                watched = new Watched(watcher, node, scope, activation, generation);
                watcher._all.Add(watched);
            }
            watcher.Overlap(scope);
            return watched;
        }
    }

    private sealed class Watched(Watcher watcher, string node, int scope, IScopeActivation inner, int generation) : IScopeActivation
    {
        public string Node => node;
        public IScopeActivation Inner => inner;
        public bool Disposed;

        public int Scope => scope;
        public bool Active => !Disposed && inner.Active;

        public ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default) => inner.WokenAsync(actor, cancellationToken);

        public async ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default)
        {
            if (Disposed) lock (watcher._lock) watcher._violations.Add($"SEAM {node} ran a pass of scope {scope} after giving it up");
            watcher.Enter(node, scope, "ran a pass of");
            var held = inner.Active;
            try { return await inner.RunAsync(cancellationToken); }
            finally
            {
                watcher.Leave(node, scope, "ran a pass of");
                lock (watcher._lock)
                    if (held && !inner.Active && !Disposed) watcher._fenced.Add(node);
                watcher.Overlap(scope);
            }
        }

        public async ValueTask DisposeAsync()
        {
            lock (watcher._lock)
            {
                if (Disposed) return;
                Disposed = true;
                // Layer 2 hands a node the same activation again when it takes the scope again; giving up this one then
                // would give up the new epoch.
                if (watcher._generations.GetValueOrDefault((node, scope)) > generation && inner.Active)
                    watcher._violations.Add($"SEAM {node} gave up an old activation of scope {scope} after activating it again");
                if (watcher._killing.Contains(node) && inner.Active)
                    watcher._violations.Add($"SEAM {node} gave scope {scope} up while it was being killed: a crash gives nothing up");
            }
            watcher.Enter(node, scope, "gave up");
            try { await inner.DisposeAsync(); }
            finally { watcher.Leave(node, scope, "gave up"); }
        }
    }
}
