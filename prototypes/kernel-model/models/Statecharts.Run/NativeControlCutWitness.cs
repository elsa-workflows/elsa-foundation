using System.Text.Json;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static NativeInternalWitness;

internal static class NativeControlCutWitness
{
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable }
        from purpose in new[] { "business", "prelude", "loop" } from cut in new[] { "lost-before", "lost-answer" }
        select $"native-control-cut:{m},purpose={purpose},cut={cut}";
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var purpose in new[] { "business", "prelude", "loop" })
        foreach (var cut in new[] { "lost-before", "lost-answer" }) Run(mode, purpose, cut);
    }
    private static void Run(string mode, string purpose, string cut)
    {
        var h = new MachineInboxHarness();
        var graph = new StructuralDefinition([new("root", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "root"), new("Done", NodeKind.Final, "root")],
            [new("auto", 0, "A", null, VisitMode.Internal, "", Kind: TriggerKind.Automatic), new("go", 1, "A", "Done", VisitMode.Reenter, "go")]);
        var conditions = new Dictionary<string, string> { ["auto"] = "enabled" }; if (purpose == "business") conditions["go"] = "permission";
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(graph)), ("InputInbox", Binding.Literal(new MachineInboxSettings(3, 8, 180000))),
            ("InternalExecution", Binding.Literal(new InternalExecutionSettings(4, "internal", conditions)))).With(Control());
        var changed = purpose == "prelude" ? (object)"wrong-type" : purpose == "loop";
        var wf = Wf.Workflow("machine-inbox", Wf.Parallel("parallel", null, machine,
            Wf.Sequence("repair", Wf.Event("wait", "cut-enable"), Wf.Set("change", "enabled", Binding.Literal(changed)))), ("enabled", false), ("permission", "wrong-type"));
        wf.InterventionRequested = true; Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode); NativeGuardWitness.Release(h, "cut-enable");
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        MachineState Chart() => h.State.Frames[frame].State!.Deserialize<MachineState>()!;
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "go", "go")); h.Drain();
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "tail", "unknown")); h.Drain();
        var before = Chart(); var source = h.State.Frames.Values.Single(f => f.Node == "internal"); var inbox = Json.Write(h.State.Inboxes![frame]);
        Check(before.Evaluating == "go" && before.BusinessReady == (purpose == "business") &&
            before.Control!.Spec.Purpose == (purpose == "loop" ? ControlPurpose.Loop : ControlPurpose.Guard) && source.Intervention is not null,
            "NRC1: control-cut fixture did not retain its exact guard/loop evaluation");
        var retry = new NativeRetry("repair", source.Id, source.Intervention!.Id, "explicit control recovery"); var transport = h.Send(NativeRetry.Kind, retry);
        var prepared = false;
        for (var i = 0; i < 100 && !prepared; i++)
        {
            var a = h.Actor; h.Apply(new("poll", a.Id)); if (a.Worker.NeedsLoad) h.Apply(new("load", a.Id));
            if (a.Worker.Ready.Contains(h.Target.Id)) h.Apply(new("turn", a.Id, S: h.Target.Id));
            prepared = a.Worker.Queued > 0 && a.Worker.Messages.Any(m => m.Id == transport && m.Taken);
            if (!prepared && a.Worker.NeedsFlush) h.Apply(new("flush", a.Id)); if (!prepared && a.Worker.NeedsMeet) h.Apply(new("meet", a.Id));
        }
        Check(prepared, "NRC2: control retry did not reach a normal prepared checkpoint");
        var previous = h.World.Store.Instances[h.Target.Id]; h.World.Auto = false; var actor = h.Actor; var call = actor.Calls.Count;
        h.Apply(new("flush", actor.Id)); Check(actor.Calls.Count == call + 1 && actor.Calls[call].Kind == "commit", "NRC2: control cut missed its physical commit");
        h.Apply(new(cut == "lost-answer" ? "exec" : "lose", actor.Id, call));
        if (cut == "lost-answer")
        {
            var receipt = h.State.Retries!["repair"]; var expected = before with { Control = before.Control! with { Current = receipt.Successor, ReboundFrom = source.Id } };
            Check(h.State.Retries.Count == 1 && Json.Write(receipt.Source) == Json.Write(source) && Json.Write(Chart()) == Json.Write(expected) &&
                Json.Write(h.State.Inboxes![frame]) == inbox && h.State.Agenda.Count(w => w.Frame == receipt.Successor && w.RetrySource == source.Id) == 1 &&
                !h.State.Frames.ContainsKey(receipt.Successor), "NRC3: control retry commit reset semantic progress or split receipt, rebind and owed successor");
        }
        else Check(h.World.Store.Instances[h.Target.Id] == previous, "NRC3: before-apply control loss changed the authoritative row");
        h.Apply(new("answer", actor.Id, call, Flag: true)); h.World.Auto = true; h.Reload();
        var after = Chart(); var resolved = h.State.Retries!["repair"];
        Check(h.State.Retries.Count == 1 && resolved.Source.Id == source.Id && after.Evaluating == "go" && after.BusinessReady == before.BusinessReady &&
            after.Control!.Spec.Purpose == before.Control!.Spec.Purpose && after.Control.Original != before.Control.Original &&
            after.Settling!.Steps == before.Settling!.Steps && Json.Write(after.Visits) == Json.Write(before.Visits) && Json.Write(h.State.Inboxes![frame]) == inbox &&
            h.History.Count(s => s.StartsWith("statechart:", StringComparison.Ordinal) && Json.Read<string?[]>(s[11..]) is var p && p[2] == "internal-recovered") == 1,
            "NRC4: uncertain control recovery duplicated release/evaluation or lost retained input/configuration/counter");
        var unchanged = Json.Write(after);
        h.Send(NativeRetry.Kind, retry); h.Drain();
        Check(h.World.Deliveries.Select(d => d.Reply.Item).OfType<NativeRetryResult>().Last() is { Result: "Duplicate" } duplicate && duplicate.Successor == resolved.Successor &&
            h.State.Retries.Count == 1 && Json.Write(Chart()) == unchanged, "NRC5: duplicate control recovery created another successor or released a new incident");
        h.Send(NativeRetry.Kind, retry with { Reason = "conflict" }); h.Drain();
        Check(h.World.Deliveries.Select(d => d.Reply.Item).OfType<NativeRetryResult>().Last().Result == "Conflict" && Json.Write(Chart()) == unchanged,
            "NRC5: conflicting control receipt changed retained work");
        Check(h.History.Count(s => s.StartsWith("statechart:", StringComparison.Ordinal) && Json.Read<string?[]>(s[11..]) is var p && p[1] == "go" && p[2] == "evaluated") == (purpose == "business" ? 1 : 0),
            "NRC6: control retry duplicated or invented business-rule evaluation");
        h.Apply(new("add", M: new("cancel", h.Target, Kinds.Terminate, TargetCreation: 1))); h.Drain();
        Check(h.State.Inboxes![frame].Receipts["go"] is { Status: "NotProcessed", Outcome: "cancelled", EvaluationStarted: true },
            "NRC6: control uncertainty lost honest terminal evaluation evidence");
        Console.WriteLine($"PASS native-control-cut:{mode},purpose={purpose},cut={cut}");
    }
}
