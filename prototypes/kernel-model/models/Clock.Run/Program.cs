using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

if (args.SequenceEqual(["--shortwait-check"])) { Environment.ExitCode = await ShortWaitIntegration.Run(); return; }
var names = new[] { "normal", "before-apply", "lost-ack", "reload", "stale-commit", "future-pending-end", "unsupported" };
if (args.SequenceEqual(["--list"])) { foreach (var name in names) Console.WriteLine("clock:" + name); Console.WriteLine("clock:delayed-wrapper"); Console.WriteLine("clock:short-wait-promotion"); return; }
if (args.Length != 0) throw new ArgumentException("Only --list is supported.");
foreach (var name in names) new ClockWitness(name).Run();
await DelayedClock();
await ShortWaitClockWitness.Run();
Console.WriteLine("9/9 shared clock schedules passed. In-memory protocol only; terminal receipt integration remains open.");

static async Task DelayedClock()
{
    long time = 100;
    var store = new InMemoryStore(1, () => time);
    var wire = new DelayedCommit(store);
    await using var worker = new Worker("clock-delay", 0, wire, new WorkflowInstance([new ClockProbe(2)]),
        new WorkerOptions { Monotonic = () => time });
    await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync();
    var target = InstanceRef.In(0, "delayed-clock");
    worker.Post(new Message("delayed-clock-start", target, Kinds.Start, Start: new("clock-probe", "probe")), fresh: true);
    worker.RunTurn(target.Id);
    time += 2000;
    wire.DelayNext = true;
    var flush = worker.FlushAsync();
    await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var row = store.Instances.GetValueOrDefault(target.Id);
    var reference = row is null ? null : Json.Read<ClockState>(row.State)?.First;
    if (!wire.CommitClockAvailable || row?.Status != InstanceStatus.Running || reference is null ||
        CommitClockFacts.Read(row)?.GetValueOrDefault(reference) != time)
        throw new InvalidOperationException("C8: delayed wrapper lost clock capability or authoritative stamp");
    time += 100; wire.Reply.TrySetResult(); await flush;
    if (!worker.Owns || worker.Instances.Single(x => x.Id == target.Id).Row?.CommitTimes != row.CommitTimes)
        throw new InvalidOperationException("C8: delayed acknowledgement changed committed clock facts");
    Console.WriteLine("PASS clock:delayed-wrapper");
}

