using Kernel.Sim;

namespace Kernel.Store.Conformance;

internal static class IssuedChildren
{
    internal static async Task AcrossTakeover(Context c)
    {
        var parent = new InstanceRef("0/parent");
        var model = new WorkflowInstance([new Parent(), new Child()]);
        Worker MakeOwner(int partition, string name, IKernelStore store) =>
            new(name, partition, store, model, new WorkerOptions { Partitions = 2 });
        var old = MakeOwner(0, "old", c.S);
        await new DurableHost([old]).ActivateAsync(0);
        var epoch = old.Epoch;
        var outside = new Message(parent.Id + "#1.0000", new("0/outside"), "outside", KeepUntil: long.MaxValue);
        Context.That(await c.S.AddAsync(outside), "retained ordinary wire precedes the creator");
        Context.That(old.Post(new("parent-start", parent, Kinds.Start, Start: new("parent", "p", Json.Write(new ParentInput(2, 0, true))))), "parent start posted");
        await old.LoadAsync();
        old.RunTurn(parent.Id);
        await old.FlushAsync();
        var row = (await c.S.LoadAsync([parent.Id])).Single();
        var expected = Json.Read<ParentState>(row.State).Pending;
        Context.That(expected.Count == 2 && expected[0] == InstanceRef.ForChild(new InstanceRef(expected[0]).Partition, 0, epoch, 1).Id &&
            expected[1] == InstanceRef.ForChild(0, 0, epoch, 2).Id, "parent commit pins the two exact owner-issued addresses");
        await old.DisposeAsync();
        var fresh = c.Provided.NewClient();
        var owners = Enumerable.Range(0, 2).Select(p => MakeOwner(p, "new-" + p, fresh)).ToList();
        foreach (var worker in owners) await new DurableHost([worker]).ActivateAsync(worker.Partition);
        for (var pass = 0; pass < 12; pass++)
            foreach (var worker in owners) { await worker.PollAsync(); await worker.RunAsync(); }
        var results = await fresh.LoadAsync(expected.Append(parent.Id).ToList());
        var done = Json.Read<ParentState>(results.Single(r => r.Id == parent.Id).State).Done;
        Context.That(results.Count == 3 && results.All(r => r.Status == InstanceStatus.Finished) &&
            results.Where(r => r.Id != parent.Id).All(r => r.Parent == parent.Id) &&
            done.Order().SequenceEqual(expected.Order()) && !await fresh.AddAsync(outside),
            "fresh owners delivered both original children and preserved the outside namesake");
        foreach (var worker in owners) await worker.DisposeAsync();
    }
}
