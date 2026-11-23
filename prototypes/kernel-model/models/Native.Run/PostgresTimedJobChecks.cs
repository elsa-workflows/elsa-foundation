using Kernel.EntityFramework;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

/// <summary>Physical Worker evidence for the legacy relational actor protocol.</summary>
internal static class PostgresTimedJobChecks
{
    private static readonly InstanceRef Actor = InstanceRef.In(0, "timed-job");
    private const string Model = "timed-job-check";
    private const string Live = "live";
    private const string Moved = "moved";
    private const string StartNext = "start-next";
    private const string FirstOwner = "job-original";
    private const string RetryOwner = "job-retry";
    private const string NextOwner = "job-successor";
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    internal static async Task<int> RunAsync(bool inBatch)
    {
        var open = PostgresMultiRootClient.Database();
        var store = new EfStore(open, postgres: true, fenceInBatch: inBatch) { CommitSql = CommitSql.Owned };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var stop = timeout.Token;
        await store.CreateAsync(1);
        var firstModel = new Probe();
        var retryModel = new Probe();
        var nextModel = new Probe();
        await using var first = Owner(FirstOwner, firstModel);
        await using var retry = Owner(RetryOwner, retryModel);
        await using var next = Owner(NextOwner, nextModel);
        await Activate(first);
        var previousFence = first.Fences.Single();
        try
        {
            var liveDue = await Issue(Live, birth: true);
            var liveTicket = await Pending(first, firstModel, Live, liveDue, 1);
            var completed = Driver(first, Live);
            firstModel.Results[Live].SetResult("live-result");
            await completed.WaitAsync(stop);
            Check(await DatabaseNow() < liveDue, "The callback must publish before its physical deadline.");
            await AtDeadline(liveDue);
            var racing = await Snapshot();
            Check(racing.Checkpoint.Revision == 1 && racing.Rows.Length == 2 &&
                racing.Rows.Single(row => row.Kind == Kinds.Job).Id == liveTicket.Id &&
                racing.Rows.Single(row => row.Kind == Kinds.JobDeadline).Due == liveDue,
                "The queued callback must leave its ticket and now-due deadline pending until the Worker runs.");
            await Drive(first, async () => (await Snapshot()).Checkpoint.Revision >= 2);
            var liveEnd = await Snapshot();
            Check(liveEnd.Checkpoint.Revision == 2 && liveEnd.Checkpoint.Jobs is null && liveEnd.Rows.Length == 0 &&
                StateOf(liveEnd.Checkpoint) is { Active: null, Results: ["live-result"] },
                "A timely Worker completion must win over the due deadline and clear both physical rows once.");

            var movedDue = await Issue(Moved, birth: false);
            var movedTicket = await Pending(first, firstModel, Moved, movedDue, 3);
            var oldDriver = Driver(first, Moved);
            await Expire(FirstOwner);
            await Activate(retry);
            var retryFence = retry.Fences.Single();
            var trial = await Pending(retry, retryModel, Moved, movedDue, 3, recovered: true);
            var trialDriver = Driver(retry, Moved);
            Check(retryFence.Epoch > previousFence.Epoch && trial.Id == movedTicket.Id && trial.Seq == movedTicket.Seq &&
                await DatabaseNow() < movedDue, "The pre-deadline take must put the same ticket on a physical trial.");
            await AtDeadline(movedDue);
            await Expire(RetryOwner);
            await Activate(next);
            var takeoverFence = next.Fences.Single();
            var taken = await Snapshot();
            var retained = taken.Rows.Single(row => row.Kind == Kinds.Job);
            Check(takeoverFence.Epoch > retryFence.Epoch && taken.Checkpoint.Revision == 3 && taken.Rows.Length == 2 &&
                retained.Id == trial.Id && retained.Seq == trial.Seq && retained.Attempts == trial.Attempts + 1 &&
                taken.Rows.Single(row => row.Kind == Kinds.JobDeadline) is { Attempts: 0 } timer && timer.Due == movedDue,
                "A cold PostgreSQL take must count the old trial and retain the same ticket and deadline.");
            Check(!(await store.CommitAsync(new(0, retryFence.Epoch, retryFence.Position, Lease, false, [], [], []), stop)).Landed,
                "The trial owner must be fenced out after the take.");

            await Drive(next, async () => (await Snapshot()).Checkpoint.Revision >= 4);
            var expired = await Snapshot();
            Check(expired.Checkpoint.Revision == 4 && expired.Checkpoint.Jobs is null && expired.Rows.Length == 0 &&
                StateOf(expired.Checkpoint) is { Active: null, Results: ["live-result", "deadline"] } &&
                nextModel.Results.Count == 0,
                "The due ticket must expire after takeover without starting a new callback.");
            firstModel.Results[Moved].SetResult("late-original-result");
            retryModel.Results[Moved].SetResult("late-trial-result");
            await Task.WhenAll(oldDriver, trialDriver).WaitAsync(stop);
            await first.RunAsync(stop);
            await retry.RunAsync(stop);
            await Pass(next);
            var final = await Snapshot();
            Check(final.Checkpoint.State == expired.Checkpoint.State && final.Checkpoint.Revision == 4 && final.Rows.Length == 0 &&
                final.History.Select(entry => (entry.Revision, Json.Read<string[]>(entry.Entries).Single())).SequenceEqual(
                    [(1L, "start:live"), (2L, "result:live-result"), (3L, "start:moved"), (4L, "result:deadline")]),
                "The late callback must leave exactly one physical journal outcome for each job on the same actor.");
            Console.WriteLine($"PostgreSQL owned {(inBatch ? "in-batch" : "checked")}: {Actor.Id}, epoch {previousFence.Epoch}->{retryFence.Epoch}->{takeoverFence.Epoch}, " +
                "revision 4, timely completion wins, due takeover counts one trial and delivers one deadline.");
            return 0;
        }
        finally
        {
            foreach (var body in firstModel.Results.Values.Concat(retryModel.Results.Values).Concat(nextModel.Results.Values))
                body.TrySetCanceled();
        }

        Worker Owner(string node, Probe model) => new(node, 0, store, model, new() { Lease = Lease });

        async Task Activate(Worker worker)
        {
            var activated = await new DurableHost([worker]).ActivateAsync(0, stop);
            Check(ReferenceEquals(activated, worker), "The worker must own the PostgreSQL partition.");
        }

        async Task<long> Issue(string key, bool birth)
        {
            var due = checked(await DatabaseNow() + 3_500);
            Check(await store.AddAsync(new(key, Actor, birth ? Kinds.Start : StartNext,
                Json.Write(new Job(key, due)), Start: birth ? new(Model, "timed-work") : null), stop),
                "The timed job trigger must be admitted once.");
            return due;
        }

        async Task<MessageEntity> Pending(Worker worker, Probe model, string key, long due, long revision, bool recovered = false)
        {
            await Drive(worker, () => Task.FromResult(model.Results.ContainsKey(key)));
            var visible = await Snapshot();
            var ticket = visible.Rows.Single(row => row.Kind == Kinds.Job);
            var timer = visible.Rows.Single(row => row.Kind == Kinds.JobDeadline);
            var job = Json.Read<JobTicket>(ticket.Payload!);
            var delivery = model.Entries[key];
            Check(visible.Checkpoint.Revision == revision && visible.Checkpoint.Status == InstanceStatus.Running &&
                StateOf(visible.Checkpoint).Active == new Job(key, due) && JobRegistry.Read(visible.Checkpoint.Jobs).SequenceEqual([new(key, 0)]) &&
                visible.Rows.Length == 2 && ticket.Id == Worker.TicketId(Actor.Id, key) && ticket.InboxScope == InboxKey.Ticket(Actor.Id, key).Scope &&
                ticket.JobGeneration == 0 && !ticket.Done && !timer.Done && ticket.Seen == worker.Fences.Single().Epoch &&
                ticket.Trial == (recovered ? worker.Fences.Single().Epoch : 0) && ticket.Attempts == 0 &&
                job.Key == key && job.Deadline == due && job.Timer == timer.Id && timer.Due == due && timer.InboxScope == InboxKey.Issued(timer.Id).Scope &&
                timer.Sender == Actor.Id && job.Model == Model &&
                delivery.Instance == Actor && delivery.Key == key && delivery.Deadline == due && delivery.AttemptNumber == 1,
                "The committed checkpoint, exact issued ticket, physical trial, deadline and job context must agree.");
            if (!recovered)
            {
                await using var db = open();
                Check(await db.Database.SqlQuery<bool>($"""
                    SELECT COUNT(*) = 2 AND bool_and(m.xmin = i.xmin AND j.xmin = i.xmin) AS "Value"
                    FROM "Instances" i
                    JOIN "Messages" m ON m."Target" = i."Id" AND m."Partition" = i."Partition"
                    JOIN "Journal" j ON j."Instance" = i."Id" AND j."Partition" = i."Partition" AND j."Revision" = i."Revision"
                    WHERE i."Id" = {Actor.Id} AND i."Revision" = {revision} AND (m."Id" = {ticket.Id} OR m."Id" = {timer.Id})
                    """).SingleAsync(stop), "The job ticket, timer, checkpoint and waiting journal entry must share a PostgreSQL transaction.");
            }
            return ticket;
        }

        static Task Driver(Worker owner, string key) => owner.JobCompletion(Actor.Id, key, 0)
            ?? throw new InvalidOperationException("The worker did not retain its physical job driver.");

        async Task Expire(string node)
        {
            // Inject owner loss through the PostgreSQL node record. The outstanding input rows stay unchanged.
            await using var db = open();
            Check(await db.Nodes.Where(n => n.Id == node)
                .ExecuteUpdateAsync(change => change.SetProperty(n => n.LeaseUntil, 0L), stop) == 1,
                "The displaced node lease must exist.");
        }

        async Task<long> DatabaseNow()
        {
            await using var db = open();
            return await db.Database.SqlQuery<long>($"""SELECT (extract(epoch from clock_timestamp()) * 1000)::bigint AS "Value" """).SingleAsync(stop);
        }

        async Task AtDeadline(long cutoff)
        {
            while (await DatabaseNow() < cutoff)
                await Task.Delay(10, stop);
        }

        async Task<Physical> Snapshot()
        {
            await using var db = open();
            return new(await db.Instances.AsNoTracking().SingleAsync(row => row.Id == Actor.Id, stop),
                await db.Messages.AsNoTracking().Where(row => row.Target == Actor.Id &&
                    (row.Kind == Kinds.Job || row.Kind == Kinds.JobDeadline || row.Kind == Kinds.JobFinished)).OrderBy(row => row.Seq).ToArrayAsync(stop),
                await db.Journal.AsNoTracking().Where(row => row.Instance == Actor.Id).OrderBy(row => row.Position).ToArrayAsync(stop));
        }

        async Task Pass(Worker worker)
        {
            await worker.WokenAsync(null, stop);
            await worker.RunAsync(stop);
            Check(worker.Owns, "The worker must retain its PostgreSQL fence while driving.");
        }

        async Task Drive(Worker worker, Func<Task<bool>> reached)
        {
            while (!await reached())
            {
                await Pass(worker);
                await Task.Delay(5, stop);
            }
        }
    }

