using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

internal static partial class BoundaryCases
{
    internal static readonly string[] Names = ["immutable-header", "unsupported-store", "legacy-v2-reader", "generation-exhaustion-reader", "semantic-start-envelope", "mixed-event-addresses"];
    internal static async Task Run()
    {
        await ImmutableHeader(); await Unsupported(); await Legacy(); await Exhaustion(); await StartEnvelope(); await MixedEvents();
    }

    private static void Require(bool ok, string detail)
    { if (!ok) throw new InvalidOperationException("C6: " + detail); }

    private static TurnCommit Write(InstanceRow row, InstanceRow? previous, bool delete = false) =>
        new(row.Id, row, previous?.Revision, [], [], [], [], [], "[]", Delete: delete);
    private static CommitBatch Batch(InMemoryStore store, TurnCommit turn) =>
        new(0, store.Partitions[0].Epoch, store.Partitions[0].Position, TimeSpan.FromSeconds(10), false, [turn], [], []);

    private static async Task Refused(InMemoryStore store, Func<ValueTask> operation, string detail)
    {
        var before = Json.Write(store.Instances); var position = store.Partitions[0].Position;
        var messages = store.Messages.Count; var subscriptions = store.Subscriptions.Count; var journal = store.Journal.Count;
        try { await operation(); throw new InvalidOperationException("C6: accepted " + detail); }
        catch (StoreException error) when (error.Failure == Failure.Definite) { }
        Require(Json.Write(store.Instances) == before && position == store.Partitions[0].Position && messages == store.Messages.Count &&
            subscriptions == store.Subscriptions.Count && journal == store.Journal.Count, "partial write while refusing " + detail);
    }

    private static async Task ImmutableHeader()
    {
        await using var h = new Harness(new Http(0, (_, _) => throw new InvalidOperationException("C6: unexpected job body"))); await h.Activate();
        await h.Send(Kinds.Start, start: Http.Spec(Http.Wait) with { Creation = new(1, 0, "header") }, creation: 1);
        var original = h.Row;
        Require(original is { Creation: 1, Status: InstanceStatus.Running }, "normal writer did not create qualified header");
        foreach (var invalid in new[] { original with { Creation = 2 }, original with { StartIntent = "replacement" },
            original with { ParentCreation = 1 }, original with { Parent = "0/forged-parent" }, original with { PendingStart = "forged" },
            original with { Retired = true }, CreationProtocol.Retire(original) })
        {
            var turn = Write(invalid, original, invalid.Retired);
            await Refused(h.Store, async () => { await h.Store.CommitAsync(Batch(h.Store, turn)); }, "forged immutable/retired header");
        }
        var bareDelete = new TurnCommit(h.Target.Id, null, original.Revision, [], [], [], [], [], "[]", Delete: true);
        await Refused(h.Store, async () => { await h.Store.CommitAsync(Batch(h.Store, bareDelete)); }, "qualified delete without high-water mark");
        await h.Send(Kinds.Terminate, creation: 1); await h.Send(Kinds.Delete, creation: 1);
        Require(h.Row is { Retired: true, State: "null", Model: "", Definition: "", StartIntent: null }, "normal retirement retained workflow content");
        var retired = h.Row;
        foreach (var invalid in new[] { original with { Revision = 1 }, original with { Creation = 3, Revision = 1 },
            retired with { State = "\"private payload\"" } })
            await Refused(h.Store, async () => { await h.Store.CommitAsync(Batch(h.Store, Write(invalid, retired))); }, "forged recreation/retired payload");
        Console.WriteLine("PASS creation:immutable-header (normal writer; malformed commits are protocol-negative setup)");
    }

