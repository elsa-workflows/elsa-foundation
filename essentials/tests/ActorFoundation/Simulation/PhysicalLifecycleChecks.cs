namespace Kernel.Sim;

internal static partial class GenerationSamplingChecks
{
    private static async Task CheckRecoveryAndPromotion(Action<bool, string> check)
    {
        foreach (var endpoint in new[] { "finished", "marker", "next", "changed receipt", "changed marker", "invented birth" })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            var owner = w.Of(0, 0); var id = InstanceRef.In(0, "recovered");
            await using var earlier = new Worker("earlier", 0, w.Store,
                new WorkflowInstance([new Http(9, (_, _) => new ValueTask<object?>("answer"))]), new WorkerOptions { Monotonic = () => w.Time });
            await new DurableHost([earlier]).ActivateAsync(0);
            var old = Http.Spec(Http.Respond) with { Creation = new(1, 0, "old"), History = History.Minimal };
            w.Apply(new("add", M: new("old", id, Kinds.Start, Start: old, TargetCreation: 1))); await Pump(earlier);
            var active = w.Store.Instances[id.Id];
            w.Apply(new("add", M: new("erase", id, Kinds.Delete, TargetCreation: 1))); await Pump(earlier);
            await earlier.DisposeAsync(); await owner.Runtime.ActivateAsync(0);
            var marker = w.Store.Instances[id.Id]; var receipt = w.Store.Journal.Single(p => p.Value.Revision == -1);
            var first = w.Check(); string? expected = null;
            switch (endpoint)
            {
                case "changed receipt":
                    w.Store.Journal[receipt.Key] = receipt.Value with { Entries = """["substitution"]""" };
                    expected = $"I22 {id} changed its fenced deletion receipt at {receipt.Key.Partition}/{receipt.Key.Position}";
                    break;
                case "changed marker":
                    w.Store.Instances[id.Id] = marker with { Revision = marker.Revision + 1 };
                    expected = $"I22 {id} has an invalid lifecycle tombstone";
                    break;
                case "invented birth":
                    w.Store.Instances[id.Id] = active with { Creation = 2 };
                    expected = $"I10 {id} changed creation 1 to 2 without a valid observed retirement";
                    break;
                default:
                    await Start(w, owner, id, 2, Http.Wait);
                    w.Apply(new("add", M: new("resume", id, "resume", TargetCreation: 2))); await Pump(owner.Worker);
                    w.Time += 350; await Pump(owner.Worker);
                    if (endpoint != "finished") await Delete(w, owner, id, 2);
                    if (endpoint == "next") await Start(w, owner, id, 3);
                    break;
            }
            check(first is null && marker.Retired && receipt.Value.Entries == """["deleted"]""" &&
                w.Check() == expected && w.Check() == expected && w.Settle() == expected, $"recovered physical marker and witnessed successors, {endpoint}");
        }

        foreach (var ephemeral in new[] { false, true })
        foreach (var cancel in new[] { false, true })
        {
            var (w, owner, id) = await New();
            w.Apply(new("add", M: new("old", id, Kinds.Start, Start: Http.Spec(Http.Respond)))); await Pump(owner.Worker);
            w.Apply(new("add", M: new("erase", id, Kinds.Delete))); await Pump(owner.Worker);
            var absent = !w.Store.Instances.ContainsKey(id.Id) && w.Check() is null;
            var spec = Http.Spec(Http.Timer, ephemeral) with { ShortWait = TimeSpan.FromSeconds(5), History = History.Minimal };
            w.Apply(new("request", 0, S: Http.Timer, M: new("local", id, Kinds.Start, Start: spec, ReplyTo: "|q1"))); await Pump(owner.Worker);
            var unwritten = owner.Worker.Unwritten.Contains(id.Id); var caller = w.Asked.Single().Request;
            if (cancel) w.Apply(new("disconnect", S: caller.Registration, Flag: true));
            else w.Apply(new("add", M: new("keep", id, "keepalive")));
            await Pump(owner.Worker); var physical = w.Store.Instances[id.Id]; var promoted = w.Check();
            w.Time += 350; await Pump(owner.Worker); var final = w.Store.Instances[id.Id];
            check(absent && unwritten == ephemeral && physical.Creation == 0 && physical.Revision == 2 && promoted is null &&
                final.Revision == (cancel ? 2 : 3) && final.Status == (cancel ? InstanceStatus.Cancelled : InstanceStatus.Finished) &&
                w.Check() is null && w.Settle() is null, $"selected named birth survives its first physical write, ephemeral={ephemeral}, cancel={cancel}");
        }

