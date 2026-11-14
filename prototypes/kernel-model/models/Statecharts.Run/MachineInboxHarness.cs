using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal sealed class MachineInboxHarness
{
    internal readonly World World = new(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
    internal readonly InstanceRef Target = InstanceRef.In(0, "machine-inbox");
    private int _owner, _transport;
    internal Actor Actor => World.Of(_owner, 0);
    internal NativeState State => Json.Read<NativeState>(World.Store.Instances[Target.Id].State);
    internal Admission[] Replies => World.Deliveries.Select(d => d.Reply.Item).OfType<Admission>().ToArray();
    internal string[] History => World.Store.Journal.Values.Where(j => j.Instance == Target.Id).OrderBy(j => j.Position).SelectMany(j => Json.Read<string[]>(j.Entries)).ToArray();
    internal void Apply(Act act)
    {
        World.Apply(act);
        if (World.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
    }
    internal void Start(string mode)
    {
        Apply(new("take", Actor.Id));
        Apply(new("add", M: new("start", Target, Kinds.Start, Start: NativeModel.Spec("machine-inbox", mode) with { Creation = new(1, 0, "inbox") }))); Drain();
    }
    internal string Send(string kind, object payload, bool reply = true)
    {
        var id = "request-" + ++_transport;
        Apply(new("add", M: new(id, Target, kind, Json.Write(payload), ReplyTo: reply ? ReplyTo.Of("outside", id, World.Time + 120000) : null, TargetCreation: 1)));
        return id;
    }
    internal void Drain(string? untilReply = null)
    {
        for (var i = 0; i < 120; i++)
        {
            var a = Actor; Apply(new("poll", a.Id)); if (a.Worker.NeedsLoad) Apply(new("load", a.Id));
            if (a.Worker.Ready.Contains(Target.Id)) Apply(new("turn", a.Id, S: Target.Id));
            else if (!a.Worker.NeedsFlush && !a.Worker.NeedsMeet) return;
            if (a.Worker.NeedsFlush) Apply(new("flush", a.Id));
            if (untilReply is not null && World.Deliveries.Any(d => d.Reply.Request == untilReply)) return;
            if (a.Worker.NeedsMeet) Apply(new("meet", a.Id));
        }
        throw new InvalidOperationException("Q0: machine inbox did not settle");
    }
    internal void Reload()
    {
        Apply(new("crash", _owner)); Apply(new("tick", 20000)); _owner = 1 - _owner;
        Apply(new("take", Actor.Id)); Drain();
    }
    internal void Cut(InboxKey request, bool landed)
    {
        var prepared = false;
        for (var i = 0; i < 100 && !prepared; i++)
        {
            var a = Actor; Apply(new("poll", a.Id)); if (a.Worker.NeedsLoad) Apply(new("load", a.Id));
            if (a.Worker.Ready.Contains(Target.Id)) Apply(new("turn", a.Id, S: Target.Id));
            prepared = a.Worker.Queued > 0 && a.Worker.Messages.Any(m => m.Id == request && m.Taken);
            if (!prepared && a.Worker.NeedsFlush) Apply(new("flush", a.Id));
            if (!prepared && a.Worker.NeedsMeet) Apply(new("meet", a.Id));
        }
        Check(prepared, "Q9: uncertainty missed the admitted-input commit");
        World.Auto = false; var actor = Actor; var call = actor.Calls.Count;
        Apply(new("flush", actor.Id)); Check(actor.Calls.Count == call + 1 && actor.Calls[call].Kind == "commit", "Q9: missing physical commit");
        Apply(new(landed ? "exec" : "lose", actor.Id, call)); Apply(new("answer", actor.Id, call, Flag: true));
        World.Auto = true; Reload();
    }
    internal static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
}
