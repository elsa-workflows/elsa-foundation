using Kernel.Runtime.Hosting;

namespace Kernel.Runtime.Conformance;

/// <summary>
/// CodeRabbit 4637850137: a pass that returns <see cref="TimeSpan.Zero"/> synchronously must not hold the node's
/// exclusive scheduler so the balance loop never runs. This host's <see cref="IScopeActivation.RunAsync"/> completes
/// with zero delay and no await.
/// </summary>
public static class LoopYieldCheck
{
    public static async Task<int> RunAsync()
    {
        const int n = 4;
        var membership = new CountingMembership(n);
        var host = new ZeroHost(n);
        var node = new RuntimeNode("yield", membership, n, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(10),
            PollInterval = TimeSpan.FromSeconds(1),
            Window = TimeSpan.FromSeconds(3),
        });
        await node.StartAsync(host);
        await Task.Delay(80);
        var (passes, balances) = (host.Passes, membership.Heartbeats);
        await node.KillAsync();
        var ok = passes > 0 && balances > 1;
        Console.WriteLine($"{(ok ? "ok" : "FAIL")} a Zero-delay pass yields the exclusive scheduler: passes {passes}, balances {balances}");
        return ok ? 0 : 1;
    }

    private sealed class ZeroHost(int partitions) : IScopeHost
    {
        private readonly ZeroActivation[] _a = Enumerable.Range(0, partitions).Select(s => new ZeroActivation(s)).ToArray();
        public int Passes => _a.Sum(a => a.Passes);
        public ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default) => new(_a[scope]);
    }

    private sealed class ZeroActivation(int scope) : IScopeActivation
    {
        public int Scope { get; } = scope;
        public bool Active => true;
        public int Passes;
        public ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Passes);
            return new(TimeSpan.Zero);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingMembership(int partitions) : IMembership
    {
        public int Heartbeats;
        public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Heartbeats);
            return new(new Cluster(1, Enumerable.Range(0, partitions).ToList(), 0));
        }
    }
}