        foreach (var generation in new[] { 0, 1 })
        foreach (var next in new[] { "absent", "counter", "lost row" })
        {
            var (w, owner, id) = await New(); long? addressed = generation == 0 ? null : generation;
            var old = Http.Spec(Http.Respond) with { Creation = generation == 0 ? null : new(1, 0, "http"), History = History.Minimal };
            w.Apply(new("add", M: new("old", id, Kinds.Start, Start: old, TargetCreation: addressed))); await Pump(owner.Worker);
            var previous = w.Store.Instances[id.Id]; string? expected = null;
            if (next != "lost row")
            {
                w.Apply(new("add", M: new("erase", id, Kinds.Delete, TargetCreation: addressed))); await Pump(owner.Worker);
                if (next == "counter")
                {
                    var c = generation + 1; var spec = Counter.Spec with { Creation = new(c, c - 1, "counter"), History = History.Minimal };
                    w.Apply(new("add", M: new("next", id, "add", Json.Write(new Add("one")), Start: spec, TargetCreation: c))); await Pump(owner.Worker);
                }
            }
            else
            {
                await owner.Runtime.DeactivateAsync(0); w.Store.Instances.Remove(id.Id);
                expected = $"I10 the fenced row of {id} disappeared";
            }
            check(previous.Status == InstanceStatus.Finished && (next != "counter" || w.Store.Instances[id.Id] is { Model: "counter", Status: InstanceStatus.Running }) &&
                w.Check() == expected && w.Settle() == expected, $"terminal HTTP disposition belongs to its occurrence, generation={generation}, {next}");
        }

        foreach (var history in new[] { History.Full, History.State, History.Minimal })
        foreach (var sample in new[] { false, true })
        foreach (var edit in new[] { false, true })
        {
            var (w, owner, id) = await New();
            var wait = Http.Spec(Http.Wait) with { Creation = new(1, 0, "wait"), History = history };
            w.Apply(new("add", M: new("start", id, Kinds.Start, Start: wait, TargetCreation: 1))); await Pump(owner.Worker);
            var written = w.Store.Instances[id.Id]; var observed = sample ? w.Check() : null;
            await owner.Runtime.DeactivateAsync(0);
            if (edit) w.Store.Instances[id.Id] = written with { Status = InstanceStatus.Finished };
            string? expected = edit ? $"I10 {id} went from revision 1 Running to revision 1 Finished" : null;
            var first = w.Check(); var again = w.Check(); var settled = w.Settle(); var final = w.Store.Instances[id.Id];
            check(written is { Status: InstanceStatus.Running, Revision: 1 } && observed is null && first == expected &&
                again == expected && settled == expected && final.Status == InstanceStatus.Finished && final.Revision == (edit ? 1 : 3),
                $"status agrees with the actual fence until a real later completion, history={history}, sampled={sample}, edit={edit}");
        }

        foreach (var generation in new[] { 0, 1 })
        {
            var (w, owner, id) = await New(); long? addressed = generation == 0 ? null : generation;
            var timer = Http.Spec(Http.Timer) with { Creation = generation == 0 ? null : new(1, 0, "timer") };
            w.Apply(new("request", 0, S: Http.Timer, M: new("start", id, Kinds.Start, Start: timer, ReplyTo: "|q1", TargetCreation: addressed)));
            await Pump(owner.Worker); var caller = w.Asked.Single().Request;
            w.Apply(new("disconnect", S: caller.Registration, Flag: false));
            w.Apply(new("add", M: new("host", id, Kinds.Terminate, TargetCreation: addressed))); await Pump(owner.Worker);
            var final = w.Store.Instances[id.Id];
            check(final.Status == InstanceStatus.Cancelled && w.Check() is null && w.Settle() == $"END {id} (timer) ended as Cancelled",
                $"a genuine host terminal write leaves the independent HTTP cancellation obligation intact, generation={generation}");
        }

        foreach (var retire in new[] { false, true })
        foreach (var edit in new[] { false, true })
        {
            var (w, owner, id) = await New(); await Start(w, owner, id, 1);
            await owner.Runtime.DeactivateAsync(0); var written = w.Store.Instances[id.Id];
            if (edit) w.Store.Instances[id.Id] = written with { Status = InstanceStatus.Faulted };
            if (retire) { await owner.Runtime.ActivateAsync(0); await Delete(w, owner, id, 1); }
            string? expected = !edit ? null : retire ? $"I10 {id} replaced a different checkpoint at its fenced input" :
                $"I10 {id} went from revision {written.Revision} Finished to revision {written.Revision} Faulted";
            check(written.Status == InstanceStatus.Finished && w.Check() == expected && w.Check() == expected && w.Settle() == expected,
                $"a fenced terminal status survives its physical endpoint and retirement, retire={retire}, edit={edit}");
        }
    }
}
