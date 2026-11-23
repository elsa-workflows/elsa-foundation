using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

// Seeded mixed/corrupt checkpoint consumption, not old-writer or mixed-binary compatibility proof.
internal static class ScopedContextReaders
{
    private static IEnumerable<(string Mode, bool Nested, string Shape)> Cases() => from m in new[] { Modes.Balanced, Modes.Durable }
        from n in new[] { false, true } from shape in new[] { "mixed-legacy", "wrong-parent", "stray-resource", "wrong-machine-owner" }
        where n || shape != "wrong-machine-owner" select (m, n, shape);
    internal static IEnumerable<string> Names => Cases().Select(c => $"scope-context-reader:{c.Shape},{c.Mode},nested={c.Nested}");
    internal static void Run()
    {
        foreach (var c in Cases()) Run(c.Mode, c.Nested, c.Shape);
    }
    private static void Run(string mode, bool nested, string shape)
    {
        var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
        var target = InstanceRef.In(0, "scope-context-reader"); int owner = 0; var expectedSeen = false;
        Actor Actor() => world.Of(owner, 0);
        var expected = shape switch { "mixed-legacy" => "Legacy statechart checkpoint already owns lexical resources.",
            "wrong-parent" => "Invalid statechart visit scope owner or parent.", "wrong-machine-owner" => "Invalid statechart machine scope owner.",
            _ => "Statechart lexical resource ownership differs from its visit map." };
        void Apply(Act action)
        {
            world.Apply(action);
            if (world.Check() is { } error)
            {
                if (error.StartsWith("I14 ", StringComparison.Ordinal) && error.Contains(expected, StringComparison.Ordinal)) { expectedSeen = true; return; }
                throw new InvalidOperationException("V12: unexpected context refusal: " + error);
            }
        }
        void Drain()
        {
            for (var i = 0; i < 100 && !expectedSeen; i++)
            {
                Apply(new("poll", Actor().Id)); if (Actor().Worker.NeedsLoad) Apply(new("load", Actor().Id));
                if (Actor().Worker.Ready.Contains(target.Id)) Apply(new("turn", Actor().Id, S: target.Id));
                else if (!Actor().Worker.NeedsFlush && !Actor().Worker.NeedsMeet) return;
                if (Actor().Worker.NeedsFlush) Apply(new("flush", Actor().Id));
                if (Actor().Worker.NeedsMeet) Apply(new("meet", Actor().Id));
            }
        }
        var definition = new StructuralDefinition([new("r", NodeKind.Root, Initial: "Parent"), new("Parent", NodeKind.Compound, "r", "A"), new("A", NodeKind.Atomic, "Parent")], []);
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(definition)));
        var root = nested ? Wf.Flowchart("outer", [machine], []) : machine;
        NativeWorkflows.Registry.Add(Wf.Workflow("scope-context-reader", root));
        Apply(new("take", Actor().Id)); Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec("scope-context-reader", mode)))); Drain();
        var row = world.Store.Instances[target.Id]; var json = JsonNode.Parse(row.State)!;
        var frames = json["Frames"]!.AsObject(); var frame = frames.Single(p => p.Value!["Node"]!.ToString() == "machine");
        var state = frame.Value!["State"]!.Deserialize<MachineState>()!; var resources = json["LexicalScopes"]!.AsObject();
        Apply(new("crash", owner));
        if (shape == "mixed-legacy") frame.Value["State"] = JsonSerializer.SerializeToNode(state with { Version = 3, Scopes = null });
        else if (shape == "wrong-parent") resources[state.Scopes!.Visits[state.Visits!.Single(v => v.State == "A").Occurrence]]!["Parent"] = state.Scopes.Machine;
        else if (shape == "wrong-machine-owner") resources[state.Scopes!.Machine]!["Owner"] = frames.Single(p => p.Value!["Node"]!.ToString() == "outer").Key;
        else resources["stray-resource"] = new JsonObject { ["Owner"] = frame.Key, ["Parent"] = null, ["Values"] = new JsonObject() };
        var inputResources = resources.ToJsonString();
        world.Store.Instances[target.Id] = row with { State = json.ToJsonString() };
        Apply(new("tick", 20_000)); owner = 1; Apply(new("take", Actor().Id)); Drain();
        Apply(new("add", M: new("probe", target, CompositeSignal.Kind, Json.Write(new CompositeSignal(frame.Key, "unknown"))))); Drain();
        if (!expectedSeen) throw new InvalidOperationException("V12: invalid mixed/context checkpoint did not refuse before migration: " + shape);
        // Park preserves the input checkpoint on technical refusal. No allocated resources become committed.
        var after = JsonNode.Parse(world.Store.Instances[target.Id].State)!;
        if (after["LexicalScopes"]!.ToJsonString() != inputResources) throw new InvalidOperationException("V12: refused context allocated or replaced resources");
        Console.WriteLine($"PASS scope-context-reader:{shape},{mode},nested={nested}");
    }
}