sealed class ClockWitness(string scenario)
{
    private readonly World _world = new(new Config(2, 1, Flaw.None),
        node => new ClockProbe(scenario == "future-pending-end" && node == 1 ? 1 : 2)) { Auto = true };
    private readonly InstanceRef _target = InstanceRef.In(0, "clock-probe");
    private int _owner, _serial;
    private Actor Actor => _world.Of(_owner, 0);
    private InstanceRow Row => _world.Store.Instances[_target.Id];
    private Dictionary<string, long?> Times => CommitClockFacts.Read(Row) ?? [];
    private ClockState State => Json.Read<ClockState>(Row.State);
    private static void Require(bool ok, string assertion, string detail)
    { if (!ok) throw new InvalidOperationException(assertion + ": " + detail); }
    private void Apply(Act act)
    {
        _world.Apply(act);
        if (_world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
    }
    private void Send(string kind, string? input = null, bool reply = false)
    {
        var id = "clock-message-" + ++_serial;
        Apply(new("add", M: new Message(id, _target, kind, Start: kind == Kinds.Start ? new("clock-probe", "probe", input) : null,
            ReplyTo: reply ? ReplyTo.Of("outside", id, _world.Time + 30_000) : null)));
    }
    private void Prepare()
    {
        for (var i = 0; i < 100; i++)
        {
            Apply(new("poll", Actor.Id));
            if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
            if (Actor.Worker.Ready.Contains(_target.Id))
            {
                Apply(new("turn", Actor.Id, S: _target.Id));
                Require(Actor.Worker.Queued > 0, "C0", "no model/control outcome was staged");
                return;
            }
            if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id));
        }
        throw new InvalidOperationException("C0: target never became ready");
    }
    private void Drain(string? target = null)
    {
        target ??= _target.Id;
        for (var i = 0; i < 150; i++)
        {
            Apply(new("poll", Actor.Id));
            if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
            if (Actor.Worker.Ready.Contains(target)) Apply(new("turn", Actor.Id, S: target));
            else if (!Actor.Worker.NeedsFlush && !Actor.Worker.NeedsMeet) return;
            if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id));
            if (Actor.Worker.NeedsMeet) Apply(new("meet", Actor.Id));
        }
        throw new InvalidOperationException("C0: work bound exceeded");
    }
    private void Takeover(bool zombie = false)
    {
        Apply(new("crash", _owner, Flag: zombie)); Apply(new("tick", 20_000));
        _owner = 1 - _owner; Apply(new("take", Actor.Id));
        Require(Actor.Worker.Owns, "C0", "takeover failed");
    }
    public void Run()
    {
        Apply(new("take", Actor.Id));
        if (scenario == "unsupported") { Unsupported(); return; }
        Send(Kinds.Start, scenario == "future-pending-end" ? "pending" : null); Prepare();
        Apply(new("tick", 2000));
        if (scenario is "before-apply" or "lost-ack")
        {
            _world.Auto = false; var call = Actor.Calls.Count; Apply(new("flush", Actor.Id));
            Require(Actor.Calls.Count == call + 1 && Actor.Calls[call].Kind == "commit", "C0", "wrong commit cut");
            Apply(new(scenario == "before-apply" ? "lose" : "exec", Actor.Id, call));
            Require(_world.Store.Instances.ContainsKey(_target.Id) == (scenario == "lost-ack"), "C2", "wrong apply boundary");
            Apply(new("answer", Actor.Id, call, Flag: true)); _world.Auto = true;
            Takeover(); Drain();
        }
        else { Apply(new("flush", Actor.Id)); Drain(); }
        if (scenario == "future-pending-end")
        {
            Require(Row.CommitTimes is not null && Times.Count == 0, "C5", "pending-only activation did not persist opt-in");
            var opaque = Row.State; Takeover(); Send(Kinds.Terminate); Prepare(); Apply(new("tick", 2000)); Apply(new("flush", Actor.Id)); Drain();
            Require(Row.Status == InstanceStatus.Cancelled && Row.State == opaque && Row.StateVersion == 2 &&
                Times.GetValueOrDefault(WorkflowCommitClock.Terminal) == _world.Time, "C5", "older model lost opaque-state terminal anchor");
            var ended = Times[WorkflowCommitClock.Terminal];
            Send("clock-metadata", reply: true); Drain();
            Require(_world.Deliveries.Last().Reply.Item is ModelMetadataUnavailable { StoredVersion: 2, AvailableVersion: 1 } &&
                Row.State == opaque && Row.StateVersion == 2, "C5", "older metadata reader parsed or downgraded opaque future state");
            Send("late"); Drain();
            Require(!_world.Store.Messages.ContainsKey("clock-message-" + _serial) && Row.State == opaque,
                "C5", "ordinary terminal late input did not drain through the older model");
            Apply(new("tick", 50)); Send(Kinds.Terminate); Drain();
            Require(Times[WorkflowCommitClock.Terminal] == ended, "C3", "repeat termination restamped end");
            Console.WriteLine("PASS clock:" + scenario); return;
        }
        // A lost acknowledgement can leave the row cold with no outstanding work; the read loads it normally.
        if (scenario == "lost-ack") { Send("read"); Drain(); }
        var first = State.First;
        Require(first is not null && first == State.Alias && Times.GetValueOrDefault(first) is { }, "C2", "committed shared fact missing");
        var stamp = Times[first!];
        Require(stamp == (scenario == "lost-ack" ? _world.Time - 20_000 : _world.Time), "C1", "fact used staged time rather than atomic application");
        Send("read"); Drain();
        Require(State.ObservedFirst == stamp && State.AbsentWasNull, "C7", "model could not resolve committed/missing fact");
        if (scenario == "reload")
        {
            Takeover(); Send("read"); Drain();
            Require(State.ObservedFirst == stamp && State.AbsentWasNull, "C7", "cold model lookup lost retained fact");
        }
        if (scenario == "stale-commit")
        {
            var old = Actor; Send("write"); Prepare(); _world.Auto = false;
            var call = old.Calls.Count; Apply(new("flush", old.Id)); _world.Auto = true;
            Takeover(true); Send("write"); Drain();
            var position = _world.Store.Partitions[0].Position; var stored = Json.Write(Row.CommitTimes);
            Apply(new("exec", old.Id, call)); Apply(new("answer", old.Id, call));
            Require(!old.Worker.Owns && _world.Store.Partitions[0].Position == position && Json.Write(Row.CommitTimes) == stored,
                "C2", "stale clock commit changed authoritative facts");
        }
        Apply(new("tick", 100)); Send("write"); Prepare(); Apply(new("tick", 2000)); Apply(new("flush", Actor.Id)); Drain();
        Require(Times[first!] == stamp && Times[State.Second!] == _world.Time, "C3", "later commit restamped earlier fact");
        var cache = Actor.Worker.Instances.Single(x => x.Id == _target.Id).Row;
        Require(Json.Write(cache?.CommitTimes) == Json.Write(Row.CommitTimes), "C2", "worker cache differs from committed facts");
        Send("release"); Drain();
        Require(!Times.ContainsKey(first!) && Times.ContainsKey(State.Second!) && !Times.ContainsKey("absent"),
            "C2", "release/resolve changed another fact or created a missing one");
        Apply(new("tick", 50)); Send("end"); Prepare(); Apply(new("tick", 2000)); Apply(new("flush", Actor.Id)); Drain();
        Require(Row.Status == InstanceStatus.Finished && Times.GetValueOrDefault(WorkflowCommitClock.Terminal) == _world.Time,
            "C5", "normal end lacks committed workflow-owned fact");
        if (scenario == "normal") RejectForgedFacts();
        Console.WriteLine("PASS clock:" + scenario);
    }
    private void RejectForgedFacts()
    {
        var facts = Times;
        var altered = new Dictionary<string, long?>(facts);
        altered[State.Second!] = facts[State.Second!] + 1;
        var removed = new Dictionary<string, long?>(facts); removed.Remove(WorkflowCommitClock.Terminal);
        var forged = new InstanceRow("0/forged-clock", "clock-probe", "probe", InstanceStatus.Running, "{}", 1, null,
            CommitTimes: Json.Write(new Dictionary<string, long?> { ["turn:1"] = _world.Time }));
        foreach (var candidate in new[] { forged, Row with { Revision = Row.Revision + 1, CommitTimes = Json.Write(altered) },
            Row with { Revision = Row.Revision + 1, CommitTimes = Json.Write(removed) } })
        {
            var position = _world.Store.Partitions[0].Position;
            var original = Row.CommitTimes;
            var change = new TurnCommit(candidate.Id, candidate, candidate.Id == _target.Id ? Row.Revision : null, [], [], [], [], [], "[]");
            try
            {
                _world.Store.CommitAsync(new(0, Actor.Worker.Epoch, position, TimeSpan.FromSeconds(10), false, [change], [], [])).GetAwaiter().GetResult();
                throw new InvalidOperationException("C6: forged, altered or released actor fact was accepted");
            }
            catch (StoreException error) when (error.Failure == Failure.Definite) { }
            Require(_world.Store.Partitions[0].Position == position && Row.CommitTimes == original &&
                !_world.Store.Instances.ContainsKey(forged.Id), "C6", "invalid clock protocol batch partially wrote");
        }
    }
    private void Unsupported()
    {
        _world.Store.CommitClockAvailable = false;
        var before = _world.Store.Partitions[0].Position;
        var row = new InstanceRow("0/direct", "clock-probe", "probe", InstanceStatus.Running, "{}", 1, null, CommitTimes: "{}");
        var turn = new TurnCommit(row.Id, row, null, [], [], [], [], [], "[]");
        try
        {
            _world.Store.CommitAsync(new(0, Actor.Worker.Epoch, before, TimeSpan.FromSeconds(10), false, [turn], [], [])).GetAwaiter().GetResult();
            throw new InvalidOperationException("C4: unsupported store accepted clock-bearing batch");
        }
        catch (StoreException error) when (error.Failure == Failure.Definite) { }
        Require(!_world.Store.Instances.ContainsKey(row.Id) && _world.Store.Partitions[0].Position == before, "C4", "unsupported guard wrote state");
        _world.Oracle.Fragiles[_target.Id] = true; // expected initial-turn failure, not a seeded store result
        Send(Kinds.Start); Drain();
        Require(Row.Status == InstanceStatus.Faulted && Row.Fault == "store has no qualified commit clock" && Row.CommitTimes is null,
            "C4", "unsupported activation accepted a promise");
        Send(Kinds.Delete); Drain();
        Require(!_world.Store.Instances.ContainsKey(_target.Id), "C4", "failed-start row could not be deleted");
        var ordinary = InstanceRef.In(0, "ordinary-clock");
        Apply(new("add", M: new Message("ordinary-clock-start", ordinary, Kinds.Start, Start: new("clock-probe", "probe", "ordinary"))));
        Drain(ordinary.Id);
        Require(_world.Store.Instances[ordinary.Id] is { Status: InstanceStatus.Running, CommitTimes: null, Fault: null },
            "C4", "unqualified store stopped an ordinary model that requested no clock");
        Console.WriteLine("PASS clock:" + scenario);
    }
}

