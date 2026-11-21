using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static partial class ContractActorWorkload
{
    public static async Task<int> RunJobTrialChecks(int partitions, Flaw flaws)
    {
        try
        {
            await CheckWithdrawal(partitions, flaws, loss: null, hold: false);
            await CheckWithdrawal(partitions, flaws, loss: false, hold: false);
            await CheckWithdrawal(partitions, flaws, loss: true, hold: false);
            await CheckWithdrawal(partitions, flaws, loss: null, hold: true);
            await CheckWithdrawal(partitions, flaws, loss: null, hold: false, cold: true);
            await CheckWithdrawal(partitions, flaws, loss: null, hold: false, predecessor: true);
            await CheckLostFirstSeen(partitions, flaws, landed: false);
            await CheckLostFirstSeen(partitions, flaws, landed: true);
            await CheckPublishedResult(partitions, flaws);
            await CheckGroupWithdrawal(partitions, flaws);
            await CheckDeadlineStillUnwinding(partitions, flaws, loseAnswer: false);
            await CheckDeadlineStillUnwinding(partitions, flaws, loseAnswer: true);
            await CheckDeadlineStillUnwinding(partitions, flaws, loseAnswer: false, withdrawFirst: true);
            await CheckEarlierSameActorTerminate(partitions, flaws);
            await CheckFinishedActorJob(partitions, flaws);
            await CheckFamilyPredecessor(partitions, flaws, pendingBirth: false);
            await CheckFamilyPredecessor(partitions, flaws, pendingBirth: true);
            await CheckStoreTrialCount(landed: false);
            await CheckStoreTrialCount(landed: true);
            Console.WriteLine($"Job trial checks passed at {partitions} partitions: fenced withdrawal, lost clear before and after, held/refused controls, preserved success.");
            return 0;
        }
        catch (InvalidOperationException error)
        {
            Console.Error.WriteLine($"Job trial checks failed at {partitions} partitions ({flaws}): {error.Message}");
            return 1;
        }
    }

    private static async Task CheckWithdrawal(int partitions, Flaw flaws, bool? loss, bool hold,
        bool cold = false, bool predecessor = false)
    {
        var (before, landed, lost) = (0, 0, false);
        await using var schedule = new WithdrawalSchedule(partitions, flaws, hold, (batch, after) =>
        {
            if (batch.ClearJobTrials.Count == 0) return;
            if (after) landed++;
            else before++;
            if (after != loss || lost) return;
            lost = true;
            throw new StoreException(Failure.Unknown, "lost the job trial clear");
        });
        await schedule.Start(cold);
        var trial = schedule.Attempts[1];
        var denied = await schedule.Terminate("wrong-selection", "unissued");
        Require(denied is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection },
            "a forged selection could request withdrawal");
        await schedule.Pass();
        Require(!trial.Token.IsCancellationRequested && before == 0, "a refused control interrupted an isolated job");

        if (predecessor) await schedule.SendOrdinary(schedule.ControlTarget);
        await schedule.Terminate();
        var holds = schedule.ControlReads;
        await schedule.Pass();
        Require(trial.Token.IsCancellationRequested && !trial.Result.Task.IsCompleted && before == 0 && schedule.ControlReads == holds &&
            (await schedule.Target()).Header.Status == Contract.ActorStatus.Running,
            "the control or trial clear passed the job before physical exit");
        trial.Complete("withdrawn");
        await schedule.Pass(20);
        Require((loss is null || lost) && before > 0 && landed <= 1 && (loss is not true || landed == 1),
            $"the clear did not follow its expected commit boundary ({loss}, {before}/{landed})");
        var target = await schedule.Target();
        var pending = (await schedule.Driver.Reads.InboxAsync(schedule.Scope, schedule.Job)).Items;
        var targetInbox = (await schedule.Driver.Reads.InboxAsync(schedule.Scope, schedule.ControlTarget)).Items
            .Where(item => item.Kind == Contract.Kinds.Terminate).ToArray();
        Require(pending is [{ Attempts: 0 }] && (await schedule.Journal()).Count == 0,
            $"the trial clear charged an owner loss or applied the withdrawn result ({loss})");
        var replacement = schedule.Attempts.Last();
        Require(schedule.Attempts.Count == 3 && replacement.Job.AttemptId != trial.Job.AttemptId &&
            !replacement.Token.IsCancellationRequested, "the job did not retry with a fresh physical attempt after the control settled");
        Require(hold
            ? target.Header.Status == Contract.ActorStatus.Running && targetInbox is [{ State: Contract.DeliveryState.Held }]
            : target.Header.Status == Contract.ActorStatus.Cancelled && targetInbox.Length == 0,
            $"the qualified control did not settle after the lost-clear boundary ({loss}, held={hold})");
        if (cold) Require(schedule.ControlReads > holds, "the cold checkpoint's deferred predicate was not reevaluated after yield");
        if (predecessor)
            Require((await schedule.Journal(schedule.ControlTarget)).SequenceEqual([new("ordinary", "2"), new("terminate", "2")]),
                "the terminate overtook its ordinary predecessor or the job retried before the command committed");

        replacement.Complete("replacement");
        await schedule.Pass();
        Require((await schedule.Journal()).SequenceEqual([new("result", "replacement")]),
            "the replacement result was lost or applied more than once");
    }

    private static async Task CheckPublishedResult(int partitions, Flaw flaws)
    {
        var clears = 0;
        await using var schedule = new WithdrawalSchedule(partitions, flaws, hold: false, (batch, landed) =>
        {
            if (landed) clears += batch.ClearJobTrials.Count;
        });
        await schedule.Start();
        await schedule.Terminate();
        var trial = schedule.Attempts[1];
        trial.Complete("observed");
        await schedule.Pass();
        Require(clears == 0 &&
            (await schedule.Journal()).SequenceEqual([new("result", "observed")]) &&
            (await schedule.Target()).Header.Status == Contract.ActorStatus.Cancelled,
            "withdrawal discarded a result already published by the isolated job");
    }

    private static async Task CheckLostFirstSeen(int partitions, Flaw flaws, bool landed)
    {
        var lost = false;
        var armed = false;
        await using var schedule = new WithdrawalSchedule(partitions, flaws, hold: false, (batch, after) =>
        {
            if (!armed || lost || after != landed || batch.Seen.Count == 0) return;
            lost = true;
            throw new StoreException(Failure.Unknown, "lost the terminate's first Seen mark");
        });
        await schedule.Start();
        var isolated = schedule.Attempts[1];
        await schedule.Terminate();
        armed = true;
        await schedule.Pass(20);
        Require(lost && isolated.Token.IsCancellationRequested && !isolated.Result.Task.IsCompleted &&
            schedule.Attempts.Count == 2 && (await schedule.Target()).Header.Status == Contract.ActorStatus.Running,
            "a lost first Seen mark opened the local physical trial gate before the driver exited");
        isolated.Complete("old generation");
        await schedule.Pass(20);
        var pending = (await schedule.Driver.Reads.InboxAsync(schedule.Scope, schedule.Job)).Items;
        Require(schedule.Attempts.Count == 3 && schedule.Attempts[2].Job.AttemptId != isolated.Job.AttemptId &&
            pending is [{ Attempts: 0 }] && (await schedule.Journal()).Count == 0 &&
            (await schedule.Target()).Header.Status == Contract.ActorStatus.Cancelled &&
            (await schedule.Journal(schedule.ControlTarget)).SequenceEqual([new("terminate", "2")]),
            "local recovery did not dispatch the terminate before a fresh job attempt without charging a death");
    }

    private static async Task CheckDeadlineStillUnwinding(int partitions, Flaw flaws, bool loseAnswer, bool withdrawFirst = false)
    {
        var lost = false;
        var cleared = 0;
        await using var schedule = new WithdrawalSchedule(partitions, flaws, hold: false, (batch, landed) =>
        {
            if (landed) cleared += batch.ClearJobTrials.Count;
            if (!loseAnswer || lost || !landed || !batch.Turns.SelectMany(turn => turn.Sent).Any(message =>
                message.Kind == Kinds.JobFinished && message.Read<JobFinished>().Error == "timeout")) return;
            lost = true;
            throw new StoreException(Failure.Unknown, "lost the deadline settlement answer");
        });
        schedule.Timeout = TimeSpan.FromMilliseconds(85);
        await schedule.Start();
        var trial = schedule.Attempts[1];
        await schedule.Terminate();
        if (withdrawFirst)
        {
            await schedule.Pass();
            Require(trial.Token.IsCancellationRequested && !trial.Result.Task.IsCompleted,
                "the deadline witness must begin withdrawal before the timed cutoff");
        }
        schedule.Driver.Advance(34);
        await schedule.Pass(16);
        Require(trial.Token.IsCancellationRequested && !trial.Result.Task.IsCompleted && cleared == 0 &&
            (await schedule.Target()).Header.Status == Contract.ActorStatus.Running && (!loseAnswer || lost),
            "the logical timeout released physical isolation before callback exit");
        if (!loseAnswer)
            Require((await schedule.Journal()).SequenceEqual([new("result", "timeout")]),
                "the physical gate withheld the timed job's own logical settlement");
        trial.Complete("late timeout result");
        await schedule.Pass(20);
        Require((await schedule.Target()).Header.Status == Contract.ActorStatus.Cancelled && cleared == 0 &&
            (await schedule.Journal()).SequenceEqual([new("result", "timeout")]),
            "deadline settlement lost its one timeout or kept the unrelated control gated after exit");
    }

    private static async Task CheckEarlierSameActorTerminate(int partitions, Flaw flaws)
    {
        var cleared = 0;
        await using var schedule = new WithdrawalSchedule(partitions, flaws, hold: false, (batch, landed) =>
        {
            if (landed) cleared += batch.ClearJobTrials.Count;
        });
        schedule.Timeout = TimeSpan.FromMilliseconds(85);
        await schedule.Start();
        var trial = schedule.Attempts[1];
        var view = await schedule.Driver.Reads.GetAsync(schedule.Scope, schedule.Job);
        var accepted = await schedule.Terminate(schedule.Job, "end-timed-actor", view!.Selection!, dueAt: schedule.Driver.Now);
        Require(accepted.Outcome == Contract.AdmissionOutcome.Accepted, "the earlier same-target terminate was refused");
        schedule.Driver.Advance(34);
        await schedule.Pass(16);
        Require(cleared == 0 && trial.Token.IsCancellationRequested && !trial.Result.Task.IsCompleted &&
            (await schedule.Journal()).Count == 0,
            "the same-target command or later timeout ran ahead of physical exit");
        trial.Complete("too late");
        await schedule.Pass(20);
        view = await schedule.Driver.Reads.GetAsync(schedule.Scope, schedule.Job);
        Require(cleared == 1 && view?.Header.Status == Contract.ActorStatus.Cancelled &&
            (await schedule.Journal()).SequenceEqual([new("terminate", "2")]) &&
            (await schedule.Driver.Reads.InboxAsync(schedule.Scope, schedule.Job)).Items.Count == 0,
            "the later deadline held the earlier terminate after the driver exited");
    }

    private static async Task CheckFinishedActorJob(int partitions, Flaw flaws)
    {
        await using var schedule = new WithdrawalSchedule(partitions, flaws, hold: false, (_, _) => { });
        await schedule.Start();
        var trial = schedule.Attempts[1];
        var accepted = await schedule.Driver.Ingress.SendAsync(schedule.Scope, new Contract.Target.Ref(schedule.Job), "finish", "finish");
        Require(accepted.Outcome == Contract.AdmissionOutcome.Accepted, "the job actor's normal finish was refused");
        await schedule.Pass();
        var view = await schedule.Driver.Reads.GetAsync(schedule.Scope, schedule.Job);
        Require(view?.Header.Status == Contract.ActorStatus.Finished && !trial.Token.IsCancellationRequested,
            "the finished actor must still have an outstanding job attempt");
        var terminated = await schedule.Terminate(schedule.Job, "settle-finished-actor", view!.Selection!);
        Require(terminated.Outcome == Contract.AdmissionOutcome.Accepted, "the finished actor's exact operator terminate was refused");
        await schedule.Pass();
        Require(trial.Token.IsCancellationRequested && !trial.Result.Task.IsCompleted,
            "the operator could not withdraw the active job of a finished actor");
        trial.Complete("withdrawn");
        await schedule.Pass();
        view = await schedule.Driver.Reads.GetAsync(schedule.Scope, schedule.Job);
        Require(view?.Header.Status == Contract.ActorStatus.Finished &&
            (await schedule.Journal()).SequenceEqual([new("terminate", "2")]) &&
            (await schedule.Driver.Reads.InboxAsync(schedule.Scope, schedule.Job)).Items.Count == 0,
            "the operator failed to settle the finished actor's job or changed its terminal status");
    }

    private static async Task CheckFamilyPredecessor(int partitions, Flaw flaws, bool pendingBirth)
    {
        await using var schedule = new WithdrawalSchedule(partitions, flaws, hold: false, (_, _) => { });
        var root = await schedule.Begin("root");
        var extra = await schedule.Begin("extra");
        var work = await schedule.Begin("work");
        var driver = schedule.Driver;
        var scope = schedule.Scope;
        await driver.Start([(scope, root), (scope, extra)]);
        await schedule.Pass();
        var child = schedule.Child ?? throw new InvalidOperationException("the root did not issue a child");
        var moved = await driver.StartElsewhere(pendingBirth ? [(scope, work)] : [(scope, work), (scope, child)]);
        Require(moved == (pendingBirth ? 1 : 2), "the fixture needs the child's real scope on the job's second owner");
        await schedule.Pass();
        await driver.TakeOver();
        await schedule.Pass();
        Require(schedule.Attempts.Count == 2, "the family's second owner did not start the recovered job");
        var previous = schedule.Attempts[0];
        var current = schedule.Attempts[1];
        previous.Complete("old");
        await schedule.SendOrdinary(child);
        var selected = await driver.Reads.GetAsync(scope, root);
        var admission = await schedule.Terminate(root, "terminate-root", selected!.Selection!);
        Require(admission.Outcome == Contract.AdmissionOutcome.Accepted, "the family terminate was refused");
        await schedule.Pass();
        if (pendingBirth) Require(await driver.Reads.GetAsync(scope, child) is null &&
            await driver.StartElsewhere([(scope, child)]) == 1, "the committed creator was expected on the still-unowned scope");
        await schedule.Pass();
        Require(current.Token.IsCancellationRequested && !current.Result.Task.IsCompleted &&
            (await driver.Reads.GetAsync(scope, root))?.Header.Status == Contract.ActorStatus.Cancelled &&
            (await driver.Reads.GetAsync(scope, child))?.Header.Status ==
                (pendingBirth ? null : Contract.ActorStatus.Running),
            "the authenticated cascade did not signal withdrawal while preserving the pending child work");
        current.Complete("withdrawn");
        await schedule.Pass(20);
        var terminal = await driver.Reads.GetAsync(scope, child);
        Require(terminal?.Header is { Status: Contract.ActorStatus.Cancelled, Parent: var parent } && parent == root &&
            (await schedule.Journal(child)).SequenceEqual([new("born"), new("ordinary", "2"), new("terminate", "2")]),
            "the child creator, ordinary input and terminate did not run in order before job retry");
    }

    private static async Task CheckStoreTrialCount(bool landed)
    {
        var now = 1_700_000_000_000L;
        var lease = TimeSpan.FromMilliseconds(50);
        var store = new InMemoryStore(2, () => now);
        var owner = await store.TakeAsync(0, "first", lease) ?? throw new InvalidOperationException("first take was refused");
        var actor = InstanceRef.In(0, "accounting");
        var key = InboxKey.Ticket(actor.Id, "work");
        var message = new Message(key.Id, actor, Kinds.Job).WithInboxScope(key.Scope);
        var send = new TurnCommit(actor.Id, null, null, [], [], [message], [], [], null);
        var begin = new CommitBatch(0, owner.Epoch, owner.Position, lease, false, [send], [], []);
        Require((await store.CommitAsync(begin)).Landed, "the real store did not commit the test ticket");
        var mark = begin with { Turns = [], Trial = [key] };
        Require((await store.CommitAsync(mark)).Landed, "the real store did not mark the trial");
        var clear = mark with { Trial = [], ClearJobTrials = [key] };
        if (landed)
        {
            Require((await store.CommitAsync(clear)).Landed, "the store did not clear the trial");
            var row = store.Messages[key];
            Require(row.Trial == 0 && row.Seen == owner.Epoch && row.Attempts == 0 && !row.Done && !row.Held,
                "the trial clear altered the other logical job fields");
            try
            {
                await store.CommitAsync(clear);
                throw new InvalidOperationException("a second clear landed without a current trial");
            }
            catch (StoreException error) when (error.Failure == Failure.Unknown) { }
        }

        now += 51;
        Require(await store.TakeAsync(0, "second", lease) is not null, "second take was refused");
        var charged = landed ? 0 : 1;
        Require(!(await store.CommitAsync(clear)).Landed && store.Messages[key].Attempts == charged,
            "the stale owner cleared a newer fence or its trial was mischarged");
        now += 51;
        Require(await store.TakeAsync(0, "third", lease) is not null && store.Messages[key].Attempts == charged,
            "the store charged one old trial to two takeovers");
    }

    private static async Task CheckGroupWithdrawal(int partitions, Flaw flaws)
    {
        var clears = new List<int>();
        await using var schedule = new WithdrawalSchedule(partitions, flaws, hold: false, (batch, landed) =>
        {
            if (landed && batch.ClearJobTrials.Count > 0) clears.Add(batch.ClearJobTrials.Count);
        }, jobs: 2);
        await schedule.Start();
        var recovered = schedule.Attempts.Skip(2).ToArray();
        await schedule.Terminate();
        await schedule.Pass();
        Require(recovered.Length == 2 && recovered.All(attempt => attempt.Token.IsCancellationRequested),
            "the blocked terminate did not signal both selected job bodies");
        recovered[0].Complete("withdrawing first");
        await schedule.Pass();
        Require(clears.Count == 0 && (await schedule.Target()).Header.Status == Contract.ActorStatus.Running,
            "the first exit released a trial despite its running sibling");
        recovered[1].Complete("withdrawing second");
        await schedule.Pass();
        Require(clears.SequenceEqual([2]) && (await schedule.Target()).Header.Status == Contract.ActorStatus.Cancelled,
            "both callbacks exited without the one counted clear and terminate");
    }

    private sealed class WithdrawalSchedule : IAsyncDisposable
    {
        private readonly WithdrawalActor _type;
        private readonly int _jobs;
        internal ContractWorkload.InMemoryDriver Driver { get; }
        internal Contract.BoundScope Scope { get; } = new("job-trial");
        internal Contract.ActorRef Job { get; private set; }
        internal Contract.ActorRef ControlTarget { get; private set; }
        internal List<WorkAttempt> Attempts => _type.Attempts;
        internal int ControlReads => _type.ControlReads;
        internal TimeSpan? Timeout { get => _type.Timeout; set => _type.Timeout = value; }
        internal Contract.ActorRef? Child => _type.Child;

        internal WithdrawalSchedule(int partitions, Flaw flaws, bool hold, Action<CommitBatch, bool> observe, int jobs = 1)
        {
            _jobs = jobs;
            _type = new WithdrawalActor(hold, jobs);
            Driver = new(partitions, flaws, _type,
                authorizeOperator: (scope, kind) => scope.Tenant == Scope.Tenant && kind == Contract.Kinds.Terminate,
                observeCommit: observe, trialGroup: jobs, mixedTypes: true);
}
        internal Task Start() => Start(cold: false);

        internal async Task Start(bool cold)
        {
            Job = await Begin("work");
            ControlTarget = await Begin("control");
            await Driver.Start([(Scope, Job), (Scope, ControlTarget)]);
            await Pass();
            Require(Attempts.Count == _jobs, "the original jobs were not running at takeover");
            // A future input loads the target's checkpoint on the new owner before the recovered job enters.
            if (!cold)
            {
                var accepted = await Driver.Ingress.SendAsync(Scope, new Contract.Target.Ref(ControlTarget), "observe", "observe",
                    options: new Contract.AddOptions(DueAt: Driver.Now + 1_000));
                Require(accepted.Outcome == Contract.AdmissionOutcome.Accepted, "the control target's future input was refused");
            }
            await Driver.TakeOver();
            await Pass();
            Require(Attempts.Count == _jobs * 2 && Attempts.Take(_jobs).All(attempt => attempt.Token.IsCancellationRequested) &&
                Attempts.Skip(_jobs).All(attempt => !attempt.Token.IsCancellationRequested),
                "the successor did not start the real isolated job attempts");
            foreach (var attempt in Attempts.Take(_jobs)) attempt.Complete("old owner");
        }

        internal Task Pass(int count = 8) => Driver.Pass(count);
        public ValueTask DisposeAsync() => Driver.DisposeAsync();
        internal Task<List<WorkEntry>> Journal(Contract.ActorRef? actor = null) => Entries<WorkEntry>(Driver, Scope, actor ?? Job, nameof(WorkEntry.Action));
        internal async Task<Contract.ActorView> Target() =>
            await Driver.Reads.GetAsync(Scope, ControlTarget) ?? throw new InvalidOperationException("control target unavailable");

        internal async Task<Contract.ActorRef> Begin(string name)
        {
            var accept = await Driver.Ingress.SendAsync(Scope, new Contract.Target.Key(name, new(_type.Type, name)), name, Contract.Kinds.Start);
            Require(accept is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "job-trial creator was refused");
            return accept.Actor!.Value;
        }

        internal async Task Terminate()
        {
            var target = await Target();
            var admission = await Terminate("terminate", target.Selection!);
            Require(admission.Outcome == Contract.AdmissionOutcome.Accepted, "the valid terminate was refused");
        }

        internal ValueTask<Contract.Admission> Terminate(string id, string selection) =>
            Terminate(ControlTarget, id, selection);

        internal ValueTask<Contract.Admission> Terminate(Contract.ActorRef actor, string id, string selection, long? dueAt = null) =>
            Driver.Ingress.SendAsync(Scope, new Contract.Target.Ref(actor), id, Contract.Kinds.Terminate,
                Json.Write(new Contract.OperatorCommand(selection)), new Contract.AddOptions(DueAt: dueAt));

        internal async Task SendOrdinary(Contract.ActorRef actor)
        {
            var admitted = await Driver.Ingress.SendAsync(Scope, new Contract.Target.Ref(actor), "ordinary", "ordinary");
            Require(admitted.Outcome == Contract.AdmissionOutcome.Accepted, "the ordinary predecessor was refused");
        }
    }

    private sealed class WithdrawalActor(bool hold, int jobs) : Contract.IActorType
    {
        public string Type => "job-trial";
        internal List<WorkAttempt> Attempts { get; } = [];
        internal TimeSpan? Timeout { get; set; }
        internal Contract.ActorRef? Child { get; private set; }
        internal int ControlReads { get; private set; }
        public bool Withholds(Contract.ActorHeader header)
        {
            if (header.TypeData == "control") ControlReads++;
            return false;
        }
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            var attempt = new WorkAttempt(job, cancellationToken);
            Attempts.Add(attempt);
            return new(attempt.Result.Task);
        }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start && turn.Header.TypeData == "work")
                    for (var i = 0; i < jobs; i++) turn.StartJob($"hold-{i}", timeout: Timeout);
                if (message.Kind == Contract.Kinds.Start && turn.Header.TypeData == "root")
                    Child = turn.Start(new(Type, "child"), Contract.Placement.Spread);
                if (message.Kind == Contract.Kinds.Start && turn.Header.TypeData == "child")
                    turn.Record(new WorkEntry("born"));
                if (message.Kind == "ordinary")
                    turn.Record(new WorkEntry("ordinary", Attempts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                if (message.Kind == "finish")
                    turn.End(Contract.ActorStatus.Finished);
                if (message.Kind == Contract.Kinds.Terminate)
                {
                    turn.Record(new WorkEntry("terminate", Attempts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    if (hold) turn.Hold(message);
                    else if (turn.Header.Status == Contract.ActorStatus.Running) turn.End(Contract.ActorStatus.Cancelled);
                }
                if (message.Kind == Contract.Kinds.JobFinished)
                {
                    var result = Json.Read<Contract.JobFinished>(message.Payload!);
                    turn.Record(new WorkEntry("result", result.Result is { } payload ? Json.Read<string>(payload) : result.Error));
                }
            }
            return default;
        }
    }
}
