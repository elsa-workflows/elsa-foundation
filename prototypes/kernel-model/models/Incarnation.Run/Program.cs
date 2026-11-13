using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

var names = (from mode in new[] { Modes.Balanced, Modes.Durable } from nested in new[] { false, true }
    from cut in new[] { "recreation", "before-apply", "lost-ack" } select (mode, nested, cut)).ToArray();
var parentNames = (from mode in new[] { Modes.Balanced, Modes.Durable }
    from cut in new[] { "pending", "before-apply", "lost-ack" } select (mode, cut)).ToArray();
var transportNames = (from mode in new[] { Modes.Balanced, Modes.Durable } from kind in new[] { "event", "timer" } select (mode, kind)).ToArray();
if (args.SequenceEqual(["--transport-races"]))
{ foreach (var (mode, kind) in transportNames) new CreationWitness(mode, false).TransportRace(kind); await JobCreationRace.Run(); return; }
if (args.SequenceEqual(["--parent-race"]))
{ foreach (var (mode, cut) in parentNames) new CreationWitness(mode, false).ParentRace(cut); return; }
if (args.SequenceEqual(["--job-ack"]))
{ foreach (var qualified in new[] { false, true }) foreach (var caughtUp in new[] { false, true }) await JobAcknowledgement.Run(qualified, caughtUp); return; }
if (args.SequenceEqual(["--boundaries"])) { await BoundaryCases.Run(); return; }
if (args.SequenceEqual(["--list"]))
{
    foreach (var name in names) Console.WriteLine("creation:" + name);
    foreach (var name in parentNames) Console.WriteLine("creation:parent-race," + name);
    foreach (var name in transportNames) Console.WriteLine("creation:transport-race," + name);
    Console.WriteLine("creation:job-generation-race");
    foreach (var name in BoundaryCases.Names) Console.WriteLine("creation:" + name);
    foreach (var qualified in new[] { false, true }) foreach (var caughtUp in new[] { false, true }) Console.WriteLine($"creation:job-ack,qualified={qualified},caughtUp={caughtUp}");
    return;
}
if (args.Length != 0) throw new ArgumentException("Only --list is supported.");
foreach (var (mode, nested, cut) in names) new CreationWitness(mode, nested).Run(cut);
foreach (var (mode, cut) in parentNames) new CreationWitness(mode, false).ParentRace(cut);
foreach (var (mode, kind) in transportNames) new CreationWitness(mode, false).TransportRace(kind);
await JobCreationRace.Run();
await BoundaryCases.Run();
foreach (var qualified in new[] { false, true }) foreach (var caughtUp in new[] { false, true }) await JobAcknowledgement.Run(qualified, caughtUp);
Console.WriteLine("31/31 creation schedules/reader boundaries passed. Isolated mutations and provider/binary qualification remain separate.");