public sealed class ClockState
{
    public string? First { get; set; }
    public string? Alias { get; set; }
    public string? Second { get; set; }
    public long? ObservedFirst { get; set; }
    public bool AbsentWasNull { get; set; }
    public string FutureCanary { get; set; } = "opaque-future-state";
}
public sealed class ClockProbe(int version) : IExecutionModel, IModelMetadata
{
    public string Key => "clock-probe";
    public Type StateType => typeof(ClockState);
    public int StateVersion => version;
    public bool HandlesMetadata(Message message) => message.Kind == "clock-metadata";
    public ValueTask MetadataAsync(ITurn turn, InstanceStatus status, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("older metadata reader must not execute opaque future state");
    public void TerminateMetadata(object state) { }
    public bool ProtectsDeletion(object state, long now, ICommitClock clock) => false; // this clock probe promises no receipt retention
    public object CreateState(StartSpec start) => new ClockState();
    public object UpgradeState(string state, int fromVersion) => Json.Read<ClockState>(state);
    public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken) => new((object?)null);
    public ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken)
    {
        if (version == 1) throw new InvalidOperationException("older model must not execute future private state");
        var state = (ClockState)turn.State;
        foreach (var message in turn.Inbox)
            switch (message.Kind)
            {
                case Kinds.Start:
                    if (message.Start?.Input == "ordinary") break;
                    turn.Clock.Require();
                    if (message.Start?.Input != "pending") { state.First = turn.Clock.Capture(); state.Alias = turn.Clock.Capture(); }
                    break;
                case "write": state.Second = turn.Clock.Capture(); break;
                case "read": state.ObservedFirst = turn.Clock.Resolve(state.First ?? "absent");
                    state.AbsentWasNull = turn.Clock.Resolve("absent") is null; break;
                case "release":
                    _ = turn.Clock.Resolve("absent");
                    try { turn.Clock.Release(WorkflowCommitClock.Terminal); throw new InvalidOperationException("C5: model released workflow fact"); }
                    catch (ArgumentException) { }
                    turn.Clock.Release(state.First!); state.First = state.Alias = null; break;
                case "end": turn.SetStatus(InstanceStatus.Finished); break;
            }
        return ValueTask.CompletedTask;
    }
}


