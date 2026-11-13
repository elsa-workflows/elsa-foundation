using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal static class TerminalReceipts
{
    public static void Run(bool list)
    {
        var modes = new[] { Modes.Balanced, Modes.Durable };
        var endings = new[] { "completed", "cancelled-queued", "cancelled-selected", "cancelled-earlier" };
        var controls = new[] { (AdmissionFlaw.DropMetadata, "T1"), (AdmissionFlaw.IgnoreRetention, "T2"),
            (AdmissionFlaw.KeepPayload, "T3"), (AdmissionFlaw.Restamp, "T4"),
            (AdmissionFlaw.UnsafeNull, "T5"), (AdmissionFlaw.ReleaseClockEarly, "T6"), (AdmissionFlaw.RestampCompleted, "T7") };
        foreach (var mode in modes)
        foreach (var nested in new[] { false, true })
        foreach (var ending in endings)
            if (list) Console.WriteLine($"terminal:{mode},nested={nested},ending={ending}");
            else RunCase(mode, nested, ending);
        if (list)
        { Console.WriteLine("terminal:null-state"); Console.WriteLine("terminal:shared-clock"); Console.WriteLine("terminal:parent-notice"); }
        else { TerminalEdges.NullState(); TerminalEdges.SharedClock(); TerminalEdges.ParentNotice(); }
        foreach (var (defect, assertion) in controls)
        {
            if (list) { Console.WriteLine("terminal-control:" + defect); continue; }
            try
            {
                if (defect == AdmissionFlaw.UnsafeNull) TerminalEdges.NullState(defect);
                else if (defect == AdmissionFlaw.ReleaseClockEarly) TerminalEdges.SharedClock(defect);
                else if (defect == AdmissionFlaw.RestampCompleted) RunCase(Modes.Balanced, false, "cancelled-earlier", defect);
                else RunCase(Modes.Balanced, false, "completed", defect);
            }
            catch (InvalidOperationException error) when (error.Message.StartsWith(assertion + ":", StringComparison.Ordinal))
            { Console.WriteLine($"Detected {defect}: {error.Message}"); continue; }
            throw new InvalidOperationException($"Control {defect} missed named assertion {assertion}");
        }
        if (!list) Console.WriteLine("19/19 terminal schedules passed;7/7 named controls detected. Compatible-state in-memory model only.");
    }

    private static void RunCase(string mode, bool nested, string ending, AdmissionFlaw defect = AdmissionFlaw.None)
    {
        TerminalReceiver.Forbidden = false;
        TerminalReceiver.Closed = false;
        var world = new World(new Config(1, 1, Flaw.None, Native: true, AdmissionFlaws: defect)) { Auto = true };
        var actor = world.Of(0, 0);
        var target = InstanceRef.In(0, "terminal");
        var retention = ending == "cancelled-earlier" ? 10_000 : 1000;
        var receiver = Wf.Node<TerminalReceiver>("receiver", ("SelectOnly", ending == "cancelled-selected"),
            ("RetainHandled", ending == "cancelled-earlier"), ("Retention", retention))
            .With(Wf.Event("step", "step"), Wf.Event("waiting", "waiting"));
        var root = nested ? Wf.Sequence("parent", receiver, Wf.Set("after", "returned", true), Wf.Event("outer", "outer")) : receiver;
        var workflow = Wf.Workflow("terminal-probe", root);
        NativeWorkflows.Registry.Add(workflow);
        var serial = 0;
        var delayFinalCommit = false;
        long? closingPreparedAt = null;
        void Require(bool ok, string name, string detail)
        { if (!ok) throw new InvalidOperationException(name + ": " + detail); }
        void Apply(Act act)
        {
            world.Apply(act);
            if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
        }
        void Add(string kind, object? payload = null, StartSpec? start = null, bool reply = false)
        {
            var id = "terminal-message-" + ++serial;
            Apply(new("add", M: new Message(id, target, kind, payload is null ? null : Json.Write(payload), Start: start,
                ReplyTo: reply ? ReplyTo.Of("outside", id, world.Time + 30_000, beforeCommit: true) : null)));
        }
        void Drain()
        {
            for (var i = 0; i < 150; i++)
            {
                Apply(new("poll", actor.Id));
                if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
                var before = world.Deliveries.Count;
                var ran = false;
                if (actor.Worker.Ready.Contains(target.Id))
                {
                    Apply(new("turn", actor.Id, S: target.Id));
                    ran = true;
                    Require(world.Deliveries.Count == before, "T1", "metadata response escaped before commit");
                }
                else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) return;
                if (actor.Worker.NeedsFlush)
                {
                    // Let finalization sit staged for longer than R. The full window starts at durable disposition.
                    if (delayFinalCommit && ran && (ending != "completed" || TerminalReceiver.Closed))
                    { closingPreparedAt = world.Time; Apply(new("tick", 2000)); delayFinalCommit = false; }
                    Apply(new("flush", actor.Id));
                }
                if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
            }
            throw new InvalidOperationException("T1: terminal inbox failed to settle");
        }
        NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        Admission Request(string kind, object input)
        {
            var before = world.Deliveries.Select(x => x.Reply.Item).OfType<Admission>().Count();
            Add(kind, input, reply: true); Drain();
            var replies = world.Deliveries.Select(x => x.Reply.Item).OfType<Admission>().ToArray();
            Require(replies.Length == before + 1, "T1", "missing or duplicated correlated receipt");
            return replies[^1];
        }
        Apply(new("take", actor.Id));
        Add(Kinds.Start, start: NativeModel.Spec(workflow.Id, mode)); Drain();
        var frame = State().Frames.Values.Single(f => f.Node == "receiver").Id;
        var a = new InvocationInput(frame, "a", "business", "payload-canary-a");
        var b = new InvocationInput(frame, "b", "business", "payload-canary-b");
        Require(Request(InvocationInput.Kind, a).Result == "Accepted" && Request(InvocationInput.Kind, b).Order == 2,
            "T0", "normal invocation admission failed");
        Apply(new("tick", 2000)); // pending longer than the captured 1000ms terminal window
        delayFinalCommit = ending == "completed";
        if (ending != "cancelled-queued") { Add(NativeKinds.Resume, new ResumeStimulus("step")); Drain(); }
        var earlier = ending == "cancelled-earlier" ? State().Inboxes![frame].Receipts["a"] : null;
        var earlierTime = earlier?.Clock is { } oldReference ? CommitClockFacts.Read(world.Store.Instances[target.Id])!.GetValueOrDefault(oldReference) : null;
        if (earlier is not null) Apply(new("tick", 100));
        if (ending != "completed") { delayFinalCommit = true; Add(Kinds.Terminate); Drain(); }
        Require(closingPreparedAt is { } prepared && prepared < world.Time, "T0", "actual closing commit was not delayed");
        var inbox = State().Inboxes![frame];
        if (earlier is not null) Require(inbox.Receipts["a"] == earlier && earlier.Outcome == "handled" && earlier.Order == 1,
            "T7", "terminalization changed an earlier committed outcome, order or clock reference");
        Require(inbox.Closed == (ending == "completed" ? "completed" : "cancelled"), "T0", "terminal reason not committed");
        Require(inbox.Pending.Count == 0 && !world.Store.Instances[target.Id].State.Contains("payload-canary"), "T3", "receipt payload retained");
        Require(inbox.Receipts["b"].Status == "NotProcessed", "T0", "queued input not finalized");
        var expected = ending switch { "completed" or "cancelled-earlier" => "Completed", "cancelled-selected" => "Interrupted", _ => "NotProcessed" };
        Require(inbox.Receipts["a"].Status == expected, "T0", "selected/queued disposition conflated");
        var clocks = CommitClockFacts.Read(world.Store.Instances[target.Id])!;
        var finalized = clocks.GetValueOrDefault(inbox.Receipts["a"].Clock!);
        var until = finalized + inbox.Receipts["a"].Retention;
        var expectedTime = earlier is null ? world.Time : earlierTime;
        Require(finalized is not null && until is not null && expectedTime is not null &&
            until == expectedTime + retention && finalized == expectedTime,
            "T4", "long pending input lost its full final retention window");
        var latestUntil = inbox.Receipts.Values.Max(r => clocks.GetValueOrDefault(r.Clock!) + r.Retention);
        if (ending == "completed" && nested)
            Require(State().Variables["returned"]!.GetValue<bool>(), "T0", "ordinary caller did not continue");
        TerminalReceiver.Forbidden = true;
        NativeWorkflows.Registry.Remove(workflow.Id);
        var found = Request(ReceiptQuery.Kind, new ReceiptQuery(frame, "a"));
        Require(found.Result == "Found" && found.Actor == target.Id && found.Frame == frame && found.Status == expected && found.ValidUntil == until && found.FinalizedAt == finalized,
            "T1", "cold terminal query lost exact identity/outcome");
        Apply(new("tick", 400)); Apply(new("skew", 0, 100_000));
        found = Request(ReceiptQuery.Kind, new ReceiptQuery(frame, "a"));
        Require(found.ValidUntil == until, "T4", "query restamped promise or used unrelated wall clock");
        var duplicate = Request(InvocationInput.Kind, a);
        Require(duplicate.Result == "Duplicate", "T1", "closed duplicate was lost");
        Require(duplicate.Order == 1 && duplicate.Outcome == (expected == "Completed" ? "handled" : "cancelled") &&
            duplicate.ValidUntil == until && duplicate.FinalizedAt == finalized && State().Inboxes![frame].Receipts["a"].Clock == inbox.Receipts["a"].Clock,
            "T7", "query or retry changed retained outcome, order or promised deadline");
        Require(Request(InvocationInput.Kind, a with { Value = "different" }).Result == "Conflict", "T1", "closed conflict was lost");
        Require(Request(InvocationInput.Kind, a with { Id = "new" }).Result == "Ended", "T1", "ended invocation accepted new work");
        Add(Kinds.Delete); Drain();
        Require(world.Store.Instances.ContainsKey(target.Id), "T2", "delete removed an unexpired promise");
        Apply(new("tick", checked((int)(until!.Value - world.Time))));
        Require(Request(ReceiptQuery.Kind, new ReceiptQuery(frame, "a")).Result == "MissingOrExpired", "T4", "exact expiry boundary not enforced");
        if (latestUntil > world.Time)
        {
            Add(Kinds.Delete); Drain();
            Require(world.Store.Instances.ContainsKey(target.Id), "T2", "delete ignored a later receipt promise");
            Apply(new("tick", checked((int)(latestUntil!.Value - world.Time))));
        }
        Add(Kinds.Delete); Drain();
        Require(world.Store.Instances.ContainsKey(target.Id) == (nested && ending == "completed"), "T2", "normal delete ignored expiry/running owner");
        NativeWorkflows.Registry.Add(workflow);
        TerminalReceiver.Forbidden = false;
        Console.WriteLine($"PASS terminal:{mode},nested={nested},ending={ending}");
    }
}

public sealed class TerminalReceiver : CompositeActivity
{
    public static bool Forbidden, Closed;
    [Input] public bool SelectOnly { get; set; }
    [Input] public bool RetainHandled { get; set; }
    [Input] public int Retention { get; set; } = 1000;
    public TerminalReceiver()
    { if (Forbidden) throw new InvalidOperationException("T1: metadata constructed authored receiver"); }
    protected override void Execute(CompositeContext context)
    {
        context.RegisterInputInbox(2, 2, TimeSpan.FromMilliseconds(Retention));
        context.Schedule(context.Children.Single(c => c.Id == "step"));
    }
    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        var input = context.SelectInput()!;
        if (SelectOnly) context.Schedule(context.Children.Single(c => c.Id == "waiting"));
        else
        {
            context.FinishInput(input.Id, "handled");
            if (RetainHandled) context.Schedule(context.Children.Single(c => c.Id == "waiting"));
            else { context.Complete(); Closed = true; }
        }
    }
}
