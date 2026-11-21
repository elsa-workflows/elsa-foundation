using Kernel.Runtime.Hosting;

namespace Kernel.Runtime.Conformance;

/// <summary>
/// CodeRabbit 4637850137: a pass that returns <see cref="TimeSpan.Zero"/> synchronously must not hold the node's
/// exclusive scheduler so the balance loop never runs. This host's <see cref="IScopeActivation.RunAsync"/> completes
/// with zero delay and no await.
/// </summary>
public static class LoopYieldCheck
{
    private const int Rounds = 3;

    public static async Task<int> RunAsync() => await PassYieldsAsync() | await BalanceYieldsAsync();

    private static async Task<int> PassYieldsAsync()
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
        int passes = 0, balances = 0, after = 0;
        bool ok;
        try
        {
            // Wait for the scopes to be taken and the Zero-delay passes to start spinning, then require balance
            // rounds that land while they spin. Without the yield the spinning loop holds the exclusive scheduler,
            // so no further round lands. Deadlines, not a fixed sleep: only a missed deadline fails.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (host.Passes == 0 && DateTime.UtcNow < deadline) await Task.Delay(5);
            passes = host.Passes;
            balances = Volatile.Read(ref membership.Heartbeats);
            deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (passes > 0 && Volatile.Read(ref membership.Heartbeats) - balances < Rounds && DateTime.UtcNow < deadline) await Task.Delay(5);
            after = Volatile.Read(ref membership.Heartbeats) - balances;
            passes = host.Passes;
            ok = passes > 0 && after >= Rounds;
        }
        finally
        {
            await node.KillAsync();
        }
        Console.WriteLine($"{(ok ? "ok" : "FAIL")} a Zero-delay pass yields the exclusive scheduler: passes {passes}, balances while spinning {after} (need {Rounds})");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// CodeRabbit 4644811407, the mirror: a balancing round that uses up its interval leaves a zero delay, and a completed
    /// <c>Task.Delay(0)</c> would never release the exclusive scheduler, so the pass loop would never resume. Here every
    /// round's membership read takes 3 ms synchronously against a 1 ms interval.
    /// </summary>
    private static async Task<int> BalanceYieldsAsync()
    {
        const int n = 4;
        var host = new ZeroHost(n, TimeSpan.FromMilliseconds(20));
        var node = new RuntimeNode("balance-yield", new CountingMembership(n, spin: TimeSpan.FromMilliseconds(3)), n, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(1),
            PollInterval = TimeSpan.FromSeconds(1),
            Window = TimeSpan.FromSeconds(3),
        });
        await node.StartAsync(host);
        int passes;
        bool ok;
        try
        {
            // Deadline, not a fixed sleep: only a missed deadline fails. Without the yield no pass ever runs.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (host.Passes < Rounds * n && DateTime.UtcNow < deadline) await Task.Delay(5);
            passes = host.Passes;
            ok = passes >= Rounds * n;
        }
        finally
        {
            await node.KillAsync();
        }
        Console.WriteLine($"{(ok ? "ok" : "FAIL")} a balancing round that uses up its interval yields the exclusive scheduler: passes {passes} (need {Rounds * n})");
        return ok ? 0 : 1;
    }

    private sealed class ZeroHost(int partitions, TimeSpan? delay = null) : IScopeHost
    {
        private readonly ZeroActivation[] _a = Enumerable.Range(0, partitions).Select(s => new ZeroActivation(s, delay ?? TimeSpan.Zero)).ToArray();
        public int Passes => _a.Sum(a => a.Passes);
        public ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default) => new(_a[scope]);
    }

    private sealed class ZeroActivation(int scope, TimeSpan delay) : IScopeActivation
    {
        public int Scope { get; } = scope;
        public bool Active => true;
        public int Passes;
        public ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Passes);
            return new(delay);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingMembership(int partitions, TimeSpan spin = default) : IMembership
    {
        public int Heartbeats;
        public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Heartbeats);
            if (spin > TimeSpan.Zero) Thread.Sleep(spin);       // a synchronous round longer than the interval
            return new(new Cluster(1, Enumerable.Range(0, partitions).ToList(), 0));
        }
    }
}
