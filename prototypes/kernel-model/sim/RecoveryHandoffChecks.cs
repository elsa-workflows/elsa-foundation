namespace Kernel.Sim;

internal static partial class GenerationSamplingChecks
{
    private static async Task CheckRecoveryHandoffs(Action<bool, string> check)
    {
        foreach (var variant in new[] { "valid", "changed intent", "changed parent", "zero marker", "one marker", "successor" })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            var id = InstanceRef.In(0, "recovery");
            await using var prior = new Worker("prior", 0, w.Store, new WorkflowInstance([new Http(8, (_, _) => new ValueTask<object?>("done"))]),
                new WorkerOptions { Monotonic = () => w.Time });
            await new DurableHost([prior]).ActivateAsync(0);
            var start = Http.Spec(Http.Respond) with { Creation = new(1, 0, "one"), History = History.Minimal };
            w.Apply(new("add", M: new("start", id, Kinds.Start, Start: start, TargetCreation: 1)));
            await Pump(prior);
            var opening = w.Store.Instances[id.Id];
            string? initial = variant is "zero marker" or "one marker" ? null : w.Check();
            if (variant is "changed intent" or "changed parent")
            {
                await prior.DisposeAsync();
                w.Store.Instances[id.Id] = variant == "changed parent" ? opening with { Parent = "0/other", ParentCreation = 1 } :
                    opening with { StartIntent = CreationProtocol.Fingerprint(start with { Creation = new(1, 0, "other") }) };
                var error = $"I10 {id} changed the fenced header of creation 1";
                check(initial is null && w.Check() == error && w.Settle() == error, $"a recovered active sample retains its immutable creation header, {variant}");
                continue;
            }
            w.Apply(new("add", M: new("erase", id, Kinds.Delete, TargetCreation: 1)));
            await Pump(prior); await prior.DisposeAsync();
            var marker = w.Store.Instances[id.Id];
            var receipt = w.Store.Journal.Values.Single(j => j.Instance == id.Id && j.Revision == -1);
            if (variant is "zero marker" or "one marker")
            {
                w.Store.Instances[id.Id] = marker with { Revision = variant == "zero marker" ? 0 : 1 };
                var error = $"I22 {id} has an invalid lifecycle tombstone";
                check(marker == CreationProtocol.Retire(opening) && marker.Revision == 2 && receipt.Entries == """["deleted"]""" &&
                    w.Check() == error && w.Settle() == error, $"the first recovery marker must follow a durable active revision, {variant}");
                continue;
            }
            var owner = w.Of(0, 0); await owner.Runtime.ActivateAsync(0);
            var atMarker = variant == "valid" ? w.Check() : null;
            var next = Counter.Spec with { Creation = new(2, 1, "next"), History = History.Minimal };
            w.Apply(new("add", M: new("next", id, "add", Json.Write(new Add("one")), Start: next, TargetCreation: 2)));
            await Pump(owner.Worker);
            check(initial is null && atMarker is null && opening.Status == InstanceStatus.Finished && marker == CreationProtocol.Retire(opening) &&
                w.Store.Journal.Values.Contains(receipt) && w.Store.Instances[id.Id] is { Model: "counter", Creation: 2, Revision: 1 } &&
                w.Check() is null && w.Settle() is null, $"the genuine fenced successor reconciles the old owner's retirement, {variant}");
        }

