using Kernel.Runtime.Hosting;

namespace Kernel.Runtime.Conformance;

/// <summary>
/// What an actor runtime (layer 1) plugs into the runtime conformance suite. The suite builds layer 2 itself (the
/// workers, their store and their hosts) and reaches the runtime only through the seam: the runtime activates scopes
/// through the <see cref="IScopeHost"/> it is given for each node, and runs their passes.
/// </summary>
public interface IRuntimeProvider
{
    string Name { get; }

    /// <summary>A cluster with no nodes yet, over <paramref name="environment"/>. Disposing it stops every node.</summary>
    Task<IRuntimeCluster> CreateAsync(RuntimeEnvironment environment);
}

/// <summary>What a node gets from layer 2: the host it activates scopes through, and the membership that the built-in runtime counts nodes with. A runtime with a membership of its own ignores the second.</summary>
public sealed record NodeServices(IScopeHost Host, IMembership Membership);

/// <param name="Partitions">The scopes: every one of them must be held by some node while one is alive.</param>
/// <param name="Timing">Layer 2's lease, and how often a held scope must read its inbox at the latest.</param>
/// <param name="Node">Layer 2 for a node, by the node's name. The runtime calls it once per node start, with a name it never used before (finding F9).</param>
public sealed record RuntimeEnvironment(int Partitions, RuntimeTiming Timing, Func<string, NodeServices> Node);

/// <param name="Lease">Layer 2's lease. A runtime should try a declined activation again well within it.</param>
/// <param name="PollInterval">Every held scope is woken at least this often (<see cref="IScopeActivation.WokenAsync"/> with null).</param>
public sealed record RuntimeTiming(TimeSpan Lease, TimeSpan PollInterval);

public interface IRuntimeCluster : IAsyncDisposable
{
    /// <summary>
    /// A new node: a new process with a new name. Nodes on different <paramref name="side"/>s do not see each other, as
    /// in a network split: each side's runtime may activate the same scope at once, and only layer 2's fence keeps one
    /// writer. Every provider must be able to start nodes on two sides.
    /// </summary>
    Task<IRuntimeNode> StartNodeAsync(int side = 0);
}

public interface IRuntimeNode
{
    string Name { get; }
    int Side { get; }

    /// <summary>A hint, through this node's runtime, that an actor may have work.</summary>
    ValueTask WakeAsync(InstanceRef actor);

    /// <summary>A graceful stop: the node gives its scopes up.</summary>
    Task StopAsync();

    /// <summary>A crash: the node's passes stop where they are and nothing is given up.</summary>
    Task KillAsync();
}

/// <summary>The reference: the built-in runtime, one <see cref="RuntimeNode"/> per node, on one wake signal per side.</summary>
public sealed class BuiltInProvider : IRuntimeProvider
{
    public string Name => "built-in";

    public Task<IRuntimeCluster> CreateAsync(RuntimeEnvironment environment) => Task.FromResult<IRuntimeCluster>(new Cluster(environment));

    private sealed class Cluster(RuntimeEnvironment environment) : IRuntimeCluster
    {
        private readonly Dictionary<int, InProcessWakeSignal> _signals = [];
        private readonly List<Node> _nodes = [];
        private int _started;

        private bool _disposed;

        public async Task<IRuntimeNode> StartNodeAsync(int side = 0)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var name = $"builtin.{side}.{Interlocked.Increment(ref _started)}";
            var services = environment.Node(name);
            InProcessWakeSignal signal;
            lock (_signals) signal = _signals.TryGetValue(side, out var known) ? known : _signals[side] = new InProcessWakeSignal();
            var lease = environment.Timing.Lease;
            var runtime = new RuntimeNode(name, services.Membership, environment.Partitions, new LoopOptions
            {
                BalanceEvery = lease / 4, Window = lease / 2, PollInterval = environment.Timing.PollInterval,     // the window spans two rounds
            }, signal);
            await runtime.StartAsync(services.Host);
            var node = new Node(runtime, side);
            lock (_nodes) _nodes.Add(node);
            return node;
        }

        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            List<Node> nodes;
            lock (_nodes) nodes = [.. _nodes];
            foreach (var node in nodes) await node.StopAsync();
        }
    }

    private sealed class Node(RuntimeNode runtime, int side) : IRuntimeNode
    {
        public string Name => runtime.Node;
        public int Side => side;
        public ValueTask WakeAsync(InstanceRef actor) => runtime.WakeAsync(actor);
        public Task StopAsync() => runtime.StopAsync().AsTask();
        public Task KillAsync() => runtime.KillAsync().AsTask();
    }
}
