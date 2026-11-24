namespace Kernel.Sim;

internal static class ObservationBoundaryChecks
{
    private static async Task Pump(Worker w) { for (var i = 0; i < 12; i++) { await w.PollAsync(); await w.RunAsync(); } }
    private static StartSpec Spec(int g, string mode = Http.Respond) => Http.Spec(mode) with { History = History.Minimal, Creation = g == 0 ? null : new(g, g - 1, "start" + g) };
    private static Worker Direct(World w) => new("other", 0, w.Store, new WorkflowInstance([new Http(7, (_, _) => new((object?)"done"))]), new() { Monotonic = () => w.Time });
    private static void Require(bool ok) { if (!ok) throw new InvalidOperationException("Observation fixture prerequisite failed."); }

    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var scope in new[] { "ordinary", "issued", "event", "ticket" })
        foreach (var generation in new[] { 1, 2 })
        foreach (var posted in new[] { false, true })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true }; var a = w.Of(0, 0); var id = InstanceRef.In(0, "refused");
            await a.Runtime.ActivateAsync(0);
            w.Apply(new("add", M: new("first", id, "add", Json.Write(new Add("accepted")), Start: Counter.Spec with { History = History.Minimal, Creation = new(1, 0, "counter") }, TargetCreation: 1)));
            await Pump(a.Worker); var before = w.Store.Instances[id.Id]; Require(w.Check() is null && Json.Read<CounterState>(before.State).Tokens.SequenceEqual(["accepted"]));
            var input = new Message("refused", id, Kinds.Start, Start: Spec(generation), TargetCreation: generation, KeepUntil: w.Time + 60000);
            input = scope switch { "issued" => input.AsIssued(), "event" => input.WithInboxScope(-2, "outside"), "ticket" => input.WithInboxScope(100, "outside"), _ => input };
            var admission = w.Apply(new(posted ? "post" : "add", a.Id, M: input));
            var canonical = posted ? null : w.Store.Messages[input.AsOrdinary().InboxKey];
            Require(posted ? admission == "" : admission == "added" && canonical!.Message.InboxScope == 0 && canonical.Message.EventMatch is null);
            await Pump(a.Worker);
            check(w.Store.Instances[id.Id] == before && (posted || canonical!.Done) && w.Asked.Count == 0 && w.Check() is null && w.Settle() is null,
                $"the actual canonical refused creator closes only its own HTTP obligation, {scope}, g{generation}, posted={posted}");
        }

        foreach (var g in new[] { 0, 1 })
        foreach (var edit in new[] { "none", "intent", "parent-generation", "parent" })
        foreach (var sampleEdit in new[] { false, true })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true }; var id = InstanceRef.In(0, "known-header");
            await using var first = Direct(w); await new DurableHost([first]).ActivateAsync(0);
            w.Apply(new("add", M: new("first", id, Kinds.Start, Start: Spec(g), TargetCreation: g == 0 ? null : g))); await Pump(first);
            var real = w.Store.Instances[id.Id]; Require(real is { Status: InstanceStatus.Finished, Revision: 1 } && w.Check() is null); await first.DisposeAsync();
            var damaged = edit switch { "intent" => real with { StartIntent = "unissued" }, "parent-generation" => real with { ParentCreation = 77 }, "parent" => real with { Parent = "0/foreign" }, _ => real };
            w.Store.Instances[id.Id] = damaged; var before = sampleEdit ? w.Check() : null;
            await using var retiring = Direct(w); await new DurableHost([retiring]).ActivateAsync(0);
            var watch = w.Store.WatchDeletion(new(id.Id, g)); w.Apply(new("add", M: new("erase", id, Kinds.Delete, TargetCreation: g == 0 ? null : g))); await Pump(retiring);
            Require(watch.IsCompletedSuccessfully && watch.Result.Row == damaged && watch.Result.Receipt?.Entries == "[\"deleted\"]");
            var retired = w.Check(); await retiring.DisposeAsync(); var a = w.Of(0, 0); await a.Runtime.ActivateAsync(0);
            w.Apply(new("add", M: new("next", id, Kinds.Start, Start: Spec(g == 0 ? 0 : 2), TargetCreation: g == 0 ? null : 2))); await Pump(a.Worker);
            var after = w.Check(); var again = w.Check(); var end = w.Settle();
            check(edit == "none" ? before is null && retired is null && after is null && again is null && end is null :
                (!sampleEdit || before?.StartsWith("I10 ", StringComparison.Ordinal) == true) && retired?.StartsWith("I10 ", StringComparison.Ordinal) == true && after == retired && again == retired && end == retired,
                $"known creation header contradictions survive actual deletion and reuse, g{g}, {edit}, sampleEdit={sampleEdit}");
        }

        foreach (var g in new[] { 0, 1 })
        foreach (var direct in new[] { false, true })
        foreach (var cancel in new[] { false, true })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true }; var id = InstanceRef.In(0, "cancel-proof"); var a = w.Of(0, 0); await a.Runtime.ActivateAsync(0);
            Require(w.Apply(new("request", a.Id, S: Http.Timer, M: new("start", id, Kinds.Start, Start: Spec(g, Http.Timer), TargetCreation: g == 0 ? null : g, ReplyTo: "|q1"))) == "");
            await Pump(a.Worker); var caller = w.Asked.Single().Request; Require(w.Check() is null && !caller.Done);
            w.Apply(new("disconnect", S: caller.Registration, Flag: cancel)); if (direct) await a.Runtime.DeactivateAsync(0);
            await using var other = Direct(w); if (direct) await new DurableHost([other]).ActivateAsync(0); var owner = direct ? other : a.Worker;
            if (!cancel) w.Apply(new("add", M: new("stop", id, Kinds.Terminate, TargetCreation: g == 0 ? null : g)));
            await Pump(owner); var ended = w.Store.Instances[id.Id]; Require(ended is { Status: InstanceStatus.Cancelled, Revision: 2 } && caller is { Error: "disconnected", Ends: 1, Responses: 0 });
            var watch = w.Store.WatchDeletion(new(id.Id, g)); w.Apply(new("add", M: new("erase", id, Kinds.Delete, TargetCreation: g == 0 ? null : g))); await Pump(owner);
            Require(watch.IsCompletedSuccessfully && watch.Result.Row == ended); await other.DisposeAsync(); if (direct) await a.Runtime.ActivateAsync(0);
            w.Apply(new("add", M: new("next", id, Kinds.Start, Start: Spec(g == 0 ? 0 : 2), TargetCreation: g == 0 ? null : 2))); await Pump(a.Worker);
            check(w.Check() is null && w.Settle() == (cancel ? null : $"END {id} (timer) ended as Cancelled"),
                $"only a registered cancel actually applied by the model settles its retired HTTP occurrence, g{g}, direct={direct}, cancel={cancel}");
        }

        foreach (var variant in new[] { "legacy", "qualified", "negative-generation", "legacy-intent", "legacy-parent-generation", "qualified-parent-generation", "signed-minus-two", "signed-zero",
            "qualified-null-intent", "qualified-empty-intent", "qualified-pending-finished", "qualified-pending-empty-id" })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true }; var id = InstanceRef.In(0, "cold"); var g = variant.StartsWith("qualified", StringComparison.Ordinal) ? 1 : 0;
            var waiting = variant == "qualified-pending-empty-id";     // a held start is only valid on a Running row, so test the empty id on one
            await using var producer = Direct(w); await new DurableHost([producer]).ActivateAsync(0);
            w.Apply(new("add", M: new("start", id, Kinds.Start, Start: Spec(g, waiting ? Http.Wait : Http.Respond), TargetCreation: g == 0 ? null : g))); await Pump(producer);
            var real = w.Store.Instances[id.Id]; Require(real is { Revision: 1 } && real.Status == (waiting ? InstanceStatus.Running : InstanceStatus.Finished)); await producer.DisposeAsync();
            w.Store.Instances[id.Id] = variant switch { "negative-generation" => real with { Creation = -1 }, "legacy-intent" => real with { StartIntent = "unissued" },
                "legacy-parent-generation" or "qualified-parent-generation" => real with { ParentCreation = 77 }, "signed-minus-two" => real with { Revision = -2 }, "signed-zero" => real with { Revision = 0 },
                "qualified-null-intent" => real with { StartIntent = null }, "qualified-empty-intent" => real with { StartIntent = "" },
                "qualified-pending-finished" => real with { PendingStart = new InboxKey("held") }, "qualified-pending-empty-id" => real with { PendingStart = new InboxKey("") }, _ => real };
            var expected = variant is "legacy" or "qualified" or "signed-minus-two" or "signed-zero" ? null : $"I10 {id} has invalid creation metadata";
            check(w.Check() == expected && w.Check() == expected && w.Settle() == expected,
                $"cold samples validate local creation metadata without inventing historical revision bounds, {variant}");
        }
    }
}
