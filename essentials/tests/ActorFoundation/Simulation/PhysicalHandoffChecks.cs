namespace Kernel.Sim;

internal static partial class GenerationSamplingChecks
{
    private static Worker RecoveryOwner(World world) => new("other", 0, world.Store,
        new WorkflowInstance([new Http(8, (_, _) => new ValueTask<object?>("done"))]), new() { Monotonic = () => world.Time });

    private static async Task CheckPhysicalHandoffs(Action<bool, string> check)
    {
        foreach (var priorGeneration in new[] { 0, 1 })
        foreach (var outcome in new[] { "finish", "terminate" })
        foreach (var sampled in new[] { false, true })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            var id = InstanceRef.In(0, "handoff"); var owner = w.Of(0, 0);
            await using var old = RecoveryOwner(w);
            await new DurableHost([old]).ActivateAsync(0);
            long? gen = priorGeneration == 0 ? null : priorGeneration;
            var wait = Http.Spec(Http.Wait) with { History = History.Minimal, Creation = gen is null ? null : new(1, 0, "before") };
            w.Apply(new("add", M: new("start", id, Kinds.Start, Start: wait, TargetCreation: gen)));
            await Pump(old); var first = w.Check();
            w.Apply(new("add", M: new("end", id, outcome == "finish" ? "resume" : Kinds.Terminate, TargetCreation: gen)));
            await Pump(old); w.Time += 350; await Pump(old); var terminal = w.Store.Instances[id.Id];
            w.Apply(new("add", M: new("remove", id, Kinds.Delete, TargetCreation: gen))); await Pump(old);
            var closed = w.Store.Instances.GetValueOrDefault(id.Id); var middle = sampled ? w.Check() : null;
            var receipt = w.Store.Journal.Values.Single(j => j.Revision == -1);
            await old.DisposeAsync(); await owner.Runtime.ActivateAsync(0);
            var counter = Counter.Spec with { History = History.Minimal, Creation = gen is null ? null : new(2, 1, "after") };
            w.Apply(new("add", M: new("counter", id, "add", Json.Write(new Add("next")), Start: counter, TargetCreation: counter.Creation?.Generation)));
            await Pump(owner.Worker); var now = w.Store.Instances[id.Id];
            var end = w.Settle();
            check(first is null && middle is null && w.Check() is null && end == (outcome == "finish" ? null : $"END {id} (wait) ended as Cancelled") &&
                terminal.Status == (outcome == "finish" ? InstanceStatus.Finished : InstanceStatus.Cancelled) &&
                closed == (gen is null ? null : CreationProtocol.Retire(terminal)) && receipt.Entries == """["deleted"]""" &&
                now is { Model: "counter", Status: InstanceStatus.Running, Revision: 1 } && now.Creation == (gen is null ? 0 : 2),
                $"retirement keeps the old HTTP end across a direct-owner handoff to Counter, g{priorGeneration}, {outcome}, sampled={sampled}: {end}");
        }

        foreach (var genuine in new[] { false, true })
        foreach (var sampleMarker in new[] { false, true })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            var id = InstanceRef.In(0, "receipts"); var owner = w.Of(0, 0);
            await using var old = RecoveryOwner(w); await new DurableHost([old]).ActivateAsync(0);
            for (var generation = 1; generation <= 2; generation++)
            {
                var spec = Http.Spec(Http.Respond) with { History = History.Minimal, Creation = new(generation, generation - 1, "start-" + generation) };
                w.Apply(new("add", M: new("start-" + generation, id, Kinds.Start, Start: spec, TargetCreation: generation)));
                await Pump(old);
                if (generation == 1) { w.Apply(new("add", M: new("delete-one", id, Kinds.Delete, TargetCreation: 1))); await Pump(old); }
            }
            var second = w.Store.Instances[id.Id]; var first = w.Check();
            if (genuine) { w.Apply(new("add", M: new("delete-two", id, Kinds.Delete, TargetCreation: 2))); await Pump(old); }
            else w.Store.Instances[id.Id] = CreationProtocol.Retire(second);
            var middle = sampleMarker ? w.Check() : null;
            await old.DisposeAsync(); await owner.Runtime.ActivateAsync(0); await Start(w, owner, id, 3);
            var after = w.Check(); var end = w.Settle();
            var mismatch = sampleMarker ? $"I22 {id} has an invalid lifecycle tombstone" : $"I10 {id} replaced a different checkpoint at its fenced input";
            check(first is null && second.Creation == 2 && w.Store.Instances[id.Id].Creation == 3 &&
                w.Store.Journal.Values.Count(j => j.Revision == -1) == (genuine ? 2 : 1) &&
                middle == (!genuine && sampleMarker ? mismatch : null) && after == (genuine ? null : mismatch) && end == after,
                $"each sampled active lifetime needs its own real retirement, genuine={genuine}, sampleMarker={sampleMarker}: {middle}, {after}, {end}");
        }

        foreach (var inlet in new[] { "durable", "expired", "live" })
        {
            var (w, owner, id) = await New();
            var spec = Counter.Spec with { History = History.Minimal, Creation = new(1, 0, "counter") };
            w.Apply(new("add", M: new("original", id, "add", Json.Write(new Add("token")), Start: spec, TargetCreation: 1)));
            await Pump(owner.Worker); var actual = w.Store.Instances[id.Id];
            var rejected = Http.Spec(Http.Respond) with { History = History.Minimal, Creation = new(2, 1, "declined") };
            var command = new Message("refused", id, Kinds.Start, Start: rejected, TargetCreation: 2, KeepUntil: w.Time + 60000);
            if (inlet == "durable") w.Apply(new("add", M: command));
            else w.Apply(new("request", 0, S: Http.Respond, M: command with { ReplyTo = inlet == "expired" ? "|q0" : "|q1" }));
            if (inlet == "expired") w.Apply(new("tick", 300));
            await Pump(owner.Worker);
            var request = w.Asked.LastOrDefault().Request;
            var disposition = inlet switch
            {
                "durable" => request is null && w.Store.Messages[command.InboxKey].Done,
                "expired" => request is { Error: "timeout", Responses: 0, Ends: 1 },
                _ => request?.Answer is CreationResult { Result: "StartConflict", Requested: 2, Current: 1 }
            };
            check(disposition && w.Store.Instances[id.Id] == actual && w.Check() is null && w.Settle() is null,
                $"actual host refusal discharges exactly the admitted creator regardless of its return route, {inlet}");
        }
    }
}
