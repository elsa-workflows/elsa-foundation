using Kernel.Store.Conformance;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo.Run;

internal static class JobGenerationChecks
{
    internal static async Task RunAsync(string connection, MongoStoreOptions options)
    {
        const string actor = "0/job-owner", legacy = "0/legacy";
        var database = "kgen_" + Guid.NewGuid().ToString("N");
        var duration = TimeSpan.FromMinutes(1);
        using var oldClient = MongoProvider.Client(connection, "kernel-v1");
        using var newClient = MongoProvider.Client(connection, "kernel-generation-stage");
        using var restartedClient = MongoProvider.Client(connection, "kernel-generation-restart");
        var old = new MongoStore(oldClient, database, options);
        var staged = options with { JobGenerations = true };
        var writer = new MongoStore(newClient, database, staged);
        var cold = new MongoStore(restartedClient, database, staged);
        var instances = old.Database.GetCollection<BsonDocument>("instances");
        var messages = old.Database.GetCollection<BsonDocument>("messages");
        var nodes = old.Database.GetCollection<BsonDocument>("nodes");
        try
        {
            await old.CreateAsync(1);
            var before = (await old.TakeAsync(0, "old", duration))!;
            var original = Make.M("legacy-ticket", 0);
            Check((await Commit(old, before, 0, Make.New(legacy, [original]))).Landed, "the default writer opens and writes format 1");
            var row = Make.Row(actor, 1) with { NextJobGeneration = 19 };
            var ordinary = Make.M("external", 0) with { JobGeneration = 18 };
            await Refuses(() => Commit(old, before, 1, Make.Turn(actor, row)), "schema=2");
            await Refuses(() => old.AddAsync(ordinary).AsTask(), "schema=2");
            await Refuses(() => Commit(writer, before, 1, Make.Turn(actor, row)), "Stored schema=1");
            await Refuses(() => writer.AddAsync(ordinary).AsTask(), "Stored schema=1");
            await Refuses(() => writer.CreateAsync(1), "Stored schema=1");
            await RejectOrdinaryIngress(writer, "Stored schema=1");
            Check((await writer.ReadJournalOfAsync(0, legacy, 0)).Count == 1, "the staged read-only role can read the unchanged v1 journal");
            await Refuses(() => writer.UpgradeJobGenerationFormatAsync(), "Live node 'old'");
            Check(!(await instances.Find(new BsonDocument("_id", legacy)).SingleAsync()).Contains("nextJobGeneration") &&
                  !(await messages.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync()).Contains("jobGeneration"),
                "the blocked upgrade and refused writes retain sparse legacy documents");

            await nodes.UpdateOneAsync(new BsonDocument("_id", "old"), Builders<BsonDocument>.Update.Set("leaseUntil", 0L));
            Check(await writer.UpgradeJobGenerationFormatAsync() && !await writer.UpgradeJobGenerationFormatAsync(),
                "a coordinated stop upgrades once to schema 2, protocol 2 and codec 1");
            await Refuses(() => old.NodeHeartbeatAsync("old", duration, duration).AsTask(), "Stored schema=2");
            await RejectOrdinaryIngress(old, "Stored schema=2");
            await Refuses(() => old.ReadJournalAsync(0, 0, 1).AsTask(), "Stored schema=2");
            var oldNode = await nodes.Find(new BsonDocument("_id", "old")).SingleAsync();
            Check(!oldNode.Contains("formatGrant") && oldNode["leaseUntil"] == 0L, "the revoked v1 grant cannot renew an expired owner");
            await writer.CreateAsync(1);
            var lease = (await writer.TakeAsync(0, "new", duration))!;
            var advertised = await nodes.Find(new BsonDocument("_id", "new")).SingleAsync();
            Check(advertised["readableStoreSchema"].AsBsonArray.SequenceEqual(new BsonArray { 2 }) &&
                  advertised["readableMessageProtocol"].AsBsonArray.SequenceEqual(new BsonArray { 2 }) &&
                  advertised["readableJournalCodec"].AsBsonArray.SequenceEqual(new BsonArray { 1 }),
                "the new writer grants only the active format and advertises its ownership capabilities");
            Check((await cold.LoadAsync([legacy])).Single().NextJobGeneration is null &&
                  (await cold.PollAsync(0, lease.Epoch, TimeSpan.Zero, 20)).Messages.Single().Message.JobGeneration == 0,
                "a new client decodes absent checkpoint and envelope fields as null and zero");

            var prior = row with { Creation = 1, NextJobGeneration = 18 };
            await using var worker = new Worker("compose", 0, new InMemoryStore(1, () => 1000), new WorkflowInstance([]));
            await using var activation = (await new DurableHost([worker]).ActivateAsync(0))!;
            var turn = new DurableTurn(new TurnInput(worker, new Slot { Row = prior, Loaded = true }, actor, [], 0, null, 0), foundation: true);
            turn.StartJob("reused", 37);
            var sealedTurn = turn.Seal(turn.Outcome(InstanceStatus.Running), prior with { Revision = 2 }, null, "[]");
            var issued = sealedTurn.Sent.Single();
            await Refuses(() => Commit(writer, lease, 1, Make.Turn(actor, sealedTurn.Row, 1, sent: [issued])), "no qualified creation identity");
            Check(issued.JobGeneration == 18 && sealedTurn.Row!.NextJobGeneration == 19,
                "a real Foundation job allocates 18 and 19; the qualified Foundation turn is refused");

            // Exercise the BSON fields with an unqualified carrier on the ordinary fenced path.
            var legacyTurn = new DurableTurn(new TurnInput(worker, new Slot { Row = row, Loaded = true }, actor, [], 0, null, 0));
            legacyTurn.StartJob("reused", 37);
            var legacyTicket = legacyTurn.Seal(legacyTurn.Outcome(InstanceStatus.Running), row, null, "[]").Sent.Single();
            var carrier = Make.M("physical-carrier", 0) with { Payload = issued.Payload, JobGeneration = issued.JobGeneration };
            Check((await Commit(writer, lease, 1, Make.Turn(actor, row, sent: [legacyTicket, carrier]))).Landed && await writer.AddAsync(ordinary),
                "the generation-zero ticket and both positive-generation ordinary carriers are persisted");
            var read = (await cold.PollAsync(0, lease.Epoch, TimeSpan.Zero, 20)).Messages.Select(m => m.Message).ToDictionary(m => m.Id);
            Check(read[legacyTicket.Id] is { Kind: Kinds.Job, JobGeneration: 0 } && read[carrier.Id].JobGeneration == 18 && read[carrier.Id].Payload == carrier.Payload &&
                  read[ordinary.Id].JobGeneration == 18 && (await cold.LoadAsync([actor])).Single().NextJobGeneration == 19,
                "a separate client recovers the exact ticket, checkpoint and admitted envelope");
            Check((await Commit(writer, lease, 2, Make.Turn(actor, row with { Revision = 2, NextJobGeneration = 20 }, 1))).Landed,
                "the current revision can advance its counter");
            foreach (var rewind in new long?[] { 19, null })
            {
                var rejected = Make.Turn(actor, row with { Revision = 3, NextJobGeneration = rewind }, 2, sent: [Make.M("must-roll-back", 0)]);
                await Refuses(() => Commit(writer, lease, 3, rejected), "cannot move backwards");
                Check(!(await Commit(writer, lease, 2, rejected)).Landed, "a stale fence takes precedence over the counter rejection");
            }
            Check(!(await Commit(writer, lease, 3, Make.Turn(actor, row with { Revision = 3, NextJobGeneration = 1 }, 1))).Landed,
                "a stale revision keeps its original refusal");
            Check((await cold.LoadAsync([actor])).Single() is { Revision: 2, NextJobGeneration: 20 } &&
                  (await cold.ProjectedAsync(0)).Written == 3 && (await cold.ReadJournalOfAsync(0, actor, 0)).Count == 2 &&
                  !(await cold.PollAsync(0, lease.Epoch, TimeSpan.Zero, 20)).Messages.Any(m => m.Message.Id == "must-roll-back"),
                "rewind and removal roll back checkpoint, sends, journal and position");
        }
        finally { await oldClient.DropDatabaseAsync(database); }

        async Task RejectOrdinaryIngress(MongoStore issuer, string storedVersion)
        {
            var clocks = old.Database.GetCollection<BsonDocument>("clocks");
            var id = new BsonDocument("_id", 0);
            var before = await clocks.Find(id).SingleAsync();
            var count = await messages.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
            var ordinary = Make.M("wrong-format", 0);
            var delivery = new EventRoute(new EventRow("signal", "one", "payload", 0),
                new SubscriptionRow("signal", actor, "wait")).Delivery();
            await Refuses(() => issuer.AddAsync(ordinary).AsTask(), storedVersion);
            await Refuses(() => issuer.AddEventAsync(delivery).AsTask(), storedVersion);
            Check(before == await clocks.Find(id).SingleAsync() && count == await messages.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty),
                $"both zero-generation admissions refuse {storedVersion} before changing the inbox clock or message count");
        }

        Task<Committed> Commit(MongoStore store, Lease lease, long position, TurnCommit turn) =>
            store.CommitAsync(new CommitBatch(0, lease.Epoch, position, duration, false, [turn], [], [])).AsTask();
    }

    private static async Task Refuses(Func<Task> operation, string reason)
    {
        try { await operation(); }
        catch (StoreException ex) when (ex.Failure == Failure.Definite && ex.Message.Contains(reason, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException($"Expected definite refusal: {reason}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"ok {message}");
    }
}
