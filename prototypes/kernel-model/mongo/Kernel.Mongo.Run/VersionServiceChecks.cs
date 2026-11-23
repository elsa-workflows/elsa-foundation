using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo.Run;

internal static class VersionServiceChecks
{
    internal static async Task RunAsync(string connection, MongoStoreOptions options)
    {
        using var client = MongoProvider.Client(connection, "kernel-role-version-check");
        using var coldClient = MongoProvider.Client(connection, "kernel-role-version-cold");
        var database = "kformatservices_" + Guid.NewGuid().ToString("N");
        var store = new MongoStore(client, database, options);
        var db = store.Database;
        const string actor = "0/versioned-actor";
        var row = new JournalRow(0, 1, actor, 1, "[]");
        var meta = db.GetCollection<BsonDocument>("meta");
        var marker = new BsonDocument("_id", "partitions");
        var messages = db.GetCollection<BsonDocument>("messages");
        var events = db.GetCollection<BsonDocument>("events");
        var views = db.GetCollection<BsonDocument>("views");
        var projections = db.GetCollection<BsonDocument>("projections");
        var nodes = db.GetCollection<BsonDocument>("nodes");
        var partition = new BsonDocument("_id", 0);
        var retained = new BsonDocument { { "_id", "retained" }, { "done", true }, { "keepUntil", 1L } };
        var expired = new BsonDocument { { "_id", "expired" }, { "keepUntil", 1L } };
        try
        {
            await store.CreateAsync(2);
            await db.GetCollection<BsonDocument>("partitions").UpdateOneAsync(partition, new BsonDocument("$set", new BsonDocument("position", 1L)));
            await db.GetCollection<BsonDocument>("journal").InsertOneAsync(new BsonDocument
                { { "partition", 0 }, { "position", 1L }, { "instance", actor }, { "revision", 1L }, { "entries", "[]" } });
            await messages.InsertOneAsync(retained);
            await events.InsertOneAsync(expired);
            await SetCodec(2);
            var cold = new MongoStore(coldClient, database, options);
            var host = DurableHost.ForNode("cold", cold, new WorkflowInstance([]), partitions: 2);
            var runtime = new PartitionRuntime("cold", cold, 2, TimeSpan.FromSeconds(30));

            foreach (var (role, call) in new (string, Func<Task>)[]
            {
                ("counted host startup", () => runtime.StartAsync(host).AsTask()),
                ("projection cursor", () => store.ProjectedAsync(0).AsTask()),
                ("partition journal", () => store.ReadJournalAsync(0, 0, 8).AsTask()),
                ("actor journal", () => store.ReadJournalOfAsync(0, actor, 0).AsTask()),
                ("projection write", () => store.ProjectAsync(0, 0, 1, [row]).AsTask()),
                ("views", () => store.ReadViewsAsync([actor]).AsTask()),
                ("empty view read", () => store.ReadViewsAsync([]).AsTask()),
                ("sweep", () => store.SweepAsync().AsTask()),
                ("requested projector", () => new Projector(store, 0).CatchUpAsync())
            })
                await Refuses(call, "journal codec=2", role + " refused the unsupported journal codec");

            var reports = new List<StoreFormatException>();
            var scheduled = new NodeProjection(store, reportFormatFailure: reports.Add);
            scheduled.Nudge([0, 1]);
            await scheduled.Idle.WaitAsync(TimeSpan.FromSeconds(10));
            Check(reports is [var failure] && ReferenceEquals(failure, scheduled.FormatFailure) &&
                failure.Message.Contains("journal codec=2", StringComparison.Ordinal), "scheduled projection reports its typed error");
            scheduled.Nudge([0, 1]);
            Check(scheduled.Idle.IsCompletedSuccessfully && scheduled.Rounds == 1, "subsequent nudges leave the stopped scheduler idle");
            Check((await projections.Find(partition).SingleAsync())["position"] == 0 &&
                !await views.Find(FilterDefinition<BsonDocument>.Empty).AnyAsync() &&
                !await nodes.Find(FilterDefinition<BsonDocument>.Empty).AnyAsync() &&
                (await messages.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync()).Equals(retained) &&
                (await events.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync()).Equals(expired),
                "refusal preserves empty membership, cursor, views and both expired retained documents");

            var stored = await meta.FindOneAndDeleteAsync(marker);
            var missing = new PartitionRuntime("cold", cold, 2, TimeSpan.FromSeconds(30));
            Check(await db.GetCollection<BsonDocument>("partitions").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty) == 2,
                "the unversioned store has exactly the two partitions requested by the host");
            await Refuses(() => missing.StartAsync(host).AsTask(), "marker is missing", "counted host refuses a missing marker on a populated store");
            Check(!await missing.HeartbeatAsync() && !await nodes.Find(FilterDefinition<BsonDocument>.Empty).AnyAsync(),
                "the refused counted start cannot publish its first heartbeat");
            await meta.InsertOneAsync(stored);
            await SetCodec(1);
            var lastPartition = new BsonDocument("_id", 1);
            foreach (var name in new[] { "clocks", "projections" })
            {
                await db.GetCollection<BsonDocument>(name).DeleteOneAsync(lastPartition);
                var incomplete = new PartitionRuntime("cold", cold, 2, TimeSpan.FromSeconds(30));
                try
                {
                    await incomplete.StartAsync(host);
                    throw new InvalidOperationException($"Counted start accepted the incomplete {name} collection.");
                }
                catch (StoreException error) when (error.Failure == Failure.Definite &&
                    error.Message.Contains($"1 of 2 {name} documents", StringComparison.Ordinal))
                {
                    Check(!await incomplete.HeartbeatAsync() && !await nodes.Find(FilterDefinition<BsonDocument>.Empty).AnyAsync(),
                        $"counted start refuses missing {name} rows before its first heartbeat");
                }
                await cold.CreateAsync(2);
            }

            await runtime.StartAsync(host);
            Check(await runtime.HeartbeatAsync() && await nodes.Find(new BsonDocument("_id", "cold")).AnyAsync(),
                "the compatible counted host publishes its first heartbeat");
            await runtime.StopAsync();
            Check(await cold.PartitionCountAsync() == 2, "the counted host starts at the recorded partition count after repair");
            Check(await store.ProjectedAsync(0) == new Projection(0, 1) &&
                (await store.ReadJournalAsync(0, 0, 8)).SequenceEqual([row]) &&
                (await store.ReadJournalOfAsync(0, actor, 0)).SequenceEqual([row]), "readers reopen at the unchanged journal and cursor");
            var restarted = new NodeProjection(store);
            restarted.Nudge([0, 1]);
            await restarted.Idle.WaitAsync(TimeSpan.FromSeconds(10));
            Check(restarted.FormatFailure is null && await store.ProjectedAsync(0) == new Projection(1, 1) &&
                (await store.ReadViewsAsync([actor])).Single() == new ViewRow(actor, 1, 1), "a restarted projector advances the cursor and writes the view");
            Check(await store.SweepAsync() == 2 && !await messages.Find(FilterDefinition<BsonDocument>.Empty).AnyAsync() &&
                !await events.Find(FilterDefinition<BsonDocument>.Empty).AnyAsync(), "sweep removes both expired records after compatibility returns");
        }
        finally { await client.DropDatabaseAsync(database); }

        Task SetCodec(int version) => meta.UpdateOneAsync(marker, new BsonDocument("$set", new BsonDocument("journalCodec", version)));
    }

    private static async Task Refuses(Func<Task> action, string diagnostic, string message)
    {
        try { await action(); }
        catch (StoreFormatException e) when (e.Failure == Failure.Definite && e.Message.Contains(diagnostic, StringComparison.Ordinal))
        {
            Check(true, message);
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
        Console.WriteLine("ok " + message);
    }
}