    private static JobState StateOf(InstanceEntity row) => Json.Read<JobState>(row.State);
    private sealed record Physical(InstanceEntity Checkpoint, MessageEntity[] Rows, JournalEntity[] History);
    private sealed record Job(string Key, long Due);
    private sealed record JobState(Job? Active, string[] Results);

    private sealed class Probe : IActorType
    {
        public Dictionary<string, IJob> Entries { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, TaskCompletionSource<object?>> Results { get; } = new(StringComparer.Ordinal);
        public bool Withholds(InstanceRow row, ITurnServices services) => false;
        public bool SelectsAlone(string? model, InstanceRow? row, Delivery delivery) =>
            delivery.Message.Kind is Kinds.JobDeadline or Kinds.JobFinished;
        public TurnOutcome? Command(TurnInput input) => null;
        public TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error) =>
            throw new InvalidOperationException(error);

        public ValueTask<object?> ExecuteJobAsync(string model, IJob job, CancellationToken cancellationToken)
        {
            Check(model == Model && job.Instance == Actor, "The Worker must execute only the issued same-actor job.");
            var body = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Entries.Add(job.Key, job);
            Results.Add(job.Key, body);
            return new(body.Task);
        }

        public ValueTask<TurnOutcome> TurnAsync(TurnInput input)
        {
            var turn = new DurableTurn(input);
            var message = turn.Inbox.Single();
            var state = input.Row is { } current ? Json.Read<JobState>(current.State) : new(null, []);
            string entry;
            if (message.Kind is Kinds.Start or StartNext)
            {
                Check(state.Active is null, "The preceding timed job must have ended before the next starts.");
                var job = message.Read<Job>();
                turn.StartJobWithPayload(job.Key, null, job.Due);
                state = state with { Active = job };
                entry = "start:" + job.Key;
            }
            else
            {
                // This fixture observes the input selected by the Worker. It owns only its own application state.
                var active = state.Active ?? throw new InvalidOperationException("The Worker selected a second job outcome.");
                var result = message.Kind switch
                {
                    Kinds.JobFinished when input.Taken.Single().IsWorkerJobCompletion && message.Read<JobFinished>() is { Error: null, Result: { } value } done &&
                        done.Key == active.Key => Json.Read<string>(value),
                    Kinds.JobDeadline when message.InboxKey.HasIssuedOrigin && message.Due == active.Due &&
                        message.Read<JobDeadline>().Key == active.Key => "deadline",
                    _ => throw new InvalidOperationException("The Worker selected an unexpected job input.")
                };
                if (message.Kind == Kinds.JobFinished)
                    turn.Recall(input.Taken.Single().Original!.Read<JobTicket>().Timer!);
                else
                    turn.CancelJob(active.Key);
                state = new(null, [.. state.Results, result]);
                entry = "result:" + result;
            }
            var row = input.Row ?? new(input.Actor, Model, "timed-work", InstanceStatus.Running, "null", 0, null);
            row = row with { State = Json.Write(state), Revision = turn.Revision, Jobs = turn.Jobs() };
            return ValueTask.FromResult(turn.Seal(turn.Outcome(row.Status), row, state, Json.Write(new[] { entry })));
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
