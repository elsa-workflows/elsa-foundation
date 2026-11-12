using System.Diagnostics;
using Kernel.Store.Conformance;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo.Run;

/// <summary>
/// Targeted experiments behind the findings in MONGO.md.
/// <list type="bullet">
/// <item><c>probe crash</c>: a process dies between the fence and commitTransaction. How long until another node can take the partition?</item>
/// <item><c>probe limits</c>: how large one commit can be, and what it costs in round trips and time.</item>
/// </list>
/// </summary>
public static class Probes
{
    public static async Task<int> RunAsync(string connection, MongoStoreOptions options, string[] args)
    {
        switch (args)
        {
            case ["crash"]: return await Crash(connection);
            case ["crash-child", var database]: return await CrashChild(connection, database);
            case ["limits"]: return await Limits(connection, options);
            default:
                Console.Error.WriteLine("probe crash | probe limits");
                return 2;
        }
    }

    // ---- a process that dies inside a commit ------------------------------------------------------------------------

    private static async Task<int> Crash(string connection)
    {
        using var admin = MongoProvider.Client(connection, "kernel-admin");
        var adminDb = admin.GetDatabase("admin");
        var original = (await adminDb.RunCommandAsync<BsonDocument>(new BsonDocument { { "getParameter", 1 }, { "transactionLifetimeLimitSeconds", 1 } }))["transactionLifetimeLimitSeconds"].ToInt32();
        try
        {
            foreach (var limit in new[] { original, 3 })
            {
                await adminDb.RunCommandAsync<BsonDocument>(new BsonDocument { { "setParameter", 1 }, { "transactionLifetimeLimitSeconds", limit } });
                var database = "kprobe_" + Guid.NewGuid().ToString("N")[..12];
                var store = new MongoStore(admin, database);
                await store.CreateAsync(1);
                // The child takes the partition with a 500 ms lease, opens a commit's transaction, runs the fence, and dies.
                using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, [typeof(Probes).Assembly.Location, connection, "probe", "crash-child", database]) { RedirectStandardOutput = true })!;
                var line = await child.StandardOutput.ReadLineAsync();
                await child.WaitForExitAsync();
                Console.WriteLine($"transactionLifetimeLimitSeconds {limit}: child said '{line}', exit code {child.ExitCode}");
                var watch = Stopwatch.StartNew();
                var errors = new Dictionary<string, int>();
                Lease? lease = null;
                while (lease is null && watch.Elapsed < TimeSpan.FromSeconds(limit + 30))
                {
                    try { lease = await store.TakeAsync(0, "heir", TimeSpan.FromSeconds(30)); }
                    catch (Exception e)
                    {
                        var key = e is MongoCommandException c ? $"{c.CodeName} ({c.Code}), labels [{string.Join(", ", c.ErrorLabels)}]" : e.GetType().Name;
                        errors[key] = errors.GetValueOrDefault(key) + 1;
                    }
                    if (lease is null) await Task.Delay(100);
                }
                Console.WriteLine($"  the heir took the partition after {watch.ElapsedMilliseconds} ms (the dead owner's lease was 500 ms): epoch {lease?.Epoch}, position {lease?.Position}");
                foreach (var (error, n) in errors) Console.WriteLine($"  take failed x{n}: {error}");
                var polled = await store.PollAsync(0, 1, TimeSpan.Zero, 1);
                Console.WriteLine($"  a poll meanwhile is not blocked: {polled.Messages.Count} rows at {polled.Now}");
                await admin.DropDatabaseAsync(database);
            }
        }
        finally
        {
            await adminDb.RunCommandAsync<BsonDocument>(new BsonDocument { { "setParameter", 1 }, { "transactionLifetimeLimitSeconds", original } });
        }
        return 0;
    }

    private static async Task<int> CrashChild(string connection, string database)
    {
        var client = MongoProvider.Client(connection, "kernel-dying");
        var store = new MongoStore(client, database);
        var lease = (await store.TakeAsync(0, "dying", TimeSpan.FromMilliseconds(500)))!;
        var session = await client.StartSessionAsync();
        session.StartTransaction(new TransactionOptions(ReadConcern.Snapshot, ReadPreference.Primary, WriteConcern.WMajority));
        await client.GetDatabase(database).GetCollection<BsonDocument>("partitions").UpdateOneAsync(session,
            new BsonDocument { { "_id", 0 }, { "epoch", lease.Epoch }, { "position", lease.Position } }, new BsonDocument("$set", new BsonDocument("position", lease.Position + 1)));
        Console.WriteLine($"fenced at epoch {lease.Epoch}; dying before commitTransaction");
        Console.Out.Flush();
        Environment.FailFast("the probe kills this process inside a commit");
        return 0;
    }

    // ---- how large one commit can be ------------------------------------------------------------------------------------

    private static async Task<int> Limits(string connection, MongoStoreOptions options)
    {
        var counter = new CommandCounter();
        using var client = MongoProvider.Client(connection, "kernel-limits", counter.Started);
        var database = "kprobe_" + Guid.NewGuid().ToString("N")[..12];
        var store = new MongoStore(client, database, options);
        await store.CreateAsync(2);
        var lease = (await store.TakeAsync(0, "big", TimeSpan.FromMinutes(5)))!;
        var (epoch, position) = (lease.Epoch, lease.Position);
        var n = 0;

        async Task Try(string name, int steps, int stateBytes, int sends)
        {
            var batch = new CommitBatch(0, epoch, position, TimeSpan.FromMinutes(5), false, Enumerable.Range(0, steps).Select(_ =>
            {
                var id = $"0/big{++n}";
                var sent = Enumerable.Range(0, sends).Select(i => new Message($"{id}#1.{i:D4}", InstanceRef.In(1, "sink"), "k")).ToList();
                return new StepCommit(id, new InstanceRow(id, "m", "d", InstanceStatus.Running, new string('s', stateBytes), 1, null), null, [], [], sent, [], [], "[\"j\"]");
            }).ToList(), [], []);
            counter.Reset();
            var watch = Stopwatch.StartNew();
            string outcome;
            try
            {
                var result = await store.CommitAsync(batch);
                outcome = result.Landed ? "landed" : "refused by the fence";
                if (result.Landed) position += batch.Positions;
            }
            catch (StoreException e) { outcome = $"{e.Failure}: {e.InnerException?.GetType().Name}: {e.Message.Split('\n')[0][..Math.Min(160, e.Message.Split('\n')[0].Length)]}"; }
            var reading = counter.Read();
            var documents = steps * (2 + sends) + 1;
            Console.WriteLine($"{name,-52} {documents,7} documents, {(long)steps * stateBytes / 1024 / 1024,5} MiB of state: {watch.ElapsedMilliseconds,6} ms, {reading.RoundTrips} round trips ({reading.Detail}): {outcome}");
        }

        await Try("one step", 1, 200, 2);
        await Try("the kernel's default batch: 500 steps", 500, 200, 2);
        await Try("2,000 steps", 2_000, 200, 2);
        await Try("10,000 steps", 10_000, 200, 2);
        await Try("50,000 steps", 50_000, 200, 2);
        await Try("100 steps of 1 MiB of state", 100, 1024 * 1024, 0);
        await Try("one step of 15 MiB of state", 1, 15 * 1024 * 1024, 0);
        await Try("one step of 17 MiB of state", 1, 17 * 1024 * 1024, 0);
        await client.DropDatabaseAsync(database);
        return 0;
    }
}
