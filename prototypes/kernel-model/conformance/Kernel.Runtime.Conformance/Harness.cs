using System.Collections.Concurrent;

namespace Kernel.Runtime.Conformance;

/// <param name="FaultRate">The share of store calls whose connection breaks, as in the store suite's stress test.</param>
/// <param name="BrokenFence">MUTATION: the store's commit ignores epoch and position. The fence's cases and the stress test must fail with it.</param>
public sealed record HarnessSetup(int Partitions, RuntimeTiming Timing, double FaultRate = 0, bool BrokenFence = false);

/// <summary>
/// One cluster of one runtime over one fresh store, with layer 2 built by the suite: every node gets a worker for every
/// partition, on its own <see cref="NodeStore"/> over a store that records every commit that landed, behind a
/// <see cref="Watcher"/>. The suite reads the end state through the store, as the store suite does.
/// </summary>
public sealed class Harness : IAsyncDisposable
{
    private readonly ProvidedStore _provided;
    private readonly ConcurrentDictionary<string, bool> _names = new();
    private readonly List<IRuntimeNode> _nodes = [];
    private readonly HashSet<string> _gone = [];
    private IRuntimeCluster _cluster = null!;
    private long _turns;

    private Harness(ProvidedStore provided, HarnessSetup setup)
    {
        _provided = provided;
        Setup = setup;
        Faulty = new FaultyStore(provided.Store, setup.FaultRate, 5) { Calm = setup.FaultRate == 0 };
        Recorded = new RecordingStore(Faulty);
    }

    public static async Task<Harness> CreateAsync(IRuntimeProvider runtime, IStoreProvider store, HarnessSetup setup)
    {
        var provided = await store.CreateAsync(setup.Partitions, new StoreSetup(BrokenFence: setup.BrokenFence));
        var harness = new Harness(provided, setup);
        try { harness._cluster = await runtime.CreateAsync(new RuntimeEnvironment(setup.Partitions, setup.Timing, harness.Node)); }
        catch
        {
            await provided.DisposeAsync();
            throw;
        }
        return harness;
    }

    public HarnessSetup Setup { get; }
    public int Partitions => Setup.Partitions;
    public TimeSpan Lease => Setup.Timing.Lease;
    public Watcher Watcher { get; } = new();
    public FaultyStore Faulty { get; }
    public RecordingStore Recorded { get; }
    public ProvidedStore Provided => _provided;
    public long Turns => Interlocked.Read(ref _turns);

    /// <summary>The store as the outside world and the checks see it: not stalled, not faulty, not recorded.</summary>
    public IKernelStore Outside => _provided.NewClient();

    /// <summary>Names the runtime used twice: a restarted process must have a new name (finding F9).</summary>
    public List<string> Reused { get; } = [];

    /// <summary>What the harness itself saw go wrong: a node whose name is not the one it asked layer 2 for.</summary>
    public List<string> Misnamed { get; } = [];

    /// <summary>Every violation the harness can see from outside the store: the watcher's, invariant 1 on the recorded commits, the store's ghosts, names.</summary>
    public List<string> Violations() =>
        [.. Watcher.Violations, .. Recorded.CheckOneWriter(), .. Provided.Ghosts.Select(g => "GHOST " + g),
         .. Reused.Select(n => $"F9 the runtime reused the node name {n}"), .. Misnamed];

    public IReadOnlyList<IRuntimeNode> Alive
    {
        get { lock (_nodes) return _nodes.Where(n => !_gone.Contains(n.Name)).ToList(); }
    }

    private NodeServices Node(string name)
    {
        var store = Recorded;
        if (!_names.TryAdd(name, true)) lock (Reused) Reused.Add(name);
        var options = new WorkerOptions
        {
            Lease = Lease, Horizon = TimeSpan.FromSeconds(2), PollLimit = 40, MaxBatch = 40, MaxAttempts = 1_000_000, Partitions = Partitions,
            OnTurn = (_, _, _) => Interlocked.Increment(ref _turns),
            JobScheduler = TaskScheduler.Default,      // a job's body off the runtime's scheduler, whatever the runtime
        };
        // No trial gate shared by the node's workers: a runtime may run the passes of a node's scopes at the same time,
        // and the gate is not safe for that (FINDINGS, "The runtime conformance suite and the Orleans adapter", R6).
        var workers = Enumerable.Range(0, Partitions).Select(p => new Worker(name, p, store, new WorkflowInstance(Stress.Models()), options));
        return new NodeServices(Watcher.Wrap(name, new DurableHost(workers)), store);
    }

    public async Task<IRuntimeNode> StartAsync(int side = 0)
    {
        var before = _names.Keys.ToHashSet();
        var node = await _cluster.StartNodeAsync(side);
        var asked = _names.Keys.Where(n => !before.Contains(n)).ToList();
        if (asked is not [var name] || name != node.Name)
            lock (Misnamed) Misnamed.Add($"NAME the node started as {node.Name} asked layer 2 for [{string.Join(", ", asked)}]");
        lock (_nodes) _nodes.Add(node);
        return node;
    }

    public async Task StopAsync(IRuntimeNode node)
    {
        lock (_nodes) if (!_gone.Add(node.Name)) return;
        await node.StopAsync();
        Watcher.Stopped(node.Name);
    }

    public async Task KillAsync(IRuntimeNode node)
    {
        lock (_nodes) if (!_gone.Add(node.Name)) return;
        await node.KillAsync();
        Watcher.Dead(node.Name);
    }

    /// <summary>Add a message as the outside world does, retrying until the insert answers, and send a hint through a live node.</summary>
    public async Task AddAsync(Message message, bool wake = true)
    {
        var keep = (await Outside.PollAsync(0, 0, TimeSpan.Zero, 1)).Now + 3_600_000;
        for (var attempt = 0; ; attempt++)
            try
            {
                await Outside.AddAsync(message with { KeepUntil = keep });
                break;
            }
            catch (Exception) when (attempt < 100) { await Task.Delay(10); }
        if (wake && Alive.FirstOrDefault() is { } node) await node.WakeAsync(message.Target);
    }

    public async Task<InstanceRow?> RowAsync(string id) => (await Outside.LoadAsync([id])).FirstOrDefault();

    /// <summary>Wait until <paramref name="condition"/> holds; false when <paramref name="within"/> runs out first.</summary>
    public static async Task<bool> WaitAsync(Func<Task<bool>> condition, TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;
        while (true)
        {
            if (await condition()) return true;
            if (DateTime.UtcNow > deadline) return false;
            await Task.Delay(20);
        }
    }

    public Task<bool> WaitAsync(Func<bool> condition, TimeSpan within) => WaitAsync(() => Task.FromResult(condition()), within);

    /// <summary>Every scope is held by exactly one live node.</summary>
    public bool Settled() => Watcher.Holders(Partitions).All(h => h.Count == 1);

    /// <summary>Stop every live node, then the cluster (a node already killed stays as it is). The store stays, for the checks.</summary>
    public async Task StopAllAsync()
    {
        if (_stopped) return;
        _stopped = true;
        foreach (var node in Alive) await StopAsync(node);
        Watcher.TearingDown();
        await _cluster.DisposeAsync();
    }

    private bool _stopped;

    public async ValueTask DisposeAsync()
    {
        try { await StopAllAsync(); }
        finally { await _provided.DisposeAsync(); }
    }
}
