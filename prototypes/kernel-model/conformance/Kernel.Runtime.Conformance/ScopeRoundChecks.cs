using Kernel.Runtime.Hosting;

namespace Kernel.Runtime.Conformance;

public static class ScopeRoundChecks
{
    public static async Task<int> RunAsync()
    {
        var host = new Host();
        await using var node = new RuntimeNode("ready-rounds", new Membership(), 3,
            new LoopOptions { PollInterval = TimeSpan.FromSeconds(2) });
        await node.StartAsync(host);
        int[] trace;
        try
        {
            trace = await host.Trace.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            trace = [];
        }
        finally
        {
            await node.KillAsync();
        }
        var ordered = trace is [0, 1, 2, 1, 2, 0, 1, 2, 0];
        Console.WriteLine($"{(ordered ? "ok" : "FAIL")} the node offers every ready scope one pass per oldest-first round ({string.Join(", ", trace)})");
        return ordered ? 0 : 1;
    }

    private sealed class Host : IScopeHost
    {
        private readonly int[] _trace = new int[9];
        private int _count;
        internal TaskCompletionSource<int[]> Trace { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default) =>
            new(new Activation(this, scope));

        private void Record(int scope)
        {
            if (_count >= _trace.Length) return;
            _trace[_count++] = scope;
            if (_count == _trace.Length) Trace.TrySetResult(_trace);
        }

        private sealed class Activation(Host host, int scope) : IScopeActivation
        {
            public int Scope => scope;
            public bool Active => true;
            public ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default)
            {
                host.Record(scope);
                return new(TimeSpan.Zero);
            }
            public async ValueTask<ScopePass> RunWithAgeAsync(CancellationToken cancellationToken = default) =>
                new(await RunAsync(cancellationToken), TimeSpan.FromSeconds(scope == 1 ? 40 : scope == 2 ? 20 : 10));
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Membership : IMembership
    {
        public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default) =>
            new(new Cluster(1, [0, 1, 2], 0));
    }
}
