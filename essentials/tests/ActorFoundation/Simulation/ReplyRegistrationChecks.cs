namespace Kernel.Sim;

internal static class ReplyRegistrationChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var finish in new[] { "answer", "end", "deadline", "disconnect" })
        {
            var bank = new Requests("test-incarnation");
            var old = bank.Open("same", 100);
            var response = new Reply(ReplyTo.Parse(old.ReplyAddress("node")).Request, "issued-key", Request.Response, "first");
            var end = response with { Channel = null, Item = null, Status = InstanceStatus.Finished };
            if (finish == "deadline") bank.Expire(100);
            else if (finish == "disconnect") bank.Disconnect(old);
            else bank.Receive(finish == "answer" ? response : end);
            var next = bank.Open(old.Id, old.Deadline);
            bank.Receive(response); bank.Receive(end); bank.Disconnect(old);
            var open = bank.IsOpen(next);
            bank.Receive(response with { Request = next.Registration, Item = "next" });
            check(old.Done && next.Id == old.Id && next.Registration != old.Registration && open && next.Answer as string == "next" &&
                next.Responses == 1 && next.Ends == 0 && bank.All.Count == 0, $"delayed response and end after {finish} cannot complete a reopened caller");
        }
        var before = new Requests(); var after = new Requests();
        var prior = before.Open("same", 50); var replacement = after.Open("same", 50);
        var other = after.Open("same", 50);
        after.Receive(new(prior.Registration, "old", null, null, InstanceStatus.Cancelled));
        after.Disconnect("same");
        var separate = after.IsOpen(replacement) && after.IsOpen(other);
        after.Disconnect(replacement);
        after.Receive(new(other.Registration, "new", Request.Response, 37));
        check(separate && replacement.Error == "disconnected" && other.Answer is 37 && after.All.Count == 0,
            "a replacement process and simultaneous same-name callers have independent live registrations");
        for (var n = 0; n < 128; n++)
        {
            var finished = before.Open("other" + n, 20);
            before.Receive(new(finished.Registration, "done", null, null, InstanceStatus.Finished));
        }
        before.Expire(50);
        check(before.All.Count == 0, "completed and expired registrations leave no retained route per past caller");

        var w = new World(new Config(2, 2, Flaw.None)) { Auto = true };
        await w.Runtimes[0].ActivateAsync(0); await w.Runtimes[1].ActivateAsync(1);
        var local = w.Of(0, 0); var remote = w.Of(1, 1);
        var first = InstanceRef.In(1, "prior"); var nextActor = InstanceRef.In(0, "next");
        w.Apply(new("add", M: new("birth", first, Kinds.Start, Start: Http.Spec(Http.Wait))));
        await Pump(remote.Worker);
        w.Apply(new("resume-request", 0, M: new("resume", first, "resume", ReplyTo: "q1")));
        await Pump(remote.Worker); w.Time += 350; await Pump(remote.Worker);
        var responseIndex = w.Deliveries.FindIndex(x => x.Reply.Channel == Request.Response);
        w.Apply(new("deliver", B: responseIndex, Flag: true));
        var previous = w.Asked.Single().Request;
        w.Apply(new("request", local.Id, S: Http.Respond, M: new("create", nextActor, Kinds.Start, Start: Http.Spec(Http.Respond), ReplyTo: "|q1")));
        var current = w.Asked.Last().Request;
        for (var i = w.Deliveries.Count - 1; i >= 0; i--) w.Apply(new("deliver", B: i));
        var pending = !current.Done;
        await Pump(local.Worker);
        check(previous.Answer is string earlier && pending && current.Answer is string latest && earlier != latest &&
            Json.Read<HttpState>(w.Store.Instances[nextActor.Id].State).Answers.SequenceEqual([latest]),
            "a real duplicated remote reply cannot win a later local HTTP request before its first turn");

        foreach (var addressed in new long?[] { null, 1, 2 })
        {
            var world = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            var owner = world.Of(0, 0); await owner.Runtime.ActivateAsync(0);
            var id = InstanceRef.In(0, "addressed");
            var start = Http.Spec(Http.Wait) with { Creation = new(1, 0, "first") };
            world.Apply(new("add", M: new("initial", id, Kinds.Start, Start: start, TargetCreation: 1)));
            await Pump(owner.Worker);
            world.Apply(new("request", 0, S: Http.Wait, M: new("another", id, Kinds.Start, Start: start, TargetCreation: addressed, ReplyTo: "|q1")));
            var caller = world.Asked.Single().Request;
            var cancelled = world.Apply(new("disconnect", S: caller.Registration, Flag: true));
            var command = world.Store.Messages.Values.Single(m => m.Message.Kind == "cancel");
            await Pump(owner.Worker);
            var row = world.Store.Instances[id.Id];
            check(cancelled == "cancel added" && caller.Error == "disconnected" && command.Message.TargetCreation == (addressed ?? 1) &&
                row.Creation == 1 && row.Status == (addressed == 2 ? InstanceStatus.Running : InstanceStatus.Cancelled),
                $"disconnect cancellation follows the addressed occurrence, target={addressed?.ToString() ?? "from Start"}");
        }

        foreach (var same in new[] { false, true })
        foreach (var foreign in new[] { false, true })
        {
            var world = new World(new Config(2, 2, Flaw.None)) { Auto = true };
            await world.Runtimes[0].ActivateAsync(0); await world.Runtimes[1].ActivateAsync(1);
            string? firstCheck = null;
            for (var i = 0; i < 2; i++)
            {
                var actor = world.Of(i, i);
                var name = i == 0 || same ? "q1" : "q2";
                var target = InstanceRef.In(i, "caller" + i);
                world.Apply(new("request", actor.Id, S: Http.Respond, M: new("start" + i, target, Kinds.Start,
                    Start: Http.Spec(Http.Respond), ReplyTo: "|" + name)));
                var opened = world.Asked.Last().Request;
                if (foreign && i == 1) world.Processes[actor.Worker.Node].Receive(new(opened.Registration, "foreign", Request.Response, "unwritten-answer"));
                await Pump(actor.Worker);
                if (i == 0) firstCheck = world.Check();
            }
            var caller = world.Asked.Last().Request;
            var expected = foreign ? $"I15 request {caller.Id} was answered unwritten-answer, which no commit of 1/caller1 wrote" : null;
            var sampled = world.Check(); var again = world.Check(); var final = world.Settle();
            check(firstCheck is null && caller.Responses == 1 && caller.Answer is string answer && (answer == "unwritten-answer") == foreign &&
                sampled == expected && again == expected && final == expected,
                $"each registration proves its own answer across repeated public checks, same local ID={same}, substituted={foreign}");
        }

        foreach (var sampleEarly in new[] { true, false })
        foreach (var flaw in new[] { Flaw.None, Flaw.EmitBeforeCommit })
        foreach (var profile in new[] { "ordinary", "qualified-ephemeral", "legacy-ephemeral" })
        {
            var world = new World(new Config(1, 1, flaw)) { Auto = true };
            await world.Runtimes[0].ActivateAsync(0); var actor = world.Of(0, 0); await actor.Worker.PollAsync();
            var id = InstanceRef.In(0, "release"); var qualified = profile != "legacy-ephemeral";
            var spec = Http.Spec(Http.Respond, profile != "ordinary") with { Creation = qualified ? new(1, 0, "g1") : null };
            world.Apply(new("request", actor.Id, S: Http.Respond, M: new("start", id, Kinds.Start, Start: spec, TargetCreation: qualified ? 1 : null, ReplyTo: "|q1")));
            if (actor.Worker.NeedsLoad) await actor.Worker.LoadAsync();
            world.Auto = false; world.Apply(new("turn", actor.Id, S: id.Id)); world.Apply(new("flush", actor.Id));
            var commitIndex = actor.Calls.FindIndex(c => !c.Executed && c.Kind == "commit");
            var received = world.Asked.Single().Request.Responses; var empty = world.Store.Instances.Count == 0 && world.Store.Journal.Count == 0;
            var early = sampleEarly ? world.Check() : null;
            if (commitIndex >= 0) { world.Apply(new("exec", actor.Id, commitIndex)); world.Apply(new("answer", actor.Id, commitIndex)); }
            world.Auto = true; await Pump(actor.Worker);
            var bad = qualified && flaw == Flaw.EmitBeforeCommit;
            var expected = bad ? "I15 answer 0/release:1@0 for request q1 was released before the emitting turn of 0/release committed" : null;
            check(empty && (commitIndex >= 0) == qualified && received == (!qualified || bad ? 1 : 0) && early == (sampleEarly ? expected : null) &&
                world.Store.Instances.ContainsKey(id.Id) == qualified && world.Check() == expected && world.Settle() == expected,
                $"the actual release retains its commit requirement, {profile}, {flaw}, sampled before commit={sampleEarly}");
        }

        foreach (var history in new[] { History.Full, History.State, History.Minimal })
        foreach (var sample in history == History.Full ? new[] { "before", "after", "lost-entry" } : new[] { "before", "after" })
        {
            var world = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            await world.Runtimes[0].ActivateAsync(0); var actor = world.Of(0, 0); var id = InstanceRef.In(0, "retired");
            var spec = Http.Spec(Http.Respond) with { Creation = new(1, 0, "g1"), History = history };
            world.Apply(new("request", actor.Id, S: Http.Respond, M: new("start", id, Kinds.Start, Start: spec, TargetCreation: 1, ReplyTo: "|q1")));
            await Pump(actor.Worker); var caller = world.Asked.Single().Request;
            var early = sample == "before" ? world.Check() : null;
            if (sample == "lost-entry") foreach (var (key, row) in world.Store.Journal.ToArray()) world.Store.Journal[key] = row with { Entries = "[]" };
            world.Apply(new("add", M: new("delete", id, Kinds.Delete, TargetCreation: 1))); await Pump(actor.Worker);
            var expected = sample == "lost-entry" ? "I15 request q1 was answered 0/retired:1@0, which no commit of 0/retired wrote" : null;
            check(caller.Answer as string == "0/retired:1@0" && caller.Responses == 1 && early is null && world.Store.Instances[id.Id].Retired &&
                world.Check() == expected && world.Settle() == expected, $"qualified Delete preserves the available answer proof, {history}, first sample {sample}");
        }

        await ReplyCarrierChecks.Run(check);

        static async Task Pump(Worker worker)
        {
            for (var i = 0; i < 12; i++) { await worker.PollAsync(); await worker.RunAsync(); }
        }
    }
}
