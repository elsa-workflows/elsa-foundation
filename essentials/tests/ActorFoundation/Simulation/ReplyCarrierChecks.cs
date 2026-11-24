namespace Kernel.Sim;

internal static class ReplyCarrierChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var history in new[] { History.Full, History.State, History.Minimal })
        foreach (var retire in new[] { false, true })
        foreach (var input in new[] { "none", "prior", "different" })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            await w.Runtimes[0].ActivateAsync(0);
            var owner = w.Of(0, 0).Worker; var target = InstanceRef.In(0, "joined");
            var spec = Http.Spec(Http.Respond) with { Creation = new(1, 0, "g1"), History = history };
            w.Apply(new("request", 0, S: Http.Respond, M: new("start", target, Kinds.Start, Start: spec, TargetCreation: 1, ReplyTo: "|q1")));
            await Pump(owner);
            var first = w.Asked.Single().Request; var earlier = w.Check();
            w.Apply(new("resume-request", 0, M: new("resume", target, "resume", TargetCreation: 1, ReplyTo: "q2")));
            var next = w.Asked.Last().Request;
            if (input != "none") w.Processes[owner.Node].Receive(first.AcceptedResponse! with
                { Request = next.Registration, Item = input == "prior" ? first.Answer : "different" });
            await Pump(owner);
            if (retire) { w.Apply(new("add", M: new("delete", target, Kinds.Delete, TargetCreation: 1))); await Pump(owner); }
            var expected = input switch
            {
                "prior" => $"I15 request q2 accepted {first.Answer} without an emission to this registration",
                "different" => "I15 request q2 was answered different, which no commit of 0/joined wrote",
                _ => null
            };
            check(earlier is null && first.Answer as string == "0/joined:1@0" && first.Registration != next.Registration &&
                next.Responses == (input == "none" ? 0 : 1) && next.Ends == (input == "none" ? 1 : 0) && w.Store.Instances[target.Id].Retired == retire &&
                w.Check() == expected && w.Settle() == expected, $"a later caller cannot borrow the earlier emission: {history}, {input}, retired={retire}");
        }

        foreach (var input in new[] { "genuine", "before emission", "clone in transit", "wrong journal revision" })
        {
            var w = new World(new Config(2, 2, Flaw.None)) { Auto = true };
            await w.Runtimes[0].ActivateAsync(0); await w.Runtimes[1].ActivateAsync(1);
            var actor = w.Of(1, 1); var target = InstanceRef.In(1, "async");
            var spec = Http.Spec(Http.Wait) with { Creation = new(1, 0, "g1") };
            w.Apply(new("add", M: new("start", target, Kinds.Start, Start: spec, TargetCreation: 1)));
            await Pump(actor.Worker);
            w.Apply(new("resume-request", 0, M: new("resume", target, "resume", TargetCreation: 1, ReplyTo: "q1")));
            var caller = w.Asked.Single().Request; var receiver = w.Processes[w.Of(0, 0).Worker.Node];
            var value = $"{target}:3@{actor.Id}";
            var equalCarrier = new Reply(caller.Registration, $"{CreationProtocol.Origin(target.Id, 1)}#3.0", Request.Response, value);
            if (input == "before emission") receiver.Receive(equalCarrier);
            await Pump(actor.Worker); w.Time += 350; await Pump(actor.Worker);
            var index = w.Deliveries.FindIndex(item => item.Reply.Channel == Request.Response);
            var genuine = w.Deliveries[index].Reply;
            if (input == "clone in transit") receiver.Receive(genuine with { });
            if (input == "wrong journal revision")
            {
                var source = w.Store.Journal.Single(j => j.Value.Instance == target.Id && j.Value.Revision == 3);
                var earlier = w.Store.Journal.Single(j => j.Value.Instance == target.Id && j.Value.Revision == 1);
                w.Store.Journal[source.Key] = source.Value with { Entries = "[]" };
                w.Store.Journal[earlier.Key] = earlier.Value with { Entries = source.Value.Entries };
            }
            w.Apply(new("deliver", B: index, Flag: true)); w.Apply(new("deliver", B: index));
            var copy = input is "before emission" or "clone in transit";
            var expected = copy ? $"I15 request q1 accepted {value} without an emission to this registration" :
                input == "wrong journal revision" ? $"I15 request q1 was answered {value}, which no commit of {target} wrote" : null;
            check(equalCarrier == genuine && caller.AcceptedResponse == genuine && ReferenceEquals(caller.AcceptedResponse, genuine) != copy &&
                caller.Responses == 1 && caller.Ends == 0 && w.Check() == expected && w.Settle() == expected,
                $"the caller joined before a later timer emitted, with delayed redelivery and {input}");
        }

        foreach (var mode in new[] { Http.Respond, Http.Timer })
        foreach (var admitBefore in new[] { true, false })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            await w.Runtimes[0].ActivateAsync(0);
            var owner = w.Of(0, 0).Worker; var target = InstanceRef.In(0, "two-births");
            await owner.PollAsync();
            w.Apply(new("request", 0, S: mode, M: new("first", target, Kinds.Start, Start: Http.Spec(mode, true), ReplyTo: "|q1")));
            var future = new Message("successor", target, Kinds.Start, TargetCreation: 1, Due: w.Time + 2000,
                Start: Http.Spec(Http.Respond) with { Creation = new(1, 0, "g1") });
            if (admitBefore) w.Apply(new("add", M: future));
            w.Apply(new("turn", 0, S: target.Id));
            var inMemory = owner.Unwritten.Contains(target.Id);
            if (mode == Http.Timer) { w.Time += 350; await Pump(owner); }
            if (!admitBefore) w.Apply(new("add", M: future));
            var q = w.Asked.Single().Request;
            var prior = w.Check();
            var writeFree = !owner.NeedsFlush && w.Store.Instances.Count == 0 && w.Store.Journal.Count == 0;
            var pending = !w.Store.Messages[future.InboxKey].Done;
            w.Time += 2100; await Pump(owner);
            check(inMemory == (mode == Http.Timer) && writeFree && pending && prior is null &&
                q.Responses == 1 && q.Ends == 0 && q.AcceptedResponse?.Key == $"{target}#{(mode == Http.Timer ? 2 : 1)}.0" &&
                w.Store.Instances[target.Id].Creation == 1 && w.Check() is null && w.Settle() is null,
                $"a later durable birth cannot change an earlier write-free {mode} emission, admitted before release={admitBefore}");
        }

        foreach (var beforeResume in new[] { true, false })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            await w.Runtimes[0].ActivateAsync(0);
            var owner = w.Of(0, 0).Worker; var target = InstanceRef.In(0, "resumed");
            w.Apply(new("add", M: new("start", target, Kinds.Start, TargetCreation: 1,
                Start: Http.Spec(Http.Wait) with { Creation = new(1, 0, "g1") })));
            await Pump(owner);
            var future = new Message("next", target, Kinds.Start, TargetCreation: 2, Due: w.Time + 2000,
                Start: Http.Spec(Http.Respond, true) with { Creation = new(2, 1, "g2") });
            if (beforeResume) w.Apply(new("add", M: future));
            w.Apply(new("resume-request", 0, M: new("resume", target, "resume", TargetCreation: 1, ReplyTo: "q2")));
            if (!beforeResume) w.Apply(new("add", M: future));
            await Pump(owner); w.Time += 350; await Pump(owner);
            var caller = w.Asked.Single().Request; var current = w.Store.Instances[target.Id];
            check(caller.Responses == 1 && caller.Answer as string == $"{target}:3@0" && !w.Store.Messages[future.InboxKey].Done &&
                current.Creation == 1 && current.Revision == 3 && w.Check() is null && w.Settle() is null,
                $"a resumed caller proves its emitting occurrence across a pending successor, admitted before resume={beforeResume}");
        }

        foreach (var corrupt in new[] { false, true })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            await w.Runtimes[0].ActivateAsync(0); var owner = w.Of(0, 0).Worker;
            var target = InstanceRef.In(0, "ambient"); var spec = Http.Spec(Http.Respond) with { Creation = new(1, 0, "g1") };
            w.Apply(new("request", 0, S: Http.Respond, M: new("start", target, Kinds.Start, Start: spec, TargetCreation: 1, ReplyTo: "|q1")));
            await Pump(owner);
            w.Apply(new("add", M: new("next", target, Kinds.Start, Due: w.Time + 2000, TargetCreation: 2,
                Start: spec with { Creation = new(2, 1, "g2") })));
            var row = w.Store.Instances[target.Id]; var state = Json.Read<HttpState>(row.State);
            var genuine = state.Ambient.SequenceEqual(["seen@0"]);
            await w.Runtimes[0].DeactivateAsync(0);    // isolate the stored observation from the live cache invariant
            if (corrupt) { state.Ambient[0] = "seen@99"; w.Store.Instances[target.Id] = row with { State = Json.Write(state) }; }
            var expected = corrupt ? $"I15 {target} generation 1 observed ambient seen@99 outside its starting activation" : null;
            check(genuine && w.Check() == expected && w.Settle() == expected,
                $"ambient proof for the live occurrence survives another admission, wrong process={corrupt}");
        }

        static async Task Pump(Worker worker)
        {
            for (var i = 0; i < 12; i++) { await worker.PollAsync(); await worker.RunAsync(); }
        }
    }
}
