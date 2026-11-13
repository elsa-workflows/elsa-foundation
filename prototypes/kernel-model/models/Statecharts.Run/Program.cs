using System.Text.Json;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
foreach (var nested in new[] { false, true })
foreach (var fault in Enum.GetValues<Fault>())
    new Witness(mode, nested, fault).Run();
Console.WriteLine("20 passed: S1–S4 and kernel checks, root and Flowchart, Balanced and Durable, 5 fault schedules.");

enum Fault { None, LostCommit, UnacknowledgedCommit, CrashedWhileHeld, StaleOwner }

sealed class Witness(string mode, bool nested, Fault fault)
{
    private readonly World _world = new(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
    private readonly InstanceRef _instance = InstanceRef.In(0, "machine");
    private int _owner;
    private readonly HashSet<string> _observed = [];
    private string _frame = "";

    public void Run()
    {
        // Initial is a nested compound state, Running has two parallel regions.
        var chart = new Definition(new("Initial",
            [new("Initial", [new("Idle", [new("Idle")])]),
             new("Running", [new("Low", [new("Low", Entry: "wait"), new("High")]),
                             new("Cold", [new("Cold"), new("Hot")])]),
             new("Done", Final: true)]),
            [new("Initial", "begin", "Running", "action", When: "allow"),
             new("Low", "advance", "High"), new("Cold", "advance", "Hot"),
             new("Running", "finish", "Done")]);
        var machine = Wf.Node<StateMachine>("machine", ("ChartDefinition", Binding.Literal(chart)))
            .With(Wf.Set("action", "selected", "yes"), Wf.Event("wait", "release"));
        var root = nested ? Wf.Flowchart("flow", [machine, Wf.Set("after", "returned", "yes")],
            [new("machine", "after")]) : machine;
        NativeWorkflows.Registry.Add(Wf.Workflow("chart", root));
        Apply(new("take", Actor.Id));
        Add(new("start", _instance, Kinds.Start, Start: NativeModel.Spec("chart", mode)));
        Drain();
        _frame = Native().Frames.Values.Single(f => f.Node == "machine").Id;
        Signal("guard", "begin", "deny");
        Signal("bad", "unknown");
        Drain();
        Signal("first", "begin", "allow");
        if (fault is Fault.LostCommit or Fault.UnacknowledgedCommit) FailFlush(fault);
        Drain();
        Require(Native().Waits.Values.Any(wait => wait.Key == "event:release"), "entry did not suspend");

        Signal("second", "advance");
        Drain();
        Require(_world.Store.Messages.Values.Any(row => !row.Done && row.Message.Id == "second"), "held signal left the inbox");
        Require(Entries().Contains($"signal-deferred:second:{_frame}"), "held signal not observed");
        if (fault is Fault.CrashedWhileHeld or Fault.StaleOwner) ChangeOwner(fault == Fault.StaleOwner);

        Add(new("resume", _instance, NativeKinds.Resume, Json.Write(new ResumeStimulus("release"))));
        Drain();
        Require(Native().Frames[_frame].State!.Deserialize<MachineState>()!.Active.ToHashSet()
            .SetEquals(["Running", "High", "Hot"]), "parallel regions did not both react");
        Signal("finish", "finish");
        Drain();
        var row = _world.Store.Instances[_instance.Id];
        Require(row.Status == InstanceStatus.Finished, $"instance ended {row.Status}");
        if (nested) Require(Native().Variables["returned"]!.GetValue<string>() == "yes", "Flowchart did not resume");
        CheckHistory();
        Require(_observed.SetEquals(["initial","parallel-start","parallel-end"]), "S3 configuration coverage");
    }

    private Actor Actor => _world.Of(_owner, 0);
    private NativeState Native() => Json.Read<NativeState>(_world.Store.Instances[_instance.Id].State);
    private IEnumerable<string> Entries() => _world.Store.Journal.Values.OrderBy(row => row.Position)
        .Where(row => row.Instance == _instance.Id).SelectMany(row => Json.Read<string[]>(row.Entries));

    private void Signal(string id, string name, string? value = null) =>
        Add(new(id, _instance, CompositeSignal.Kind, Json.Write(new CompositeSignal(_frame, name, value))));

    private void Add(Message message) => Apply(new("add", M: message));

    private void Apply(Act action)
    {
        _world.Apply(action);
        if (_world.Check() is { } violation) throw new InvalidOperationException($"{action}: {violation}");
        if (!_world.Store.Instances.ContainsKey(_instance.Id)) return;
        var frame = Native().Frames.Values.SingleOrDefault(f => f.Node == "machine");
        if (frame?.State?.Deserialize<MachineState>() is not { } state) return;
        // S3 is inspected at every committed turn (including when an ordinary action suspends).
        var active = state.Active.Order().ToArray();
        var configuration = string.Join(",", active) switch
        {
            "Idle,Initial" => "initial",
            "Cold,Low,Running" => "parallel-start",
            "High,Hot,Running" => "parallel-end",
            _ => throw new InvalidOperationException($"S3 illegal configuration {string.Join(",", active)}")
        };
        _observed.Add(configuration);
    }

    private void Drain()
    {
        for (var i = 0; i < 100; i++)
        {
            Apply(new("poll", Actor.Id));
            if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
            if (Actor.Worker.Ready.Contains(_instance.Id)) Apply(new("turn", Actor.Id, S: _instance.Id));
            else if (!Actor.Worker.NeedsFlush && !Actor.Worker.NeedsMeet) return;
            if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id));
            if (Actor.Worker.NeedsMeet) Apply(new("meet", Actor.Id));
        }
        throw new InvalidOperationException("Could not drain in 100 iterations.");
    }

    private void FailFlush(Fault cut)
    {
        Apply(new("poll", Actor.Id));
        // First flush may only mark the polled row; interrupt the flush containing the chart turn.
        if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id));
        Require(Actor.Worker.Ready.Contains(_instance.Id), "commit cut missed its input");
        Apply(new("turn", Actor.Id, S: _instance.Id));
        _world.Auto = false;
        var call = Actor.Calls.Count;
        Apply(new("flush", Actor.Id));
        Require(Actor.Calls.Count == call + 1 && Actor.Calls[call].Kind == "commit", "commit cut not reached");
        Apply(new(cut == Fault.LostCommit ? "lose" : "exec", Actor.Id, call));
        Apply(new("answer", Actor.Id, call, Flag: true));
        _world.Auto = true;
        ChangeOwner(false);
    }

    private void ChangeOwner(bool zombie)
    {
        var old = Actor;
        Apply(new("crash", _owner, Flag: zombie));
        Apply(new("tick", 20000));
        _owner = 1 - _owner;
        Apply(new("take", Actor.Id));
        Require(Actor.Worker.Owns, "new owner did not take the partition");
        if (zombie)
        {
            var before = _world.Store.Partitions[0].Position;
            Apply(new("beat", old.Id));
            Require(!old.Worker.Owns && before == _world.Store.Partitions[0].Position, "stale owner retained the fence");
        }
    }

    private void CheckHistory()
    {
        var trace = Entries().Where(entry => entry.StartsWith("statechart:", StringComparison.Ordinal))
            .Select(entry => JsonSerializer.Deserialize<string?[]>(entry["statechart:".Length..])!).ToList();
        // S1: the next evaluation follows the previous terminal outcome, even across suspension and takeover.
        string? current = null;
        var outcomes = new List<string>();
        foreach (var part in trace.Where(part => part[1] is not null))
        {
            var (id, phase) = (part[1]!, part[2]);
            if (phase == "evaluated")
            {
                Require(current is null, $"S1 evaluated {id} while {current} was in flight");
                current = id;
            }
            Require(current == id, $"S1 phase {phase} outside evaluation of {id}");
            if (phase is "handled" or "unhandled")
            {
                outcomes.Add($"{id}:{phase}");
                current = null;
            }
        }
        Require(current is null && outcomes.SequenceEqual(
            ["guard:unhandled", "bad:unhandled", "first:handled", "second:handled", "finish:handled"]), "S2 incorrect event outcomes");
        // S4: check the complete order, allowing either order between independent regions.
        string[] Phases(string id) => trace.Where(p => p[1] == id && p[2] is "exit" or "transition" or "entry")
            .Select(p => $"{p[2]}:{p[3]}").ToArray();
        var first = Phases("first");
        Require(first.Length == 6 && first[..4].SequenceEqual(["exit:Idle", "exit:Initial", "transition:Initial", "entry:Running"])
            && first[4..].ToHashSet().SetEquals(["entry:Low","entry:Cold"]), "S4 compound exit or parallel entry order");
        var finish = Phases("finish");
        Require(finish.Length == 5 && finish[..2].ToHashSet().SetEquals(["exit:High","exit:Hot"])
            && finish[2..].SequenceEqual(["exit:Running","transition:Running","entry:Done"]), "S4 parallel exit order");
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
