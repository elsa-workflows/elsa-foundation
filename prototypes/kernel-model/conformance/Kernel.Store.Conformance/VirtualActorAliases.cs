using Kernel.Sim;

namespace Kernel.Store.Conformance;

internal static class VirtualActorAliases
{
    private sealed class Waiting : Model<List<string>>
    {
        public override string Key => "virtual-identity";
        public readonly List<string> Entries = [];
        public override async ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token)
        {
            Entries.Add(job.Instance.Id);
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return null;
        }

        protected override void Turn(ITurn turn, List<string> state)
        {
            if (state.Count == 0) turn.StartJob("one");
            state.AddRange(turn.Inbox.Select(m => m.Id));
        }
    }

    internal static async Task Run(Context c)
    {
        var ownerName = new InstanceRef("0/Actor");
        var incoming = new InstanceRef("0/actor");
        var addresses = await c.S.LoadActorsAsync([ownerName.Id, incoming.Id]);
        var aliases = addresses[0].Canonical == addresses[1].Canonical;
        var model = new Waiting();
        var spec = new StartSpec(model.Key, "probe", Ephemeral: true, ShortWait: TimeSpan.FromMinutes(10));
        var worker = new Worker("virtual-actor", 0, c.S, new WorkflowInstance([model]), new() { Lease = TimeSpan.FromMinutes(1) });
        await using (worker)
        {
            await new DurableHost([worker]).ActivateAsync(0);
            foreach (var m in new[] { new Message("first", ownerName, Kinds.Start, Start: spec),
                new Message("second", incoming, Kinds.Start, Start: spec), new Message("third", incoming, "ping") })
            {
                Context.That(worker.Post(m), "owner rejected virtual input");
                await worker.PollAsync();
                await worker.RunAsync();
            }
            Context.Equal(aliases ? 1 : 2, worker.Unwritten.Count(), "wrong number of unwritten actors");
            Context.Equal(aliases ? 1 : 2, model.Entries.Count, "wrong number of physical job entries");
            Context.That((await c.S.LoadAsync([ownerName.Id, incoming.Id])).Count == 0, "test actor promoted early");
        }
        var rows = await c.S.LoadAsync([ownerName.Id, incoming.Id]);
        Context.Equal(aliases ? 1 : 2, rows.Count, "promotion lost an actor");
        var row = rows.Single(r => r.Id == ownerName.Id);
        Context.That(Json.Read<List<string>>(row.State).SequenceEqual(aliases ? ["first", "second", "third"] : ["first"]),
            "canonical workflow lost its live state");
        Context.That(DurableKeyJson.ReadKeys(row.Jobs!).SequenceEqual(["one"]), "canonical workflow lost its held job");
    }
}
