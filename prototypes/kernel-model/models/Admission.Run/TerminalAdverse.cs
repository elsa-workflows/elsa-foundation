using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

// Actual workflow-owner commits and transport uncertainty, not edited receipt rows or process-restart proof.
internal static class TerminalAdverse
{
    internal static void Run(bool list)
    {
        var count = 0;
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        foreach (var nested in new[] { false, true })
        foreach (var operation in new[] { "complete", "cancel-queued", "cancel-selected", "query" })
        foreach (var applied in new[] { false, true })
        {
            var name = $"{mode},nested={nested},operation={operation},applied={applied}";
            if (list) Console.WriteLine("terminal-adverse:" + name);
            else { new TerminalWire(mode, nested).Uncertain(operation, applied); Console.WriteLine("PASS terminal-adverse:" + name); }
            count++;
        }
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        {
            if (list) Console.WriteLine("terminal-adverse:stale-delete," + mode);
            else { new TerminalWire(mode, false).StaleDelete(); Console.WriteLine("PASS terminal-adverse:stale-delete," + mode); }
            count++;
        }
        if (list)
        { Console.WriteLine("terminal-adverse:query-reply-boundary"); Console.WriteLine("terminal-adverse-control:stale-delete-fence"); return; }
        new TerminalWire(Modes.Balanced, false).QueryReplyBoundary(); count++;
        Detect("Kernel: I1 a commit prepared under epoch ", () => new TerminalWire(Modes.Balanced, false, Flaw.FenceWithoutEpoch).StaleDelete());
        Console.WriteLine($"{count}/{count} terminal adverse schedules passed;1/1 internal control detected. Targeted-reply source mutation is separate. In-memory wire/reload only.");
    }

    private static void Detect(string prefix, Action run)
    {
        try { run(); }
        catch (InvalidOperationException error) when (error.Message.StartsWith(prefix, StringComparison.Ordinal))
        { Console.WriteLine("Detected terminal-adverse-control: " + error.Message); return; }
        throw new InvalidOperationException("Control missed named failure " + prefix);
    }
}

