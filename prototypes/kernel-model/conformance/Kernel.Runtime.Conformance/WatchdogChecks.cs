using System.Reflection;
using Kernel.Durable;
using Kernel.Runtime.Hosting;

namespace Kernel.Runtime.Conformance;

/// <summary>K1b slice 5b, round 1 of #4922: the watchdog on every owner-path call (L1), and its loop beside a node (L2, A1).</summary>
public static class WatchdogChecks
{
    private static readonly TimeSpan Lease = TimeSpan.FromMilliseconds(300);

    public static async Task<int> RunAsync() => Report(await OwnerPathAsync()) | Report(await BesideAsync());
    private static int Report((bool Ok, string What) r) { Console.WriteLine($"{(r.Ok ? "ok" : "FAIL")} {r.What}"); return r.Ok ? 0 : 1; }
    // L1: each owner-path member, stuck in the store, is closed at twice the lease, and the call ends on it.
    private static readonly (string What, Func<IKernelStore, Task> Call)[] OwnerPath =
    [
        ("commit", s => s.CommitAsync(null!).AsTask()), ("take", s => s.TakeAsync(0, "n", Lease).AsTask()),
        ("set take", s => s.TakeAsync([0], "n", Lease).AsTask()), ("retake", s => s.RetakeAsync(0, "n", 1, Lease).AsTask()),
        ("counting retake", s => s.RetakeAsync(0, "n", 1, Lease, true).AsTask()), ("poll", s => s.PollAsync(0, 1, Lease, 1).AsTask()),
        ("set poll", s => s.PollAsync([], Lease, 1).AsTask()), ("load", s => s.LoadAsync([]).AsTask()),
        ("actor load", s => s.LoadActorsAsync([]).AsTask()), ("node heartbeat", s => s.NodeHeartbeatAsync("n", Lease, Lease).AsTask()),
        ("membership heartbeat", s => s.HeartbeatAsync("n", Lease).AsTask()),
    ];

    private static async Task<(bool, string)> OwnerPathAsync()
    {
        List<string> unwatched = [];
        foreach (var (what, call) in OwnerPath)
        {
            long now = 0;
            var watchdog = new Watchdog(Lease, _ => { }, () => now);
            var running = call(watchdog.Watch(DispatchProxy.Create<IKernelStore, HangingStore>()));
            now = (long)Lease.TotalMilliseconds * 2;
            var closed = watchdog.Check();
            if (closed != 1 || await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(2))) != running) unwatched.Add(what);
        }
        return (unwatched.Count == 0, $"every owner-path call of the watched store is closed at twice the lease: unwatched [{string.Join(", ", unwatched)}]");
    }

    // L2 and A1: the loop runs off the node's scheduler, guards a graceful stop whose last pass is stuck in a commit (it
    // closes it, and the stop ends), and ends after that stop; it ends on a kill; and an error of its own is recorded.
    private static async Task<(bool, string)> BesideAsync()
    {
        var ended = new List<string>();
        var watchdog = new Watchdog(Lease, ended.Add);
        var host = new StuckHost(watchdog.Watch(DispatchProxy.Create<IKernelStore, HangingStore>()));
        bool offNode = false, besideEnded = false, killEnded = false;
        var node = Node(host, async t => { offNode = TaskScheduler.Current == TaskScheduler.Default; await watchdog.RunAsync(t); besideEnded = true; });
        await node.StartAsync(host);
        await Until(() => host.Stuck);
        var stop = node.StopAsync().AsTask();
        var stopped = await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5))) == stop;     // a deadline: the old code hangs here
        if (!stopped) await node.KillAsync();
        var killed = Node(null, async t => { try { await Task.Delay(Timeout.Infinite, t); } finally { killEnded = true; } });
        await killed.StartAsync(new StuckHost(null));
        await killed.KillAsync();
        var failing = Node(null, _ => throw new InvalidOperationException("the beside loop failed"));
        await failing.StartAsync(new StuckHost(null));
        await Until(() => failing.Errors > 0);
        var recorded = failing.LastError is InvalidOperationException;
        await failing.KillAsync();
        return (stopped && watchdog.Closed == 1 && ended.Count == 0 && offNode && besideEnded && killEnded && recorded,
            $"the beside loop: off the node's scheduler {offNode}, a stuck commit closed during a graceful stop {stopped} (closed {watchdog.Closed}, ended {ended.Count}), ends after the stop {besideEnded}, on a kill {killEnded}, records its error {recorded}");
    }
    private static RuntimeNode Node(StuckHost? host, Func<CancellationToken, Task> beside) =>
        new(host is null ? "beside-idle" : "beside", new All(), 1, new LoopOptions { BalanceEvery = TimeSpan.FromMilliseconds(10), PollInterval = TimeSpan.FromSeconds(1), Window = TimeSpan.FromSeconds(3) }, beside: beside);
    private static async Task Until(Func<bool> done) { for (var end = DateTime.UtcNow.AddSeconds(5); !done() && DateTime.UtcNow < end;) await Task.Delay(5); }
    /// <summary>A store whose every call hangs until its token is cancelled, as a call on a stuck connection that aborts on a close.</summary>
    public class HangingStore : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var hang = Task.Delay(Timeout.Infinite, args?.OfType<CancellationToken>().FirstOrDefault() ?? default);
            if (method!.ReturnType == typeof(ValueTask)) return new ValueTask(hang);
            return typeof(HangingStore).GetMethod(nameof(Hang), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(method.ReturnType.GetGenericArguments()[0]).Invoke(null, [hang]);
        }
        private static async ValueTask<T> Hang<T>(Task hang) { await hang; return default!; }
    }
    private sealed class All : IMembership { public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken ct = default) => new(new Cluster(1, [0], 0)); }
    /// <summary>One scope, whose first pass commits through the store; with no store, its passes do nothing.</summary>
    private sealed class StuckHost(IKernelStore? store) : IScopeHost, IScopeActivation
    {
        public volatile bool Stuck;
        public int Scope => 0; public bool Active => true;
        public ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default) => new(this);
        public ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public async ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default)
        {
            if (store is null || Stuck) return TimeSpan.FromSeconds(1);
            Stuck = true;
            try { await store.CommitAsync(null!, cancellationToken); }
            catch (OperationCanceledException) { }      // closed by the watchdog: a failed call, as to a Worker
            return TimeSpan.FromSeconds(1);
        }
    }
}
