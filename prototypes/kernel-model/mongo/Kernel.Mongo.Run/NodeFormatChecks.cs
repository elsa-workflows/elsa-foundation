using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo.Run;

internal static class NodeFormatChecks
{
    internal static async Task RunAsync(string connection, MongoStoreOptions options)
    {
        using var client = MongoProvider.Client(connection, "kernel-node-formats");
        var database = "kfmt_" + Guid.NewGuid().ToString("N");
        var store = new MongoStore(client, database, options);
        var nodes = store.Database.GetCollection<BsonDocument>("nodes");
        var lease = TimeSpan.FromSeconds(30);
        try
        {
            await store.CreateAsync(2);
            await nodes.InsertOneAsync(new BsonDocument { { "_id", "existing" }, { "seenAt", 1L }, { "leaseUntil", 3L } });
            var legacy = await Read("existing");
            Check(!legacy.Contains("readableStoreSchema") && !legacy.Contains("readableMessageProtocol") && !legacy.Contains("readableJournalCodec"),
                "format-1 node documents can lack advertisements");
            await store.HeartbeatAsync("existing", lease);
            var membership = await Read("existing");
            Check(Advertises(membership) && membership["leaseUntil"].AsInt64 == 3 && membership["seenAt"].AsInt64 > 1,
                "the next membership write fills missing facts and preserves the old lease");
            await store.HeartbeatAsync("passive", lease);
            var passive = await Read("passive");
            Check(Advertises(passive) && !passive.Contains("leaseUntil"), "new membership advertises without acquiring a lease");

            await store.NodeHeartbeatAsync("owner", lease, lease);
            Check(Renewed(await Read("owner")), "a node's single-document heartbeat sets the formats with its lease");
            await Stale("owner");
            var take = await store.TakeAsync([0, 1], "owner", lease);
            Check(take.Taken.Count == 2 && Renewed(await Read("owner")), "set take replaces all three prior advertisements");
            await Stale("owner");
            var retake = await store.RetakeAsync(0, "owner", take.Taken.Single(f => f.Partition == 0).Epoch, lease);
            Check(retake is { Epoch: 2 } && Renewed(await Read("owner")), "retake refreshes the formats and advances the owned epoch");
            await Stale("owner");
            await store.HeartbeatAsync("owner", lease);
            var observed = await Read("owner");
            Check(Advertises(observed), "membership rewrites stale arrays on an existing live owner");
            await Stale("owner");
            await store.NodeHeartbeatAsync("owner", lease, lease);
            Check(Renewed(await Read("owner")), "the next ownership heartbeat replaces stale arrays");
            await Stale("owner");
            var settled = await Read("owner");
            var refused = await store.TakeAsync(0, "other", lease);
            Check(refused is null && Renewed(await Read("other")), "a contender still advertises when its take finds no free partition");
            Check((await Read("owner")).Equals(settled),
                "a contender only writes its own advertisement");
        }
        finally { await client.DropDatabaseAsync(database); }

        Task<BsonDocument> Read(string node) => nodes.Find(new BsonDocument("_id", node)).SingleAsync();
        Task Stale(string node) => nodes.UpdateOneAsync(new BsonDocument("_id", node), new BsonDocument("$set", new BsonDocument
        {
            { "readableStoreSchema", new BsonArray { 1, 2 } },
            { "readableMessageProtocol", new BsonArray { 3 } },
            { "readableJournalCodec", new BsonArray { 4 } }
        }));
        bool Renewed(BsonDocument node) => Advertises(node) && node["seenAt"].AsInt64 > 0 &&
            node["leaseUntil"].AsInt64 == node["seenAt"].AsInt64 + (long)lease.TotalMilliseconds;
    }

    private static bool Advertises(BsonDocument node) =>
        new[] { "readableStoreSchema", "readableMessageProtocol", "readableJournalCodec" }
            .All(field => node.GetValue(field, BsonNull.Value) is BsonArray { Count: 1 } versions && versions[0].IsInt32 && versions[0].AsInt32 == 1);

    private static void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
