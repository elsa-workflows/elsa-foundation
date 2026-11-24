namespace Kernel.Sim;

internal static partial class GenerationSamplingChecks
{
    private static async Task CheckNamedHandoffs(Action<bool,string> check)
    {
        foreach (var history in new[] { History.Full, History.Minimal })
        foreach (var sampleAbsence in new[] { false, true })
        foreach (var model in new[] { "http", "counter" })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            var owner = w.Of(0, 0); var id = InstanceRef.In(0, "named-reuse");
            await using var prior = new Worker("previous", 0, w.Store, new WorkflowInstance([new Http(8, (_, _) => new ValueTask<object?>("done"))]),
                new WorkerOptions { Monotonic = () => w.Time });
            await new DurableHost([prior]).ActivateAsync(0);
            w.Apply(new("add", M: new("first", id, Kinds.Start, Start: Http.Spec(Http.Wait) with { History = history })));
            await Pump(prior);
            w.Apply(new("add", M: new("resume", id, "resume"))); await Pump(prior); w.Time += 350; await Pump(prior);
            var ended = w.Store.Instances[id.Id]; var before = w.Check();
            w.Apply(new("add", M: new("remove", id, Kinds.Delete))); await Pump(prior);
            var absent = !w.Store.Instances.ContainsKey(id.Id);
            var receipt = w.Store.Journal.Values.Single(j => j.Instance == id.Id && j.Revision == -1);
            var middle = sampleAbsence ? w.Check() : null;
            await prior.DisposeAsync(); await owner.Runtime.ActivateAsync(0);
            var next = model == "http" ? new Message("next", id, Kinds.Start, Start: Http.Spec(Http.Respond) with { History = history })
                : new Message("next", id, "add", Json.Write(new Add("one")), Start: Counter.Spec with { History = history });
            w.Apply(new("add", M: next)); await Pump(owner.Worker); var born = w.Store.Instances[id.Id];
            check(before is null && middle is null && ended is { Revision: 3, Creation: 0, Status: InstanceStatus.Finished } && absent &&
                receipt.Entries == """["deleted"]""" && w.Store.Journal.Values.Contains(receipt) &&
                born is { Revision: 1, Creation: 0, Retired: false } && born.Model == model &&
                born.Status == (model == "http" ? InstanceStatus.Finished : InstanceStatus.Running) &&
                w.Check() is null && w.Settle() is null, $"the prior owner's actual named retirement permits a new selected legacy birth, {model}, sampled={sampleAbsence}, {history}");
        }

        foreach (var boundary in new[] { "admission alone", "changed receipt" })
        {
            var w = new World(new Config(1, 1, Flaw.None)) { Auto = true };
            var owner = w.Of(0, 0); var id = InstanceRef.In(0, "invalid-reuse");
            await using var prior = new Worker("previous", 0, w.Store, new WorkflowInstance([new Http(8, (_, _) => new ValueTask<object?>("done"))]),
                new WorkerOptions { Monotonic = () => w.Time });
            await new DurableHost([prior]).ActivateAsync(0);
            var start = Http.Spec(Http.Respond) with { History = History.Minimal };
            w.Apply(new("add", M: new("old", id, Kinds.Start, Start: start))); await Pump(prior);
            var opening = w.Check();
            w.Apply(new("add", M: new("erase", id, Kinds.Delete, Due: boundary == "admission alone" ? w.Time + 1000000 : null)));
            if (boundary == "admission alone") w.Store.Instances.Remove(id.Id);
            else
            {
                await Pump(prior);
                var receipt = w.Store.Journal.Single(j => j.Value.Instance == id.Id && j.Value.Revision == -1);
                w.Store.Journal[receipt.Key] = receipt.Value with { Entries = """["different"]""" };
            }
            await prior.DisposeAsync(); await owner.Runtime.ActivateAsync(0);
            w.Apply(new("add", M: new("new", id, Kinds.Start, Start: start))); await Pump(owner.Worker);
            var expected = boundary == "admission alone" ? $"I10 {id} replaced a different checkpoint at its fenced input" :
                $"I22 {id} lost the checkpoint or receipt of its observed named deletion";
            check(opening is null && w.Store.Instances[id.Id] is { Status: InstanceStatus.Finished, Creation: 0, Revision: 1 } &&
                w.Check() == expected && w.Check() == expected && w.Settle() == expected,
                $"a same-revision named replacement requires both ended predecessor and exact deletion receipt, {boundary}");
        }
    }
}
