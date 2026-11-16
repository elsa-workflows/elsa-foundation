namespace Kernel.Sim;

internal static class HttpAmbientHistoryChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var source in new[] { "none", "prior legacy", "pending intent", "same intent", "rejected intent" })
        foreach (var corrupt in new[] { false, true })
        {
            var w = new World(new Config(2, 1, Flaw.None)) { Auto = true };
            var actor = w.Of(0, 0); await actor.Runtime.ActivateAsync(0); await actor.Worker.PollAsync();
            var target = InstanceRef.In(0, "winner"); var legacy = source is "none" or "prior legacy";
            var spec = Http.Spec(Http.Respond) with { Creation = legacy ? null : new(1, 0, "actual") };
            var candidate = new Message("candidate", target, Kinds.Start, ReplyTo: "|q1", TargetCreation: 1,
                Start: source == "same intent" ? spec : spec with { Creation = new(1, 0, "different") });
            if (source == "prior legacy")
            {
                w.Apply(new("request", actor.Id, S: Http.Respond, M: new("unwritten", target, Kinds.Start, Start: Http.Spec(Http.Respond, true), ReplyTo: "|q1")));
                await Pump(actor.Worker);
            }
            var rowless = w.Store.Instances.Count == 0;
            if (source is "pending intent" or "same intent")
                w.Apply(new("request", actor.Id, S: Http.Respond, M: candidate with { Due = w.Time + 10000 }));
            if (legacy)
            {
                await actor.Runtime.DeactivateAsync(0); actor = w.Of(1, 0); await actor.Runtime.ActivateAsync(0);
            }
            w.Apply(new("add", M: new("durable", target, Kinds.Start, Start: spec, TargetCreation: legacy ? null : 1)));
            await Pump(actor.Worker);
            if (source == "rejected intent")
            {
                w.Apply(new("request", actor.Id, S: Http.Respond, M: candidate)); await Pump(actor.Worker);
            }
            var row = w.Store.Instances[target.Id]; var state = Json.Read<HttpState>(row.State);
            var actual = state.Ambient.Count == 0 && state.Answers.Single() == $"{target}:1@{actor.Id}";
            await actor.Runtime.DeactivateAsync(0);
            if (corrupt) { state.Ambient.Add("seen@0"); w.Store.Instances[target.Id] = row with { State = Json.Write(state) }; }
            var expected = corrupt ? $"I15 {target} generation {row.Creation} observed ambient seen@0 outside its starting activation" : null;
            check(rowless && actual && row.Creation == (legacy ? 0 : 1) && (legacy || row.StartIntent == CreationProtocol.Fingerprint(spec)) &&
                w.Check() == expected && w.Check() == expected && w.Settle() == expected,
                $"the landed creator owns the ambient proof, {source}, corrupt={corrupt}");
        }

        foreach (var wrongTarget in new[] { false, true })
        foreach (var inject in new[] { false, true })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            var actor = w.Of(0, 0); await actor.Runtime.ActivateAsync(0);
            var target = InstanceRef.In(0, "host-refusal");
            var start = Http.Spec(Http.Respond) with { Creation = new(1, 0, "winner") };
            w.Apply(new("add", M: new("winner", target, Kinds.Start, Start: start, TargetCreation: 1))); await Pump(actor.Worker);
            w.Apply(new("request", 0, S: Http.Respond, M: new("loser", target, Kinds.Start, TargetCreation: wrongTarget ? 2 : 1,
                Start: start with { Creation = new(1, 0, "other") }, ReplyTo: "|q1")));
            var q = w.Asked.Single().Request;
            var result = new CreationResult(target.Id, wrongTarget ? "WrongCreation" : "StartConflict", wrongTarget ? 2 : 1, 1);
            if (inject) w.Processes[actor.Worker.Node].Receive(new(q.Registration, "predicted", Request.Response, result));
            await Pump(actor.Worker);
            var expected = inject ? $"I15 request q1 received CreationResult for {target}, whose HTTP model emits strings" : null;
            check(Equals(q.Answer, result) && q.Responses == 1 && w.Check() == expected && w.Settle() == expected,
                $"only the protocol's actual refusal to this caller qualifies a typed result, wrong target={wrongTarget}, injected={inject}");
        }

        foreach (var corrupt in new[] { false, true })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            var actor = w.Of(0, 0); await actor.Runtime.ActivateAsync(0);
            var id = InstanceRef.In(0, "skipped-marker");
            var one = Http.Spec(Http.Respond) with { Creation = new(1, 0, "one") };
            var two = Http.Spec(Http.Wait) with { Creation = new(2, 1, "two") };
            w.Apply(new("add", M: new("one", id, Kinds.Start, Start: one, TargetCreation: 1))); await Pump(actor.Worker);
            var first = w.Check();
            w.Apply(new("add", M: new("erase", id, Kinds.Delete, TargetCreation: 1))); await Pump(actor.Worker);
            var marker = w.Store.Instances[id.Id];
            w.Apply(new("add", M: new("two", id, Kinds.Start, Start: two, TargetCreation: 2))); await Pump(actor.Worker);
            w.Apply(new("resume-request", 0, M: new("later", id, "resume", TargetCreation: 2, ReplyTo: "q2")));
            await Pump(actor.Worker); w.Time += 350; await Pump(actor.Worker);
            var later = w.Store.Instances[id.Id]; await actor.Runtime.DeactivateAsync(0);
            if (corrupt) w.Store.Instances[id.Id] = later with { StartIntent = "different" };
            var expected = corrupt ? $"I10 {id} changed creation 1 to 2 without a valid observed retirement" : null;
            check(first is null && marker.Retired && marker.Creation == 1 && later.Creation == 2 && later.Revision == 3 &&
                w.Check() == expected && w.Settle() == expected,
                $"a fenced retirement and actual next birth validate an unsampled marker through revision three, corrupt header={corrupt}");
        }

        foreach (var history in new[] { History.Full, History.State, History.Minimal })
        foreach (var beforeDelete in new[] { true, false })
        foreach (var edit in new[] { "none", "foreign", "extra" })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            var owner = w.Of(0, 0); await owner.Runtime.ActivateAsync(0);
            var target = InstanceRef.In(0, "retired-ambient");
            var spec = Http.Spec(Http.Respond) with { Creation = new(1, 0, "first"), History = history };
            w.Apply(new("request", 0, S: Http.Respond, M: new("q1", target, Kinds.Start, Start: spec, TargetCreation: 1, ReplyTo: "|q1")));
            await Pump(owner.Worker); var caller = w.Asked.Single().Request;
            var row = w.Store.Instances[target.Id]; var state = Json.Read<HttpState>(row.State);
            var original = state.Ambient.SequenceEqual(["seen@0"]); await owner.Runtime.DeactivateAsync(0);
            var stray = edit == "foreign" ? "seen@97" : "seen@0";
            if (edit != "none") { state.Ambient.Add(stray); w.Store.Instances[target.Id] = row with { State = Json.Write(state) }; }
            var expected = edit == "none" ? null : $"I15 {target} generation 1 observed ambient {stray} outside its starting activation";
            var early = beforeDelete ? w.Check() : null;
            await owner.Runtime.ActivateAsync(0);
            w.Apply(new("add", M: new("delete", target, Kinds.Delete, TargetCreation: 1))); await Pump(owner.Worker);
            var retired = w.Store.Instances[target.Id].Retired;
            w.Apply(new("request", 0, S: Http.Respond, M: new("q2", target, Kinds.Start,
                Start: spec with { Creation = new(2, 1, "second") }, TargetCreation: 2, ReplyTo: "|q2")));
            await Pump(owner.Worker);
            check(original && caller.Responses == 1 && early == (beforeDelete ? expected : null) && retired &&
                w.Store.Instances[target.Id].Creation == 2 && w.Check() == expected && w.Check() == expected && w.Settle() == expected,
                $"a sampled or unsampled retirement keeps its own ambient checkpoint, {history}, sampled={beforeDelete}, {edit}");
        }
    }

    private static async Task Pump(Worker worker)
    {
        for (var pass = 0; pass < 12; pass++) { await worker.PollAsync(); await worker.RunAsync(); }
    }
}
