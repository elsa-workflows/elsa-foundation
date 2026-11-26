using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Kernel.EntityFramework;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

internal static class PostgresRuntimeCluster
{
    internal const int DefaultPartitions = 8;
    private const string Definition = "two-stage-cluster-workflow";
    private sealed record OwnershipRow(int Id, string? Owner, long Epoch, bool Died, string Transaction);

    internal static async Task<int> RunAsync(int partitions, bool wake)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        var factory = Database();
        await using (var db = factory())
        {
            if (!await PostgresFixtureDatabase.IsEmptyAsync(db, token))
            {
                Console.Error.WriteLine("Choose an empty PostgreSQL database dedicated to this fixture.");
                return 2;
            }
        }

        var ingress = new EfStore(factory, postgres: true);
        await ingress.CreateAsync(partitions);
        var a = new Member(factory, partitions, wake);
        await using var first = a.Node;
        await a.StartAsync(token);
        var alone = await WaitForOwners(rows => rows.All(p => p.Owner == first.Node));
        Console.WriteLine($"first PID {Environment.ProcessId} owns {alone.Length} partitions");

        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true };
        start.ArgumentList.Add(typeof(PostgresRuntimeCluster).Assembly.Location);
        start.ArgumentList.Add("pg-runtime-cluster-member");
        start.ArgumentList.Add(partitions.ToString(CultureInfo.InvariantCulture));
        if (wake) start.ArgumentList.Add("wake");
        using var second = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the second runtime.");
        try
        {
            var b = NodeName(second.Id);
            var joined = await WaitForOwners(rows => rows.Count(p => p.Owner == first.Node) == partitions / 2 &&
                rows.Count(p => p.Owner == b) == partitions / 2 && !a.Releases.IsEmpty);
            foreach (var p in joined)
                Check(!p.Died && (p.Owner == first.Node ? p.Epoch == alone[p.Id].Epoch : p.Epoch == alone[p.Id].Epoch + 1),
                    "the join moves only the surplus partitions through orderly give-up");
            var target = joined.First(p => p.Owner == b);
            var moved = joined.Where(p => p.Owner == b).ToArray();
            Check(a.Releases.ToArray() is [var released] && released.SequenceEqual(moved.Select(p => p.Id)),
                "the first runtime gave up all and only the moved partitions in one set call");
            Check(moved.Select(p => p.Transaction).Distinct(StringComparer.Ordinal).Count() == 1,
                "one PostgreSQL transaction took the successor's entire set");
            Console.WriteLine($"second PID {second.Id} joined: {partitions / 2} partitions each, one set give-up and one take transaction; target {target.Id} at epoch {target.Epoch}");
            if (wake)
            {
                await using var db = factory();
                while (await db.Database.SqlQuery<int>($"""
                    SELECT COUNT(*)::int AS "Value" FROM pg_stat_activity
                    WHERE datname = current_database() AND application_name = 'elsa-kernel-wake-listener'
                      AND state = 'idle' AND query = 'LISTEN elsa_kernel_wake'
                    """).SingleAsync(token) != 2)
                    await Task.Delay(50, token);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    SELECT pg_notify('elsa_kernel_wake', 'invalid'), pg_notify('elsa_kernel_wake', '-1'),
                           pg_notify('elsa_kernel_wake', {partitions.ToString(CultureInfo.InvariantCulture)})
                    """, token);
            }

            var actor = InstanceRef.In(target.Id, "cluster-approval");
            await ingress.AddAsync(new Message("cluster-start", actor, Kinds.Start, Start: NativeModel.Spec(Definition)), token);
            if (wake) await first.WakeAsync(actor, token);
            await WaitForState(1, InstanceStatus.Running);
            await Publish("first", "approval-1", "Ada");
            if (wake) await first.WakeAsync(actor, token);
            var atSecondWait = await WaitForState(2, InstanceStatus.Running);
            Check(atSecondWait.Variables["first"]!.GetValue<string>() == "Ada", "first approval survived the second wait");
            var beforeLeave = await ingress.ReadJournalOfAsync(target.Id, actor.Id, 0, token);
            Check(beforeLeave.Select(x => x.Revision).SequenceEqual([1L, 2L]), "journal records the first two revisions");
            Console.WriteLine("revision 2 on the second runtime: Ada recorded, waiting for Grace");

            await second.StandardInput.WriteLineAsync("leave");
            second.StandardInput.Close();
            await second.WaitForExitAsync(token);
            Check(second.ExitCode == 0, "second runtime exited after its graceful stop");
            var reunited = await WaitForOwners(rows => rows.All(p => p.Owner == first.Node));
            foreach (var p in reunited)
                Check(!p.Died && (joined[p.Id].Owner == first.Node ? p.Epoch == joined[p.Id].Epoch : p.Epoch == joined[p.Id].Epoch + 1),
                    "departure returns each moved partition through an orderly give-up");
            Console.WriteLine($"second runtime left: first owns all {partitions}; target advanced to epoch {reunited[target.Id].Epoch}");

            await Publish("second", "approval-2", "Grace");
            var finished = await WaitForState(3, InstanceStatus.Finished);
            if (wake) Console.WriteLine("Grace finished by polling with no second-approval hint");
            var journal = await ingress.ReadJournalOfAsync(target.Id, actor.Id, 0, token);
            Check(finished.Variables["first"]!.GetValue<string>() == "Ada" && finished.Variables["second"]!.GetValue<string>() == "Grace",
                "the final Native checkpoint contains both approvals");
            Check(journal.Take(2).SequenceEqual(beforeLeave) && journal.Select(x => x.Revision).SequenceEqual([1L, 2L, 3L]) &&
                journal.Select(x => x.Position).SequenceEqual([1L, 2L, 3L]), "one gapless journal continues across node departure");
            Check(a.Turns.ToArray() is [var home] && home == actor.Id, "the survivor ran the final model turn once");
            await first.StopAsync(token);
            Check(first.Errors == 0 && first.Heartbeats.Renewed > 0, "first runtime renewed its lease and reported zero loop errors");
            var stopped = await WaitForOwners(rows => rows.All(p => p.Owner is null));
            Console.WriteLine($"revision 3: Ada and Grace; journal positions 1,2,3; all {stopped.Length} partitions released");
            return 0;

            async Task Publish(string stage, string id, string approver) =>
                await Events.PublishAsync(ingress, new EventRow("event:" + stage, id, Json.Write(approver), long.MaxValue), partitions, token);

            async Task<NativeState> WaitForState(long revision, InstanceStatus status)
            {
                while (true)
                {
                    var row = (await ingress.LoadAsync([actor.Id], token)).SingleOrDefault();
                    if (row?.Revision == revision && row.Status == status) return Json.Read<NativeState>(row.State);
                    if (row is { Status: InstanceStatus.Faulted } || row?.Revision > revision)
                        throw new InvalidOperationException($"Unexpected workflow status {row.Status} at revision {row.Revision}: {row.Fault}");
                    await Task.Delay(75, token);
                }
            }
        }
        finally
        {
            if (!second.HasExited)
            {
                second.StandardInput.Close();
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await second.WaitForExitAsync(limit.Token); }
                catch (OperationCanceledException) { second.Kill(); await second.WaitForExitAsync(); }
            }
        }

        async Task<OwnershipRow[]> WaitForOwners(Func<OwnershipRow[], bool> complete)
        {
            await using var db = factory();
            while (true)
            {
                var rows = await db.Database.SqlQueryRaw<OwnershipRow>("""
                    SELECT "Id", "Owner", "Epoch", "Died", xmin::text AS "Transaction" FROM "Partitions"
                    """).OrderBy(p => p.Id).ToArrayAsync(token);
                if (rows.Length == partitions && complete(rows)) return rows;
                await Task.Delay(75, token);
            }
        }
    }

    internal static async Task<int> MemberAsync(int partitions, bool wake)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var member = new Member(Database(), partitions, wake);
        await using var node = member.Node;
        await member.StartAsync(limit.Token);
        if (await Console.In.ReadLineAsync(limit.Token) != "leave") return 1;
        if (wake)
        {
            var wait = Stopwatch.StartNew();
            while ((member.Hints.Count < 2 || member.Forwarded.Count < 2) && wait.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(25, limit.Token);
        }
        await node.StopAsync(limit.Token);
        var turns = member.Turns.ToArray();
        Check(node.Errors == 0 && node.Heartbeats.Renewed > 0, "departing runtime renewed its lease and reported zero loop errors");
        Check(turns is [var start, var approval] && start == approval, "the departing runtime ran both earlier turns on the same actor");
        if (wake)
        {
            var partition = new InstanceRef(turns[0]).Partition;
            Check(member.Hints.ToArray().SequenceEqual([partition, partition]), "the remote listener received two hints for its partition");
            Console.WriteLine($"second runtime received two PostgreSQL wake hints for partition {partition}");
            Check(member.Forwarded.ToArray().SequenceEqual([(partition, partition), (partition, partition)]),
                "the active scope forwarded both matching hints to its durable worker");
            Console.WriteLine($"both hints reached the durable worker through activation {partition}");
        }
        Console.WriteLine($"second runtime stopped gracefully after two model turns for {turns[0]}");
        return 0;
    }

    private static string Connection() => Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
        throw new InvalidOperationException("Set KERNEL_PREVIEW_POSTGRES to a dedicated empty database.");

    private static Func<KernelDb> Database()
    {
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(Connection()).Options;
        return () => new KernelDb(options);
    }

    private static string NodeName(int pid) => $"cluster-fixture-{pid}";

    private sealed class Member
    {
        private readonly IScopeHost _host;
        internal RuntimeNode Node { get; }
        internal ConcurrentQueue<string> Turns { get; } = new();
        internal ConcurrentQueue<int> Hints { get; } = new();
        internal ConcurrentQueue<(int Scope, int Partition)> Forwarded { get; } = new();
        internal ConcurrentQueue<int[]> Releases { get; } = new();

        internal Member(Func<KernelDb> factory, int partitions, bool wake)
        {
            var store = new EfStore(factory, postgres: true);
            var workflow = Wf.Workflow(Definition, Wf.Sequence("root", Wf.Event("hear1", "first"), Wf.Set("record1", "first", Binding.Result("hear1", "Payload")),
                Wf.Event("hear2", "second"), Wf.Set("record2", "second", Binding.Result("hear2", "Payload"))));
            var actors = new WorkflowInstance([new NativeModel(new WorkflowRegistry().Add(workflow))]);
            var id = NodeName(Environment.ProcessId);
            var host = DurableHost.ForNode(id, store, actors, partitions, new WorkerOptions
                { Partitions = partitions, OnTurn = (actor, _, _) => Turns.Enqueue(actor.Id) });
            _host = new ObservedHost(host, Forwarded, Releases);
            var signal = wake ? new PostgresWakeSignal(Connection(), partitions) : null;
            signal?.Listen(actor => Hints.Enqueue(actor.Partition));
            Node = new RuntimeNode(id, store, partitions, new LoopOptions
                { BalanceEvery = TimeSpan.FromMilliseconds(125), HeartbeatEvery = TimeSpan.FromMilliseconds(125),
                  PollInterval = wake ? TimeSpan.FromSeconds(3) : TimeSpan.FromMilliseconds(100), Window = TimeSpan.FromSeconds(1) },
                wake: signal, beside: signal is null ? null : signal.RunAsync);
        }

        internal ValueTask StartAsync(CancellationToken token) => Node.StartAsync(_host, token);
    }

    // Observe the activation that the runtime actually chooses. Periodic polls pass null.
    private sealed class ObservedHost(DurableHost host, ConcurrentQueue<(int, int)> forwarded, ConcurrentQueue<int[]> releases)
        : IScopeHost, IScopeCount, INodeLeaseHost, IScopeSetGain, IScopeSetRelease
    {
        public TimeSpan? NodeLease => host.NodeLease;
        public ValueTask CheckScopesAsync(int count, CancellationToken token) => host.CheckScopesAsync(count, token);
        public IScopeHostRun? AttachRuntime() => ((IScopeHost)host).AttachRuntime();
        public ValueTask<Cluster?> HeartbeatAsync(TimeSpan window, CancellationToken token) => host.HeartbeatAsync(window, token);
        public ValueTask<IReadOnlyCollection<int>> PassAsync(CancellationToken token) => host.PassAsync(token);
        public bool SupportsSetGain => ((IScopeSetGain)host).SupportsSetGain;
        public bool SupportsSetRelease => ((IScopeSetRelease)host).SupportsSetRelease;

        public async ValueTask<IReadOnlyList<IScopeActivation>> ActivateAsync(IReadOnlyList<int> scopes, CancellationToken token)
        {
            var active = await ((IScopeSetGain)host).ActivateAsync(scopes, token);
            return active.Select(scope => new ObservedActivation(scope, forwarded)).ToArray();
        }

        public async ValueTask<IReadOnlyList<int>> ReleaseAsync(IReadOnlyList<IScopeActivation> scopes, CancellationToken token)
        {
            var active = scopes.Cast<ObservedActivation>().Select(scope => scope.Inner).ToArray();
            var freed = await ((IScopeSetRelease)host).ReleaseAsync(active, token);
            releases.Enqueue(active.Select(scope => scope.Scope).Order().ToArray());
            return freed;
        }

        public async ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken token)
        {
            var active = await host.ActivateAsync(scope, token);
            return active is null ? null : new ObservedActivation(active, forwarded);
        }
    }

    private sealed class ObservedActivation(IScopeActivation inner, ConcurrentQueue<(int, int)> forwarded) : IScopeActivation
    {
        internal IScopeActivation Inner => inner;
        public int Scope => inner.Scope;
        public bool Active => inner.Active;
        public ValueTask<TimeSpan?> RunAsync(CancellationToken token) => inner.RunAsync(token);
        public ValueTask<ScopePass> RunWithAgeAsync(CancellationToken token) => inner.RunWithAgeAsync(token);
        public ValueTask DisposeAsync() => inner.DisposeAsync();

        public async ValueTask WokenAsync(InstanceRef? actor, CancellationToken token)
        {
            await inner.WokenAsync(actor, token);
            if (actor is { } target) forwarded.Enqueue((Scope, target.Partition));
        }
    }

    private static void Check(bool result, string expectation)
    {
        if (!result) throw new InvalidOperationException(expectation);
    }
}
