using Kernel.Durable;
using Kernel.Runtime.Hosting;
using Kernel.Store.Conformance;

namespace Kernel.Runtime.Conformance;

/// <summary>
/// N2b (Code Review #1 and Architect F1 of #4877, under <see cref="RuntimeNode"/>): a fixed host that <see cref="DurableHost.ForNode"/>
/// makes on a node-lease store without owned sets has no Worker before its first take. The node must still heartbeat,
/// renew, and keep its scopes past one lease. Before the fix it sent no heartbeat and its rows sat at an expired node.
/// </summary>
public static class FixedHostHeartbeatCheck
{
    public static async Task<int> RunAsync()
    {
        const int n = 4;
        var lease = TimeSpan.FromSeconds(2);
        var store = new InMemoryStore(n, InMemoryProvider.Clock);
        var wire = new NodeStore(store) { WithoutOwnedSets = true };
        var host = DurableHost.ForNode("fixed", wire, new WorkflowInstance(Stress.Models()), n, new WorkerOptions { Lease = lease, Partitions = n });
        var node = new RuntimeNode("fixed", wire, n, new LoopOptions { BalanceEvery = lease / 4, Window = lease / 2, PollInterval = TimeSpan.FromMilliseconds(200) });
        await node.StartAsync(host);
        bool held = false, kept = false;
        (long Sent, long Renewed) beats = (0, 0);
        try
        {
            // Deadlines, not a fixed sleep: only a missed deadline fails.
            var deadline = DateTime.UtcNow + lease * 3;
            while (!(held = Enumerable.Range(0, n).All(node.Runtime.Owns)) && DateTime.UtcNow < deadline) await Task.Delay(20);
            await Task.Delay(lease * 2);        // past two leases: only renewals keep the node record
            kept = Enumerable.Range(0, n).All(node.Runtime.Owns);
            for (var p = 0; p < n; p++) kept &= await store.TakeAsync(p, "probe", lease) is null;
            beats = node.Heartbeats;
        }
        finally
        {
            await node.KillAsync();
        }
        var ok = held && kept && beats.Renewed > 0;
        Console.WriteLine($"{(ok ? "ok" : "FAIL")} a fixed host without owned sets heartbeats under RuntimeNode: held {held}, kept past two leases {kept}, renewed {beats.Renewed} of {beats.Sent}");
        return ok ? 0 : 1;
    }
}
