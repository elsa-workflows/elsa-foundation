using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static NativeInternalWitness;

internal static class NativeInternalStructureWitness
{
    private static string[] Cases => ["stale-completion", "recheck-after-completion", "fresh-completion-visit", "local-completion", "child-lazy", "parent-fallback", "ready-order", "parallel-E-first", "parallel-L-first"];
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from nested in new[] { false, true } from name in Cases
        select $"native-internal-structure:{m},nested={nested},case={name}";
    internal static void Run()
    { foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var nested in new[] { false, true }) foreach (var name in Cases) Run(mode, nested, name); }
    private static void Run(string mode, bool nested, string name)
    {
        ChartMark.Calls.Clear(); var h = new MachineInboxHarness();
        var nodes = new List<GraphNode>(); var rules = new List<GraphRule>(); var conditions = new Dictionary<string, string>(); var children = new List<Node> { Control() };
        Node Mark(string id) => Wf.Node<ChartMark>(id, ("Name", Binding.Literal(id)), ("Local", Binding.Literal(17)));
        void Rule(string id, int order, string source, string? target, TriggerKind kind, VisitMode visit = VisitMode.Reenter, string? action = null, string? condition = null)
        { rules.Add(new(id, order, source, target, visit, kind == TriggerKind.Event ? id : "", action, Kind: kind)); if (condition is not null) conditions[id] = condition; }
        if (name is "ready-order" or "parallel-E-first" or "parallel-L-first")
        {
            nodes.AddRange([new("root", NodeKind.Root, Initial: "P"), new("P", NodeKind.Parallel, "root"), new("E", NodeKind.Region, "P", name == "ready-order" ? "A" : "EF"),
                new("L", NodeKind.Region, "P", name == "ready-order" ? "B" : "LF")]);
            if (name == "ready-order")
            {
                nodes.AddRange([new("A", NodeKind.Compound, "E", "AF"), new("AF", NodeKind.Final, "A"), new("B", NodeKind.Compound, "L", "BF"), new("BF", NodeKind.Final, "B")]);
                Rule("b", 0, "B", null, TriggerKind.Completion, VisitMode.Internal, "mark-b"); Rule("a", 1, "A", null, TriggerKind.Completion, VisitMode.Internal, "mark-a");
                children.AddRange([Mark("mark-a"), Mark("mark-b")]);
                Rule("finish", 2, "P", "Done", TriggerKind.Event);
            }
            else
            {
                nodes.AddRange([new("EF", NodeKind.Final, "E", Entry: "wait-e"), new("LF", NodeKind.Final, "L", Entry: "wait-l")]);
                children.AddRange([Wf.Event("wait-e", "region-e"), Wf.Event("wait-l", "region-l")]);
                Rule("complete", 0, "P", "Done", TriggerKind.Completion, action: "complete"); children.Add(Mark("complete"));
            }
        }
        else
        {
            nodes.AddRange([new("root", NodeKind.Root, Initial: "C"), new("C", NodeKind.Compound, "root", name is "child-lazy" or "parent-fallback" ? "A" : name == "local-completion" ? "Inner" : "F"),
                new("F", NodeKind.Final, "C"), new("Waiting", NodeKind.Atomic, "root"), new("Trap", NodeKind.Atomic, "root")]);
            if (name == "local-completion") nodes.AddRange([new("Inner", NodeKind.Compound, "C", "IF"), new("IF", NodeKind.Final, "Inner")]);
            if (name is "child-lazy" or "parent-fallback")
            {
                nodes.Add(new("A", NodeKind.Atomic, "C"));
                Rule("parent", 0, "C", "Waiting", TriggerKind.Automatic, condition: name == "child-lazy" ? "invalid_expression(" : "true");
                Rule("child-false", 1, "A", "Trap", TriggerKind.Automatic, condition: "false");
                Rule("child", 2, "A", "Waiting", TriggerKind.Automatic, condition: name == "child-lazy" ? "true" : "false");
                if (name == "child-lazy") Rule("later", 3, "A", "Trap", TriggerKind.Automatic, condition: "invalid_expression(");
                Rule("finish", 4, "Waiting", "Done", TriggerKind.Event);
            }
            else if (name == "stale-completion")
            {
                Rule("auto", 0, "C", "Waiting", TriggerKind.Automatic);
                Rule("stale", 1, "C", "Trap", TriggerKind.Completion, condition: "invalid_expression("); Rule("finish", 2, "Waiting", "Done", TriggerKind.Event);
            }
            else if (name == "recheck-after-completion")
            {
                nodes[1] = nodes[1] with { Defaults = new Dictionary<string, JsonNode?> { ["ready"] = Values.From(false) } };
                Rule("auto", 0, "C", "Done", TriggerKind.Automatic, condition: "ready");
                Rule("complete", 1, "C", null, TriggerKind.Completion, VisitMode.Internal, "make-ready"); children.Add(Wf.Set("make-ready", "ready", true));
            }
            else if (name == "fresh-completion-visit")
            {
                Rule("complete", 0, "C", null, TriggerKind.Completion, VisitMode.Internal, "complete"); children.Add(Mark("complete"));
                Rule("reset", 1, "C", "C", TriggerKind.Event); Rule("finish", 2, "C", "Done", TriggerKind.Event);
            }
            else { Rule("never-parent", 0, "C", "Trap", TriggerKind.Completion, condition: "invalid_expression("); Rule("finish", 1, "C", "Done", TriggerKind.Event); }
        }
        nodes.Add(new("Done", NodeKind.Final, "root"));
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(new StructuralDefinition(nodes.ToArray(), rules.ToArray()))),
            ("InputInbox", Binding.Literal(new MachineInboxSettings(3, 8, 120000))),
            ("InternalExecution", Binding.Literal(new InternalExecutionSettings(16, "internal", conditions)))).With(children.ToArray());
        var wf = Wf.Workflow("machine-inbox", nested ? Wf.Flowchart("flow", [machine, Wf.Set("returned", "returned", true)], [new("machine", "returned")]) : machine);
        wf.InterventionRequested = true; Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode);
        string Frame() => h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        MachineState Chart() => h.State.Frames[Frame()].State!.Deserialize<MachineState>()!;
        void Input(string id, string trigger) { h.Send(InvocationInput.Kind, new InvocationInput(Frame(), id, trigger)); h.Drain(); }
        if (name.StartsWith("parallel-", StringComparison.Ordinal))
        {
            Check(Chart().Pending is not null && ChartMark.Calls.Count == 0 && h.State.Waits.Count == 2, "NS1: parallel initial final Entry did not wait independently");
            NativeGuardWitness.Release(h, name == "parallel-E-first" ? "region-e" : "region-l"); h.Reload();
            Check(Chart().Pending is not null && ChartMark.Calls.Count == 0 && h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Running,
                "NS1: one final region completed the containing parallel state");
            NativeGuardWitness.Release(h, name == "parallel-E-first" ? "region-l" : "region-e");
            Check(ChartMark.Calls.SequenceEqual(new[] { ("complete", 17) }), "NS1: all-final parallel did not take its authored completion exactly once");
        }
        else if (name == "recheck-after-completion")
        {
            var kinds = h.History.Where(s => s.StartsWith("statechart:", StringComparison.Ordinal)).Select(s => Json.Read<string?[]>(s[11..])).Where(p => p[2] == "internal-evaluated").Select(p => p[3]);
            Check(kinds.SequenceEqual(["Completion", "Automatic"]), "NS2: automatic guards were not rechecked immediately after a completion action");
        }
        else
        {
            var state = Chart(); Check(state.Settling is { Stable: true, Block: null } && !h.State.Frames.Values.Any(f => f.Intervention is not null),
                "NS3: suppressed/local/stale completion or ordered guard was incorrectly evaluated");
            if (name is "stale-completion" or "child-lazy" or "parent-fallback") Check(state.Active.SequenceEqual(["Waiting"]) && state.Settling!.Ready!.Length == 0 && state.Settling.Offered!.Length == 0,
                "NS3: hierarchy selection or stale completion cleanup changed the active configuration");
            if (name == "local-completion") Check(state.Active.ToHashSet().SetEquals(["C", "Inner", "IF"]) && state.Settling!.Offered is [{ State: "Inner" }],
                "NS4: child completion escaped its local owner");
            if (name == "ready-order") Check(ChartMark.Calls.SequenceEqual(new[] { ("mark-a", 17), ("mark-b", 17) }) &&
                state.Settling!.Offered!.Select(v => v.State).SequenceEqual(["A", "B"]) && state.Settling.Serial == 2,
                "NS5: simultaneous completion opportunities lost their recorded readiness order");
            if (name == "fresh-completion-visit")
            {
                Check(ChartMark.Calls.Count == 1, "NS6: first completion did not settle once");
                var first = Json.Write(state.Visits); Input("unknown", "unknown"); h.Reload();
                Check(ChartMark.Calls.Count == 1 && Json.Write(Chart().Visits) == first, "NS6: unhandled business input revived a consumed completion");
                Input("reset", "reset"); h.Reload();
                Check(ChartMark.Calls.Count == 2 && Json.Write(Chart().Visits) != first && Chart().Settling is { Serial: 2, Offered.Length: 1 },
                    "NS6: re-entry failed to offer a fresh completion for the new parent visit");
            }
            Input("finish", "finish");
        }
        Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && h.State.LexicalScopes is null &&
            (!nested || h.State.Variables["returned"]?.ToString() == "true"), "NS7: structural internal progress failed normal completion/return/cleanup");
        Console.WriteLine($"PASS native-internal-structure:{mode},nested={nested},case={name}");
    }
}
