using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Native;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    private const string Fourth = "discard-ended-input";
    private const string DelayedInput = "after-timeout";

    internal static async Task<int> RunAsync(bool inBatch, string scenario)
    {
        var unhandled = scenario == "unhandled-timeout";
        var timeoutWins = scenario != "complete";
        var delayedClock = scenario == "late-clock";
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a dedicated PostgreSQL fixture database.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var stop = budget.Token;
        var sampled = new PublicationSamples();
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).AddInterceptors(sampled).Options;
        KernelDb Open() => KernelDb.ForPostgresRootTimedJobSettlement(options);
        await using var db = Open();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "Settlement requires a fresh fixture database.");
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromMinutes(5));
        var delays = new ObservationDelay();
        var delayedOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection)
            .AddInterceptors(sampled, new DelayedPoll(delays), new DelayedCommit(delays)).Options;
        var firstStore = delayedClock ? Store(() => KernelDb.ForPostgresRootTimedJobSettlement(delayedOptions)) : Store();
        await firstStore.CreateAsync(1);
        await admission.InstallAsync(stop);
        var issuingFormat = new EfStore(() => KernelDb.ForPostgresRootTimedJobIssuance(options), postgres: true);
        try
        {
            await issuingFormat.TakeAsync(0, "old-format", Defaults.Lease, stop);
            throw new InvalidOperationException("The issuance profile opened a settlement database.");
        }
        catch (StoreFormatException) { }

        var api = new PostgresPreviewFoundation(admission, new(firstStore, await admission.OpenJournalCursorAsync(stop), 16),
            WorkflowActor.WorkflowActor.ActorType);
        var scope = EmbeddedTenant.Bind(Tenant);
        var receipt = await new WorkflowRuntime(api, api).StartAsync(scope, "issue", NativeExecutionModel.Start(Definition), cancellationToken: stop);
        var actor = receipt.Actor ?? throw new InvalidOperationException("The Native workflow was not admitted.");
        Check(receipt.Outcome == Contract.AdmissionOutcome.Accepted, "The fixture must admit a fresh root.");
        var work = new Node(Background, typeof(ControlledJob)) { JobTimeout = TimeSpan.FromSeconds(6) };
        var registry = new WorkflowRegistry().Add(Wf.Workflow(Definition, unhandled ? work : Wf.Node<CatchTimeout>("finish").With(work)));
        ControlledJob.Attempts.Clear();
        TurnCommit? winningTurn = null;
        TurnCommit? applyingTurn = null;
        var cleanupTurns = 0;
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
        if (delayedClock)
        {
            Check(first.Owns && first.Fences.Single() == old && first.Now < deadline && delays.Polls > 0 && delays.Commits > 0,
                "After delayed PostgreSQL responses, the still-current Worker must estimate a time before the now-due deadline.");
        }
        else await Expire(First);
        var secondStore = delayedClock ? firstStore : Store();
        await using var replacement = delayedClock ? null : await Take(secondStore, Second);
        var second = replacement ?? first;
        var settling = second.Fences.Single();
        if (!delayedClock)
            Check(settling.Epoch > old.Epoch && settling.Position == old.Position, "A fresh owner must take the same waiting position.");
        if (!timeoutWins)
        {
            await Drive(second, () => Task.FromResult(ControlledJob.Attempts.Count == 2));
            var trial = (await Rows(before.Id)).Single(row => row.Kind == Kinds.Job);
            Check(trial.Id == ticket.Id && trial.Seq == ticket.Seq && trial.Trial == settling.Epoch,
                "The cold callback must run on the exact recovered ticket under a current physical trial.");
            var currentCallback = second.JobCompletion(before.Id, issued.Key, before.Creation)
                ?? throw new InvalidOperationException("The cold attempt did not enter its callback.");
            var samplesBefore = sampled.Count;
            ControlledJob.Attempts.Last().Exit.SetResult("completed");
            await currentCallback.WaitAsync(stop);
            Check(await Now() < deadline && sampled.Count == samplesBefore + 1,
                "The successor must sample PostgreSQL once and publish its result before the deadline.");
            await AtDeadline();
        }
        if (!delayedClock)
            Check(!(await CommitTimeout(firstStore, old)).Landed && (await Rows(before.Id)).Length == 2,
                "A due timeout offered by the displaced owner must leave both inputs intact.");
        var lateSamplesBefore = sampled.Count;
        ControlledJob.Attempts.First().Exit.SetResult("late-return");
        await oldCallback.WaitAsync(stop);
        if (delayedClock)
            Check(sampled.Count == lateSamplesBefore + 1 && first.Fences.Single() == old && first.Now < deadline,
                "The late callback must sample PostgreSQL once while the original Worker still holds the fence and estimates an earlier time.");
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

        await Expire(delayedClock ? First : Second);
        var thirdStore = Store();
        await using var third = await Take(thirdStore, Third);
        var applyingFence = third.Fences.Single();
        Check(applyingFence.Epoch > settling.Epoch, "Result application must start with a cold take.");
        // A Faulted end over an applied result commits on a family that ends Faulted roots, whatever the result was, as the
        // in-memory reference ends a model's fault: only a Park over a recorded timeout stays refused here. pg-held-callers
        // commits a Faulted end over a successful result end to end.
        if (unhandled)
        {
            var waiting = (await thirdStore.LoadAsync([before.Id], stop)).Single();
            var recorded = RootJobTicket.RecordedResult(before.Id, selected.Key, resultRow.JobGeneration, resultRow.Payload!);
            var unsupported = new TurnCommit(before.Id, waiting with { Status = InstanceStatus.Faulted, Revision = 3, Jobs = null },
                2, [recorded.InboxKey], [], [], [], [], Json.Write(new[] { "end" }), Unsubscribe: true)
            {
                OpensPark = unhandled
            };
            try
            {
                await Commit(thirdStore, applyingFence, unsupported);
                throw new InvalidOperationException("The fixed-root writer accepted a Park over a recorded timeout.");
            }
            catch (NotSupportedException) { }
            Check((await Rows(before.Id)).Single().Id == resultRow.Id &&
                (await thirdStore.ReadFirstRootAsync(Tenant, actor.Id, stop)) is { Revision: 2, Status: InstanceStatus.Running },
                "Refusing an unsupported fault must preserve the settled result and its waiting checkpoint.");
        }
        MessageEntity? queued = null;
        if (unhandled)
        {
            var due = await Now() + 5_000;
            const string payload = "kept-until-due";
            var accepted = await api.SendAsync(scope, new Contract.Target.Ref(actor), DelayedInput, "probe", payload,
                new(DueAt: due), stop);
            queued = (await Rows(before.Id)).Single(row => row.InboxScope == InboxKey.FoundationOccurrenceScope);
            Check(accepted is { Outcome: Contract.AdmissionOutcome.Accepted } && accepted.Actor == actor &&
                !queued.Done && queued.Due == due && queued.Payload == payload,
                "A delayed input must be admitted while the workflow is still running.");
        }
        var status = unhandled ? InstanceStatus.Faulted : InstanceStatus.Finished;
        await Drive(third, async () => (await thirdStore.ReadFirstRootAsync(Tenant, actor.Id, stop))?.Status == status);
        await first.RunAsync(stop);
        if (replacement is not null) await replacement.RunAsync(stop);
        await third.RunAsync(stop);
        var end = (await thirdStore.ReadFirstRootAsync(Tenant, actor.Id, stop))!;
        await RefuseLegacyCheckpointReads();
        var history = await thirdStore.ReadJournalOfAsync(0, before.Id, 0, stop);
        var entries = history.SelectMany(entry => Json.Read<string[]>(entry.Entries)).ToArray();
        var native = Json.Read<NativeState>(end.State!);
        Check(end is { Revision: 3, TerminalRevision: 3, NextJobGeneration: 1, Jobs: null } && end.Status == status &&
            native is { Frames.Count: 0, Waits.Count: 0, Agenda.Count: 0 } &&
            (unhandled ? !native.Results.ContainsKey(Background) : native.Results["finish"]?.GetValue<string>() == expected) &&
            (await Rows(before.Id)).Select(row => row.Id).SequenceEqual(queued is null ? [] : [queued.Id]) &&
            history.Select(entry => entry.Revision).SequenceEqual([1L, 3L]) && entries.Count(entry => entry == "end") == 1 &&
            entries.Count(entry => entry.EndsWith(":" + Background, StringComparison.Ordinal) &&
                entry.StartsWith(timeoutWins ? "f:" : "c:", StringComparison.Ordinal)) == 1 &&
            ControlledJob.Attempts.Count == (timeoutWins ? 1 : 2) && ControlledJob.Attempts.All(entry => entry.Actor == actor.Id),
            "The fresh Native model must apply the same actor's stored result once, with one end and no job input left.");
        long? cleanupEpoch = null;
        if (queued is not null)
        {
            var due = queued.Due;
            var coldApi = new PostgresPreviewFoundation(admission,
                new(thirdStore, await admission.OpenJournalCursorAsync(stop), 16), WorkflowActor.WorkflowActor.ActorType);
            var view = await coldApi.GetAsync(scope, actor, stop)
                ?? throw new InvalidOperationException("The cold public read did not return the faulted root.");
            using var header = JsonDocument.Parse(view.Header.TypeData);
            var authored = applyingTurn ?? throw new InvalidOperationException("The Worker did not prepare the authored fault.");
            var afterEnd = await coldApi.SendAsync(scope, new Contract.Target.Ref(actor), "after-end", "probe", cancellationToken: stop);
            var replay = await coldApi.SendAsync(scope, new Contract.Target.Ref(actor), DelayedInput, "probe", "changed", cancellationToken: stop);
            var sharesJournal = await db.Database.SqlQuery<bool>($"""
                SELECT count(*) = 1 AND bool_and(i.xmin = j.xmin) AS "Value" FROM "Instances" i
                JOIN "Journal" j ON j."Instance" = i."Id" AND j."Partition" = i."Partition" AND j."Revision" = i."Revision"
                WHERE i."Id" = {before.Id} AND i."Revision" = 3
                """).SingleAsync(stop);
            Check(view is { Header: { Status: Contract.ActorStatus.Faulted, Fault: null }, Park: null } &&
                view.Header.Ref == actor && end.TerminalAt >= deadline && end.TerminalAt <= await Now() &&
                view.Header.EndedAt == end.TerminalAt && header.RootElement.GetProperty("SubStatus").GetString() == "job" &&
                authored is { Row.Status: InstanceStatus.Faulted, Unsubscribe: true, OpensPark: false, FoundationCascade: null } &&
                authored.Consumed.SequenceEqual([timeoutResult.InboxKey]) && sharesJournal &&
                !(await Commit(thirdStore, third.Fences.Single(), authored)).Landed &&
                afterEnd.Outcome == Contract.AdmissionOutcome.Refused && end.TerminalAt < due &&
                SameQueued((await Rows(before.Id)).Single()),
                "The cold owner must persist the authored fault and leave the exact future occurrence pending.");

            var input = (await admission.ReadPendingDirectAsync(Tenant, actor.Id, stop)).Single();
            var rowless = new TurnCommit(before.Id, null, null, [], [input.InboxKey], [], [], [], null);
            await Expire(Third);
            var fourthStore = Store();
            await using var fourth = await Take(fourthStore, Fourth);
            var fourthFence = fourth.Fences.Single();
            cleanupEpoch = fourthFence.Epoch;
            var position = await db.Partitions.Where(part => part.Id == fourthFence.Partition).Select(part => part.Position).SingleAsync(stop);
            Check(fourthFence.Epoch > applyingFence.Epoch && await Now() < due &&
                !(await Commit(fourthStore, fourthFence, rowless)).Landed && SameQueued((await Rows(before.Id)).Single()),
                "Even after a cold take, the exact terminal input must remain pending until its database due time.");
            while (await Now() < due) await Task.Delay(10, stop);

            Check(!(await Commit(thirdStore, applyingFence, rowless)).Landed && SameQueued((await Rows(before.Id)).Single()),
                "A displaced owner must leave the due occurrence pending.");
            for (var pass = 0; pass < 8 && !(await Rows(before.Id)).Single().Done; pass++)
            {
                await fourth.WokenAsync(null, stop);
                await fourth.RunAsync(stop);
                await Task.Delay(10, stop);
            }
            var disposed = (await Rows(before.Id)).Single();
            var after = await fourthStore.ReadFirstRootAsync(Tenant, actor.Id, stop);
            var afterHistory = await fourthStore.ReadJournalOfAsync(0, before.Id, 0, stop);
            var replayAfter = await coldApi.SendAsync(scope, new Contract.Target.Ref(actor), DelayedInput, "probe", cancellationToken: stop);
            Check(replay.Outcome == Contract.AdmissionOutcome.Duplicate && replay.Actor == actor &&
                replayAfter.Outcome == Contract.AdmissionOutcome.Duplicate && replayAfter.Actor == actor,
                "The live receipt must remain Duplicate both before and after settling the delayed input.");
            Check(disposed is { Done: true, Payload: null, Start: null, ReplyTo: null } &&
                disposed.Id == queued.Id && disposed.Seq == queued.Seq && disposed.Due == due && disposed.KeepUntil == queued.KeepUntil &&
                after is { Revision: 3, Status: InstanceStatus.Faulted, TerminalRevision: 3 } &&
                after.TerminalAt == end.TerminalAt && after.State == end.State && after.TypeData == end.TypeData &&
                cleanupTurns == 0 && afterHistory.SequenceEqual(history) &&
                await db.Partitions.Where(part => part.Id == fourthFence.Partition).Select(part => part.Position).SingleAsync(stop) == position,
                "The cold owner must settle the original due input without an authored turn or a change to checkpoint, journal or position.");
            Console.WriteLine("ok live receipt replay before and after cold terminal settlement of the original delayed input");

            bool SameQueued(MessageEntity row) => !row.Done && row.Id == queued.Id && row.Seq == queued.Seq &&
                row.Due == due && row.KeepUntil == queued.KeepUntil && row.Payload == queued.Payload && row.Start == queued.Start;
        }
        var outcome = delayedClock ? "late-clock timeout" : unhandled ? "unhandled timeout Faulted" : expected;
        Console.WriteLine($"ok Native timed job {(inBatch ? "in-batch" : "checked")} {outcome}, epochs {old.Epoch}->{settling.Epoch}->{third.Fences.Single().Epoch}{(cleanupEpoch is { } epoch ? $"->{epoch}" : "")}, " +
            $"revisions 1->2->3, {sampled.Count} PostgreSQL publication clock queries");
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

        EfStore Store(Func<KernelDb>? factory = null) => new(factory ?? Open, postgres: true, fenceInBatch: inBatch) { CommitSql = CommitSql.Owned };
        async Task<Worker> Take(EfStore store, string name)
        {
            await store.EnableRootJobsAsync(stop);
            var roots = await store.BindWorkerRootsAsync([(Tenant, actor.Id)], stop);
            var worker = new Worker(name, roots.Partition, store, PostgresNativeRootOwner.Adapter(store, roots, registry),
                new WorkerOptions
                {
                    PassTurnBudget = 1,
                    OnTurn = name == Fourth ? (_, _, _) => cleanupTurns++ : null,
                    OnPreparedCheckpoint = (turn, _, _) =>
                    {
                        if (turn.Sent.Any(message => message.FoundationJobResult)) winningTurn = turn;
                        if (name == Third) applyingTurn = turn;
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

    private sealed class PublicationSamples : DbCommandInterceptor
    {
        private int count;
        internal int Count => Volatile.Read(ref count);

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(EfStore.TimedJobPublicationClockQuery, StringComparison.Ordinal))
                Interlocked.Increment(ref count);
            return new(result);
        }
    }

    private sealed class ObservationDelay
    {
        internal static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(600);
        internal int Polls;
        internal int Commits;
    }

    private sealed class DelayedPoll(ObservationDelay delay) : DbCommandInterceptor
    {
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("t.now AS \"Now\", m.\"Id\"", StringComparison.Ordinal))
            {
                delay.Polls++;
                await Task.Delay(ObservationDelay.Duration, cancellationToken);
            }
            return result;
        }
    }

    private sealed class DelayedCommit(ObservationDelay delay) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            delay.Commits++;
            return Task.Delay(ObservationDelay.Duration, cancellationToken);
        }
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
