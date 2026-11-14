using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static MachineInboxHarness;

internal static class ClrFaultWitness
{
    private static string[] Places => ["root", "sequence", "parallel", "entry", "exit", "action", "nested-entry", "nested-exit", "nested-action"];
    private static string[] FaultKinds => ["activation", "leaf", "start", "resume"];
    private static string[] Cuts => ["steady", "lost-before", "lost-answer", "cancel"];
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from p in Places from k in FaultKinds from c in Cuts select $"clr-recovery:{m},place={p},kind={k},cut={c}";
    internal static void Run()
    {
        foreach (var m in new[] { Modes.Balanced, Modes.Durable }) foreach (var p in Places) foreach (var k in FaultKinds) foreach (var c in Cuts) Run(m, p, k, c);
    }
    internal static void Red() => Run(Modes.Balanced, "entry", "leaf", "steady");
    private static void Run(string mode, string place, string kind, string cut)
    {
        ClrWork.Reset(kind); ChartMark.Calls.Clear(); var h = new MachineInboxHarness();
        var phase = place.Replace("nested-", "", StringComparison.Ordinal); var chart = phase is "entry" or "exit" or "action";
        var dependency = new Node("dependency", kind is "start" or "resume" ? typeof(ClrWait) : typeof(ClrDependency));
        dependency.Inputs["Local"] = chart ? Binding.Var("local") : Binding.Literal(17);
        Node Mark(string id) => Wf.Node<ChartMark>(id, ("Name", Binding.Literal(id)), ("Local", chart ? Binding.Var("local") : Binding.Literal(17)));
        Node root;
        if (chart)
        {
            var def = new StructuralDefinition([new("root", NodeKind.Root, Initial: "A"),
                new("A", NodeKind.Atomic, "root", Entry: phase == "entry" ? "dependency" : "entry", Exit: phase == "exit" ? "dependency" : "exit", Defaults: new Dictionary<string, JsonNode?> { ["local"] = Values.From(17) }),
                new("B", NodeKind.Final, "root", Entry: "final", Defaults: new Dictionary<string, JsonNode?> { ["local"] = Values.From(99) })],
                [new("go", 0, "A", "B", VisitMode.Reenter, "go", phase == "action" ? "dependency" : "action")]);
            var children = new List<Node> { dependency, Mark("final") }; children.AddRange(new[] { "entry", "exit", "action" }.Where(n => n != phase).Select(Mark));
            var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(def)),
                ("InputInbox", Binding.Literal(new { Capacity = 2, ReceiptCapacity = 8, RetentionMilliseconds = 120000L }))).With(children.ToArray());
            root = place.StartsWith("nested-", StringComparison.Ordinal) ? Wf.Flowchart("flow", [machine, Wf.Set("after", "returned", true)], [new("machine", "after")]) : machine;
        }
        else root = place switch { "sequence" => Wf.Sequence("sequence", Mark("before"), dependency, Mark("after")),
            "parallel" => Wf.Parallel("parallel", null, dependency, Wf.Sequence("sibling", Mark("sibling-mark"), Wf.Event("sibling-wait", "sibling"))), _ => dependency };
        var wf = Wf.Workflow("machine-inbox", root); wf.InterventionRequested = true; Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode);
        var frame = chart ? h.State.Frames.Values.Single(f => f.Node == "machine").Id : "";
        if (chart) { h.Send(InvocationInput.Kind, new InvocationInput(frame, "go", "go")); h.Drain(); }
        void Resume() { h.Send(NativeKinds.Resume, new ResumeStimulus("release"), false); h.Drain(); }
        if (kind == "resume") Resume();
        var source = h.State.Frames.Values.SingleOrDefault(f => f.Node == "dependency");
        Check(source?.Intervention is { Fault.Code: "exception" } && source.RecoveryInputs?["Local"]?.ToString() == "17" &&
            source.Intervention.Fault.Message.Contains("CLR dependency unavailable", StringComparison.Ordinal), "X1: authored CLR failure did not retain its ordinary intervention");
        var marks = ChartMark.Calls.ToArray(); var scopes = Json.Write(h.State.LexicalScopes);
        if (chart)
        {
            Check(h.State.Inboxes![frame].Receipts["go"].Status == (phase == "entry" ? "Queued" : "Selected"), "X2: CLR failure changed the selected receipt");
            h.Send(InvocationInput.Kind, new InvocationInput(frame, "later", "go")); h.Drain();
            h.Send(InvocationInput.Kind, new InvocationInput(frame, "go", "go")); h.Drain();
            Check(h.Replies[^1].Result == "Duplicate" && ChartMark.Calls.SequenceEqual(marks), "X2: blocked CLR admission replayed earlier work");
        }
        if (cut == "cancel")
        {
            h.Apply(new("add", M: new("cancel", h.Target, Kinds.Terminate, TargetCreation: 1))); h.Drain();
            Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Cancelled && (!chart || h.State.Inboxes![frame].Receipts.Values.All(r => r.Outcome == "cancelled")), "X3: CLR cancellation reopened failed work or lost terminal input");
        }
        else
        {
            h.Reload();
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                source = h.State.Frames.Values.Single(f => f.Node == "dependency");
                var id = h.Send(NativeRetry.Kind, new NativeRetry("clr-fix-" + attempt, source.Id, source.Intervention!.Id, "dependency repaired"));
                if (attempt == 1 && cut.StartsWith("lost-", StringComparison.Ordinal)) h.Cut(id, cut == "lost-answer"); else h.Drain(id);
                if (attempt == 1) Check(Json.Write(h.State.LexicalScopes) == scopes && ChartMark.Calls.SequenceEqual(marks), "X4: CLR retry reset completed phases or source resources");
                h.Reload(); if (kind == "resume") Resume();
                if (attempt == 1) Check(h.State.Frames.Values.Any(f => f.Node == "dependency" && f.Intervention?.Fault.Code == "exception"), "X4: repeated CLR failure lost its continuing obligation");
            }
            if (place == "parallel") { h.Send(NativeKinds.Resume, new ResumeStimulus("sibling"), false); h.Drain(); }
            Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && h.State.LexicalScopes is null &&
                ClrWork.Calls.All(c => c.Local == 17) && ClrWork.Calls.Select(c => c.Operation).Distinct().Count() == 1 &&
                ChartMark.Calls.All(c => c.Local == (c.Name == "final" ? 99 : 17)) && ChartMark.Calls.GroupBy(c => c.Name).All(g => g.Count() == 1), "X5: CLR repair repeated successful work or lost pinned values/operation identity");
            if (chart) Check(h.State.Inboxes![frame].Receipts["go"] is { Status: "Completed", Outcome: "handled" } &&
                h.State.Inboxes[frame].Receipts["later"] is { Status: "NotProcessed", Outcome: "completed" } &&
                h.History.Count(s => s.StartsWith("statechart:", StringComparison.Ordinal) && s.Contains("\"evaluated\"", StringComparison.Ordinal)) == 1, "X6: CLR repair reevaluated or prematurely settled an admitted event");
        }
        if (chart)
        {
            Kernel.Sim.Native.NativeWorkflows.Registry.Remove(wf.Id); h.Send(ReceiptQuery.Kind, new ReceiptQuery(frame, "go")); h.Drain();
            Check(h.Replies[^1] is { Result: "Found", ValidUntil: not null }, "X7: terminal CLR metadata reactivated authored material");
        }
        Console.WriteLine($"PASS clr-recovery:{mode},place={place},kind={kind},cut={cut}");
    }
}
