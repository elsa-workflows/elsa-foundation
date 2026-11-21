using System.Text.Json;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

/// <summary>Sequence, subscription-table waits, actor correlation and event-created actors on the public turn.</summary>
internal static class ContractEventWorkload
{
    private const string WaitName = "w";
    private const string CorrelatedEvent = "correlation-event";
    private sealed record Step(int Number);
    private sealed record EventSeen(string Key, string Id, string Wait, string? Payload, string? Sender = null);
    private sealed record WaitInput(string[] Keys, bool Keep);
    private sealed class WaitCheckpoint
    {
        public WaitInput Input { get; set; } = new([], false);
        public bool Listening { get; set; }
    }

    private sealed class ActorWait
    {
        public Contract.ActorRef[] Topics { get; set; } = [];
        public bool Listening { get; set; }
    }

    public static async Task<int> Run(int from, int seeds, int partitions, Flaw flaws)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seeds);
        for (var seed = from; seed < checked(from + seeds); seed++)
        {
            var trace = new List<string>();
            try
            {
                await new Schedule(seed, partitions, flaws, trace).Run();
            }
            catch (InvalidOperationException error)
            {
                Console.Error.WriteLine($"Public event and sequence workload seed {seed} failed at {partitions} virtual partitions ({flaws}). {error.Message}");
                Console.Error.WriteLine(string.Join(Environment.NewLine, trace));
                return 1;
            }
        }

        Console.WriteLine($"Public event and sequence workload passed {seeds} seeds from {from} at {partitions} virtual partitions. Each seed ran Sequence, Waiter, Correlation, CWaiter and OnEvent.");
        return 0;
    }

    private sealed class Schedule(int seed, int partitions, Flaw flaws, List<string> trace)
    {
        private readonly Random _random = new(seed);
        private readonly Contract.BoundScope _scope = new("events");
        private readonly ContractWorkload.InMemoryDriver _driver = new(partitions, flaws, new PublicWorkflow([
            new Model<SequenceState>("sequence", SequenceStart, SequenceTurn),
            new Model<WaitCheckpoint>("waiter", start => new() { Input = Json.Read<WaitInput>(start.Input!) }, WaiterTurn, WaiterRestore),
            new Model<CorrelationState>("correlation", _ => new(), (turn, state) => CorrelationTurn(turn, state, flaws.HasFlag(Flaw.CorrelationForgets))),
            new Model<ActorWait>("cwaiter", start => new() { Topics = Json.Read<Contract.ActorRef[]>(start.Input!) }, CWaiterTurn),
            new Model<List<EventSeen>>("onevent", _ => [], OnEventTurn)
        ]));

        internal async Task Run()
        {
            await using var driver = _driver;
            var historic = await Begin("topic-history", new("correlation", "key"));
            var live = await Begin("topic-live", new("correlation", "key"));
            await driver.Start([(_scope, historic), (_scope, live)]);
            await driver.Pass(12);
            await Sequences();
            await FoundationWaiters();
            await ActorCorrelation(historic, live);
            await EventBirths();
        }

        private async Task Sequences()
        {
            foreach (var mode in new[] { Sequence.Balanced, Sequence.Durable })
            {
                var count = _random.Next(8, 19);
                var durable = mode == Sequence.Durable;
                trace.Add($"start {mode} sequence of {count} activities and lose its first {(durable ? "landed answer" : "write")}");
                var actor = await Begin(mode, new("sequence", "s", Json.Write(new SequenceInput(count, mode))));
                await Take(actor);
                await _driver.LoseTurnCommit(landed: durable, stopAfterLoss: true);
                var atFault = await Journal<Step>(actor, nameof(Step.Number));
                var view = await _driver.Reads.GetAsync(_scope, actor);
                if (durable)
                {
                    Require(atFault.Select(entry => entry.Value.Number).SequenceEqual([1]) &&
                        view is { Header.Status: Contract.ActorStatus.Running } &&
                        (await _driver.Reads.InboxAsync(_scope, actor)).Items is [{ Kind: "continue" }],
                        "the durable sequence did not leave one committed activity and one continuation");
                }
                else
                {
                    // A creator is private until its first checkpoint. The outcome must stay invisible here,
                    // and this very ref must finish after takeover without another start.
                    Require(atFault.Count == 0 && view is null, "the lost balanced write exposed an outcome before it landed");
                }
                await _driver.TakeOver();
                await _driver.Pass(count + 12);
                var done = await Journal<Step>(actor, nameof(Step.Number));
                Require(done.Select(entry => entry.Value.Number).SequenceEqual(Enumerable.Range(1, count)),
                    $"the {mode} sequence duplicated, skipped or reordered an activity");
                Require(done.Select(entry => entry.Revision).Distinct().Count() == (durable ? count : 1) &&
                    (await _driver.Reads.GetAsync(_scope, actor)) is { Header.Status: Contract.ActorStatus.Finished },
                    $"the {mode} sequence violated its commit boundaries");
                Require((await _driver.Reads.InboxAsync(_scope, actor)).Items.Count == 0,
                    $"the {mode} sequence left committed input in the public inbox");
            }
        }

        private async Task FoundationWaiters()
        {
            trace.Add("publish first and compare a keeping waiter with a live-only waiter");
            await PublishEvent("historic", "earlier", "original", keep: 1_000);
            var keys = new[] { "historic", "pulse" };
            var keeping = await Begin("wait-kept", new("waiter", "w", Json.Write(new WaitInput(keys, Keep: true))));
            var liveOnly = await Begin("wait-live", new("waiter", "w", Json.Write(new WaitInput(keys, Keep: false))));
            await _driver.Start([(_scope, keeping), (_scope, liveOnly)]);
            await _driver.Pass(22);
            await Seen(keeping, new("historic", "earlier", WaitName, "original"), "the keeping waiter missed its single catch-up");
            await Waiting(liveOnly, "the live-only waiter received a publication that predated its wait");

            await _driver.TakeOver();
            await PublishEvent("pulse", "first", "chosen");
            if (_random.Next(2) == 0) await _driver.Pass(8);
            await PublishEvent("pulse", "first", "changed", duplicate: true);
            await PublishEvent("pulse", "second", "too late");
            await _driver.Pass(20);
            await Seen(liveOnly, new("pulse", "first", WaitName, "chosen"), "the live waiter lost the first input's order or identity");

            await PublishEvent("pulse", "after-end", "no route");
            Require((await _driver.Reads.InboxAsync(_scope, keeping)).Items.Count == 0 &&
                (await _driver.Reads.InboxAsync(_scope, liveOnly)).Items.Count == 0,
                "a finished waiter retained a live route or a surplus delivery");
        }

        private async Task ActorCorrelation(Contract.ActorRef historic, Contract.ActorRef live)
        {
            var retention = _random.Next(160, 221);
            var expires = _driver.Now + retention;
            trace.Add($"store an event on the correlation actor until {expires}");
            await PublishActor(historic, "store-first", new("prior", "original", retention));
            await PublishActor(historic, "same-occurrence", new("prior", "changed", retention));
            await _driver.Pass(20);
            var keeping = await Begin("actor-catch-up", new("cwaiter", "w", Json.Write(new[] { historic, live })));
            var active = await Begin("actor-live", new("cwaiter", "w", Json.Write(new[] { live })));
            await _driver.Start([(_scope, keeping), (_scope, active)]);
            await _driver.Pass(22);
            await Seen(keeping, new("", "prior", WaitName, "original", historic.Id), "the correlation actor forgot an event before its waiter arrived");
            await Waiting(active, "the correlation waiter received an event from the wrong topic");

            await _driver.TakeOver();
            await PublishActor(live, "live-1", new("next", "first", 1_000));
            if (_random.Next(2) == 0) await _driver.Pass(8);
            await PublishActor(live, "live-2", new("next", "changed", 1_000));
            await PublishActor(live, "live-3", new("later", "second", 1_000));
            await _driver.Pass(22);
            await Seen(active, new("", "next", WaitName, "first", live.Id), "the live correlation waiter lost the first input's order or identity");

            while (_driver.Now <= expires)
            {
                _driver.Advance(20);
                await _driver.Pass(8);
            }
            var expired = await Begin("actor-expired", new("cwaiter", "w", Json.Write(new[] { historic })));
            await Take(expired);
            await _driver.Pass(22);
            await Waiting(expired, "the correlation waiter received an expired publication");
            await PublishActor(historic, "after-expiry", new("fresh", "right", 1_000));
            await _driver.Pass(22);
            await Seen(expired, new("", "fresh", WaitName, "right", historic.Id), "the waiter did not switch from expired catch-up to live delivery");
        }

        private async Task EventBirths()
        {
            trace.Add("register starts before and after publication, lose a landed birth answer, then remove a route");
            await Register("live-birth");
            await Register("live-birth");
            await PublishEvent("live-birth", "first", "trigger", keep: 1_000);
            await PublishEvent("live-birth", "first", "changed", keep: 1_000, duplicate: true);
            await _driver.StartPendingEventCreators();
            await _driver.LoseTurnCommit(landed: true, stopAfterLoss: true);
            await _driver.TakeOver();
            await _driver.Pass(20);
            await Birth("live-birth", "first", "trigger");

            await PublishEvent("late-birth", "kept", "earlier", keep: 1_000);
            await PublishEvent("absent-birth", "unkept", "unavailable");
            await Register("late-birth");
            await Register("late-birth");
            await Register("absent-birth");
            await _driver.StartPendingEventCreators();
            await _driver.Pass(20);
            await Birth("late-birth", "kept", "earlier");

            // A repeated registration still matches the retained publication after its first actor finished.
            await Register("live-birth");
            await Register("late-birth");
            await _driver.Ingress.SetStartAsync(_scope, "live-birth", "live-birth", null);
            await PublishEvent("live-birth", "removed", "no route");
            await _driver.StartPendingEventCreators();
            await _driver.Pass(20);
            await Birth("live-birth", "first", "trigger");
            await Birth("late-birth", "kept", "earlier");
            Require((await Births("absent-birth")).Items.Count == 0, "an unkept event created an actor when its start was registered later");
        }

        private ValueTask Register(string key) =>
            _driver.Ingress.SetStartAsync(_scope, key, key, PublicWorkflow.Start(new("onevent", key)));

        private ValueTask<Contract.Page<Contract.ActorView>> Births(string definition) => _driver.Reads.ListAsync(_scope,
            new(Labels: new Dictionary<string, string> { ["definition"] = definition }));

        private async Task Birth(string key, string id, string payload)
        {
            var page = await Births(key);
            Require(page is { Items: [{ Header.Status: Contract.ActorStatus.Finished }], Next: null },
                $"the event-start route {key} exposed {page.Items.Count} actors instead of one finished life");
            await Seen(page.Items[0].Header.Ref, new(key, id, key, payload), $"event-created actor {key} received the wrong delivery");
        }

        private async Task PublishEvent(string key, string id, string payload, int? keep = null, bool duplicate = false)
        {
            var options = keep is { } millis ? new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(millis)) : null;
            var admitted = await _driver.Ingress.SendAsync(_scope, new Contract.Target.Event(key), id, Contract.Kinds.Event, payload, options);
            Require(admitted.Outcome == (duplicate ? Contract.AdmissionOutcome.Duplicate : Contract.AdmissionOutcome.Accepted),
                $"event {key}/{id} was not {(duplicate ? "deduplicated" : "admitted")}");
        }

        private async Task PublishActor(Contract.ActorRef topic, string operation, Publish publication)
        {
            var receipt = await _driver.Ingress.SendAsync(_scope, new Contract.Target.Ref(topic), operation, "publish", Json.Write(publication));
            Require(receipt.Outcome == Contract.AdmissionOutcome.Accepted, $"correlation input {operation} was refused");
        }

        private async Task<Contract.ActorRef> Begin(string callerKey, Models.WorkflowStart spec)
        {
            var opened = await new PublicRuntime(_driver.Ingress, _driver.Reads).StartAsync(_scope, "start", spec, callerKey);
            Require(opened is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, $"start {callerKey} was refused");
            return opened.Actor!.Value;
        }

        private Task Take(Contract.ActorRef actor) => _driver.Start([(_scope, actor)]);

        private async Task Seen(Contract.ActorRef actor, EventSeen expected, string error)
        {
            var journal = await Journal<EventSeen>(actor, nameof(EventSeen.Id));
            Require(journal.Select(entry => entry.Value).SequenceEqual([expected]) &&
                await _driver.Reads.GetAsync(_scope, actor) is { Header.Status: Contract.ActorStatus.Finished, Park: null }, error);
        }

        private async Task Waiting(Contract.ActorRef actor, string error) =>
            Require((await Journal<EventSeen>(actor, nameof(EventSeen.Id))).Count == 0 &&
                await _driver.Reads.GetAsync(_scope, actor) is { Header.Status: Contract.ActorStatus.Running, Park: null }, error);

        private async Task<List<(long Revision, T Value)>> Journal<T>(Contract.ActorRef actor, string field)
        {
            var result = new List<(long, T)>();
            Contract.PageToken? token = null;
            for (var pageNumber = 0; pageNumber < 1_024; pageNumber++)
            {
                var page = await _driver.Reads.TailAsync(_scope, actor, token);
                if (page.Items.Count == 0) return result;
                Require(page.Next is not null && page.Next != token, "the public journal cursor did not advance");
                token = page.Next;
                foreach (var item in page.Items)
                {
                    using var json = JsonDocument.Parse(item.Entry);
                    if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty(field, out _))
                        result.Add((item.Revision, json.RootElement.Deserialize<T>()!));
                }
            }
            throw new InvalidOperationException("the public journal did not settle");
        }
    }

    private static SequenceState SequenceStart(Models.WorkflowStart start)
    {
        var input = Json.Read<SequenceInput>(start.Input!);
        return new() { Count = input.Count, Mode = input.Mode };
    }

    private static void SequenceTurn(Models.ITurn turn, SequenceState state)
    {
        foreach (var message in turn.Inbox)
        {
            if (message.Kind == Contract.Kinds.Start ? state.Began : message.Kind != "continue") continue;
            state.Began = true;
            do
            {
                var number = state.Done.Count + 1;
                state.Done.Add(number);
                turn.Record(new Step(number));
            } while (state.Mode != Sequence.Durable && state.Done.Count < state.Count);
            if (state.Done.Count < state.Count) turn.Send(turn.Instance.Ref, "continue");
            else turn.SetStatus(Contract.ActorStatus.Finished);
        }
    }

    private static void WaiterTurn(Models.ITurn turn, WaitCheckpoint state)
    {
        foreach (var message in turn.Inbox)
        {
            if (message.Kind == Contract.Kinds.Start)
            {
                state.Listening = true;
                foreach (var key in state.Input.Keys) turn.Subscribe(key, WaitName, state.Input.Keep);
            }
            else if (state.Listening && message is { Kind: Contract.Kinds.Event, Event: { } @event })
            {
                state.Listening = false;
                foreach (var key in state.Input.Keys) turn.Unsubscribe(key, WaitName);
                turn.Record(new EventSeen(@event.Key, @event.Id, @event.Wait, message.Payload));
                turn.SetStatus(Contract.ActorStatus.Finished);
            }
        }
    }

    private static Contract.RestorePlan WaiterRestore(WaitCheckpoint state) =>
        new([], [], state.Listening ? state.Input.Keys.Select(key => new Contract.SubscriptionSpec(key, WaitName, state.Input.Keep)).ToArray() : []);

    private static void CorrelationTurn(Models.ITurn turn, CorrelationState state, bool forgets)
    {
        foreach (var message in turn.Inbox)
        {
            switch (message.Kind)
            {
                case "subscribe" when Json.Read<Wait>(message.Payload!) is var wait && !state.Waits.Contains(wait):
                    state.Waits.Add(wait);
                    if (!forgets)
                        foreach (var item in state.Events) Deliver(wait, item);
                    break;
                case "unsubscribe":
                    state.Waits.Remove(Json.Read<Wait>(message.Payload!));
                    break;
                case "publish" when Json.Read<Publish>(message.Payload!) is var publication && state.Events.All(item => item.Id != publication.Id):
                    var kept = new Kept(publication.Id, publication.Payload);
                    state.Events.Add(kept);
                    foreach (var subscriber in state.Waits) Deliver(subscriber, kept);
                    turn.Send(turn.Instance.Ref, "expire", publication.Id, dueAt: checked(turn.Now + publication.KeepMs));
                    break;
                case "expire" when message.Sender == turn.Instance.Ref:
                    var id = Json.Read<string>(message.Payload!);
                    state.Events.RemoveAll(item => item.Id == id);
                    break;
            }
        }

        // This model's routing is an ordinary actor send. Event is reserved for the foundation's own delivery.
        void Deliver(Wait wait, Kept item) => turn.Send(new(wait.Instance), CorrelatedEvent, new EventSeen("", item.Id, wait.Name, item.Payload));
    }

    private static void CWaiterTurn(Models.ITurn turn, ActorWait state)
    {
        var me = new Wait(turn.Instance.Ref.Id, WaitName);
        foreach (var message in turn.Inbox)
        {
            if (message.Kind == Contract.Kinds.Start)
            {
                state.Listening = true;
                foreach (var topic in state.Topics) turn.Send(topic, "subscribe", me);
            }
            else if (state.Listening && message is { Kind: CorrelatedEvent, Sender: { } sender } && state.Topics.Contains(sender))
            {
                state.Listening = false;
                foreach (var topic in state.Topics) turn.Send(topic, "unsubscribe", me);
                turn.Record(Json.Read<EventSeen>(message.Payload!) with { Sender = sender.Id });
                turn.SetStatus(Contract.ActorStatus.Finished);
            }
        }
    }

    private static void OnEventTurn(Models.ITurn turn, List<EventSeen> state)
    {
        foreach (var message in turn.Inbox)
        {
            if (message is not { Kind: Contract.Kinds.Event, Event: { } @event }) continue;
            var value = new EventSeen(@event.Key, @event.Id, @event.Wait, message.Payload);
            state.Add(value);
            turn.Record(value);
        }
        // The public route has a Start row and a subsequent Event delivery.
        if (state.Count > 0) turn.SetStatus(Contract.ActorStatus.Finished);
    }

    private sealed class Model<T>(string key, Func<Models.WorkflowStart, T> create, Action<Models.ITurn, T> reduce,
        Func<T, Contract.RestorePlan>? restore = null) : Models.IExecutionModel where T : class
    {
        public string Key => key;
        public Type StateType => typeof(T);
        public int StateVersion => 1;
        public object CreateState(Models.WorkflowStart start) => create(start);
        public object UpgradeState(string state, int fromVersion) => Json.Read<T>(state);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Models.InstanceInfo instance, object state) => restore?.Invoke((T)state) ?? new([], [], []);
        public long TerminalGrowth(Models.InstanceInfo instance, object state) => 0;
        public ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken)
        {
            reduce(turn, (T)turn.State);
            return default;
        }
    }

    private static void Require(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }
}