// The short-wait path keeps a requested fact unstamped until its first actual durable commit.
static class ShortWaitClockWitness
{
    public static async Task Run()
    {
        long now = 100;
        var store = new InMemoryStore(1, () => now);
        await using var worker = new Worker("short-clock", 0, store, new WorkflowInstance([new ShortClockModel()]),
            new WorkerOptions { Monotonic = () => now, ShortWait = TimeSpan.FromMilliseconds(100) });
        await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync();
        var id = InstanceRef.In(0, "short-clock");
        worker.Post(new Message("short-clock-start", id, Kinds.Start, Start: new("short-clock", "probe", Ephemeral: true)), fresh: true);
        await worker.RunAsync();
        if (store.Instances.ContainsKey(id.Id) || !worker.Unwritten.Contains(id.Id))
            throw new InvalidOperationException("short clock must begin unwritten");
        var pending = worker.Instances.Single(i => i.Id == id.Id).Row!;
        if (CommitClockFacts.Read(pending) is not { Count: 1 } facts || facts.Values.Single() is not null)
            throw new InvalidOperationException("short clock was stamped without a commit");
        now += 100;
        await worker.FlushAsync();
        var saved = store.Instances[id.Id];
        if (saved.Revision != 1 || CommitClockFacts.Read(saved)?.Values.Single() != now ||
            worker.Instances.Single(i => i.Id == id.Id).Row?.CommitTimes != saved.CommitTimes)
            throw new InvalidOperationException($"promotion: revision={saved.Revision}, now={now}, saved={saved.CommitTimes}, cached={worker.Instances.Single(i => i.Id == id.Id).Row?.CommitTimes}");
        Console.WriteLine("PASS clock:short-wait-promotion");
    }
}

sealed class ShortClockModel : Kernel.Sim.Model<List<string>>
{
    public override string Key => "short-clock";
    protected override void Turn(ITurn turn, List<string> state)
    {
        state.Add(turn.Clock.Capture());
        turn.Send(turn.Instance.Ref, "later", options: new SendOptions(Delay: TimeSpan.FromMilliseconds(50)));
    }
}
