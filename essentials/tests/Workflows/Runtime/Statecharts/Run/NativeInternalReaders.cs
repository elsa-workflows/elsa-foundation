using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static NativeInternalWitness;

// Malformed copies of actual checkpoints are reader fixtures, not additional workflow schedules.
internal static class NativeInternalReaders
{
    private static string[] Malformed => ["old-machine", "false-business", "no-profile", "no-progress", "limit", "lost-control", "no-original", "wrong-current", "no-spec", "purpose", "detail", "long-detail", "visit", "loop", "source", "successor", "node", "tag", "scheduling", "borrowed", "receipt-state", "receipt-marker", "payload", "missing-payload"];
    private static string[] Topology => ["missing", "type", "external", "background", "child", "connection", "capture", "gateway", "binding", "extra-input"];
    private static string[] Startup => ["missing-inbox", "missing-recovery", "missing-helper", "wrong-helper", "wrong-binding", "unknown-condition", "limit"];
    internal static IEnumerable<string> ReaderNames => from m in new[] { Modes.Balanced, Modes.Durable }
        from n in Malformed.Concat(Topology.Select(t => "topology-" + t)).Concat(Enumerable.Range(1, 11).Select(v => "host-v" + v)) select $"native-internal-seeded-reader:{m},{n}";
    internal static IEnumerable<string> RefusalNames => from m in new[] { Modes.Balanced, Modes.Durable } from n in Startup select $"native-internal-refusal:{m},{n}";
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        {
            Readers(mode);
            foreach (var name in Startup) Refuse(mode, name);
        }
    }
    private static StructuralDefinition Definition() => new([new("root", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "root", Entry: "proof"), new("Done", NodeKind.Final, "root")],
        [new("go", 0, "A", "Done", VisitMode.Reenter, "go")]);
    private static Node Machine() => Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(Definition())),
        ("InputInbox", Binding.Literal(new MachineInboxSettings(3, 8, 120000))),
        ("InternalExecution", Binding.Literal(new InternalExecutionSettings(8, "internal", new() { ["go"] = "permission" }))))
        .With(Control(), Wf.Node<NativeGuardProbe>("proof"));
    private static void Readers(string mode)
    {
        var h = new MachineInboxHarness(); var node = Machine(); var wf = Wf.Workflow("machine-inbox", node, ("permission", "wrong-type")); wf.InterventionRequested = true;
        using var registration = Register(wf); h.Start(mode);
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "go", "go")); h.Drain();
        var host = h.State; var raw = host.Frames[frame].State!.Deserialize<MachineState>()!; var source = host.Frames.Values.Single(f => f.Node == "internal");
        Check(host.Inboxes![frame].Pending.Count == 1 && host.Inboxes[frame].Pending[0].Id == "go",
            "NRD0: reader fixture did not retain its exact held input");
        var parent = (IInterventionParent)new StateMachine();
        RetryChild Child(InvocationInbox inbox) => new(source.Id, "fresh-successor", source.Node, source.Tag, source.Scope, frame, source.LexicalScope, inbox);
        foreach (var name in Malformed.Concat(Topology.Select(t => "topology-" + t)))
        {
            var state = raw; var inbox = Json.Read<InvocationInbox>(Json.Write(host.Inboxes![frame])); var child = Child(inbox); var definition = node;
            switch (name)
            {
                case "old-machine": state = state with { Version = 5 }; break;
                case "false-business": state = state with { BusinessReady = false }; break;
                case "no-profile": state = state with { Execution = null }; break;
                case "no-progress": state = state with { Settling = null }; break;
                case "limit": state = state with { Settling = state.Settling! with { Limit = 9 } }; break;
                case "lost-control": state = state with { Control = null }; break;
                case "no-original": state = state with { Control = state.Control! with { Original = "" } }; break;
                case "wrong-current": state = state with { Control = state.Control! with { Current = "other" } }; break;
                case "no-spec": state = state with { Control = state.Control! with { Spec = null! } }; break;
                case "purpose": state = state with { Control = state.Control! with { Spec = state.Control.Spec with { Purpose = (ControlPurpose)99 } } }; break;
                case "detail": state = state with { Control = state.Control! with { Spec = state.Control.Spec with { Detail = "" } } }; break;
                case "long-detail": state = state with { Control = state.Control! with { Spec = state.Control.Spec with { Detail = new string('x', 2049) } } }; break;
                case "visit": state = state with { Control = state.Control! with { Spec = state.Control.Spec with { Visit = "other" } } }; break;
                case "loop": state = state with { Control = state.Control! with { Spec = state.Control.Spec with { Purpose = ControlPurpose.Loop } } }; break;
                case "source": child = child with { Source = "other" }; break;
                case "successor": child = child with { Successor = source.Id }; break;
                case "node": child = child with { Node = "proof" }; break;
                case "tag": child = child with { Tag = null }; break;
                case "scheduling": child = child with { Scope = new Dictionary<string, JsonNode?> { [StateMachineControl.Key] = Values.From(new InternalControlSpec(ControlPurpose.Continue)) } }; break;
                case "borrowed": child = child with { LexicalScope = raw.Scopes!.Machine }; break;
                case "receipt-state": inbox.Receipts["go"] = inbox.Receipts["go"] with { Status = "Selected" }; break;
                case "receipt-marker": inbox.Receipts["go"] = inbox.Receipts["go"] with { EvaluationStarted = false }; break;
                case "payload": inbox.Pending[0] = inbox.Pending[0] with { Value = "changed" }; break;
                case "missing-payload": inbox.Pending.Clear(); break;
                default: definition = Alter(node, name[9..]); break;
            }
            var value = Values.From(state); var before = Json.Write(value); var beforeInbox = Json.Write(inbox); var refused = false;
            try { parent.PrepareRetry(definition, value, child); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused && Json.Write(value) == before && Json.Write(inbox) == beforeInbox,
                "NRD1: malformed retry preparation accepted or mutated " + name);
            Console.WriteLine($"PASS native-internal-seeded-reader:{mode},{name}");
        }
        for (var version = 1; version <= 11; version++)
        {
            var refused = false; try { new NativeModel(new()).UpgradeState(Json.Write(host), version); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "NRD2: old native schema adopted a held evaluation/control capability");
            Console.WriteLine($"PASS native-internal-seeded-reader:{mode},host-v{version}");
        }
    }
    private static Node Alter(Node original, string name)
    {
        var node = new Node(original.Id, original.Type); foreach (var input in original.Inputs) node.Inputs[input.Key] = input.Value;
        node.Children.AddRange(original.Children.Where(n => n.Id != "internal")); var control = Control();
        switch (name)
        {
            case "missing": return node;
            case "type": control = Wf.Node<NativeGuardProbe>("internal"); break;
            case "external": control.External = true; break;
            case "background": control.Background = true; break;
            case "child": control.Children.Add(Wf.Set("extra", "x", true)); break;
            case "connection": control.Connections.Add(new("a", "b")); break;
            case "capture": control.Captures["x"] = ""; break;
            case "gateway": control.Gateways["a"] = Gateway.Merge; break;
            case "binding": control.Inputs["Descriptor"] = Binding.Literal(new InternalControlSpec(ControlPurpose.Continue)); break;
            case "extra-input": control.Inputs["extra"] = Binding.Literal(true); break;
        }
        node.Children.Add(control); return node;
    }
    private static void Refuse(string mode, string name)
    {
        NativeGuardProbe.Calls = 0; var h = new MachineInboxHarness(); var node = Machine();
        if (name == "missing-inbox") node.Inputs.Remove("InputInbox");
        if (name == "missing-helper") node.Children.RemoveAll(n => n.Id == "internal");
        if (name == "wrong-helper") { node.Children.RemoveAll(n => n.Id == "internal"); node.Children.Add(Wf.Node<NativeGuardProbe>("internal")); }
        if (name == "wrong-binding") node.Children.Single(n => n.Id == "internal").Inputs["Descriptor"] = Binding.Literal(true);
        if (name == "unknown-condition") node.Inputs["InternalExecution"] = Binding.Literal(new InternalExecutionSettings(8, "internal", new() { ["other"] = "true" }));
        if (name == "limit") node.Inputs["InternalExecution"] = Binding.Literal(new InternalExecutionSettings(0, "internal"));
        var wf = Wf.Workflow("machine-inbox", node); wf.InterventionRequested = name != "missing-recovery"; using var registration = Register(wf);
        var expected = name is "missing-inbox" or "missing-recovery" ? "Statechart internal progress requires the qualified inbox/recovery profile."
            : name == "unknown-condition" ? "Invalid statechart condition mapping." : "Invalid statechart internal control definition.";
        var refused = false;
        try { h.Start(mode); }
        catch (InvalidOperationException error) when (error.Message.StartsWith("Kernel: I14 ", StringComparison.Ordinal) && error.Message.Contains(expected, StringComparison.Ordinal)) { refused = true; }
        Check(refused && NativeGuardProbe.Calls == 0 && h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Faulted,
            "NRD3: invalid fresh execution profile missed its precise refusal or ran Entry work: " + name);
        Console.WriteLine($"PASS native-internal-refusal:{mode},{name}");
    }
}
