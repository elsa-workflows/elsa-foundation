using System.Collections.Concurrent;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo.Run;

internal static class VersionTakeChecks
{
    internal static async Task RunAsync(string connection, MongoStoreOptions options)
    {
        var markerTimeouts = new ConcurrentQueue<long>();
        using var client = MongoProvider.Client(connection, "kernel-version-check", started =>
        {
            var command = started.Command;
            if (started.CommandName == "find" && command["find"] == "meta" &&
                command.GetValue("autocommit", true) == false && command["filter"]["_id"] == "partitions")
                markerTimeouts.Enqueue(command.GetValue("maxTimeMS", -1).ToInt64());
        });
        using var coldClient = MongoProvider.Client(connection, "kernel-version-check-cold");
        var database = "kfmt_" + Guid.NewGuid().ToString("N");
        var store = new MongoStore(client, database, options);
        var cold = new MongoStore(coldClient, database, options);
        var db = store.Database;
        var meta = db.GetCollection<BsonDocument>("meta");
        var fences = db.GetCollection<BsonDocument>("partitions");
        var nodes = db.GetCollection<BsonDocument>("nodes");
        var markerId = new BsonDocument("_id", "partitions");
        var lease = TimeSpan.FromSeconds(30);
        try
        {
            await CheckLegacy("only empty legacy data collection", () => db.CreateCollectionAsync("messages"));
            await CheckLegacy("legacy store with partition data", () => fences.InsertOneAsync(new BsonDocument("_id", 0)));
            await CheckLegacy("legacy partition count", () => meta.InsertOneAsync(new BsonDocument { { "_id", "partitions" }, { "count", 2 } }));
            await CheckLegacy("other existing metadata", () => meta.InsertOneAsync(new BsonDocument("_id", "legacy")));

            await db.CreateCollectionAsync("meta");
            await store.CreateAsync(2);
            Check(await store.PartitionCountAsync() == 2, "recover from an empty meta collection");
            await client.DropDatabaseAsync(database);
            await store.CreateAsync(2);
            var original = await meta.Find(markerId).SingleAsync();
            Check(original["count"] == 2 && original["storeSchema"] == 1 && original["messageProtocol"] == 1 && original["journalCodec"] == 1,
                "fresh initialization records the count and three current version families");
            await fences.DeleteOneAsync(new BsonDocument("_id", 1));
            await store.CreateAsync(2);
            Check(await store.PartitionCountAsync() == 2 && (await meta.Find(markerId).SingleAsync()).Equals(original),
                "initialization at the recorded count repairs a partial store with the original marker");

            Check(await store.TakeAsync(0, "owner", lease) is { Epoch: 1 }, "a supported marker permits the take");
            var timeouts = markerTimeouts.ToArray();
            Check(timeouts.Length > 0 && timeouts.All(ms => ms == (long)(lease / 3).TotalMilliseconds),
                "the transactional take marker read carries the third-lease server deadline");
            var stable = await Snapshot();
            foreach (var (family, value) in new (string, BsonValue)[] { ("storeSchema", 0), ("messageProtocol", 2), ("journalCodec", 2), ("journalCodec", "1") })
            {
                var alternate = original.DeepClone().AsBsonDocument;
                alternate[family] = value;
                await meta.ReplaceOneAsync(markerId, alternate);
                await FailsClosed(family + "=" + value);
                Check(stable == await Snapshot() && (await meta.Find(markerId).SingleAsync()).Equals(alternate),
                    "refusal leaves the marker, node clocks and partition fences intact");
            }
            await meta.DeleteOneAsync(markerId);
            await FailsClosed("missing marker");
            Check(stable == await Snapshot() && !await meta.Find(markerId).AnyAsync(), "reopen leaves the marker absent on an existing store");
            await meta.InsertOneAsync(original);
            Check(await cold.RetakeAsync(0, "owner", 1, lease) is { Epoch: 2 }, "a supported marker permits a cold retake");
        }
        finally { await client.DropDatabaseAsync(database); }

        async Task CheckLegacy(string label, Func<Task> seed)
        {
            await seed();
            var before = await Snapshot();
            var collections = await Names();
            var metadata = await Docs(meta);
            await Refuses(() => cold.CreateAsync(2), label + " refuses open");
            Check(before == await Snapshot() && collections == await Names() && metadata == await Docs(meta), label + " stays unchanged");
            await client.DropDatabaseAsync(database);
        }

        async Task FailsClosed(string label)
        {
            await Refuses(() => cold.CreateAsync(2), label + " refuses reopen");
            await Refuses(async () => { await cold.TakeAsync(1, "new", lease); }, label + " refuses take");
            await Refuses(async () => { await store.TakeAsync([0, 1], "owner", lease); }, label + " refuses set take");
            await Refuses(async () => { await store.RetakeAsync(0, "owner", 1, lease); }, label + " refuses retake");
        }

        async Task<string> Names() => string.Join(",", (await (await db.ListCollectionNamesAsync()).ToListAsync()).Order());
        async Task<string> Snapshot() => string.Join("|", await Docs(fences), await Docs(nodes), await Docs(db.GetCollection<BsonDocument>("messages")));
        static async Task<string> Docs(IMongoCollection<BsonDocument> coll) =>
            (await coll.Find(FilterDefinition<BsonDocument>.Empty).Sort(new BsonDocument("_id", 1)).ToListAsync()).ToJson();
    }

    private static async Task Refuses(Func<Task> action, string label)
    {
        try { await action(); }
        catch (StoreFormatException e) when (e.Failure == Failure.Definite) { Check(true, label); return; }
        throw new InvalidOperationException(label);
    }

    private static void Check(bool result, string label)
    {
        if (!result) throw new InvalidOperationException(label);
        Console.WriteLine($"ok {label}");
    }
}