    private static async Task Unsupported()
    {
        var store = new InMemoryStore(1, () => 100) { CreationIdentityAvailable = false };
        await store.TakeAsync(0, "unsupported", TimeSpan.FromSeconds(10));
        var target = InstanceRef.In(0, "unsupported");
        await Refused(store, async () => { await store.AddAsync(new Message("qualified", target, Kinds.Resume, TargetCreation: 1)); }, "qualified add");
        await Refused(store, () => store.SubscribeAsync(new SubscriptionRow("event", target.Id, "wait", Creation: 1)), "qualified subscription");
        var row = new InstanceRow(target.Id, "http", "wait", InstanceStatus.Running, "null", 1, null, Creation: 1, StartIntent: "intent");
        await Refused(store, async () => { await store.CommitAsync(Batch(store, Write(row, null))); }, "qualified commit");
        Require(await store.AddAsync(new Message("legacy", target, Kinds.Resume)), "unsupported provider also refused ordinary legacy transport");
        Console.WriteLine("PASS creation:unsupported-store (capability refusal only; no provider service)");
    }

    private static async Task Legacy()
    {
        // Literal pre-v3 state: no identity, retention, clock or close fields. This is a reader fixture, not writer evidence.
        const string state = """
            {"Started":true,"Inboxes":{"f1":{"Capacity":2,"ReceiptCapacity":4,"Next":1,
              "Receipts":{"old":{"Digest":"v1:A0B6E4134F0E7500F3DF347B35E1278D369CF33B596DB0C25B801F237E826835","Order":1,"Status":"Queued"}},
              "Pending":[{"Frame":"f1","Id":"old","Name":"event","Value":"payload"}]}}}
            """;
        await using var h = new Harness(new NativeModel(new WorkflowRegistry()));
        h.Store.Instances[h.Target.Id] = new(h.Target.Id, "native", "absent-definition", InstanceStatus.Finished, state, 7, null, History: History.Minimal, StateVersion: 2);
        await h.Activate();
        await h.Send(ReceiptQuery.Kind, new ReceiptQuery("f1", "old"));
        Require(h.Last is Admission { Result: "TerminalMetadataUnresolved", Order: 1, FinalizedAt: null, ValidUntil: null }, "legacy lookup invented a disposition/time");
        await h.Send(InvocationInput.Kind, new InvocationInput("f1", "old", "event", "payload"));
        Require(h.Last is Admission { Result: "Duplicate", Order: 1, FinalizedAt: null, ValidUntil: null }, "legacy duplicate identity lost");
        await h.Send(InvocationInput.Kind, new InvocationInput("f1", "old", "event", "changed"));
        Require(h.Last is Admission { Result: "Conflict" }, "legacy content conflict lost");
        await h.Send(InvocationInput.Kind, new InvocationInput("f1", "new", "event", "payload"));
        Require(h.Last is Admission { Result: "CreationIdentityUnavailable" }, "legacy row accepted an unattested new receipt");
        await h.Send(Kinds.Delete);
        Require(h.Store.Instances.ContainsKey(h.Target.Id) && h.Row.Creation == 0 && !h.Row.Retired && h.Row.StartIntent is null && h.Row.CommitTimes is null,
            "legacy delete discarded its unbounded receipt or invented shared metadata");
        Console.WriteLine("PASS creation:legacy-v2-reader (literal reader fixture; no historical writer/binary claim)");
    }

    private static async Task Exhaustion()
    {
        await using var h = new Harness(new Http(0, (_, _) => throw new InvalidOperationException("C6: unexpected job body")));
        h.Store.Instances[h.Target.Id] = new(h.Target.Id, "", "", InstanceStatus.Finished, "null", 9, null,
            History: History.Minimal, Creation: long.MaxValue, Retired: true); // reader boundary setup, not 2^63 normal starts
        var before = h.Row; await h.Activate();
        await h.Send(Kinds.Start, start: Http.Spec(Http.Wait) with { Creation = new(long.MaxValue, long.MaxValue, "exhausted") }, creation: long.MaxValue);
        Require(h.Last is CreationResult { Result: "CreationExhausted" } && h.Row == before, "exhausted high-water mark wrapped or ran model");
        Console.WriteLine("PASS creation:generation-exhaustion-reader (boundary setup)");
    }

