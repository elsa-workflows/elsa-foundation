using System.Collections.Concurrent;
using Kernel.EntityFramework;
using Kernel.Native;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresRootTimedJobSettlement
{
    private const string Tenant = "settlement";
    private const string Definition = "timed-job-settlement";
    private const string Background = "work";
    private const string First = "issue";
    private const string Second = "settle";
    private const string Third = "apply";

    internal static async Task<int> RunAsync(bool inBatch, bool timeoutWins)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a dedicated PostgreSQL fixture database.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var stop = budget.Token;
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootTimedJobSettlement(options);
        await using var db = Open();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "Settlement requires a fresh fixture database.");
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromMinutes(5));
        var firstStore = Store();
        await firstStore.CreateAsync(1);
        await admission.InstallAsync(stop);
        var issuingFormat = new EfStore(() => KernelDb.ForPostgresRootTimedJobIssuance(options), postgres: true);
        try
        {
            await issuingFormat.TakeAsync(0, "old-format", Defaults.Lease, stop);
            throw new InvalidOperationException("The issuance profile opened a settlement database.");
        }
        catch (StoreFormatException) { }

        var api = new PostgresPreviewFoundation(admission, firstStore, new(firstStore, await admission.OpenJournalCursorAsync(stop), 16),
            WorkflowActor.WorkflowActor.ActorType);
        var scope = FixedTenantScope.Bind(Tenant);
        var receipt = await new WorkflowRuntime(api, api).StartAsync(scope, "issue", NativeExecutionModel.Start(Definition), cancellationToken: stop);
        var actor = receipt.Actor ?? throw new InvalidOperationException("The Native workflow was not admitted.");
        Check(receipt.Outcome == Contract.AdmissionOutcome.Accepted, "The fixture must admit a fresh root.");
        var registry = new WorkflowRegistry().Add(Wf.Workflow(Definition, Wf.Node<CatchTimeout>("finish").With(
            new Node(Background, typeof(ControlledJob)) { JobTimeout = TimeSpan.FromSeconds(6) })));
        ControlledJob.Attempts.Clear();
        TurnCommit? winningTurn = null;
        await using var first = await Take(firstStore, First);
        await Drive(first, () => Task.FromResult(ControlledJob.Attempts.Count == 1));
        var initial = (await firstStore.ReadFirstRootAsync(Tenant, actor.Id, stop))!;
        await RefuseLegacyCheckpointReads();
        try
        {
            await issuingFormat.ReadFirstRootAsync(Tenant, actor.Id, stop);
            throw new InvalidOperationException("The issuance profile accepted a populated settlement checkpoint.");
        }
        catch (StoreFormatException) { }
        var physical = await Rows(initial.Actor.Id);
        var timer = physical.Single(row => row.Kind == Kinds.JobDeadline);
        var ticket = physical.Single(row => row.Kind == Kinds.Job);
        var issued = Json.Read<JobTicket>(ticket.Payload!);
        var old = first.Fences.Single();
        var oldCallback = first.JobCompletion(initial.Actor.Id, issued.Key, initial.Actor.Creation)
            ?? throw new InvalidOperationException("The issuing attempt did not enter its callback.");
        var deadline = timer.Due;
        Check(initial is { Revision: 1, NextJobGeneration: 1 } &&
            JobRegistry.Read(initial.Jobs).SequenceEqual([new(issued.Key, 0)]) &&
            physical.Length == 2 && issued.Deadline == deadline && deadline > await Now(),
            "The first owner must leave one counted job and its future deadline.");
        var before = (await firstStore.LoadAsync([initial.Actor.Id], stop)).Single();
        var timeoutResult = RootJobTicket.RecordedResult(before.Id, issued.Key, 0, Json.Write(new JobFinished(issued.Key, null, "timeout")));
        var timeoutTurn = new TurnCommit(before.Id, before with { Revision = 2 }, 1,
            [InboxKey.Issued(issued.Timer!), InboxKey.Ticket(before.Id, issued.Key, 1, 0)], [], [timeoutResult], [], [], null);
        Check(!(await CommitTimeout(firstStore, old)).Landed &&
            (await firstStore.ReadFirstRootAsync(Tenant, actor.Id, stop))?.Revision == 1 &&
            (await Rows(initial.Actor.Id)).Select(row => row.Id).SequenceEqual(physical.Select(row => row.Id)),
            "The locked timeout writer must refuse the current fence while its original deadline is in the future.");

        if (timeoutWins) await AtDeadline();
        await Expire(First);
        var secondStore = Store();
        await using var second = await Take(secondStore, Second);
        var settling = second.Fences.Single();
        Check(settling.Epoch > old.Epoch && settling.Position == old.Position, "A fresh owner must take the same waiting position.");
        if (!timeoutWins)
        {
            await Drive(second, () => Task.FromResult(ControlledJob.Attempts.Count == 2));
            var trial = (await Rows(before.Id)).Single(row => row.Kind == Kinds.Job);
            Check(trial.Id == ticket.Id && trial.Seq == ticket.Seq && trial.Trial == settling.Epoch,
                "The cold callback must run on the exact recovered ticket under a current physical trial.");
            var currentCallback = second.JobCompletion(before.Id, issued.Key, before.Creation)
                ?? throw new InvalidOperationException("The cold attempt did not enter its callback.");
            ControlledJob.Attempts.Last().Exit.SetResult("completed");
            await currentCallback.WaitAsync(stop);
            Check(await Now() < deadline, "The successor must publish its result before the deadline.");
            await AtDeadline();
        }
        Check(!(await CommitTimeout(firstStore, old)).Landed && (await Rows(before.Id)).Length == 2,
            "A due timeout offered by the displaced owner must leave both inputs intact.");
        ControlledJob.Attempts.First().Exit.SetResult("late-old-owner");
        await oldCallback.WaitAsync(stop);
        await Drive(second, async () => (await secondStore.ReadFirstRootAsync(Tenant, actor.Id, stop))?.Revision == 2);
        var settlement = (await secondStore.ReadFirstRootAsync(Tenant, actor.Id, stop))!;
        await RefuseLegacyCheckpointReads();
        var resultRow = (await Rows(before.Id)).Single();
        var selected = Json.Read<JobFinished>(resultRow.Payload!);
        var expected = timeoutWins ? "timeout" : "completed";
        Check(settlement.State == initial.State && settlement.Jobs == initial.Jobs && settlement.NextJobGeneration == 1 &&
            resultRow.Kind == Kinds.JobFinished && resultRow.JobGeneration == 0 && selected.Key == issued.Key &&
            (timeoutWins ? selected is { Error: "timeout", Result: null } :
                selected.Error is null && Json.Read<BackgroundResult>(selected.Result!).Result!.GetValue<string>() == expected),
            "A distinct settlement checkpoint must replace both rows with the single winning result while keeping the Native wait.");
        var atomic = await db.Database.SqlQuery<bool>($"""
            SELECT count(*) = 1 AND bool_and(i.xmin = m.xmin) AS "Value" FROM "Instances" i
            JOIN "Messages" m ON m."Target" = i."Id" AND m."Partition" = i."Partition"
            WHERE i."Id" = {before.Id} AND i."Revision" = 2 AND m."Kind" = {Kinds.JobFinished}
            """).SingleAsync(stop);
        var duplicate = await Commit(secondStore, second.Fences.Single(),
            winningTurn ?? throw new InvalidOperationException("The Worker did not prepare the recorded result."));
        Check(atomic && !duplicate.Landed &&
            (await Rows(before.Id)).Single().Id == resultRow.Id,
            "The recorded result shares the settlement commit and a duplicate settlement leaves it in place.");

        await Expire(Second);
        var thirdStore = Store();
        await using var third = await Take(thirdStore, Third);
        Check(third.Fences.Single().Epoch > settling.Epoch, "Result application must start with a cold take.");
        await Drive(third, async () => (await thirdStore.ReadFirstRootAsync(Tenant, actor.Id, stop))?.Status == InstanceStatus.Finished);
        await first.RunAsync(stop);
        await second.RunAsync(stop);
        await third.RunAsync(stop);
        var end = (await thirdStore.ReadFirstRootAsync(Tenant, actor.Id, stop))!;
        await RefuseLegacyCheckpointReads();
        var history = await thirdStore.ReadJournalOfAsync(0, before.Id, 0, stop);
        var entries = history.SelectMany(entry => Json.Read<string[]>(entry.Entries)).ToArray();
        Check(end is { Revision: 3, Status: InstanceStatus.Finished, NextJobGeneration: 1, Jobs: null } &&
            Json.Read<NativeState>(end.State!).Results["finish"]?.GetValue<string>() == expected && (await Rows(before.Id)).Length == 0 &&
            history.Select(entry => entry.Revision).SequenceEqual([1L, 3L]) && entries.Count(entry => entry == "end") == 1 &&
            entries.Count(entry => entry.EndsWith(":" + Background, StringComparison.Ordinal) &&
                entry.StartsWith(timeoutWins ? "f:" : "c:", StringComparison.Ordinal)) == 1 &&
            ControlledJob.Attempts.Count == (timeoutWins ? 1 : 2) && ControlledJob.Attempts.All(entry => entry.Actor == actor.Id),
            "The fresh Native model must apply the same actor's stored result once, with one end and an empty inbox.");
        Console.WriteLine($"ok Native timed job {(inBatch ? "in-batch" : "checked")} {expected}, epochs {old.Epoch}->{settling.Epoch}->{third.Fences.Single().Epoch}, revisions 1->2->3");
        return 0;

        async Task RefuseLegacyCheckpointReads()
        {
            (int Protocol, Func<KernelDb> Open)[] olderProfiles =
            [
                (2, () => new KernelDb(options)),
                (6, () => KernelDb.ForPostgresRootJobs(options))
            ];
            foreach (var (protocol, open) in olderProfiles)
            {
                var oldStore = new EfStore(open, postgres: true);
                try
                {
                    await oldStore.ReadFirstRootAsync(Tenant, actor.Id, stop);
                    throw new InvalidOperationException($"The protocol-{protocol} point reader accepted a populated settlement checkpoint.");
                }
                catch (StoreException error) when (error.Failure == Failure.Definite && error.Message == "Unsupported qualified actor checkpoint.") { }
            }
        }

        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = CommitSql.Owned };
        async Task<Worker> Take(EfStore store, string name)
        {
            await store.EnableRootJobsAsync(stop);
            var roots = await store.BindWorkerRootsAsync([(Tenant, actor.Id)], stop);
            var worker = new Worker(name, roots.Partition, store, PostgresNativeRootOwner.Adapter(store, roots, registry),
                new WorkerOptions
                {
                    PassTurnBudget = 1,
                    OnPreparedCheckpoint = (turn, _, _) =>
                    {
                        if (name == Second && turn.Sent.Any(message => message.FoundationJobResult)) winningTurn = turn;
                    }
                });
            Check(await new DurableHost([worker]).ActivateAsync(roots.Partition, stop) == worker, "The fixture must take ownership.");
            return worker;
        }
        Task<MessageEntity[]> Rows(string id) => db.Messages.AsNoTracking().Where(row => row.Target == id && row.Partition == 0)
            .OrderBy(row => row.Seq).ToArrayAsync(stop);
        Task<int> Expire(string name) => db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Nodes" SET "LeaseUntil" = 0 WHERE "Id" = {name} """, stop);
        Task<long> Now() => db.Database.SqlQueryRaw<long>("""SELECT floor(extract(epoch from clock_timestamp()) * 1000)::bigint AS "Value" """).SingleAsync(stop);
        ValueTask<Committed> CommitTimeout(EfStore store, PartitionFence fence) => Commit(store, fence, timeoutTurn);
        ValueTask<Committed> Commit(EfStore store, PartitionFence fence, TurnCommit turn) =>
            store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [turn], [], []), stop);
        async Task AtDeadline()
        {
            while (await Now() < deadline) await Task.Delay(10, stop);
        }
        async Task Drive(Worker worker, Func<Task<bool>> reached)
        {
            while (!await reached())
            {
                await worker.WokenAsync(null, stop);
                await worker.RunAsync(stop);
                Check(worker.Owns, "The worker must keep its fence during the step.");
                await Task.Delay(10, stop);
            }
        }
    }

    private static void Check(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException(failure);
    }

    [Background]
    public sealed class ControlledJob : Activity<string>
    {
        internal static readonly ConcurrentQueue<(string Actor, TaskCompletionSource<string> Exit)> Attempts = new();
        protected override async ValueTask<Transition> ExecuteAsync(ActivityContext context)
        {
            var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Attempts.Enqueue((context.Instance.Id, gate));
            return Complete(await gate.Task.WaitAsync(context.CancellationToken));
        }
    }

    public sealed class CatchTimeout : CompositeActivity
    {
        protected override void Execute(CompositeContext context) => context.Schedule(context.Children.Single());
        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.Complete(result: child.Result);
        protected override void OnChildFaulted(CompositeContext context, ChildFaulted child) => context.Complete(result: Values.From(child.Fault.Message));
    }
}
