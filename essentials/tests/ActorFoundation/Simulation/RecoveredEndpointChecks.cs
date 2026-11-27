namespace Kernel.Sim;

internal static partial class GenerationSamplingChecks
{
    private static async Task CheckRecoveredEndpoints(Action<bool, string> check)
    {
        foreach (var history in new[] { History.Minimal, History.Full })
        foreach (var source in new[] { "current", "prior", "prior with missing marker" })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true }; var current = w.Of(0, 0);
            await using var prior = new Worker("prior", 0, w.Store, new WorkflowInstance([new Http(8, (_, _) => new ValueTask<object?>("done"))]),
                new WorkerOptions { Monotonic = () => w.Time });
            var recovered = source != "current";
            if (recovered) await new DurableHost([prior]).ActivateAsync(0); else await current.Runtime.ActivateAsync(0);
            var owner = recovered ? prior : current.Worker;
            var sub = new SubscriptionRow("allocate", "", "http", Http.Spec(Http.Wait) with { History = history });
            await Events.SubscribeAsync(w.Store, sub, 1);
            EventAcceptance? accepted = null;
            await Events.PublishAsync(w.Store, new("allocate", "event", null, w.Time + 60000), matched: (_, result) => accepted = result);
            var id = accepted!.Target; await Pump(owner);
            w.Apply(new("add", M: new("resume", id, "resume"))); await Pump(owner); w.Time += 350; await Pump(owner);
            var finished = w.Store.Instances[id.Id]; var initial = w.Check();
            w.Apply(new("add", M: new("erase", id, Kinds.Delete))); await Pump(owner);
            var marker = w.Store.Instances[id.Id];
            if (recovered) { await prior.DisposeAsync(); await current.Runtime.ActivateAsync(0); }
            string? expected = null;
            if (source == "prior with missing marker")
            {
                w.Store.Instances.Remove(id.Id);
                expected = $"I10 the sampled physical row of {id} disappeared";
            }
            check(finished is { Creation: 0, Status: InstanceStatus.Finished } && CreationProtocol.IsPhysicalAddress(id.Id) &&
                marker == CreationProtocol.Retire(finished) && w.Store.Journal.Values.Any(j => j.Instance == id.Id && j.Entries == """["deleted"]""") &&
                initial is null && w.Check() == expected && w.Settle() == expected, $"a real generated HTTP run retains its canonical zero-generation retirement, {history}, {source}");
        }

        foreach (var generation in new[] { 0, 1 })
        foreach (var legitimate in new[] { false, true })
        foreach (var early in new[] { false, true })
        {
            var (w, owner, id) = await New(); long? addressed = generation == 0 ? null : generation;
            var spec = Http.Spec(Http.Timer) with { Creation = generation == 0 ? null : new(1, 0, "timer"), History = History.Minimal };
            w.Apply(new("request", 0, S: Http.Timer, M: new("start", id, Kinds.Start, Start: spec, TargetCreation: addressed, ReplyTo: "|q1")));
            await Pump(owner.Worker); var caller = w.Asked.Single().Request;
            w.Apply(new("disconnect", S: caller.Registration, Flag: legitimate));
            if (!legitimate) w.Apply(new("add", M: new("stop", id, Kinds.Terminate, TargetCreation: addressed)));
            await Pump(owner.Worker); var terminal = w.Store.Instances[id.Id];
            string? expected = legitimate ? null : $"END {id} (timer) ended as Cancelled";
            var sampled = !early || w.Settle() == expected;
            w.Apply(new("add", M: new("erase", id, Kinds.Delete, TargetCreation: addressed))); await Pump(owner.Worker);
            var removed = w.Store.Instances.GetValueOrDefault(id.Id); var observed = !early || w.Settle() == expected;
            await Start(w, owner, id, generation + 1);
            check(caller.Error == "disconnected" && terminal.Status == InstanceStatus.Cancelled && sampled && observed &&
                (generation == 0 ? removed is null : removed == CreationProtocol.Retire(terminal)) && w.Store.Instances[id.Id].Status == InstanceStatus.Finished &&
                w.Check() is null && w.Settle() == expected, $"retirement and successor retain this HTTP end obligation, g={generation}, legitimate={legitimate}, sampled={early}");
        }

        foreach (var forged in new[] { false, true })
        foreach (var atMarker in new[] { false, true })
        {
            var (w, owner, id) = await New(); var spec = Http.Spec(Http.Wait) with { Creation = new(1, 0, "wait") };
            w.Apply(new("request", 0, S: Http.Wait, M: new("start", id, Kinds.Start, Start: spec, TargetCreation: 1, ReplyTo: "|q1")));
            await Pump(owner.Worker); var caller = w.Asked.Single().Request;
            if (forged) w.Processes[owner.Worker.Node].Receive(new(caller.Registration, CreationProtocol.Origin(id.Id, 1) + "#end", null, null, InstanceStatus.Cancelled));
            w.Apply(new("add", M: new("host", id, Kinds.Terminate, TargetCreation: 1))); await Pump(owner.Worker);
            if (atMarker) await Delete(w, owner, id, 1);
            string? end = forged ? $"END {id} (wait) ended as Cancelled" : null;
            check(caller is { Status: InstanceStatus.Cancelled, Ends: 1, Responses: 0 } && w.Check() is null && w.Settle() == end,
                $"only the actual end delivered to the selected starter closes this terminal obligation, forged={forged}, marker={atMarker}");
        }

        foreach (var mode in new[] { "genuine progress", "same revision edit", "finished to counter", "invalid metadata", "invalid parent" })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true }; var owner = w.Of(0, 0); var id = InstanceRef.In(0, "old-owner");
            await using var prior = new Worker("prior", 0, w.Store, new WorkflowInstance([new Http(9, (_, _) => new ValueTask<object?>("answer"))]),
                new WorkerOptions { Monotonic = () => w.Time });
            await new DurableHost([prior]).ActivateAsync(0);
            var wait = mode is "genuine progress" or "same revision edit";
            var old = Http.Spec(wait ? Http.Wait : Http.Respond) with { Creation = new(1, 0, "first"), History = History.Minimal };
            w.Apply(new("add", M: new("one", id, Kinds.Start, Start: old, TargetCreation: 1))); await Pump(prior);
            var opening = w.Store.Instances[id.Id]; var early = mode is "invalid metadata" or "invalid parent" ? null : w.Check();
            if (wait)
            {
                await prior.DisposeAsync(); await owner.Runtime.ActivateAsync(0);
                if (mode == "same revision edit") w.Store.Instances[id.Id] = opening with { Status = InstanceStatus.Finished };
                else { w.Apply(new("add", M: new("resume", id, "resume", TargetCreation: 1))); await Pump(owner.Worker); w.Time += 350; await Pump(owner.Worker); }
                var row = w.Store.Instances[id.Id]; string? error = mode == "same revision edit" ? $"I10 {id} went from revision 1 Running to revision 1 Finished" : null;
                check(early is null && opening.Status == InstanceStatus.Running && row.Status == InstanceStatus.Finished &&
                    row.Revision == (error is null ? 3 : 1) && w.Check() == error && w.Settle() == error, $"a sampled recovered status requires a later write, {mode}");
                continue;
            }
            w.Apply(new("add", M: new("erase", id, Kinds.Delete, TargetCreation: 1))); await Pump(prior); await prior.DisposeAsync();
            await owner.Runtime.ActivateAsync(0); var marker = w.Store.Instances[id.Id];
            var damaged = mode != "finished to counter";
            if (damaged) w.Store.Instances[id.Id] = mode == "invalid metadata" ? marker with { CustomHandles = "[]" } : marker with { Parent = "0/other", ParentCreation = 1 };
            var after = w.Check(); var again = w.Check(); var markerEnd = w.Settle();
            var next = Counter.Spec with { Creation = new(2, 1, "new"), History = History.Minimal };
            w.Apply(new("add", M: new("next", id, "add", Json.Write(new Add("n")), Start: next, TargetCreation: 2))); await Pump(owner.Worker);
            string? invalid = damaged ? $"I22 {id} has an invalid lifecycle tombstone" : null;
            string? replaced = damaged ? $"I10 {id} replaced a different checkpoint at its fenced input" : null;
            check(early is null && marker == CreationProtocol.Retire(opening) && after == invalid && again == invalid && markerEnd == invalid &&
                w.Store.Instances[id.Id] is { Model: "counter", Creation: 2, Status: InstanceStatus.Running } && w.Check() == replaced && w.Settle() == replaced,
                $"recovered terminal and canonical retirement constrain the real next model, {mode}");
        }
    }
}
