using System.Collections.Concurrent;
using System.Diagnostics;
using Kernel.EntityFramework;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

internal static class PostgresRuntimeCluster
{
    private const int Partitions = 8;
    private const string Definition = "two-stage-cluster-workflow";

    internal static async Task<int> RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        var factory = Database();
        await using (var db = factory())
        {
            if (await db.Database.SqlQuery<int>($"""
                SELECT COUNT(*)::int AS "Value" FROM information_schema.tables WHERE table_schema = current_schema()
                """).SingleAsync(token) != 0)
                throw new InvalidOperationException("Choose an empty PostgreSQL database dedicated to this fixture.");
        }

        var ingress = new EfStore(factory, postgres: true);
        await ingress.CreateAsync(Partitions);
        var a = new Member(factory);
        await using var first = a.Node;
        await a.StartAsync(token);
        var alone = await WaitForOwners(rows => rows.All(p => p.Owner == first.Node));
        Console.WriteLine($"first PID {Environment.ProcessId} owns {alone.Length} partitions");

        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true };
        start.ArgumentList.Add(typeof(PostgresRuntimeCluster).Assembly.Location);
        start.ArgumentList.Add("pg-runtime-cluster-member");
        using var second = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the second runtime.");
        try
        {
            var b = NodeName(second.Id);
            var joined = await WaitForOwners(rows => rows.Count(p => p.Owner == first.Node) == Partitions / 2 &&
                rows.Count(p => p.Owner == b) == Partitions / 2);
            foreach (var p in joined)
                Check(!p.Died && (p.Owner == first.Node ? p.Epoch == alone[p.Id].Epoch : p.Epoch > alone[p.Id].Epoch),
                    "the join moves only the surplus partitions through orderly give-up");
            var target = joined.First(p => p.Owner == b);
            Console.WriteLine($"second PID {second.Id} joined: {Partitions / 2} partitions each; target partition {target.Id} moved to its epoch {target.Epoch}");

            var actor = InstanceRef.In(target.Id, "cluster-approval");
            await ingress.AddAsync(new Message("cluster-start", actor, Kinds.Start, Start: NativeModel.Spec(Definition)), token);
            await WaitForState(1, InstanceStatus.Running);
            await Publish("first", "approval-1", "Ada");
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
                Check(!p.Died && (joined[p.Id].Owner == first.Node ? p.Epoch == joined[p.Id].Epoch : p.Epoch > joined[p.Id].Epoch),
                    "departure returns each moved partition through an orderly give-up");
            Console.WriteLine($"second runtime left: first owns all {Partitions}; target advanced to epoch {reunited[target.Id].Epoch}");

            await Publish("second", "approval-2", "Grace");
            var finished = await WaitForState(3, InstanceStatus.Finished);
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
                await Events.PublishAsync(ingress, new EventRow("event:" + stage, id, Json.Write(approver), long.MaxValue), Partitions, token);

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

        async Task<PartitionEntity[]> WaitForOwners(Func<PartitionEntity[], bool> complete)
        {
            await using var db = factory();
            while (true)
            {
                var rows = await db.Partitions.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync(token);
                if (rows.Length == Partitions && complete(rows)) return rows;
                await Task.Delay(75, token);
            }
        }
    }

    internal static async Task<int> MemberAsync()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var member = new Member(Database());
        await using var node = member.Node;
        await member.StartAsync(limit.Token);
        if (await Console.In.ReadLineAsync(limit.Token) != "leave") return 1;
        await node.StopAsync(limit.Token);
        var turns = member.Turns.ToArray();
        Check(node.Errors == 0 && node.Heartbeats.Renewed > 0, "departing runtime renewed its lease and reported zero loop errors");
        Check(turns is [var start, var approval] && start == approval, "the departing runtime ran both earlier turns on the same actor");
        Console.WriteLine($"second runtime stopped gracefully after two model turns for {turns[0]}");
        return 0;
    }

    private static Func<KernelDb> Database()
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
            throw new InvalidOperationException("Set KERNEL_PREVIEW_POSTGRES to a dedicated empty database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        return () => new KernelDb(options);
    }

    private static string NodeName(int pid) => $"cluster-fixture-{pid}";

    private sealed class Member
    {
        private readonly DurableHost _host;
        internal RuntimeNode Node { get; }
        internal ConcurrentQueue<string> Turns { get; } = new();

        internal Member(Func<KernelDb> factory)
        {
            var store = new EfStore(factory, postgres: true);
            var workflow = Wf.Workflow(Definition, Wf.Sequence("root", Wf.Event("hear1", "first"), Wf.Set("record1", "first", Binding.Result("hear1", "Payload")),
                Wf.Event("hear2", "second"), Wf.Set("record2", "second", Binding.Result("hear2", "Payload"))));
            var actors = new WorkflowInstance([new NativeModel(new WorkflowRegistry().Add(workflow))]);
            var id = NodeName(Environment.ProcessId);
            _host = DurableHost.ForNode(id, store, actors, Partitions, new WorkerOptions
                { Partitions = Partitions, OnTurn = (actor, _, _) => Turns.Enqueue(actor.Id) });
            Node = new RuntimeNode(id, store, Partitions, new LoopOptions
                { BalanceEvery = TimeSpan.FromMilliseconds(125), HeartbeatEvery = TimeSpan.FromMilliseconds(125),
                  PollInterval = TimeSpan.FromMilliseconds(100), Window = TimeSpan.FromSeconds(1) });
        }

        internal ValueTask StartAsync(CancellationToken token) => Node.StartAsync(_host, token);
    }

    private static void Check(bool result, string expectation)
    {
        if (!result) throw new InvalidOperationException(expectation);
    }
}
