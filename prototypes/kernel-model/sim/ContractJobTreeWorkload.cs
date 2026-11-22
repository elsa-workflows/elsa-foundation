using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

internal static partial class ContractActorWorkload
{
    public static async Task<int> RunJobTree(int from, int seeds, int partitions, Flaw flaws, bool treeStall)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seeds);
        for (var seed = from; seed < checked(from + seeds); seed++)
        {
            var trace = new List<string>();
            try
            {
                await JobTreeReplay(seed, partitions, flaws, treeStall, trace);
            }
            catch (InvalidOperationException error)
            {
                Console.Error.WriteLine($"Public job and tree workload seed {seed} failed at {partitions} virtual partitions ({flaws}). {error.Message}");
                Console.Error.WriteLine(string.Join(Environment.NewLine, trace));
                return 1;
            }
        }

        Console.WriteLine(treeStall
            ? $"Uncooperative tree jobs kept trial isolation in {seeds} seeds from {from} at {partitions} virtual partitions."
            : $"Public job and tree workload passed {seeds} seeds from {from} at {partitions} virtual partitions. Each seed exercised stale job attempts, real row refusal, lease-loss poison and nested tree cancellation.");
        return 0;
    }

    private sealed record WorkEntry(string Action, string? Detail = null);

    private static async Task JobTreeReplay(int seed, int partitions, Flaw flaws, bool treeStall, List<string> trace)
    {
        const int maxAttempts = 3;
        const string treeEvent = "tree-jobs";
        var work = new WorkBodies();
        var random = new Random(seed);
        var scope = new Contract.BoundScope("job-tree");
        var refused = 0;
        var clears = 0;
        var type = new PublicWorkflow([new PublicRunner(work), new PublicFragile(work), new PublicTree(work, treeEvent)]);
        await using var driver = new ContractWorkload.InMemoryDriver(partitions, flaws, type, maxAttempts,
            (actorScope, kind) => actorScope.Tenant == scope.Tenant && kind == Contract.Kinds.Terminate,
            row =>
            {
                if (!row.State.Contains(Fragile.Sour, StringComparison.Ordinal)) return false;
                refused++;
                return true;
            }, (batch, landed) => clears += landed ? batch.ClearJobTrials.Count : 0);
        var runtime = new PublicRuntime(driver.Ingress, driver.Reads);
        var runner = await Begin("runner", "runner");
        var healthy = await Begin("healthy", "fragile");
        await driver.Start([(scope, runner), (scope, healthy)]);
        await driver.Pass(4);

        var keys = new[] { $"cancel-{random.Next(10_000)}", $"race-{random.Next(10_000)}" };
        await Input(runner, "go-cancel", "go", keys[0]);
        await Input(runner, "go-race", "go", keys[1]);
        await driver.Pass(8);
        var cancelled = work.Attempts(runner, keys[0]).Single();
        var old = work.Attempts(runner, keys[1]).Single();
        Require(old.Job.Arguments == Json.Write(keys[1]), "the job did not receive its model arguments");
        await Input(runner, "go-duplicate", "go", keys[1]);
        await Input(runner, "cancel", "cancel", keys[0]);
        await driver.Pass(8);
        Require(cancelled.Token.IsCancellationRequested, "Runner cancellation did not stop the active attempt");
        Require(work.Attempts(runner, keys[1]).Length == 1, "repeated Runner work produced a second active attempt");
        cancelled.Complete("cancelled attempt");

        trace.Add("Runner loses the lease while an attempt is running and completes the old attempt after replacement");
        await driver.TakeOver();
        await driver.Pass(8);
        var attempts = work.Attempts(runner, keys[1]);
        trace.Add($"Runner has {attempts.Length} physical entries with lower bounds {string.Join(",", attempts.Select(attempt => attempt.Job.AttemptNumber))}");
        Require(attempts.Length == 2 && old.Token.IsCancellationRequested &&
            attempts[1].Job.AttemptId != old.Job.AttemptId && old.Job.AttemptNumber == 1 && attempts[1].Job.AttemptNumber is >= 1 and <= 2,
            "Runner's job did not survive takeover with a distinct successor attempt");
        var successorFirst = random.Next(2) == 0;
        (successorFirst ? attempts[1] : old).Complete(successorFirst ? "successor" : "old attempt");
        await driver.Pass(5);
        (successorFirst ? old : attempts[1]).Complete(successorFirst ? "old attempt" : "successor");
        await driver.Pass(8);
        var runnerJournal = await Journal(runner);
        Require(runnerJournal.SequenceEqual([
            new("job-start", keys[0]), new("job-start", keys[1]), new("job-cancel", keys[0]),
            new("job-result", $"{keys[1]}:successor")]), "Runner applied a duplicate or stale job result");
        await Empty(runner);

        var throwing = await Begin("throws", "fragile");
        var spoiled = await Begin("refused", "fragile");
        var dies = await Begin("turn-poison", "fragile");
        var lethal = await Begin("job-poison", "fragile");
        await driver.Start([(scope, throwing), (scope, spoiled), (scope, dies), (scope, lethal)]);
        await driver.Pass(6);
        trace.Add("Fragile stages journal and self-send effects, then throws or has the row refused");
        await Input(throwing, "throw", "throw");
        await Input(spoiled, "spoil", "spoil");
        await driver.Pass(8);
        Require(refused > 0, "the real store refusal hook did not see the spoiled state");
        await Parked(throwing, "the turn threw");
        await Parked(spoiled, "was refused");
        Require((await Journal(healthy)).SequenceEqual([new("baseline")]),
            "failure escaped its actor and changed the bystander");

        trace.Add("Fragile loses real owners with a suspended turn");
        await Input(dies, "die", "die");
        await driver.Pass(8);
        Require(work.Turns.Count == 1, "the turn poison witness needs an active first invocation");
        await Exhaust(dies, "turn");
        await Parked(dies, "on trial when 3 owners were lost");

        trace.Add("Fragile loses real owners with a background attempt");
        await Input(lethal, "dierun", "dierun");
        await driver.Pass(8);
        Require(work.Attempts(lethal, "lethal").Length == 1, "the job poison witness needs an active first invocation");
        await Exhaust(lethal, "job");
        await Parked(lethal, "on trial when 3 owners were lost", new WorkEntry("job-armed"));
        var expired = work.Attempts(lethal, "lethal");
        Require(work.Turns.Count == maxAttempts + 1 && expired.Length == maxAttempts + 1 &&
            expired.All(attempt => attempt.Token.IsCancellationRequested) &&
            expired.Select(attempt => attempt.Job.AttemptId).Distinct().Count() == expired.Length,
            "the poison witness did not lose distinct real attempts at the configured limit");
        foreach (var turn in work.Turns) turn.SetResult();
        foreach (var attempt in expired) attempt.Complete("late result");
        await driver.Pass(8);
        await Parked(dies, "on trial when 3 owners were lost");
        await Parked(lethal, "on trial when 3 owners were lost", new WorkEntry("job-armed"));
        await Input(healthy, "ping", "ping");
        await driver.Pass(6);
        Require((await View(healthy)).Header.Status == Contract.ActorStatus.Running &&
            (await Journal(healthy)).SequenceEqual([new("baseline"), new("pong")]), "the bystander stopped during poison isolation");

        var fanout = random.Next(2, 4);
        var root = await Begin("tree", "tree", new TreeInput(2, fanout));
        var family = new List<(Contract.ActorRef Actor, Contract.ActorRef? Parent)> { (root, null) };
        var level = new[] { root };
        var onOtherOwner = false;
        await driver.Start([(scope, root)]);
        for (var depth = 0; depth < 2; depth++)
        {
            await driver.Pass(6);
            var next = new List<Contract.ActorRef>();
            foreach (var parent in level)
            {
                var spawned = (await Journal(parent)).Where(entry => entry.Action == "tree-child")
                    .Select(entry => new Contract.ActorRef(entry.Detail!)).ToArray();
                Require(spawned.Length == fanout && spawned.Distinct().Count() == fanout, "tree did not spawn distinct children");
                family.AddRange(spawned.Select(child => (child, (Contract.ActorRef?)parent)));
                next.AddRange(spawned);
            }
            onOtherOwner |= await driver.StartElsewhere(next.Select(actor => (scope, actor)).ToArray()) > 0;
            level = next.ToArray();
        }
        Require(onOtherOwner, "tree witness needs descendants running on another owner");
        await driver.Pass(8);
        foreach (var leaf in level)
            Require(work.Attempts(leaf, "hold").Length == 1, "a leaf did not start its background attempt");
        await Event("before-end");
        await driver.Pass(8);
        foreach (var leaf in level)
            Require((await Journal(leaf)).SequenceEqual([new("tree-leaf"), new("tree-event")]), "a live leaf did not receive exactly one event");

        trace.Add($"tree of {family.Count} actors takes over with live background jobs");
        await driver.TakeOver();
        await driver.Pass(12);
        var retrying = level.SelectMany(leaf => work.Attempts(leaf, "hold").Skip(1)).ToArray();
        Require(retrying.Length > 0 && retrying.All(attempt => !attempt.Token.IsCancellationRequested),
            "the root must encounter an already running recovered trial");
        var selection = await View(root);
        var authorized = await runtime.TerminateAsync(scope, "end-tree", selection);
        Require(authorized.Outcome == Contract.AdmissionOutcome.Accepted, "public root terminate was refused");
        await driver.Pass(20);
        Require(retrying.Any(attempt => attempt.Token.IsCancellationRequested) &&
            retrying.All(attempt => !attempt.Result.Task.IsCompleted) && clears == 0 &&
            (await View(root)).Header.Status == Contract.ActorStatus.Running,
            "the trial did not signal the recovered bodies and keep the gate until they exit");
        if (treeStall) return; // The uncooperative bodies remain in flight under the original gate.

        ExitSignalled();
        trace.Add("the root termination answer is lost before the descendants settle");
        await driver.LoseTurnCommit(landed: true, stopAfterLoss: true);
        Require((await View(root)).Header.Status == Contract.ActorStatus.Cancelled, "the lost root answer did not land");
        var queuedChildren = 0;
        foreach (var (actor, parent) in family.Where(node => node.Parent == root))
            if ((await driver.Reads.InboxAsync(scope, actor)).Items.Any(row => row.Kind == Contract.Kinds.Terminate)) queuedChildren++;
        Require(queuedChildren > 0, "the takeover witness needs a committed cascade still in flight");
        await driver.TakeOver();
        for (var pass = 0; pass < 20; pass++)
        {
            ExitSignalled();
            await driver.Pass(4);
        }
        Require(clears > 0, "the released job's actual exit did not lead to a fenced trial clear");
        for (var node = 0; node < family.Count; node++)
        {
            var actor = family[node].Actor;
            var view = await View(actor);
            var pending = (await driver.Reads.InboxAsync(scope, actor)).Items;
            trace.Add($"tree actor {node}: {view.Header.Status}, {view.Header.Fault}; pending {string.Join(",", pending.Select(row => $"{row.Kind}:{row.Attempts}"))}; leaf entries {string.Join(",", work.Attempts(actor, "hold").Select(attempt => attempt.Token.IsCancellationRequested))}");
        }
        foreach (var leaf in level)
        {
            var leafAttempts = work.Attempts(leaf, "hold");
            Require(leafAttempts.All(attempt => attempt.Token.IsCancellationRequested || attempt.Result.Task.IsCompleted),
                "nested cancellation left a leaf job running");
            foreach (var attempt in leafAttempts) attempt.Complete("late tree result");
        }

        await Event("after-end");
        foreach (var (actor, parent) in family)
        {
            var view = await View(actor);
            Require(view.Header.Status == Contract.ActorStatus.Cancelled && view.Header.Parent == parent,
                "tree termination did not cancel the exact live descendant");
            await Empty(actor);
        }
        await driver.Pass(8);
        foreach (var leaf in level)
            Require((await Journal(leaf)).SequenceEqual([new("tree-leaf"), new("tree-event")]),
                "a cancelled leaf received an event or a stale job result");

        void ExitSignalled()
        {
            // The model bodies can take time to unwind after seeing cancellation.
            foreach (var attempt in level.SelectMany(leaf => work.Attempts(leaf, "hold")).Where(attempt => attempt.Token.IsCancellationRequested))
                attempt.Complete("late tree result");
        }

        async Task<Contract.ActorRef> Begin(string key, string model, object? input = null)
        {
            var accepted = await runtime.StartAsync(scope, $"start-{key}", new(model, model, input is null ? null : Json.Write(input)), key);
            Require(accepted.Outcome == Contract.AdmissionOutcome.Accepted && accepted.Actor is not null, $"public creator {key} was refused");
            return accepted.Actor!.Value;
        }

        async Task Input(Contract.ActorRef actor, string id, string kind, string? payload = null)
        {
            var accepted = await driver.Ingress.SendAsync(scope, new Contract.Target.Ref(actor), id, kind,
                payload is null ? null : Json.Write(payload));
            Require(accepted.Outcome == Contract.AdmissionOutcome.Accepted, $"public input {id} was refused");
        }

        async Task Event(string id)
        {
            var accepted = await driver.Ingress.SendAsync(scope, new Contract.Target.Event(treeEvent), id, Contract.Kinds.Event);
            Require(accepted.Outcome == Contract.AdmissionOutcome.Accepted, $"public event {id} was refused");
        }

        async Task<Contract.ActorView> View(Contract.ActorRef actor) =>
            await driver.Reads.GetAsync(scope, actor) ?? throw new InvalidOperationException("no actor view after its creator settled");

        Task<List<WorkEntry>> Journal(Contract.ActorRef actor) => Entries<WorkEntry>(driver, scope, actor, nameof(WorkEntry.Action));

        async Task Empty(Contract.ActorRef actor) =>
            Require((await driver.Reads.InboxAsync(scope, actor)).Items.Count == 0, "a settled actor has pending input");

        async Task Exhaust(Contract.ActorRef actor, string kind)
        {
            // The first owner starts fresh. Its loss establishes a suspect without charging a trial.
            for (var loss = 1; loss <= maxAttempts + 1; loss++)
            {
                await driver.TakeOver();
                await driver.Pass(8);
                var parked = (await View(actor)).Park is not null;
                var inbox = (await driver.Reads.InboxAsync(scope, actor)).Items;
                trace.Add($"{kind} lost owner {loss}: parked={parked}, pending attempts {string.Join(",", inbox.Select(row => row.Attempts))}");
                if (loss <= maxAttempts)
                    Require(!parked && inbox is [{ Attempts: var attempts }] && attempts == loss - 1,
                        $"Fragile {kind} charged a loss outside its marked trial");
            }
        }

        async Task Parked(Contract.ActorRef actor, string fault, params WorkEntry[] committed)
        {
            var view = await View(actor);
            Require(view is { Park: not null, Header.Status: Contract.ActorStatus.Faulted } &&
                view.Header.Fault?.Contains(fault, StringComparison.Ordinal) == true,
                $"Fragile failed to park with {fault}; observed {view.Header.Status}, {view.Header.Fault}");
            Require((await Journal(actor)).SequenceEqual(new WorkEntry[] { new("baseline") }.Concat(committed)),
                "a failed turn committed partial effects or applied a job result");
            await Empty(actor);
        }
    }

    private sealed class WorkBodies
    {
        internal List<TaskCompletionSource> Turns { get; } = [];
        private readonly List<WorkAttempt> _jobs = [];

        internal WorkAttempt[] Attempts(Contract.ActorRef actor, string key) =>
            _jobs.Where(attempt => attempt.Job.Actor == actor && attempt.Job.Key == key).ToArray();

        internal ValueTask<object?> Enter(Contract.IJob job, CancellationToken token)
        {
            var attempt = new WorkAttempt(job, token);
            _jobs.Add(attempt);
            return new(attempt.Result.Task);
        }
    }

    private sealed class WorkAttempt(Contract.IJob job, CancellationToken token)
    {
        internal Contract.IJob Job { get; } = job;
        internal CancellationToken Token { get; } = token;
        internal TaskCompletionSource<object?> Result { get; } = new();
        internal void Complete(string value) => Result.TrySetResult(value);
    }

    private abstract class WorkModel<T>(string key, WorkBodies work) : Models.IExecutionModel where T : class, new()
    {
        protected WorkBodies Work { get; } = work;
        public string Key => key;
        public Type StateType => typeof(T);
        public int StateVersion => 1;
        public virtual object CreateState(Models.WorkflowStart start) => new T();
        public object UpgradeState(string state, int fromVersion) => Json.Read<T>(state);
        public long TerminalGrowth(Models.InstanceInfo instance, object state) => 0;
        public Contract.RestorePlan RestoreWaits(Models.InstanceInfo instance, object state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => Work.Enter(job, cancellationToken);
        public abstract ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken);
    }

    private sealed class PublicRunner(WorkBodies work) : WorkModel<RunnerState>("runner", work)
    {
        public override ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken)
        {
            var log = ((RunnerState)turn.State).Log;
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.JobFinished)
                {
                    var finished = Json.Read<Contract.JobFinished>(message.Payload!);
                    log.Add("result " + finished.Key);
                    turn.Record(new WorkEntry("job-result", $"{finished.Key}:{(finished.Result is { } result ? Json.Read<string>(result) : finished.Error)}"));
                    continue;
                }
                if (message.Payload is not { } payload) continue;
                var key = Json.Read<string>(payload);
                if (message.Kind == "go" && !log.Contains("start " + key))
                {
                    log.Add("start " + key);
                    turn.StartJob(key, key);
                    turn.Record(new WorkEntry("job-start", key));
                }
                else if (message.Kind == "cancel" && log.Contains("start " + key) && !log.Contains("result " + key) && !log.Contains("cancel " + key))
                {
                    log.Add("cancel " + key);
                    turn.CancelJob(key);
                    turn.Record(new WorkEntry("job-cancel", key));
                }
            }
            return default;
        }
    }

    private sealed class PublicFragile(WorkBodies work) : WorkModel<FragileState>("fragile", work)
    {
        public override ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken)
        {
            var state = (FragileState)turn.State;
            foreach (var message in turn.Inbox)
            {
                state.Hits++;
                switch (message.Kind)
                {
                    case Contract.Kinds.Start:
                        turn.Record(new WorkEntry("baseline"));
                        break;
                    case "ping":
                        turn.Record(new WorkEntry("pong"));
                        break;
                    case "dierun":
                        turn.StartJob("lethal");
                        turn.Record(new WorkEntry("job-armed"));
                        break;
                    case "throw":
                    case "spoil":
                    case "die":
                        turn.Record(new WorkEntry("partial"));
                        turn.Send(turn.Instance.Ref, "partial");
                        if (message.Kind == "throw") throw new InvalidOperationException("the turn threw");
                        if (message.Kind == "spoil") state.Note = Fragile.Sour;
                        if (message.Kind == "die")
                        {
                            var completion = new TaskCompletionSource();
                            Work.Turns.Add(completion);
                            return new(completion.Task);
                        }
                        break;
                    default:
                        turn.Record(new WorkEntry("unexpected", message.Kind));
                        break;
                }
            }
            return default;
        }
    }

    private sealed class PublicTree(WorkBodies work, string eventKey) : WorkModel<TreeState>("tree", work)
    {
        public override object CreateState(Models.WorkflowStart start)
        {
            var input = Json.Read<TreeInput>(start.Input!);
            return new TreeState { Depth = input.Depth, Fanout = input.Fanout };
        }

        public override ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken)
        {
            var spec = (TreeState)turn.State;
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start && !spec.Started)
                {
                    spec.Started = true;
                    if (spec.Depth > 0)
                    {
                        for (var i = 0; i < spec.Fanout; i++)
                        {
                            var child = turn.Start(new("tree", "tree", Json.Write(new TreeInput(spec.Depth - 1, spec.Fanout))),
                                i % 2 == 0 ? Contract.Placement.Spread : Contract.Placement.WithParent);
                            turn.Record(new WorkEntry("tree-child", child.Id));
                        }
                    }
                    else
                    {
                        turn.StartJob("hold");
                        turn.Subscribe(eventKey, "wait");
                        turn.Record(new WorkEntry("tree-leaf"));
                    }
                }
                else if (message.Kind == Contract.Kinds.Event)
                    turn.Record(new WorkEntry("tree-event"));
                else if (message.Kind == Contract.Kinds.JobFinished)
                {
                    var result = Json.Read<Contract.JobFinished>(message.Payload!).Result;
                    turn.Record(new WorkEntry("tree-job-result", result is null ? null : Json.Read<string>(result)));
                }
            }
            return default;
        }
    }
}