internal sealed class TerminalWire(string mode, bool nested, Flaw defect = Flaw.None)
{
    private readonly World _world = new(new Config(2, 1, defect, Native: true)) { Auto = true };
    private readonly InstanceRef _target = InstanceRef.In(0, "terminal-wire");
    private int _owner, _serial;
    private bool _cutArmed;
    private string _frame = "";
    private Actor Actor => _world.Of(_owner, 0);
    private InstanceRow Row => _world.Store.Instances[_target.Id];
    private NativeState State => Json.Read<NativeState>(Row.State);
    private InputReceipt Receipt => State.Inboxes![_frame].Receipts["a"];
    private Admission[] Replies => _world.Deliveries.Where(x => x.Reply.Channel == Request.Response).Select(x => x.Reply.Item).OfType<Admission>().ToArray();
    private static void Require(bool ok, string name, string detail)
    { if (!ok) throw new InvalidOperationException(name + ": " + detail); }
    private void Apply(Act act)
    {
        var before = Replies.Length;
        _world.Apply(act);
        if (_cutArmed && act.Kind == "turn") Require(Replies.Length == before, "U2", "receipt escaped before its cut commit");
        if (_world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
    }
    private void Send(string kind, object? payload = null, bool reply = false, StartSpec? start = null)
    {
        var id = "terminal-wire-" + ++_serial;
        Apply(new("add", M: new Message(id, _target, kind, payload is null ? null : Json.Write(payload), Start: start,
            ReplyTo: reply ? ReplyTo.Of("outside", id, _world.Time + 120_000, beforeCommit: true) : null)));
    }
    private void Drain()
    {
        for (var i = 0; i < 200; i++)
        {
            Apply(new("poll", Actor.Id));
            if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
            if (Actor.Worker.Ready.Contains(_target.Id)) Apply(new("turn", Actor.Id, S: _target.Id));
            else if (!Actor.Worker.NeedsFlush && !Actor.Worker.NeedsMeet) return;
            if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id));
            if (Actor.Worker.NeedsMeet) Apply(new("meet", Actor.Id));
        }
        throw new InvalidOperationException("U0: drain exceeded its work bound");
    }
    private void Prepare(bool closing = false)
    {
        for (var i = 0; i < 150; i++)
        {
            Apply(new("poll", Actor.Id));
            if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
            if (Actor.Worker.Ready.Contains(_target.Id))
            {
                Apply(new("turn", Actor.Id, S: _target.Id));
                Require(Actor.Worker.Queued > 0, "U0", "no actual turn was staged");
                if (!closing || TerminalReceiver.Closed) return;
            }
            if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id));
            if (Actor.Worker.NeedsMeet) Apply(new("meet", Actor.Id));
        }
        throw new InvalidOperationException("U0: closing turn never became ready");
    }
    private void Start(bool selected = false)
    {
        TerminalReceiver.Forbidden = false; TerminalReceiver.Closed = false;
        var receiver = Wf.Node<TerminalReceiver>("receiver", ("SelectOnly", selected), ("Retention", 60_000))
            .With(Wf.Event("step", "step"), Wf.Event("waiting", "waiting"));
        var root = nested ? Wf.Sequence("parent", receiver, Wf.Set("after", "returned", true), Wf.Event("outer", "outer")) : receiver;
        NativeWorkflows.Registry.Add(Wf.Workflow("terminal-wire-probe", root));
        Apply(new("take", Actor.Id)); Send(Kinds.Start, start: NativeModel.Spec("terminal-wire-probe", mode)); Drain();
        _frame = State.Frames.Values.Single(f => f.Node == "receiver").Id;
        Send(InvocationInput.Kind, new InvocationInput(_frame, "a", "business", "wire-canary-a"), reply: true); Drain();
        Send(InvocationInput.Kind, new InvocationInput(_frame, "b", "business", "wire-canary-b"), reply: true); Drain();
        Require(Receipt.Status == "Queued", "U0", "setup admission failed");
        if (selected) { Send(NativeKinds.Resume, new ResumeStimulus("step")); Drain(); Require(Receipt.Status == "Selected", "U0", "setup selection failed"); }
    }
    private void Takeover(bool zombie = false)
    {
        Apply(new("crash", _owner, Flag: zombie)); Apply(new("tick", 20_000));
        _owner = 1 - _owner; Apply(new("take", Actor.Id));
        Require(Actor.Worker.Owns, "U0", "new owner failed to take the expired partition");
    }
    private Admission Query()
    {
        var before = Replies.Length;
        Send(ReceiptQuery.Kind, new ReceiptQuery(_frame, "a"), reply: true); Drain();
        Require(Replies.Length == before + 1, "U2", "fresh query did not return one correlated outcome");
        return Replies[^1];
    }

    internal void Uncertain(string operation, bool applied)
    {
        Start(operation == "cancel-selected");
        if (operation == "query") { Send(NativeKinds.Resume, new ResumeStimulus("step")); Drain(); }
        var original = operation == "query" ? Query() : null;
        _cutArmed = true;
        Send(operation == "query" ? ReceiptQuery.Kind : operation.StartsWith("cancel", StringComparison.Ordinal) ? Kinds.Terminate : NativeKinds.Resume,
            operation == "query" ? new ReceiptQuery(_frame, "a") : operation == "complete" ? new ResumeStimulus("step") : null,
            reply: operation == "query");
        Prepare(closing: operation == "complete");
        var rowBefore = Row;
        Apply(new("tick", 1000)); // the turn's staging time is not the final disposition time
        var commitAt = _world.Time;
        var countBefore = Replies.Length;
        _world.Auto = false; var old = Actor; var call = old.Calls.Count;
        Apply(new("flush", old.Id));
        Require(old.Calls.Count == call + 1 && old.Calls[call].Kind == "commit", "U0", "cut missed the actual commit");
        Apply(new(applied ? "exec" : "lose", old.Id, call));
        Require(applied ? Row.Revision > rowBefore.Revision : Row == rowBefore, "U1", "wrong physical application boundary");
        var appliedReceipt = applied ? Receipt : null;
        Apply(new("answer", old.Id, call, Flag: true));
        Require(Replies.Length == countBefore, "U2", "unknown acknowledgement released a semantic receipt");
        _world.Auto = true; _cutArmed = false; Takeover(); Drain();
        var result = Query();
        var expected = operation == "cancel-selected" ? "Interrupted" : operation == "cancel-queued" ? "NotProcessed" : "Completed";
        Require(result is { Result: "Found", Order: 1 } && result.Actor == _target.Id && result.Frame == _frame && result.Status == expected,
            "U1", "recovery changed addressed order or terminal disposition");
        Require(result.FinalizedAt is { } time && result.ValidUntil == time + 60_000 &&
            (operation == "query" ? result.FinalizedAt == original!.FinalizedAt && result.ValidUntil == original.ValidUntil
                : applied ? result.FinalizedAt == commitAt : result.FinalizedAt >= commitAt + 20_000),
            "U3", "uncertainty shortened or restamped the immutable disposition window");
        if (appliedReceipt is not null) Require(Receipt == appliedReceipt, "U1", "lost acknowledgement changed an already committed receipt");
        Require(State.Inboxes![_frame].Pending.Count == 0 && !Row.State.Contains("wire-canary", StringComparison.Ordinal), "U1", "terminal receipt still owns queued business payload");
        if (nested && operation is "complete" or "query") Require(State.Variables["returned"]!.GetValue<bool>(), "U1", "ordinary caller did not continue");
        Require(_world.Deliveries.Where(x => x.Reply.Channel == Request.Response).GroupBy(x => x.Reply.Request).All(g => g.Count() == 1),
            "U2", "one transport request received multiple semantic responses");
        var beforeRetry = result;
        Send(InvocationInput.Kind, new InvocationInput(_frame, "a", "business", "wire-canary-a"), reply: true); Drain();
        Require(Replies[^1] == beforeRetry with { Result = "Duplicate" }, "U1", "retained retry changed the committed result");
    }

    internal void StaleDelete()
    {
        Start(); Send(NativeKinds.Resume, new ResumeStimulus("step")); Drain();
        Require(Row.Status == InstanceStatus.Finished, "U0", "delete setup did not end normally");
        for (var elapsed = 0; elapsed < 60_000; elapsed += 5000) { Apply(new("tick", 5000)); Apply(new("beat", Actor.Id)); }
        Send(Kinds.Delete); Prepare();
        _world.Auto = false; var old = Actor; var call = old.Calls.Count; Apply(new("flush", old.Id));
        Require(old.Calls.Count == call + 1 && old.Calls[call].Kind == "commit", "U0", "no actual delete commit staged");
        _world.Auto = true; Takeover(zombie: true);
        var stored = Row; var position = _world.Store.Partitions[0].Position;
        Apply(new("exec", old.Id, call)); Apply(new("answer", old.Id, call));
        Require(!old.Worker.Owns && _world.Store.Instances.GetValueOrDefault(_target.Id) == stored && _world.Store.Partitions[0].Position == position,
            "U4", "stale delete changed the ended row or journal position");
        Drain();
        Require(!_world.Store.Instances.ContainsKey(_target.Id), "U4", "current-owner replay of expired delete did not complete");
        Send(Kinds.Delete); Drain();
        Require(!_world.Store.Instances.ContainsKey(_target.Id), "U4", "repeated delete recreated its target");
    }

    internal void QueryReplyBoundary()
    {
        Start(); Send(NativeKinds.Resume, new ResumeStimulus("step")); Drain();
        _cutArmed = true;
        Send(ReceiptQuery.Kind, new ReceiptQuery(_frame, "a"), reply: true); Prepare();
        Apply(new("flush", Actor.Id)); _cutArmed = false; Drain();
        Require(Replies[^1].Result == "Found", "U2", "committed targeted query response missing");
        Console.WriteLine("PASS terminal-adverse:query-reply-boundary");
    }
}