        // A recovered signed active checkpoint is an explicit input. Its genuine fenced Delete
        // supplies a preimage and a resulting marker which a standalone cold marker does not.
        foreach (var importedRevision in new long[] { -1, 0 })
        foreach (var altered in new[] { false, true })
        {
            var (source, writer, id) = await New(); await Start(source, writer, id, 1);
            var (w, owner, _) = await New();
            var prior = source.Store.Instances[id.Id] with { Revision = importedRevision, History = History.Minimal };
            w.Store.Instances[id.Id] = prior;
            await Delete(w, owner, id, 1);
            var marker = w.Store.Instances[id.Id];
            if (altered) w.Store.Instances[id.Id] = marker with { Revision = marker.Revision == 0 ? 1 : 0 };
            string? expected = altered ? $"I22 {id} has an invalid lifecycle tombstone" : null;
            check(marker == CreationProtocol.Retire(prior) && marker.Revision == importedRevision + 1 &&
                w.Store.Journal.Values.Single(j => j.Instance == id.Id).Entries == """["deleted"]""" &&
                w.Check() == expected && w.Settle() == expected,
                $"the actual signed predecessor binds exactly its fenced retirement, revision={importedRevision}, changed={altered}");
        }

        foreach (var settleDelivery in new[] { false, true })
        foreach (var retired in new[] { false, true })
        {
            var w = new World(new Config(2, 1, Flaw.None)) { Auto = true };
            var owner = w.Of(0, 0); await owner.Runtime.ActivateAsync(0);
            var id = InstanceRef.In(0, "remote-end"); await Start(w, owner, id, 1); await Delete(w, owner, id, 1);
            var next = Http.Spec(Http.Wait) with { History = History.Minimal, Creation = new(2, 1, "next") };
            w.Apply(new("resume-request", 1, M: new("remote", id, Kinds.Start, Start: next, TargetCreation: 2, ReplyTo: "q1")));
            await Pump(owner.Worker); var caller = w.Asked.Single().Request;
            w.Apply(new("add", M: new("stop", id, Kinds.Terminate, TargetCreation: 2))); await Pump(owner.Worker);
            var terminal = w.Store.Instances[id.Id]; var waiting = !caller.Done && w.Deliveries.Count == 1;
            if (retired) await Delete(w, owner, id, 2);
            if (!settleDelivery) w.Apply(new("deliver", B: 0));
            check(waiting && terminal.Status == InstanceStatus.Cancelled && w.Check() is null && w.Settle() is null &&
                caller is { Done: true, Ends: 1, Responses: 0, Status: InstanceStatus.Cancelled },
                $"every remote delivery path records the selected starter's accepted host end, settled={settleDelivery}, retired={retired}");
        }

        foreach (var currentHttp in new[] { false, true })
        {
            var (w, owner, id) = await New();
            if (currentHttp)
            {
                var waiting = Http.Spec(Http.Timer) with { Creation = new(1, 0, "http"), History = History.Minimal };
                w.Apply(new("request", 0, S: Http.Timer, M: new("original", id, Kinds.Start, Start: waiting, TargetCreation: 1, ReplyTo: "|q0")));
                await Pump(owner.Worker);
                w.Apply(new("disconnect", S: w.Asked.Single().Request.Registration));
                w.Apply(new("add", M: new("stop", id, Kinds.Terminate, TargetCreation: 1)));
            }
            else
            {
                var counter = Counter.Spec with { Creation = new(1, 0, "counter"), History = History.Minimal };
                w.Apply(new("add", M: new("first", id, "add", Json.Write(new Add("first")), Start: counter, TargetCreation: 1)));
            }
            await Pump(owner.Worker); var before = w.Store.Instances[id.Id];
            var http = Http.Spec(Http.Respond) with { Creation = new(2, 1, "new") };
            w.Apply(new("request", 0, S: Http.Respond, M: new("refused", id, Kinds.Start, Start: http, TargetCreation: 2, ReplyTo: "|q1")));
            await Pump(owner.Worker);
            var caller = w.Asked.Last().Request;
            string? end = currentHttp ? $"END {id} (timer) ended as Cancelled" : null;
            check(before.Model == (currentHttp ? "http" : "counter") && caller.Answer is CreationResult { Result: "StartConflict", Requested: 2, Current: 1 } &&
                w.Store.Instances[id.Id] == before && w.Check() is null && w.Settle() == end,
                $"the genuine HTTP refusal discharges its own creator and preserves the current actor's obligation, http={currentHttp}");
        }
    }
}