    private static async Task StartEnvelope()
    {
        await using var h = new Harness(new Counter()); await h.Activate();
        var spec = Counter.Spec with { Creation = new(1, 0, "semantic-start") };
        await h.Send("add", new Add("first"), spec, 1);
        var before = h.Row;
        Require(Json.Read<CounterState>(before.State).Tokens.SequenceEqual(["first"]), "start payload did not perform its normal initial work");
        await h.Send("add", new Add("first"), spec, 1);
        Require(h.Last is CreationResult { Result: "ExistingCreation" } && h.Row == before, "same semantic start with new transport ID did not deduplicate");
        foreach (var (kind, payload) in new[] { ("add", new Add("changed")), ("other-kind", new Add("first")) })
        {
            await h.Send(kind, payload, spec, 1);
            Require(h.Last is CreationResult { Result: "StartConflict" } && h.Row == before, "changed start kind/payload did not conflict");
        }
        Console.WriteLine("PASS creation:semantic-start-envelope");
    }

    private static async Task MixedEvents()
    {
        var registry = new WorkflowRegistry(); registry.Add(Wf.Workflow("mixed-events", Wf.Event("same-wait", "mixed-event")));
        await using var h = new Harness(new NativeModel(registry)); await h.Activate();
        var legacy = new InstanceRef(h.Target.Id + "@1");
        var spec = NativeModel.Spec("mixed-events");
        await h.Send(Kinds.Start, start: spec with { Creation = new(1, 0, "mixed-qualified") }, creation: 1);
        await h.Send(Kinds.Start, start: spec, target: legacy);
        const string eventId = "event:0/boundary@1:same-wait";
        await Events.PublishAsync(h.Store, new("event:mixed-event", eventId, Json.Write("payload"), 10000));
        var deliveries = h.Store.Messages.Values.Where(m => m.Message.Kind == Kinds.Event).Select(m => m.Message).ToArray();
        Require(deliveries.Length == 2 && deliveries.Select(m => m.Id).Distinct().Count() == 2 &&
            deliveries.All(m => Events.Delivered(m)?.Event == eventId), "mixed event addresses collided or lost their logical source");
        await h.Drain();
        Require(h.Row.Status == InstanceStatus.Finished && h.Store.Instances[legacy.Id].Status == InstanceStatus.Finished,
            "mixed event fan-out failed to complete both normally authored waits");
        Console.WriteLine("PASS creation:mixed-event-addresses");
    }

    private sealed class Harness : IAsyncDisposable, IReplyTransport
    {
        internal readonly InMemoryStore Store = new(1, () => 100);
        internal readonly InstanceRef Target = InstanceRef.In(0, "boundary");
        private Worker _worker;
        private readonly IExecutionModel _model;
        private int _serial;
        internal InstanceRow Row => Store.Instances[Target.Id];
        internal object? Last;
        internal Harness(IExecutionModel model)
        { _model = model; _worker = Create("creation-boundary"); }
        private Worker Create(string name) => new(name, 0, Store, new WorkflowInstance([_model]),
            new WorkerOptions { Monotonic = () => 100, Transport = this });
        internal async Task Reload()
        { await _worker.DisposeAsync(); _worker = Create("creation-boundary-reload"); await Activate(); await Drain(); }
        internal async Task Activate() => await new DurableHost([_worker]).ActivateAsync(0);
        internal async Task Send(string kind, object? payload = null, StartSpec? start = null, long? creation = null, InstanceRef? target = null)
        {
            var id = "boundary-" + ++_serial; Last = null;
            await Store.AddAsync(new Message(id, target ?? Target, kind, payload is null ? null : Json.Write(payload), Start: start,
                ReplyTo: ReplyTo.Of("outside", id, 10000), TargetCreation: creation));
            await Drain();
        }
        internal async Task Drain()
        {
            for (var i = 0; i < 100; i++)
            {
                await _worker.PollAsync(); if (_worker.NeedsLoad) await _worker.LoadAsync();
                var ready = _worker.Ready.ToArray(); foreach (var actor in ready) _worker.RunTurn(actor);
                if (ready.Length == 0 && !_worker.NeedsFlush && !_worker.NeedsMeet) return;
                if (_worker.NeedsFlush) await _worker.FlushAsync();
                if (_worker.NeedsMeet) await _worker.MeetAsync();
            }
            throw new InvalidOperationException("C6: boundary harness did not settle");
        }
        public void Send(string node, Reply reply) { if (reply.Channel == Request.Response) Last = reply.Item; }
        public ValueTask DisposeAsync() => _worker.DisposeAsync();
    }
}
