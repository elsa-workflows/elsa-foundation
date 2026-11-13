using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

var names = new[] { "normal", "before-apply", "lost-ack", "reload", "stale-commit", "future-pending-end", "unsupported" };
if (args.SequenceEqual(["--list"])) { foreach (var name in names) Console.WriteLine("clock:" + name); return; }
if (args.Length != 0) throw new ArgumentException("Only --list is supported.");
foreach (var name in names) new ClockWitness(name).Run();
Console.WriteLine("7/7 shared clock schedules passed. In-memory protocol only; terminal receipt integration remains open.");

sealed class ClockWitness(string scenario)
{
    private readonly World _world = new(new Config(2, 1, Flaw.None),
        node => new ClockProbe(scenario == "future-pending-end" && node == 1 ? 1 : 2)) { Auto = true };
    private readonly InstanceRef _target = InstanceRef.In(0, "clock-probe");
    private int _owner, _serial;
    private Actor Actor => _world.Of(_owner, 0);
    private InstanceRow Row => _world.Store.Instances[_target.Id];
    private ClockState State => Json.Read<ClockState>(Row.State);
    private static void Require(bool ok, string assertion, string detail)
    { if (!ok) throw new InvalidOperationException(assertion + ": " + detail); }
    private void Apply(Act act)
    {
        _world.Apply(act);
        if (_world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
    }
    private void Send(string kind, string? input = null)
    {
        var id = "clock-message-" + ++_serial;
        Apply(new("add", M: new Message(id, _target, kind, Start: kind == Kinds.Start ? new("clock-probe", "probe", input) : null)));
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
            Require(Row.CommitTimes is { Count: 0 }, "C5", "pending-only activation did not persist opt-in");
            var opaque = Row.State; Takeover(); Send(Kinds.Terminate); Prepare(); Apply(new("tick", 2000)); Apply(new("flush", Actor.Id)); Drain();
            Require(Row.Status == InstanceStatus.Cancelled && Row.State == opaque && Row.StateVersion == 2 &&
                Row.CommitTimes?.GetValueOrDefault(WorkflowCommitClock.Terminal) == _world.Time, "C5", "older model lost opaque-state terminal anchor");
            var ended = Row.CommitTimes![WorkflowCommitClock.Terminal];
            Apply(new("tick", 50)); Send(Kinds.Terminate); Drain();
            Require(Row.CommitTimes[WorkflowCommitClock.Terminal] == ended, "C3", "repeat termination restamped end");
            Console.WriteLine("PASS clock:" + scenario); return;
        }
        // A lost acknowledgement can leave the row cold with no outstanding work; the read loads it normally.
        if (scenario == "lost-ack") { Send("read"); Drain(); }
        var first = State.First;
        Require(first is not null && first == State.Alias && Row.CommitTimes?.GetValueOrDefault(first) is { }, "C2", "committed shared fact missing");
        var stamp = Row.CommitTimes![first!];
        Require(stamp == (scenario == "lost-ack" ? _world.Time - 20_000 : _world.Time), "C1", "fact used staged time rather than atomic application");
        if (scenario == "reload") { Takeover(); Send("read"); Drain(); }
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
        Require(Row.CommitTimes[first!] == stamp && Row.CommitTimes[State.Second!] == _world.Time, "C3", "later commit restamped earlier fact");
        var cache = Actor.Worker.Instances.Single(x => x.Id == _target.Id).Row;
        Require(Json.Write(cache?.CommitTimes) == Json.Write(Row.CommitTimes), "C2", "worker cache differs from committed facts");
        Send("release"); Drain();
        Require(!Row.CommitTimes.ContainsKey(first!) && Row.CommitTimes.ContainsKey(State.Second!) && !Row.CommitTimes.ContainsKey("absent"),
            "C2", "release/resolve changed another fact or created a missing one");
        Apply(new("tick", 50)); Send("end"); Prepare(); Apply(new("tick", 2000)); Apply(new("flush", Actor.Id)); Drain();
        Require(Row.Status == InstanceStatus.Finished && Row.CommitTimes.GetValueOrDefault(WorkflowCommitClock.Terminal) == _world.Time,
            "C5", "normal end lacks committed workflow-owned fact");
        Console.WriteLine("PASS clock:" + scenario);
    }
    private void Unsupported()
    {
        _world.Store.CommitClockAvailable = false;
        var before = _world.Store.Partitions[0].Position;
        var row = new InstanceRow("0/direct", "clock-probe", "probe", InstanceStatus.Running, "{}", 1, null, CommitTimes: []);
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
    public string FutureCanary { get; set; } = "opaque-future-state";
}
public sealed class ClockProbe(int version) : IExecutionModel
{
    public string Key => "clock-probe";
    public Type StateType => typeof(ClockState);
    public int StateVersion => version;
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
                case "read": _ = turn.Clock.Resolve(state.First ?? "absent"); break;
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
