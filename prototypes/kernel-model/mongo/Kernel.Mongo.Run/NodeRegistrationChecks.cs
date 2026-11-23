using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;

namespace Kernel.Mongo.Run;

internal static class NodeRegistrationChecks
{
    private static readonly BsonDocument Marker = new("_id", "partitions");
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TransactionOptions Snapshot = new(ReadConcern.Snapshot, ReadPreference.Primary, WriteConcern.WMajority);

    internal static async Task RunAsync(string connection, MongoStoreOptions options)
    {
        var name = "kfmt_" + Guid.NewGuid().ToString("N");
        Action<CommandStartedEvent>? observe = null;
        using var worker = MongoProvider.Client(connection, "kernel-registration", e => Volatile.Read(ref observe)?.Invoke(e));
        using var admin = MongoProvider.Client(connection, "kernel-format-witness");
        var store = new MongoStore(worker, name, options);
        var db = admin.GetDatabase(name);
        var meta = db.GetCollection<BsonDocument>("meta");
        var nodes = db.GetCollection<BsonDocument>("nodes");
        var partitions = db.GetCollection<BsonDocument>("partitions");
        try
        {
            await store.CreateAsync(2);
            await store.NodeHeartbeatAsync("existing", Lease, Lease);
            var writes = 0;
            var transactions = 0;
            var markerCommands = 0;
            observe = e =>
            {
                if (Collection(e) == "nodes" && e.CommandName == "findAndModify") Interlocked.Increment(ref writes);
                if (Collection(e) == "meta") Interlocked.Increment(ref markerCommands);
                if (e.Command.Contains("startTransaction")) Interlocked.Increment(ref transactions);
            };
            await store.NodeHeartbeatAsync("existing", Lease, Lease);
            observe = null;
            Check(writes == 1 && transactions == 0 && markerCommands == 0, "a recurring live heartbeat renews one node document without a transaction or marker read");

            await WarmRenewalWins();
            await RegistrationWins("fresh");
            await Expire("existing");
            await RegistrationWins("existing");
            await Expire("fresh");
            await Expire("existing");
            await AdvanceWins("first", () => store.NodeHeartbeatAsync("first", Lease, Lease).AsTask());
            await AdvanceWins("existing", () => store.NodeHeartbeatAsync("existing", Lease, Lease).AsTask());
            await AdvanceWins("passive", () => store.HeartbeatAsync("passive", Lease).AsTask());

            // Pause the real take after its marker read has established a transaction snapshot.
            // The admin uses a different client, session, and connection to change the fixture.
            var taken = await store.TakeAsync(0, "owner", Lease);
            Check(taken is not null, "compatible registration admits ownership");
            await Expire("owner");
            await SnapshotRefusal("owner", () => store.RetakeAsync(0, "owner", taken!.Epoch, Lease).AsTask());
            await SnapshotRefusal("unseen-owner", () => store.TakeAsync(1, "unseen-owner", Lease).AsTask());
        }
        finally { await admin.DropDatabaseAsync(name); }

        async Task WarmRenewalWins()
        {
            await nodes.UpdateOneAsync(new BsonDocument("_id", "existing"), Builders<BsonDocument>.Update.Set("seenAt", 0L));
            using var attempt = await admin.StartSessionAsync();
            attempt.StartTransaction(Snapshot);
            await nodes.Find(attempt, new BsonDocument("_id", "existing")).SingleAsync();
            await store.NodeHeartbeatAsync("existing", Lease, Lease);
            try
            {
                await StageUnsupported(attempt);
                throw new InvalidOperationException("revoking a concurrently renewed grant succeeded in the old snapshot");
            }
            catch (MongoException ex) when (ex.HasErrorLabel("TransientTransactionError")) { }
            var marker = await meta.Find(Marker).SingleAsync();
            var owner = await nodes.Find(new BsonDocument("_id", "existing")).SingleAsync();
            Check(marker["messageProtocol"] == 1 && owner.Contains("formatGrant") && owner["seenAt"].AsInt64 > 0,
                "a live renewal conflicts with an older revocation transaction and its marker change rolls back");
        }

        async Task RegistrationWins(string node)
        {
            using var attempt = await admin.StartSessionAsync();
            attempt.StartTransaction(Snapshot);
            await meta.Find(attempt, Marker).SingleAsync();
            await store.NodeHeartbeatAsync(node, Lease, Lease);
            try
            {
                await meta.UpdateOneAsync(attempt, Marker, UnsupportedProtocol());
                throw new InvalidOperationException("registration left the migration's old snapshot writable");
            }
            catch (MongoException ex) when (ex.HasErrorLabel("TransientTransactionError")) { }
            var live = await nodes.Find(new BsonDocument
            {
                { "_id", node }, { "$expr", new BsonDocument("$gte", new BsonArray { "$leaseUntil", new BsonDocument("$toLong", "$$NOW") }) }
            }).SingleAsync();
            Check(live["readableMessageProtocol"].AsBsonArray.SequenceEqual(new BsonArray { 1 }),
                $"{node}: registration invalidates an earlier migration snapshot; a fresh scan sees a live incompatible node");
        }

        async Task AdvanceWins(string node, Func<Task> work)
        {
            using var tx = await admin.StartSessionAsync();
            tx.StartTransaction(Snapshot);
            await StageUnsupported(tx);
            var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            observe = e => { if (Collection(e) == "nodes" && e.CommandName == "findAndModify") arrived.TrySetResult(); };
            var renewal = Task.Run(work);
            try
            {
                await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await tx.CommitTransactionAsync();
                await Refuses(renewal);
                var stored = await nodes.Find(new BsonDocument("_id", node)).FirstOrDefaultAsync();
                Check(stored is null || !stored.Contains("formatGrant") && stored["leaseUntil"].AsInt64 == 0,
                    $"{node}: a queued heartbeat after the advance cannot register or renew the node");
            }
            finally { observe = null; }
            await RestoreMarker();
        }

        async Task SnapshotRefusal(string node, Func<Task> work)
        {
            var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            var before = await partitions.Find(FilterDefinition<BsonDocument>.Empty).Sort(new BsonDocument("_id", 1)).ToListAsync();
            observe = e =>
            {
                if (e.CommandName != "findAndModify" || Collection(e) != "nodes" || !e.Command.Contains("autocommit") || !arrived.TrySetResult()) return;
                if (!release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("fixture did not release the blocked take");
            };
            var taking = Task.Run(work);
            try
            {
                await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                using var migration = await admin.StartSessionAsync();
                migration.StartTransaction(Snapshot);
                await StageUnsupported(migration);
                await migration.CommitTransactionAsync();
            }
            finally { release.Set(); observe = null; }
            await Refuses(taking);
            var after = await partitions.Find(FilterDefinition<BsonDocument>.Empty).Sort(new BsonDocument("_id", 1)).ToListAsync();
            Check(before.SequenceEqual(after), $"{node}: a transaction opened before the marker change leaves all partition fences unchanged");
            await RestoreMarker();
        }

        async Task StageUnsupported(IClientSessionHandle migration)
        {
            // Test-only marker change. A real advance must refuse any live incompatible owner
            // and revoke incompatible grants in the same transaction as publishing the marker.
            await meta.UpdateOneAsync(migration, Marker, UnsupportedProtocol());
            await nodes.UpdateManyAsync(migration, new BsonDocument("formatGrant", new BsonDocument("$exists", true)),
                Builders<BsonDocument>.Update.Unset("formatGrant"));
        }

        Task RestoreMarker() => meta.UpdateOneAsync(Marker, Builders<BsonDocument>.Update.Set("messageProtocol", 1).Set("nodeRegistration", 0L));
        Task Expire(string node) => nodes.UpdateOneAsync(new BsonDocument("_id", node), Builders<BsonDocument>.Update.Set("leaseUntil", 0L));
    }

    private static UpdateDefinition<BsonDocument> UnsupportedProtocol() => Builders<BsonDocument>.Update.Set("messageProtocol", -1).Set("nodeRegistration", "future representation");
    private static string? Collection(CommandStartedEvent e) => e.Command.GetValue(e.CommandName, BsonNull.Value) is BsonString collection ? collection.Value : null;
    private static async Task Refuses(Task action)
    {
        try { await action; }
        catch (StoreFormatException e) when (e.Failure == Failure.Definite && e.Message.Contains("message protocol=-1", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("expected definite refusal for the unsupported marker");
    }

    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
        Console.WriteLine($"ok {message}");
    }
}
