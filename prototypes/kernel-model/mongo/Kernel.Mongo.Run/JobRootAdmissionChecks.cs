using MongoDB.Bson;
using MongoDB.Driver;
using BoundScope = Kernel.Foundation.Contracts.BoundScope;

namespace Kernel.Mongo.Run;

internal static class JobRootAdmissionChecks
{
    internal static async Task RunAsync(string connection, MongoStoreOptions options)
    {
        var name = "kroot_" + Guid.NewGuid().ToString("N");
        using var client = MongoProvider.Client(connection, "job-root-admit");
        using var second = MongoProvider.Client(connection, "job-root-reopen");
        var staged = options with { JobGenerations = true, ScopedJobRoots = true };
        var writer = new MongoStore(client, name, staged);
        var cold = new MongoStore(second, name, staged);
        try
        {
            var versionTwo = new MongoStore(client, name, options with { JobGenerations = true });
            await versionTwo.CreateAsync(2);
            var owner = await versionTwo.TakeAsync(0, "v2", TimeSpan.FromSeconds(30));
            try
            {
                await writer.UpgradeScopedJobRootFormatAsync();
                throw new InvalidOperationException("Format 3 admitted a live format-2 owner.");
            }
            catch (StoreFormatException ex) when (ex.Failure == Failure.Definite && ex.Message.Contains("Live node", StringComparison.Ordinal)) { }
            await versionTwo.Database.GetCollection<BsonDocument>("nodes").UpdateOneAsync(
                new BsonDocument("_id", "v2"), Builders<BsonDocument>.Update.Set("leaseUntil", 0L));
            Check(owner is not null && await writer.UpgradeScopedJobRootFormatAsync() && !await writer.UpgradeScopedJobRootFormatAsync(),
                "an explicit coordinated stop advances format (2,2,1) to (3,3,1) once");
            try
            {
                await versionTwo.TakeAsync(0, "stale-reader", TimeSpan.FromMinutes(1));
                throw new InvalidOperationException("A format-2 owner read scoped root data.");
            }
            catch (StoreFormatException ex) when (ex.Failure == Failure.Definite && ex.Message.Contains("schema=3", StringComparison.Ordinal)) { }
            await writer.CreateAsync(2);
            var scope = new BoundScope("a");
            var other = new BoundScope("b");
            var spec = new StartSpec("job-model", "type", "input");
            var calls = await Task.WhenAll(writer.AdmitJobRootAsync(scope, "operation", spec, TimeSpan.FromMinutes(2)),
                cold.AdmitJobRootAsync(scope, "operation", spec, TimeSpan.FromMinutes(2)));
            Check(calls.Count(r => r.Duplicate) == 1 && calls[0].Reference == calls[1].Reference, "concurrent callers receive one exact generated root");
            var creator = await cold.PendingJobRootAsync(scope, calls[0].Reference);
            Check(creator is { TargetCreation: 1, Start.Creation: { Generation: 1, Previous: 0 }, Payload: "input" } &&
                  creator.InboxKey.Id == "root:" + creator.Target.Id && creator.Start!.Creation!.Intent == FoundationRootCreator.OperationDigest("operation"),
                "a new client reads the admitted creator, operation digest and generated address");
            var storedCreator = await writer.Database.GetCollection<BsonDocument>("messages").Find(
                new BsonDocument("rootRef", calls[0].Reference)).SingleAsync();
            var partitionClock = await writer.Database.GetCollection<BsonDocument>("clocks").Find(
                new BsonDocument("_id", creator!.Target.Partition)).SingleAsync();
            Check(storedCreator["seq"].AsInt64 > 0 && storedCreator["seq"] == partitionClock["clock"] &&
                  storedCreator["seen"] == 0L && storedCreator["trial"] == 0L, "the creator receives one durable outside sequence and starts pending");
            Check(await writer.PendingJobRootAsync(other, calls[0].Reference) is null, "a different tenant cannot read the root creator");
            var separate = await cold.AdmitJobRootAsync(other, "operation", spec, TimeSpan.FromMinutes(1));
            Check(separate.Reference != calls[0].Reference && !separate.Duplicate, "different tenants have independent operation receipts");
            for (var partition = 0; partition < 2; partition++)
                Check((await writer.PollAsync(partition, 1, TimeSpan.Zero, 10)).Messages.Count == 0, "ordinary poll cannot deliver a scope-bound creator");
            var expired = await writer.AdmitJobRootAsync(scope, "short", spec, TimeSpan.Zero);
            await Task.Delay(20);
            await writer.SweepAsync();
            Check(!((await cold.AdmitJobRootAsync(scope, "short", spec, TimeSpan.FromMinutes(1))).Duplicate) &&
                  await cold.PendingJobRootAsync(scope, expired.Reference) is not null,
                "expiring an operation receipt leaves its original birth and pending creator intact");
        }
        finally { await client.DropDatabaseAsync(name); }
    }

    private static void Check(bool value, string description)
    {
        if (!value) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
