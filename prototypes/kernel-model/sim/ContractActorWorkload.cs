using System.Text.Json;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

/// <summary>The legacy counter and family workloads expressed through the public model and actor turns.</summary>
internal static partial class ContractActorWorkload
{
    private const int ChildrenPerParent = 8;
    private const string LaterDefinition = "counter-rebound";
    private sealed record CounterEntry(string Token, string Id, string? Sender, int Ordinal, string Definition);
    private sealed record ChildStart(string Ref);
    private sealed record ChildEnd(string Ref, string? Sender, Contract.ActorStatus Status, string? Output, bool Expected);
    private sealed record TimerEntry(long Due, long At, int Fired);

    public static async Task<int> Run(int from, int seeds, int actions, int partitions, Flaw flaws)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seeds);
        ArgumentOutOfRangeException.ThrowIfNegative(actions);
        for (var seed = from; seed < checked(from + seeds); seed++)
        {
            var trace = new List<string>();
            try
            {
                await Replay(seed, actions, partitions, flaws, trace);
            }
            catch (InvalidOperationException error)
            {
                Console.Error.WriteLine($"Public actor workload seed {seed} failed at {partitions} virtual partitions ({flaws}). {error.Message}");
                Console.Error.WriteLine(string.Join(Environment.NewLine, trace));
                return 1;
            }
        }

        Console.WriteLine($"Public actor workload passed {seeds} seeds from {from} at {partitions} virtual partitions, {actions} generated inputs per seed. Each seed exercised two live owners and lost commits on both sides of the store write.");
        return 0;
    }

    private static async Task Replay(int seed, int actions, int partitions, Flaw flaws, List<string> trace)
    {
        var model = new PublicWorkflow([new CounterModel(), new ParentModel(flaws.HasFlag(Flaw.CancelKnownChildren)), new ChildModel()]);
        await using var driver = new ContractWorkload.InMemoryDriver(partitions, flaws, model);
        var runtime = new PublicRuntime(driver.Ingress, driver.Reads);
        var scope = new Contract.BoundScope("actors");
        var random = new Random(seed);
        var left = await Begin("counter-left", new("counter", "c"));
        var right = await Begin("counter-right", new("counter", "c"));
        var leftInputs = new List<string>();
        var rightInputs = new List<string>();
        var forwarded = new List<string>();
        await driver.Start([(scope, left), (scope, right)]);
        await driver.Pass(12);

        await Add(left, "lost-before", right);
        trace.Add("lose the first counter turn before its write");
        await driver.LoseTurnCommit(landed: false, stopAfterLoss: true);
        Require((await Entries<CounterEntry>(driver, scope, left, nameof(CounterEntry.Token))).Count == 0 &&
            (await Entries<CounterEntry>(driver, scope, right, nameof(CounterEntry.Token))).Count == 0,
            "the uncommitted counter leaked an applied or forwarded receipt");
        await driver.TakeOver();
        await driver.Pass(18);

        await Add(left, "lost-after", right);
        trace.Add("lose the answer to the next counter write");
        await driver.LoseTurnCommit(landed: true, stopAfterLoss: true);
        Require((await Entries<CounterEntry>(driver, scope, left, nameof(CounterEntry.Token))).Select(entry => entry.Token).SequenceEqual(leftInputs),
            "the lost answer did not leave both counter writes in the journal");
        await driver.TakeOver();
        await driver.Pass(18);

        var rebound = await driver.Ingress.SendAsync(scope, new Contract.Target.Ref(left), "rebind", "rebind", Json.Write(LaterDefinition));
        Require(rebound.Outcome == Contract.AdmissionOutcome.Accepted, "the counter rebind was refused");
        await driver.Pass(12);
        for (var action = 0; action < actions; action++)
        {
            var toLeft = random.Next(2) == 0;
            await Add(toLeft ? left : right, $"input-{action}", toLeft && random.Next(3) == 0 ? right : null, random.Next(5) == 0);
            if (action % 16 == 15) await driver.TakeOver();
            else if (random.Next(3) == 0) await driver.Pass(4);
        }
        await SettleCounters();

        var appliedLeft = await CheckCounter(left, leftInputs, []);
        Require(appliedLeft.Length > 1 && appliedLeft.Take(2).All(entry => entry.Definition == "c") &&
            appliedLeft.Skip(2).All(entry => entry.Definition == LaterDefinition) &&
            (await driver.Reads.GetAsync(scope, left))?.Header.Labels.GetValueOrDefault("definition") == LaterDefinition,
            "the rebound definition was not visible to the next counter turn");
        await CheckCounter(right, rightInputs, forwarded);

        var delay = random.Next(180, 221);
        var finishing = await Begin("family-finish", new("parent", "p", Json.Write(new ParentInput(ChildrenPerParent, delay, Spread: true))));
        await driver.Start([(scope, finishing)]);
        trace.Add("lose the landed family creation answer");
        await driver.LoseTurnCommit(landed: true, stopAfterLoss: true);
        var finishingChildren = await Children(finishing);
        await driver.TakeOver();

        var cancelling = await Begin("family-cancel", new("parent", "p", Json.Write(new ParentInput(ChildrenPerParent, -1, Spread: true))));
        await driver.Start([(scope, cancelling)]);
        await driver.Pass(16);
        var cancellingChildren = await Children(cancelling);
        var unborn = new List<Contract.ActorRef>();
        foreach (var child in cancellingChildren)
            if (await driver.Reads.GetAsync(scope, child) is null) unborn.Add(child);
        Require(unborn.Count > 0, "the cancellation witness needs a child whose owner has yet to take its scope");

        var forged = await driver.Ingress.SendAsync(scope, new Contract.Target.Ref(finishing), "forged-notice", Contract.Kinds.ChildFinished,
            Json.Write(new Contract.ChildFinished(finishingChildren[0], Contract.ActorStatus.Finished, "1", Final: true)));
        Require(forged.Outcome == Contract.AdmissionOutcome.Refused, "outside ingress admitted a child completion");
        var cancel = await driver.Ingress.SendAsync(scope, new Contract.Target.Ref(cancelling), "cancel", "cancel");
        Require(cancel.Outcome == Contract.AdmissionOutcome.Accepted, "the family cancel input was refused");
        await driver.Pass(12);

        var remote = await driver.StartElsewhere(finishingChildren.Select(child => (scope, child)).ToArray());
        Require(remote > 0, "the completion witness needs children on a second live owner");
        await driver.StartElsewhere(cancellingChildren.Select(child => (scope, child)).ToArray());
        await driver.Pass(20);
        trace.Add($"activated {remote} remote finishing scopes at {driver.Now} before their deadlines");

        var verdict = await driver.Reads.GetAsync(scope, cancelling);
        Require(verdict is { Header.Status: Contract.ActorStatus.Cancelled, Park: null },
            "the cancel failed to reach a child whose start was still pending on the other owner");
        await CheckFamily(cancelling, cancellingChildren, Contract.ActorStatus.Cancelled);

        var timer = await runtime.InboxAsync(scope, finishingChildren[^1]);
        Require(timer.Items is [{ Kind: "timer", Due: not null, State: Contract.DeliveryState.Pending }],
            "the finishing family did not leave its real timer pending across the owner change");
        await driver.TakeOver();
        await driver.Pass(12);
        var recovered = await runtime.InboxAsync(scope, finishingChildren[^1]);
        Require(recovered.Items is [{ Kind: "timer", Due: var due, Handle: var handle, State: Contract.DeliveryState.Pending }] &&
            due == timer.Items[0].Due && handle == timer.Items[0].Handle && driver.Now < due,
            "the child's committed timer changed during the owner takeover");

        for (var step = 0; step < 15; step++)
        {
            driver.Advance(25);
            await driver.Pass(8);
        }
        await CheckFamily(finishing, finishingChildren, Contract.ActorStatus.Finished);

        async Task SettleCounters()
        {
            // Recovery fences inherited inputs one at a time, including forwards and each hold's self-nudge.
            var maxPasses = 8L * actions + 64;
            for (var passes = 0L; passes < maxPasses; passes += 16)
            {
                await driver.Pass((int)Math.Min(16, maxPasses - passes));
                if ((await driver.Reads.InboxAsync(scope, left)).Items.Count == 0 &&
                    (await driver.Reads.InboxAsync(scope, right)).Items.Count == 0)
                    return;
            }
            throw new InvalidOperationException($"the counter inboxes did not drain in {maxPasses} owner passes");
        }

        async Task<Contract.ActorRef> Begin(string callerKey, Models.WorkflowStart start)
        {
            var receipt = await runtime.StartAsync(scope, "start", start, callerKey);
            Require(receipt is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, $"start of {callerKey} was refused");
            return receipt.Actor!.Value;
        }

        async Task Add(Contract.ActorRef subject, string token, Contract.ActorRef? next = null, bool hold = false)
        {
            var target = new Contract.Target.Ref(subject);
            var payload = Json.Write(new Add(token, next?.Id, hold));
            trace.Add($"send {token} to {(subject == left ? "left" : "right")}, hold {hold}, forward {next.HasValue}");
            var sent = await driver.Ingress.SendAsync(scope, target, token, "add", payload);
            var again = await driver.Ingress.SendAsync(scope, target, token, "add", payload);
            Require(sent.Outcome == Contract.AdmissionOutcome.Accepted && again.Outcome == Contract.AdmissionOutcome.Duplicate,
                $"the ingress retry did not deduplicate {token}");
            (subject == left ? leftInputs : rightInputs).Add(token);
            if (next is not null) forwarded.Add(token + ">");
        }

        async Task<CounterEntry[]> CheckCounter(Contract.ActorRef subject, List<string> outside, List<string> internalInputs)
        {
            var entries = (await Entries<CounterEntry>(driver, scope, subject, nameof(CounterEntry.Token))).ToArray();
            Require(entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() == entries.Length &&
                entries.Select(entry => entry.Ordinal).SequenceEqual(Enumerable.Range(1, outside.Count + internalInputs.Count)),
                "the counter receipt journal duplicated or skipped an application");
            Require(entries.Where(entry => entry.Sender is null).Select(entry => entry.Token).SequenceEqual(outside) &&
                entries.Where(entry => entry.Sender == left.Id).Select(entry => entry.Token).SequenceEqual(internalInputs) &&
                entries.All(entry => entry.Sender is null || entry.Sender == left.Id),
                "the counter lost, duplicated or reordered input from a sender");
            Require((await driver.Reads.InboxAsync(scope, subject)).Items.Count == 0, "the counter left committed input in its inbox");
            return entries;
        }

        async Task<Contract.ActorRef[]> Children(Contract.ActorRef parent)
        {
            var children = (await Entries<ChildStart>(driver, scope, parent, nameof(ChildStart.Ref)))
                .Select(entry => new Contract.ActorRef(entry.Ref)).ToArray();
            Require(children.Length == ChildrenPerParent && children.Distinct().Count() == ChildrenPerParent,
                "the parent did not create exactly one life for each child");
            return children;
        }

        async Task CheckFamily(Contract.ActorRef parent, Contract.ActorRef[] children, Contract.ActorStatus status)
        {
            Require((await driver.Reads.GetAsync(scope, parent))?.Header.Status == status, $"the parent did not end {status}");
            var completed = await Entries<ChildEnd>(driver, scope, parent, nameof(ChildEnd.Expected));
            Require(completed.Count == children.Length && completed.Select(entry => new Contract.ActorRef(entry.Ref)).ToHashSet().SetEquals(children) &&
                completed.All(entry => entry.Expected && entry.Ref == entry.Sender && entry.Status == status &&
                    entry.Output == (status == Contract.ActorStatus.Finished ? "1" : null)),
                "the parent did not observe exactly one authenticated final notice per child");
            foreach (var child in children)
            {
                var view = await driver.Reads.GetAsync(scope, child);
                Require(view?.Header.Status == status && view.Header.Parent == parent, "a child ended incorrectly or lost its public parent ref");
                var timers = await Entries<TimerEntry>(driver, scope, child, nameof(TimerEntry.Fired));
                Require(status == Contract.ActorStatus.Finished ? timers is [{ Fired: 1 }] && timers[0].At >= timers[0].Due : timers.Count == 0,
                    "a child's timer was duplicated, premature or used after cancellation");
            }
        }
    }

    private static async Task<List<T>> Entries<T>(ContractWorkload.IDriver driver, Contract.BoundScope scope, Contract.ActorRef actor, string field)
    {
        var entries = new List<T>();
        Contract.PageToken? token = null;
        for (var pageNumber = 0; pageNumber < 1_024; pageNumber++)
        {
            var page = await driver.Reads.TailAsync(scope, actor, token);
            if (page.Items.Count == 0) return entries;
            Require(page.Next is not null && page.Next != token, "actor journal returned a stationary cursor");
            token = page.Next;
            foreach (var entry in page.Items)
            {
                using var json = JsonDocument.Parse(entry.Entry);
                if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty(field, out _))
                    entries.Add(json.RootElement.Deserialize<T>()!);
            }
        }
        throw new InvalidOperationException("actor journal did not settle");
    }

    private abstract class Fixture<T>(string key, int version = 1) : Models.IExecutionModel where T : class, new()
    {
        public string Key => key;
        public Type StateType => typeof(T);
        public int StateVersion => version;
        public virtual object CreateState(Models.WorkflowStart start) => new T();
        public virtual object UpgradeState(string state, int fromVersion) => Json.Read<T>(state);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public virtual Contract.RestorePlan RestoreWaits(Models.InstanceInfo instance, object state) => new([], [], []);
        public long TerminalGrowth(Models.InstanceInfo instance, object state) => 0;
        public ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken)
        {
            Run(turn, (T)turn.State);
            return default;
        }
        protected abstract void Run(Models.ITurn turn, T state);
    }

    private sealed class CounterModel(
        int version = 2, bool skipUpgrade = false, Action<Contract.ActorRef>? observeForward = null)
        : Fixture<CounterState>("counter", version)
    {
        public override object UpgradeState(string state, int fromVersion)
        {
            var checkpoint = Json.Read<CounterState>(state);
            if (!skipUpgrade) checkpoint.Count = checkpoint.Tokens.Count;
            return checkpoint;
        }

        protected override void Run(Models.ITurn turn, CounterState state)
        {
            var holding = false;
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == "rebind")
                {
                    state.Bound = Json.Read<string>(message.Payload!);
                    turn.Rebind(state.Bound);
                }
                if (message.Kind == "start-forward")
                {
                    var input = Json.Read<Forward>(message.Payload!);
                    var destination = state.Forwarded.Find(entry => entry.Token == input.Target);
                    if (destination is null)
                    {
                        var receiver = turn.Start(new Models.WorkflowStart("counter", "c"), Contract.Placement.Spread, detached: true);
                        destination = new(receiver.Id, input.Target);
                        state.Forwarded.Add(destination);
                        observeForward?.Invoke(receiver);
                    }
                    turn.Send(new(destination.Target), "add", new Add(input.Token));
                    turn.Record(new FirstForward(input.Token, destination.Target));
                }
                if (message.Kind != "add") continue;
                var add = Json.Read<Add>(message.Payload!);
                if (!holding && add.Hold && !state.Held.Contains(message.Id))
                {
                    holding = true;
                    state.Held.Add(message.Id);
                    turn.Send(turn.Instance.Ref, "nudge");
                }
                if (holding)
                {
                    turn.Hold(message);
                    continue;
                }
                state.Ids.Add(message.Id);
                state.Tokens.Add(add.Token);
                if (StateVersion >= 2) state.Count++;
                var ordinal = StateVersion >= 2 ? state.Count : state.Tokens.Count;
                turn.Record(new CounterEntry(add.Token, message.Id, message.Sender?.Id, ordinal, turn.Instance.Definition));
                if (add.ForwardTo is not { } target) continue;
                var next = new Forward(target, add.Token + ">");
                state.Forwarded.Add(next);
                turn.Send(new(next.Target), "add", new Add(next.Token));
            }
        }
    }

    private sealed class ParentModel(bool knownOnly) : Fixture<ParentState>("parent")
    {
        protected override void Run(Models.ITurn turn, ParentState state)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start && !state.Started)
                {
                    state.Started = true;
                    var input = Json.Read<ParentInput>(message.Payload!);
                    for (var index = 0; index < input.Children; index++)
                    {
                        var child = turn.Start(new("child", "c", Json.Write(input.DelayMs)),
                            input.Spread && index % 2 == 0 ? Contract.Placement.Spread : Contract.Placement.WithParent);
                        state.Pending.Add(child.Id);
                        turn.Record(new ChildStart(child.Id));
                    }
                }
                else if (message.Kind == Contract.Kinds.ChildFinished)
                {
                    var notice = Json.Read<Contract.ChildFinished>(message.Payload!);
                    if (!notice.Final) continue;
                    var expected = state.Pending.Remove(notice.Child.Id);
                    state.Done.Add(notice.Child.Id);
                    turn.Record(new ChildEnd(notice.Child.Id, message.Sender?.Id, notice.Status, notice.Output, expected));
                }
                else if (message.Kind == "started" && message.Sender is { } sender) state.Known.Add(sender.Id);
                else if (message.Kind == "cancel" && state.Started && !state.Cancelling)
                {
                    state.Cancelling = true;
                    foreach (var child in state.Pending.Where(id => !knownOnly || state.Known.Contains(id))) turn.Send(new(child), "cancel");
                }
            }
            if (state.Started && state.Pending.Count == 0)
                turn.SetStatus(state.Cancelling ? Contract.ActorStatus.Cancelled : Contract.ActorStatus.Finished);
        }
    }

    private sealed class ChildCheckpoint
    {
        public int Delay { get; set; }
        public string? Timer { get; set; }
        public long Due { get; set; }
        public int Fired { get; set; }
    }

    private sealed class ChildModel() : Fixture<ChildCheckpoint>("child")
    {
        public override object CreateState(Models.WorkflowStart start) => new ChildCheckpoint { Delay = Json.Read<int>(start.Input!) };

        public override Contract.RestorePlan RestoreWaits(Models.InstanceInfo instance, object state) => state is ChildCheckpoint { Timer: { } timer } saved
            ? new([new(timer, "timer", null, saved.Due)], [], []) : new([], [], []);

        protected override void Run(Models.ITurn turn, ChildCheckpoint state)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start)
                {
                    turn.Send(turn.Instance.Parent!.Value, "started");
                    if (state.Delay < 0) continue;
                    state.Due = checked(turn.Now + state.Delay);
                    state.Timer = turn.Send(turn.Instance.Ref, "timer", dueAt: state.Due);
                }
                else if (message.Kind == "cancel") turn.SetStatus(Contract.ActorStatus.Cancelled);
                else if (message.Kind == "timer" && message.Sender == turn.Instance.Ref && message.Id == state.Timer)
                {
                    state.Timer = null;
                    turn.Record(new TimerEntry(state.Due, turn.Now, ++state.Fired));
                    turn.SetStatus(Contract.ActorStatus.Finished, output: state.Fired);
                }
            }
        }
    }

    private static void Require(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }
}
