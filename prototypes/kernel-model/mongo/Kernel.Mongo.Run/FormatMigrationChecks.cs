using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;

namespace Kernel.Mongo.Run;

internal static class FormatMigrationChecks
{
    internal static async Task RunAsync(string connection, MongoStoreOptions options)
    {
        var database = "kfmt_" + Guid.NewGuid().ToString("N");
        Action<CommandStartedEvent>? onMigration = null, onWorker = null;
        using var coordinator = MongoProvider.Client(connection, "coordinator", e => Volatile.Read(ref onMigration)?.Invoke(e));
        using var worker = MongoProvider.Client(connection, "worker", e => Volatile.Read(ref onWorker)?.Invoke(e));
        var migrate = new MongoStore(coordinator, database, options);
        var store = new MongoStore(worker, database, options);
        var current = MongoStore.CurrentFormat;
        var target = current with { JournalCodec = 2 };
        var lease = TimeSpan.FromMinutes(1);
        var nodes = store.Database.GetCollection<BsonDocument>("nodes");
        var meta = store.Database.GetCollection<BsonDocument>("meta");
        var marker = new BsonDocument("_id", "partitions");
        var witness = new BsonDocument("_id", "migration-data");
        try
        {
            await store.CreateAsync(2);
            await Refuses(() => migrate.AdvanceStoreFormatAsync(target, target), "reads source");
            foreach (var invalid in new[] { current, current with { JournalCodec = 0 }, new MongoStoreFormat(2, 0, 2) })
            {
                try { await migrate.AdvanceStoreFormatAsync(current, invalid); }
                catch (ArgumentException) { continue; }
                throw new InvalidOperationException($"Accepted invalid advance to {invalid}.");
            }
            Check(true, "the migrator requires a known source and a strict monotone family advance");

            await RegistrationFirst();
            foreach (var next in new[] { target, current with { StoreSchema = 2 }, current with { MessageProtocol = 2 } })
                await Refuses(() => migrate.AdvanceStoreFormatAsync(current, next), "Live node 'new'");
            var id = new BsonDocument("_id", "new");
            await nodes.UpdateOneAsync(id, Builders<BsonDocument>.Update.Unset("readableJournalCodec"));
            await Refuses(() => migrate.AdvanceStoreFormatAsync(current, target), "Live node 'new'");
            await nodes.UpdateOneAsync(id, Builders<BsonDocument>.Update.Set("readableJournalCodec", new BsonArray { "2" }));
            await Refuses(() => migrate.AdvanceStoreFormatAsync(current, target), "Live node 'new'");
            Check(await store.PartitionCountAsync() == 2, "incompatible, missing and malformed advertisements retain the source marker");

            // This is a future compatible advertisement, not a second implementation of the codec.
            await nodes.UpdateOneAsync(id, Builders<BsonDocument>.Update.Set("readableJournalCodec", new BsonArray { 1, 2 }));
            await store.NodeHeartbeatAsync("expired", lease, lease);
            await nodes.UpdateOneAsync(new BsonDocument("_id", "expired"), Builders<BsonDocument>.Update.Set("leaseUntil", 0L));
            var originalMarker = await meta.Find(marker).SingleAsync();
            var originalNodes = await AllNodes();
            try
            {
                await migrate.AdvanceStoreFormatAsync(current, target, async (session, token) =>
                {
                    await meta.InsertOneAsync(session, witness, cancellationToken: token);
                    throw new InvalidDataException("fixture interrupted migration data");
                });
                throw new InvalidOperationException("Expected the migration to abort.");
            }
            catch (InvalidDataException ex) when (ex.Message == "fixture interrupted migration data") { }
            Check(originalMarker == await meta.Find(marker).SingleAsync() && originalNodes.SequenceEqual(await AllNodes()) &&
                !await meta.Find(witness).AnyAsync(), "aborting the data change rolls back the marker, every grant and migration data");

            await MigrationFirst();
            Check((await meta.Find(marker).SingleAsync())["journalCodec"] == 2 && await meta.Find(witness).AnyAsync() &&
                (await AllNodes()).All(n => !n.Contains("formatGrant")), "a majority read sees marker and data together, and all old grants revoked");
            var reran = await migrate.AdvanceStoreFormatAsync(current, target, (_, _) => throw new InvalidOperationException("migrated twice"));
            Check(!reran, "retrying a committed advance reads the completed target without running its data change again");
            await Refuses(async () => { await store.PartitionCountAsync(); }, "journal codec=2");
            await Refuses(() => migrate.AdvanceStoreFormatAsync(current, current with { StoreSchema = 2 }), "journal codec=2");
            Check(true, "the old binary refuses ownership startup and a conflicting migration after the advance");
        }
        finally { await worker.DropDatabaseAsync(database); }

        Task<List<BsonDocument>> AllNodes() => nodes.Find(FilterDefinition<BsonDocument>.Empty).Sort(new BsonDocument("_id", 1)).ToListAsync();

        async Task RegistrationFirst()
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            onMigration = e =>
            {
                if (!Is(e, "update", "nodes") || !entered.TrySetResult()) return;
                if (!release.Wait(TimeSpan.FromSeconds(12))) throw new TimeoutException("migration did not resume");
            };
            var advancing = Task.Run(() => migrate.AdvanceStoreFormatAsync(current, target));
            try
            {
                // The migration has read its marker and eligibility snapshot; the new name is still absent.
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(8));
                await store.NodeHeartbeatAsync("new", lease, lease);
            }
            finally { release.Set(); onMigration = null; }
            await Refuses(() => advancing, "Live node 'new'");
            Check(true, "a first registration committed after the scan forces a new snapshot and an eligibility refusal");
        }

        async Task MigrationFirst()
        {
            var duplicateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var staged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var advancing = migrate.AdvanceStoreFormatAsync(current, target, async (session, token) =>
            {
                await meta.InsertOneAsync(session, witness, cancellationToken: token);
                staged.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(12), token);
            });
            await staged.Task.WaitAsync(TimeSpan.FromSeconds(8));
            onWorker = e =>
            {
                if (Is(e, "findAndModify", "nodes")) entered.TrySetResult();
                if (Is(e, "update", "nodes")) duplicateEntered.TrySetResult();
            };
            var restarting = Task.Run(() => store.NodeHeartbeatAsync("expired", lease, lease).AsTask());
            var duplicate = Task.Run(() => store.AdvanceStoreFormatAsync(current, target,
                (_, _) => throw new InvalidOperationException("concurrent migration ran data twice")));
            try { await Task.WhenAll(entered.Task, duplicateEntered.Task).WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { release.TrySetResult(); onWorker = null; }
            Check(await advancing, "the eligible migration commits once while an old registration is queued");
            Check(!await duplicate, "a concurrent coordinator observes the committed target without repeating its migration data");
            await Refuses(() => restarting, "journal codec=2");
            Check((await nodes.Find(new BsonDocument("_id", "expired")).SingleAsync())["leaseUntil"] == 0L,
                "the restarting old binary receives a definite refusal and leaves its expired lease untouched");
        }
    }

    private static bool Is(CommandStartedEvent e, string command, string collection) =>
        e.CommandName == command && e.Command.GetValue(command, BsonNull.Value) == collection;

    private static async Task Refuses(Func<Task> action, string message)
    {
        try { await action(); }
        catch (StoreFormatException ex) when (ex.Failure == Failure.Definite && ex.Message.Contains(message, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException($"Expected a definite refusal containing '{message}'.");
    }

    private static void Check(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
