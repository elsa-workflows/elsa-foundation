namespace Kernel.Sim;

internal static partial class GenerationSamplingChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await ObservationBoundaryChecks.Run(check);
        await CheckPhysicalHandoffs(check);
        await CheckNamedHandoffs(check);
        await CheckRecoveryHandoffs(check);
        await CheckRecoveredEndpoints(check);
        await CheckRecoveryAndPromotion(check);
        await CheckLifetimeEdges(check);
        foreach (var endpoint in new[] { "active two", "marker two", "active three" })
        foreach (var sampling in new[] { "first", "markers", "every change" })
        {
            var (w, owner, id) = await New();
            var samples = new List<string?>();
            await Start(w, owner, id, 1); samples.Add(w.Check());
            await Delete(w, owner, id, 1);
            if (sampling != "first") samples.Add(w.Check());
            await Start(w, owner, id, 2);
            if (sampling == "every change") samples.Add(w.Check());
            if (endpoint != "active two")
            {
                await Delete(w, owner, id, 2);
                if (sampling != "first") samples.Add(w.Check());
                if (endpoint == "active three") await Start(w, owner, id, 3);
            }
            var row = w.Store.Instances[id.Id];
            check(row.Creation == (endpoint == "active three" ? 3 : 2) && row.Retired == (endpoint == "marker two") &&
                samples.All(s => s is null) && w.Check() is null && w.Check() is null && w.Settle() is null,
                $"every fenced edge survives sparse generation sampling, {endpoint}, {sampling}");
        }

        foreach (var markerSampled in new[] { false, true })
        foreach (var edit in new[] { "none", "intent", "parent", "parent generation", "missing generation" })
        {
            var (w, owner, id) = await New();
            await Start(w, owner, id, 1); var first = w.Check();
            await Delete(w, owner, id, 1); var marker = markerSampled ? w.Check() : null;
            await Start(w, owner, id, 2); await owner.Runtime.DeactivateAsync(0);
            var row = w.Store.Instances[id.Id];
            var endpoint = w.Store.Instances[id.Id] = edit switch {
                "intent" => row with { StartIntent = "unissued" }, "parent" => row with { Parent = "other" },
                "parent generation" => row with { ParentCreation = 27 }, "missing generation" => row with { Creation = 3 }, _ => row };
            var expected = edit == "none" ? null : $"I10 {id} changed creation 1 to {endpoint.Creation} without a valid observed retirement";
            check(first is null && marker is null && row.Revision == 1 && w.Check() == expected && w.Check() == expected && w.Settle() == expected,
                $"a sampled marker never authorizes a changed successor or an unsupported generation jump, marker={markerSampled}, {edit}");
        }

        foreach (var scope in new[] { "ordinary", "issued", "event" })
        foreach (var wrong in new[] { false, true })
        {
            var (w, owner, id) = await New();
            await Start(w, owner, id, 1, wrong ? Http.Respond : Http.Wait);
            var target = wrong ? 2 : 1;
            var request = new Message("resume", id, "resume", TargetCreation: target, ReplyTo: "q1");
            request = scope switch { "issued" => request.AsIssued(), "event" => request.WithInboxScope(-2, "old"), _ => request };
            var admitted = w.Apply(new("resume-request", 0, M: request)); var caller = w.Asked.Single().Request;
            var stored = w.Store.Messages.Values.Single(m => m.Message.Id == request.Id).Message;
            await Pump(owner.Worker); w.Time += 350; await Pump(owner.Worker);
            var answer = wrong ? Equals(caller.Answer, new CreationResult(id.Id, "WrongCreation", target, 1)) : caller.Answer is string;
            check(admitted == "added" && stored.InboxScope == 0 && stored.EventMatch is null && stored.TargetCreation == target &&
                caller.Responses == 1 && caller.AcceptedResponse?.Request == caller.Registration && answer && w.Check() is null && w.Settle() is null,
                $"a canonical durable resume retains the addressed occurrence and exact refusal, {scope}, wrong={wrong}");
        }

        foreach (var supplied in new[] { false, true })
        {
            var (w, owner, id) = await New();
            var valid = true;
            for (var generation = 1; generation <= 2; generation++)
            {
                await Start(w, owner, id, generation, Http.Wait);
                if (supplied) w.Apply(new("add", M: new("external-" + generation, id, "resume", KeepUntil: w.Time + 3_600_000, TargetCreation: generation)));
                var first = w.Check(); var end = w.Settle(); var row = w.Store.Instances[id.Id];
                var resume = w.Store.Messages.Values.Where(m => m.Message.Kind == "resume" && m.Message.TargetCreation == generation);
                valid &= first is null && end is null && row.Status == InstanceStatus.Finished && row.Creation == generation &&
                    row.Revision == 3 && resume.Any(m => m.Done);
                if (generation == 1) await Delete(w, owner, id, generation);
            }
            check(valid, $"settlement resumes each waiting occurrence while the earlier inbox key is retained, supplied={supplied}");
        }
    }

    private static async Task CheckLifetimeEdges(Action<bool, string> check)
    {
        foreach (var sampleMarker in new[] { false, true })
        foreach (var successor in new[] { "none", "fenced", "invented", "pending" })
        {
            var (w, owner, id) = await New();
            var one = Http.Spec(Http.Respond) with { History = History.Minimal, Creation = new(1, 0, "one") };
            w.Apply(new("add", M: new("s1", id, Kinds.Start, Start: one, TargetCreation: 1))); await Pump(owner.Worker);
            var prior = w.Store.Instances[id.Id]; var first = w.Check();
            await Delete(w, owner, id, 1); var mid = sampleMarker ? w.Check() : null;
            var two = one with { Creation = new(2, 1, "two") };
            if (successor is "fenced" or "pending")
                w.Apply(new("add", M: new("s2", id, Kinds.Start, Start: two, TargetCreation: 2, Due: successor == "pending" ? w.Time + 10000 : null)));
            await Pump(owner.Worker); await owner.Runtime.DeactivateAsync(0);
            var fake = successor is "invented" or "pending";
            if (fake) w.Store.Instances[id.Id] = prior with { Creation = 2, StartIntent = CreationProtocol.Fingerprint(two) };
            var expected = fake ? $"I10 {id} changed creation 1 to 2 without a valid observed retirement" : null;
            check(first is null && mid is null && w.Check() == expected && w.Check() == expected && w.Settle() == expected,
                $"a witnessed marker requires an actual later creator, marker sampled={sampleMarker}, successor={successor}");
        }

        foreach (var qualified in new[] { false, true })
        foreach (var sampling in new[] { "late", "old", "absence" })
        {
            var (w, owner, id) = await New();
            var legacy = Http.Spec(Http.Respond);
            w.Apply(new("add", M: new("old", id, Kinds.Start, Start: legacy))); await Pump(owner.Worker);
            var old = sampling != "late" ? w.Check() : null;
            w.Apply(new("add", M: new("erase", id, Kinds.Delete))); await Pump(owner.Worker);
            var absent = !w.Store.Instances.ContainsKey(id.Id); var erased = sampling == "absence" ? w.Check() : null;
            var spec = qualified ? legacy with { Creation = new(1, 0, "first") } : legacy;
            w.Apply(new("add", M: new("new", id, Kinds.Start, Start: spec, TargetCreation: qualified ? 1 : null))); await Pump(owner.Worker);
            var born = w.Store.Instances[id.Id];
            check(old is null && erased is null && absent && born.Creation == (qualified ? 1 : 0) && born.Revision == 1 &&
                born.Status == InstanceStatus.Finished && w.Check() is null && w.Settle() is null,
                $"a real birth after a named deletion begins a new lifetime, qualified={qualified}, sampled={sampling}");
        }

        foreach (var timing in new[] { "before", "after" })
        foreach (var actual in new[] { true, false })
        {
            var (w, owner, id) = await New();
            var one = Http.Spec(Http.Timer) with { Creation = new(1, 0, "timer") };
            w.Apply(new("request", 0, S: Http.Timer, M: new("one", id, Kinds.Start, Start: one, ReplyTo: "|q1", TargetCreation: 1)));
            await Pump(owner.Worker); var caller = w.Asked.Single().Request;
            var competing = new Message("different", id, Kinds.Start, Start: Http.Spec(Http.Respond) with { Creation = new(2, 1, "future") }, TargetCreation: 2);
            if (timing == "before") w.Apply(new("add", M: competing));
            w.Apply(new("disconnect", S: caller.Registration, Flag: actual)); await Pump(owner.Worker);
            if (timing == "after") w.Apply(new("add", M: competing));
            await Pump(owner.Worker); var row = w.Store.Instances[id.Id];
            if (!actual)
            {
                await owner.Runtime.DeactivateAsync(0);
                w.Store.Instances[id.Id] = row with { Status = InstanceStatus.Cancelled };
            }
            var end = w.Settle();
            check(row.Creation == 1 && caller.Error == "disconnected" && (actual ? row.Status == InstanceStatus.Cancelled && end is null :
                row.Status == InstanceStatus.Running && end == $"I10 {id} went from revision {row.Revision} Running to revision {row.Revision} Cancelled"),
                $"only the current occurrence's applied cancel permits its cancellation, competing={timing}, actual={actual}");
        }

        foreach (var generation in new[] { 0, 1 })
        {
            var (w, owner, id) = await New(); var key = "last-" + CreationProtocol.Origin(id.Id, generation);
            var spec = Http.Spec(Http.Wait) with { Creation = generation == 0 ? null : new(1, 0, "wait") };
            long? address = generation == 0 ? null : generation;
            var first = new Message(key, id, Kinds.Start, Start: spec, TargetCreation: address, KeepUntil: w.Time + 3600000);
            w.Apply(new("add", M: first.AsIssued())); await Pump(owner.Worker);
            w.Apply(new("resume-request", 0, M: new(key + ".1", id, "resume", TargetCreation: 99, ReplyTo: "q1", KeepUntil: w.Time + 3600000)));
            await Pump(owner.Worker); var before = w.Check(); var end = w.Settle(); var row = w.Store.Instances[id.Id];
            check(before is null && end is null && row.Status == InstanceStatus.Finished && row.Revision == 3 &&
                w.Store.Messages[key].Message == first && w.Store.Messages[key + ".1"].Message.TargetCreation == 99 &&
                w.Store.Messages[key + ".2"] is { Done: true } applied && applied.Message.Kind == "resume" && applied.Message.TargetCreation == address,
                $"a real settlement resume survives two occupied ordinary keys without replacing their envelopes, generation={generation}");
        }

        foreach (var atMarker in new[] { true, false })
        foreach (var edited in new[] { false, true })
        {
            var (w, owner, id) = await New();
            await Start(w, owner, id, 1); var first = w.Check(); await Delete(w, owner, id, 1);
            if (!atMarker) await Start(w, owner, id, 2);
            var sampled = w.Check(); await owner.Runtime.DeactivateAsync(0);
            var receipt = w.Store.Journal.Single(p => p.Value.Instance == id.Id && p.Value.Revision == -1);
            if (edited) w.Store.Journal[receipt.Key] = receipt.Value with { Entries = """["substituted"]""" };
            string? expected = !edited ? null : $"I22 {id} changed its fenced deletion receipt at {receipt.Key.Partition}/{receipt.Key.Position}";
            check(first is null && sampled is null && w.Check() == expected && w.Check() == expected && w.Settle() == expected,
                $"a retained physical deletion receipt is checked after the marker or successor was sampled, marker={atMarker}, changed={edited}");
        }

        foreach (var early in new[] { false, true })
        foreach (var edited in new[] { false, true })
        {
            var (w, owner, id) = await New();
            await Start(w, owner, id, 1); var first = w.Check(); await Delete(w, owner, id, 1);
            var spec = Http.Spec(Http.Wait) with { Creation = new(2, 1, "wait"), History = History.Minimal };
            w.Apply(new("add", M: new("next", id, Kinds.Start, Start: spec, TargetCreation: 2))); await Pump(owner.Worker);
            var born = early ? w.Check() : null;
            w.Apply(new("add", M: new("resume", id, "resume", TargetCreation: 2))); await Pump(owner.Worker); w.Time += 350; await Pump(owner.Worker);
            var finished = w.Store.Instances[id.Id]; await owner.Runtime.DeactivateAsync(0);
            if (edited) w.Store.Instances[id.Id] = finished with { Revision = 1 };
            string? expected = !edited ? null : early ? $"I10 {id} is at revision 1 after fenced revision 3" :
                $"I10 {id} changed creation 1 to 2 without a valid observed retirement";
            check(first is null && born is null && finished.Revision == 3 && w.Check() == expected && w.Settle() == expected,
                $"a sampled Minimal birth cannot erase the later fenced revision, early={early}, edited={edited}");
        }
    }

    private static async Task<(World, Actor, InstanceRef)> New()
    {
        var w = new World(new Config(1, 1, Flaw.None)) { Auto = true };
        var owner = w.Of(0, 0); await owner.Runtime.ActivateAsync(0);
        return (w, owner, InstanceRef.In(0, "qualified"));
    }

    private static async Task Start(World w, Actor actor, InstanceRef id, int generation, string mode = Http.Respond)
    {
        var start = Http.Spec(mode) with { Creation = new(generation, generation - 1, "birth-" + generation) };
        w.Apply(new("add", M: new("start-" + generation, id, Kinds.Start, Start: start, TargetCreation: generation)));
        await Pump(actor.Worker);
    }

    private static async Task Delete(World w, Actor actor, InstanceRef id, int generation)
    {
        w.Apply(new("add", M: new("erase-" + generation, id, Kinds.Delete, TargetCreation: generation)));
        await Pump(actor.Worker);
    }

    private static async Task Pump(Worker worker)
    {
        for (var pass = 0; pass < 12; pass++) { await worker.PollAsync(); await worker.RunAsync(); }
    }
}
