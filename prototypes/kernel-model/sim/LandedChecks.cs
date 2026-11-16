using Kernel.Native;
using Kernel.Sim.Native;
using static Kernel.Native.Wf;

namespace Kernel.Sim;

/// <summary>
/// Round 5 C, N7's landed half under a late answer (the review bot's reading of <see cref="Wire.CommitAsync"/>): a commit
/// executes and lands, its answer stays on the wire past the owner's lease, the owner stands down and takes again, and
/// only then does the wire deliver the old answer as a lost one. The store recorded the batch's sendings when it landed;
/// the stale failure must give nothing back to the pending list (the landing is recorded, and the take discarded the
/// memory the call came from), or the next landed batch of the same instance would carry those keys again and the
/// landed half would report a false collision. The instance sends once before its wait and once after it.
/// </summary>
public static class LandedChecks
{
    public static int Run()
    {
        var failures = 0;
        void Check(bool ok, string name) { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); if (!ok) failures++; }
        foreach (var lie in new[] { true, false }) Late(Check, lie);
        return failures;
    }

    /// <param name="lie">The old answer arrives as "connection lost" (an unknown outcome); otherwise it arrives intact, as a stale success.</param>
    private static void Late(Action<bool, string> check, bool lie)
    {
        var world = new World(new Config(1, 1, Flaw.None, Native: true)) { Auto = true };
        var actor = world.Of(0, 0);
        var target = InstanceRef.In(0, "late");
        var workflow = Workflow("landed-late", Sequence("root", Http("h1", "https://partner.example/first", "a"), Event("e", "late-go"), Http("h2", "https://partner.example/second", "b")));
        NativeWorkflows.Registry.Add(workflow);
        try
        {
            void Apply(Act act) { world.Apply(act); }
            string? Fault() => world.Faults.FirstOrDefault() ?? world.Check();
            void Drain()
            {
                for (var i = 0; i < 50; i++)
                {
                    Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
                    if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
                    else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) return;
                    if (actor.Worker.NeedsFlush) Apply(new("flush", actor.Id));
                    if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
                }
            }
            var name = lie ? "late lost answer" : "late stale success";
            Apply(new("take", actor.Id));
            Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec("landed-late", Modes.Balanced), KeepUntil: world.Time + 3_600_000)));
            // The start turn sends h1 and waits for the event; its commit goes on the wire by hand and executes, unanswered.
            for (var i = 0; i < 10 && !actor.Worker.Ready.Contains(target.Id); i++) { Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id)); if (actor.Worker.NeedsFlush) Apply(new("flush", actor.Id)); }
            Apply(new("turn", actor.Id, S: target.Id));
            check(actor.PendingSendings.Count == 1, $"{name}: the start turn's sending is pending on its owner");
            world.Auto = false;
            var call = actor.Calls.Count;
            Apply(new("flush", actor.Id));
            check(actor.Calls.Count == call + 1 && actor.Calls[call].Kind == "commit", $"{name}: the start turn's commit is on the wire");
            Apply(new("exec", actor.Id, call));
            check(world.Store.Instances.TryGetValue(target.Id, out var row) && row.Status == InstanceStatus.Running && world.Native.LandedKeys.Count == 1,
                $"{name}: the commit landed in the store and the store recorded its one sending");
            world.Auto = true;
            // The answer stays out past the lease: the owner stands down and takes the partition again.
            Apply(new("tick", 30_000));
            for (var i = 0; i < 10 && actor.Worker.Owns; i++) { Apply(new("beat", actor.Id)); Apply(new("poll", actor.Id)); }
            check(!actor.Worker.Owns, $"{name}: the owner stood down after its lease expired with the answer still out");
            Apply(new("take", actor.Id));
            check(actor.Worker.Owns && actor.PendingSendings.Count == 0, $"{name}: the owner took the partition again with nothing pending");
            // Now the old answer arrives. Whatever it says, the recorded landing stays recorded and nothing comes back to pending.
            Apply(new("answer", actor.Id, call, Flag: lie));
            check(actor.PendingSendings.Count == 0, $"{name}: the stale answer gave nothing back to the pending list");
            // The event releases the wait; the second sending lands in a batch of its own, with the first one's key nowhere near it.
            Apply(new("add", M: new("go", target, NativeKinds.Resume, Json.Write(new ResumeStimulus("late-go")), KeepUntil: world.Time + 3_600_000)));
            Drain();
            check(world.Store.Instances[target.Id].Status == InstanceStatus.Finished, $"{name}: the instance finished after the event");
            check(world.Native.LandedKeys.Count == 2 && world.Native.LandedKeys.Any(k => k.Contains("/h1/")) && world.Native.LandedKeys.Any(k => k.Contains("/h2/")),
                $"{name}: both sendings landed once each ({string.Join(", ", world.Native.LandedKeys)})");
            check(Fault() is null, $"{name}: no N7 fault from the late answer ({Fault() ?? "none"})");
        }
        finally { NativeWorkflows.Registry.Remove(workflow.Id); }
    }
}
