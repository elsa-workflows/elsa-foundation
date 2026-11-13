namespace Kernel.Runtime.Conformance;

/// <summary>
/// What the suite sees of a runtime through the seam: every activation it asked for, every pass, every scope it gave
/// up. It reports what breaks the seam's contract (two passes of one activation at once, a pass after the activation
/// was given up, giving up a scope after taking it again) and counts the double activations a runtime allowed.
/// </summary>
public sealed class Watcher
{
    private readonly object _lock = new();
    private readonly List<Watched> _all = [];
    private readonly HashSet<string> _dead = [];
    private readonly HashSet<(Watched, Watched)> _overlaps = [];
    private readonly Dictionary<IScopeActivation, int> _passing = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _violations = [];
    private readonly List<string> _fenced = [];

    /// <summary>Activations asked for while another node held the scope: a second activation at once.</summary>
    public int Second { get; private set; }

    /// <summary>Activations layer 2 declined: the fence was held by another, or the store refused the take.</summary>
    public int Declined { get; private set; }

    public int Activations { get { lock (_lock) return _all.Count; } }

    /// <summary>Pairs of activations of one scope on two live nodes that both believed they held it at once: a stale owner and its successor.</summary>
    public int Overlaps { get { lock (_lock) return _overlaps.Count; } }

    public IReadOnlyList<string> Violations { get { lock (_lock) return [.. _violations]; } }

    public IScopeHost Wrap(string node, IScopeHost inner) => new Host(this, node, inner);

    /// <summary>The node crashed: its activations are no one's now, whatever their memory says.</summary>
    public void Dead(string node)
    {
        lock (_lock) _dead.Add(node);
    }

    /// <summary>For every scope, the live nodes whose activation believes it holds the scope now.</summary>
    public IReadOnlyList<string>[] Holders(int partitions)
    {
        lock (_lock)
            return Enumerable.Range(0, partitions).Select(scope => (IReadOnlyList<string>)Live(scope).Select(w => w.Node).Distinct().ToList()).ToArray();
    }

    /// <summary>Activations of the scope so far, on any node.</summary>
    public int ActivationsOf(int scope)
    {
        lock (_lock) return _all.Count(w => w.Scope == scope);
    }

    /// <summary>Passes that started holding the scope and ended without it: the fence (a refused commit or beat) told a stale owner.</summary>
    public int Fenced { get { lock (_lock) return _fenced.Count; } }

    public int FencedOn(string node)
    {
        lock (_lock) return _fenced.Count(n => n == node);
    }

    private IEnumerable<Watched> Live(int scope) =>
        _all.Where(w => w.Scope == scope && !w.Disposed && !_dead.Contains(w.Node) && w.Inner.Active).GroupBy(w => w.Inner, ReferenceEqualityComparer.Instance).Select(g => g.Last());

    private void Violation(string what)
    {
        lock (_lock) _violations.Add(what);
    }

    private void Overlap(int scope)
    {
        lock (_lock)
        {
            var live = Live(scope).ToList();
            foreach (var a in live)
                foreach (var b in live)
                    if (a.Node != b.Node && string.CompareOrdinal(a.Node, b.Node) < 0) _overlaps.Add((a, b));
        }
    }

    private sealed class Host(Watcher watcher, string node, IScopeHost inner) : IScopeHost
    {
        public async ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default)
        {
            lock (watcher._lock)
                if (watcher.Live(scope).Any(w => w.Node != node)) watcher.Second++;
            var activation = await inner.ActivateAsync(scope, cancellationToken);
            if (activation is null)
            {
                lock (watcher._lock) watcher.Declined++;
                return null;
            }
            var watched = new Watched(watcher, node, scope, activation);
            lock (watcher._lock) watcher._all.Add(watched);
            watcher.Overlap(scope);
            return watched;
        }
    }

    private sealed class Watched(Watcher watcher, string node, int scope, IScopeActivation inner) : IScopeActivation
    {
        public string Node => node;
        public IScopeActivation Inner => inner;
        public bool Disposed;
        public int Passes;

        public int Scope => inner.Scope;
        public bool Active => !Disposed && inner.Active;

        public ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default) => inner.WokenAsync(actor, cancellationToken);

        public async ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default)
        {
            if (Disposed) watcher.Violation($"SEAM {node} ran a pass of scope {scope} after giving it up");
            lock (watcher._lock)
            {
                var passing = watcher._passing.GetValueOrDefault(inner);
                if (passing > 0) watcher._violations.Add($"SEAM {node} ran two passes of scope {scope} at once");
                watcher._passing[inner] = passing + 1;
                Passes++;
            }
            var held = inner.Active;
            try { return await inner.RunAsync(cancellationToken); }
            finally
            {
                lock (watcher._lock)
                {
                    watcher._passing[inner]--;
                    if (held && !inner.Active && !Disposed) watcher._fenced.Add(node);
                }
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
                if (watcher._all.LastOrDefault(w => w.Node == node && w.Scope == scope) is { } latest && latest != this && latest.Active)
                    watcher._violations.Add($"SEAM {node} gave up an old activation of scope {scope} after activating it again");
            }
            await inner.DisposeAsync();
        }
    }
}
