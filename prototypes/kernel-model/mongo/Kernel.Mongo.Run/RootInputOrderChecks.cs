using Contract = Kernel.Foundation.Contracts;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo.Run;

internal static class RootInputOrderChecks
{
    internal static async Task RunAsync(string connection, string database, MongoStoreOptions options, Contract.BoundScope scope,
        string reference, MongoStore.JobRootCheckpoint initial, TimeSpan lease)
    {
        using var client = MongoProvider.Client(connection, "root-order-reader");
        var store = new MongoStore(client, database, options);
        var db = store.Database;
        var inbox = db.GetCollection<BsonDocument>("messages");
        var result = await inbox.Find(new BsonDocument { { "rootRef", reference }, { "kind", Kinds.JobFinished } }).SingleAsync();
        var order = result["inputOrder"].AsInt64;
        var physical = result["seq"].AsInt64;
        var target = new Contract.Target.Ref(new(reference));
        var ingress = store.JobRootControls((_, _) => true, lease);
        await ingress.SendAsync(scope, target, "skewed", Kinds.Resume);
        await db.GetCollection<BsonDocument>("clocks").UpdateOneAsync(new BsonDocument("_id", 0),
            Builders<BsonDocument>.Update.Set("clock", physical - 1));
        await ingress.SendAsync(scope, target, "tied", Kinds.Suspend);
        async Task<BsonDocument> Command(string id) => await inbox.Find(new BsonDocument { { "rootRef", reference }, { "operatorId", id } }).SingleAsync();
        var skewed = await Command("skewed");
        var tied = await Command("tied");
        Check((await Command("before-result"))["inputOrder"].AsInt64 < order && order < skewed["inputOrder"].AsInt64 &&
            skewed["inputOrder"].AsInt64 < tied["inputOrder"].AsInt64 && physical > skewed["seq"].AsInt64 && physical == tied["seq"].AsInt64,
            "the physical positive-generation settlement and outside controls share one durable order with inverted and equal raw clocks");
        var available = new Kernel.WorkflowActor.WorkflowActor([new NativeExecutionModel(new WorkflowRegistry())]);
        var missing = new Kernel.WorkflowActor.WorkflowActor([]);
        await using var owner = new Worker("result-successor", 0, store, new WorkflowInstance([]));
        await using var active = await new DurableHost([owner]).ActivateAsync(0) ?? throw new InvalidOperationException("Order check did not obtain ownership.");
        var fence = owner.Fences.Single();
        var rev = initial.Image.Revision;
        async Task Apply(string id, Contract.IActorType actor)
        {
            var control = await store.ReadDueRootControlAsync(scope, reference, withholds: actor.Withholds);
            if (control?.Id != id || !(await store.ApplyJobRootControlAsync(scope, reference, control.InboxKey, rev, fence, lease,
                (header,row,input,token) => ActorAdapter.StageRootControlAsync(actor, owner, header, row, input, token), actor.Withholds)).Landed)
                throw new InvalidOperationException("The expected eligible control did not apply: " + id);
            rev++;
        }
        await Apply("second-suspension", available);
        await Apply("unlock", available);
        await Apply("before-result", available);
        Check(await store.ReadDueRootControlAsync(scope, reference, withholds: available.Withholds) is null,
            "an available model keeps its recorded result ahead of the later control with the smaller physical sequence");
        await Apply("skewed", missing);
        Check(await store.ReadDueRootControlAsync(scope, reference, withholds: available.Withholds) is null,
            "an available model keeps the same result ahead of the later control with the equal physical sequence");
        await Apply("tied", missing);
        var after = (await store.ReadJobRootCheckpointAsync(scope, reference))!;
        Check(after.Suspended && after.Image.Revision == rev && after.Journal == initial.Journal &&
            after.Results.Single().InboxKey == initial.Results.Single().InboxKey &&
            result.Equals(await inbox.Find(new BsonDocument("_id", result["_id"])).SingleAsync()),
            "successive eligible control turns retain the original generation-one result, root order and journal");
    }

    private static void Check(bool pass, string title)
    {
        if (!pass) throw new InvalidOperationException(title);
        Console.WriteLine("ok " + title);
    }
}
