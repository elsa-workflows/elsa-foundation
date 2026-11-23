using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

internal static class NativeInternalWitness
{
    // Proof registrations must also be removed when an assertion fails.
    internal static IDisposable Register(Workflow workflow) => new Registration(workflow);

    private sealed class Registration : IDisposable
    {
        private readonly string _id;
        internal Registration(Workflow workflow)
        {
            _id = workflow.Id;
            NativeWorkflows.Registry.Add(workflow);
        }
        public void Dispose() => NativeWorkflows.Registry.Remove(_id);
    }

    internal static IEnumerable<string> Names => from mode in new[] { Modes.Balanced, Modes.Durable }
        from nested in new[] { false, true } select $"native-internal:baseline,{mode},nested={nested}";
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var nested in new[] { false, true }) Baseline(mode, nested);
    }
    internal static Node Control() => Wf.Node<StateMachineControl>("internal", (nameof(StateMachineControl.Descriptor), Binding.Var(StateMachineControl.Key)));
    private static void Baseline(string mode, bool nested)
    {
        var h = new MachineInboxHarness();
        var definition = new StructuralDefinition([new("root", NodeKind.Root, Initial: "Checking"),
            new("Checking", NodeKind.Atomic, "root", Entry: "gate", Defaults: new() { ["permission"] = Values.From(true) }),
            new("Review", NodeKind.Compound, "root", "InnerFinal"), new("InnerFinal", NodeKind.Final, "Review"),
            new("Waiting", NodeKind.Atomic, "root"), new("Done", NodeKind.Final, "root")],
            [new("advance", 0, "Checking", "Review", VisitMode.Reenter, "", Kind: TriggerKind.Automatic),
             new("complete", 1, "Review", "Waiting", VisitMode.Reenter, "", Kind: TriggerKind.Completion),
             new("finish", 2, "Waiting", "Done", VisitMode.Reenter, "finish")]);
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(definition)),
            ("InputInbox", Binding.Literal(new MachineInboxSettings(4, 8, 90_000))),
            ("InternalExecution", Binding.Literal(new InternalExecutionSettings(8, "internal", new() { ["advance"] = "permission" }))))
            .With(Control(), Wf.Event("gate", "entry"));
        var root = nested ? Wf.Flowchart("flow", [machine, Wf.Set("after", "returned", true)], [new("machine", "after")]) : machine;
        var wf = Wf.Workflow("machine-inbox", root, ("permission", false)); wf.InterventionRequested = true; using var registration = Register(wf);
        h.Start(mode);
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        Check(h.State.CheckpointControl && h.State.Inboxes![frame].Receipts.Count == 0,
            "NI6: initial Entry omitted its native checkpoint capability gate");
        var refused = false;
        try { _ = new NativeModel(new()).UpgradeState(Json.Write(h.State), 11); }
        catch (InvalidOperationException error) when (error.Message == "Legacy native state contains unsupported evaluation data.") { refused = true; }
        Check(refused, "NI6: old native host adopted the internal profile before an event receipt existed");
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "finish-input", "finish")); h.Drain();
        Check(h.State.Inboxes![frame].Receipts["finish-input"].Status == "Queued", "NI1: pending Entry evaluated a business input");
        h.Reload();
        var wait = h.World.Store.Subscriptions.Single(s => s.Instance == h.Target.Id && s.Key == "event:entry");
        var delivery = Events.Route(new(wait.Key, "release", null, h.World.Time + 120_000), wait);
        Check(h.World.Store.AddEventAsync(delivery).Result is { Added: true } result && result.Target == h.Target, "NI0: actual entry delivery missed its wait");
        h.World.Oracle.Note(delivery.Message); h.Drain();
        Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && h.State.LexicalScopes is null,
            "NI2: automatic/completion/native input did not finish through ordinary phases");
        if (nested) Check(h.State.Variables["returned"]!.ToString() == "true", "NI2: nested internal machine did not return");
        var receipt = h.State.Inboxes![frame].Receipts["finish-input"];
        Check(receipt.Status == "Completed" && receipt.Outcome == "handled" && receipt.EvaluationStarted && h.State.Inboxes[frame].Pending.Count == 0,
            "NI3: native evaluation marker/outcome or payload cleanup was lost");
        var trace = h.History.Where(s => s.StartsWith("statechart:", StringComparison.Ordinal))
            .Select(s => JsonSerializer.Deserialize<string?[]>(s[11..])!).ToArray();
        var automatic = Array.FindIndex(trace, p => p[2] == "internal-evaluated" && p[3] == "Automatic");
        var completion = Array.FindIndex(trace, p => p[2] == "internal-evaluated" && p[3] == "Completion");
        var business = Array.FindIndex(trace, p => p[1] == "finish-input" && p[2] == "evaluated");
        Check(automatic >= 0 && completion > automatic && business > completion && trace.Count(p => p[1] == "finish-input" && p[2] == "evaluated") == 1,
            "NI4: queued business input overtook internal progress or evaluation replayed");
        Check(h.World.Store.Instances[h.Target.Id].StateVersion == 13, "NI5: new internal checkpoint lacks its native host gate");
        NativeWorkflows.Registry.Remove(wf.Id);
        h.Send(ReceiptQuery.Kind, new ReceiptQuery(frame, "finish-input")); h.Drain();
        Check(h.Replies.Last() is { Result: "Found", Status: "Completed", Outcome: "handled", EvaluationStarted: true }, "NI3: pure terminal lookup lost evaluation evidence");
        Console.WriteLine($"PASS native-internal:baseline,{mode},nested={nested}");
    }
    internal static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
}