sealed partial class CreationWitness(string mode, bool nested)
{
    private readonly World _world = new(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
    private readonly InstanceRef _target = InstanceRef.In(0, "creation-probe");
    private int _node, _serial;
    private Actor Actor => _world.Of(_node, 0);
    private InstanceRow Row => _world.Store.Instances[_target.Id];
    private NativeState State => Json.Read<NativeState>(Row.State);
    private string Frame => State.Frames.Values.Single(f => f.Node == "receiver").Id;
    private object? Last => _world.Deliveries.Last(x => x.Reply.Channel == Request.Response).Reply.Item;
    private static void Require(bool ok, string check, string detail)
    { if (!ok) throw new InvalidOperationException(check + ": " + detail); }
    private void Apply(Act action)
    { _world.Apply(action); if (_world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error); }
    private StartSpec Spec(long generation) => NativeModel.Spec("creation-probe", mode) with { Creation = new(generation, generation - 1, "intent-" + generation) };
    private void Send(string kind, long? creation, object? payload = null, StartSpec? start = null, bool reply = false, InstanceRef? target = null)
    {
        var id = "creation-message-" + ++_serial;
        Apply(new("add", M: new Message(id, target ?? _target, kind, payload is null ? null : Json.Write(payload), Start: start,
            ReplyTo: reply ? ReplyTo.Of("outside", id, _world.Time + 120_000, beforeCommit: true) : null, TargetCreation: creation)));
    }
    private void Drain(InstanceRef? target = null)
    {
        var id = (target ?? _target).Id;
        for (var i = 0; i < 200; i++)
        {
            Apply(new("poll", Actor.Id)); if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
            if (Actor.Worker.Ready.Contains(id)) Apply(new("turn", Actor.Id, S: id));
            else if (!Actor.Worker.NeedsFlush && !Actor.Worker.NeedsMeet) return;
            if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id));
            if (Actor.Worker.NeedsMeet) Apply(new("meet", Actor.Id));
        }
        throw new InvalidOperationException("C0: creation witness exceeded its work bound");
    }
    private void Prepare(InstanceRef? target = null)
    {
        var id = (target ?? _target).Id;
        for (var i = 0; i < 100; i++)
        {
            Apply(new("poll", Actor.Id)); if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
            if (Actor.Worker.Ready.Contains(id))
            { Apply(new("turn", Actor.Id, S: id)); Require(Actor.Worker.Queued > 0, "C0", "no actual creation turn staged"); return; }
            if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id));
        }
        throw new InvalidOperationException("C0: creation did not become ready");
    }
    private void Takeover()
    {
        Apply(new("crash", _node)); Apply(new("tick", 20_000)); _node = 1 - _node;
        Apply(new("take", Actor.Id)); Require(Actor.Worker.Owns, "C0", "new owner did not take partition");
    }
    private void Refused(string kind, long? creation, string reason, object? payload = null, StartSpec? start = null)
    {
        var row = Row; var births = IdentityReceiver.Births;
        Send(kind, creation, payload, start, true); Drain();
        Require(Last is CreationResult result && result.Result == reason && Row == row && IdentityReceiver.Births == births,
            "C2", "wrong creation reached authored/model state or had no explicit result: " + Json.Write(Last));
    }
    private void Retire(long generation)
    {
        Send(NativeKinds.Resume, generation, new ResumeStimulus("finish")); Drain();
        Require(Row.Status == InstanceStatus.Finished, "C1", "ordinary workflow did not complete");
        if (nested) Require(State.Variables["returned"]!.GetValue<bool>(), "C1", "ordinary caller did not continue");
        Apply(new("tick", 2000)); Send(Kinds.Delete, generation); Drain();
        Require(Row is { Retired: true, State: "null", Model: "", Definition: "", CommitTimes: null } && Row.Creation == generation,
            "C3", "delete did not clear workflow content and retain the high-water mark");
    }
    public void Run(string cut)
    {
        var receiver = Wf.Node<IdentityReceiver>("receiver").With(Wf.Event("finish", "finish"));
        NativeWorkflows.Registry.Add(Wf.Workflow("creation-probe", nested ? Wf.Sequence("parent", receiver, Wf.Set("after", "returned", true)) : receiver));
        Apply(new("take", Actor.Id)); Send(Kinds.Start, 1, start: Spec(1));
        if (cut != "recreation")
        {
            Prepare(); _world.Auto = false; var old = Actor; var call = old.Calls.Count; Apply(new("flush", old.Id));
            Require(old.Calls.Count == call + 1 && old.Calls[call].Kind == "commit", "C0", "wrong first-creation cut");
            Apply(new(cut == "lost-ack" ? "exec" : "lose", old.Id, call));
            Require(_world.Store.Instances.ContainsKey(_target.Id) == (cut == "lost-ack"), "C1", "wrong actual creation application");
            Apply(new("answer", old.Id, call, Flag: true)); _world.Auto = true; Takeover();
        }
        Drain();
        Require(Row.Creation == 1 && !Row.Retired && Row.StartIntent == CreationProtocol.Fingerprint(Spec(1)), "C1", "initial/cold creation identity changed");
        var first = Frame;
        Send(InvocationInput.Kind, 1, new InvocationInput(first, "same-id", "business", "old-payload"), reply: true); Drain();
        Require(Last is Admission { Result: "Accepted", Order: 1 }, "C1", "first addressed input failed");
        Refused(Kinds.Start, 1, "ExistingCreation", start: Spec(1));
        Refused(Kinds.Start, 1, "StartConflict", start: Spec(1) with { Input = "changed-content" });
        Retire(1);
        Refused(Kinds.Start, 1, "RetiredOrConflictingCreation", start: Spec(1));
        Send(Kinds.Start, 2, start: Spec(2)); Drain();
        Require(Row.Creation == 2 && Frame == first, "C1", "fixture did not recreate equal local frame names under a new generation");
        Refused(InvocationInput.Kind, 1, "WrongCreation", new InvocationInput(first, "same-id", "business", "old-payload"));
        Refused(ReceiptQuery.Kind, 1, "WrongCreation", new ReceiptQuery(first, "same-id"));
        Refused(Kinds.Terminate, 1, "WrongCreation");
        Refused(NativeKinds.Resume, 1, "WrongCreation", new ResumeStimulus("finish"));
        Refused(InvocationInput.Kind, null, "IdentityRequired", new InvocationInput(first, "same-id", "business"));
        Require(State.Inboxes![first].Receipts.Count == 0 && Row.Status == InstanceStatus.Running, "C2", "stale traffic touched the new inbox");
        Send(InvocationInput.Kind, 2, new InvocationInput(Frame, "same-id", "business", "new-payload"), reply: true); Drain();
        Require(Last is Admission { Result: "Accepted", Order: 1 }, "C1", "correct new creation input failed");
        Retire(2); Send(Kinds.Start, 3, start: Spec(3)); Drain();
        Takeover(); Drain(); Send(ReceiptQuery.Kind, 3, new ReceiptQuery(Frame, "missing"), reply: true); Drain();
        Require(Row.Creation == 3 && Last is Admission { Result: "MissingOrExpired" }, "C1", "takeover created another incarnation");
        Console.WriteLine($"PASS creation:{mode},nested={nested},cut={cut}");
    }
}

public sealed class IdentityReceiver : CompositeActivity
{
    public static int Births;
    public IdentityReceiver() => Births++;
    protected override void Execute(CompositeContext context)
    {
        if (context.Creation <= 0) throw new InvalidOperationException("C1: receiver has no qualified creation");
        context.RegisterInputInbox(2, 4, TimeSpan.FromSeconds(1)); context.Schedule(context.Children.Single());
    }
    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        while (context.SelectInput() is { } input) context.FinishInput(input.Id, "handled");
        context.Complete();
    }
}
