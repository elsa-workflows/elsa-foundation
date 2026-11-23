using Contract = Kernel.Foundation.Contracts;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo.Run;

internal static class RootSuspendChecks
{
    internal static async Task RunAsync(string connection, string name, MongoStoreOptions options, Contract.BoundScope scope, string reference, IReadOnlyList<Message> issued)
    {
        using var client = MongoProvider.Client(connection, "suspend-ingress");
        var resultOptions = options with { ScopedJobResults = true };
        var current = new MongoStore(client, name, resultOptions);
        var nextOptions = resultOptions with { ScopedJobSuspend = true };
        var next = new MongoStore(client, name, nextOptions);
        var nodes = next.Database.GetCollection<BsonDocument>("nodes");
        var window = TimeSpan.FromMinutes(1);
        await nodes.UpdateManyAsync(FilterDefinition<BsonDocument>.Empty, Builders<BsonDocument>.Update.Set("leaseUntil", 0L));
        await current.UpgradeScopedJobResultsAsync();
        await current.TakeAsync(0, "result-owner", window);
        try
        {
            await next.UpgradeScopedJobSuspendAsync();
            throw new InvalidOperationException("The active result-only owner was admitted to the new protocol.");
        }
        catch (StoreFormatException e) when (e.Message.Contains("Live node", StringComparison.Ordinal)) { }
        await nodes.UpdateManyAsync(FilterDefinition<BsonDocument>.Empty, Builders<BsonDocument>.Update.Set("leaseUntil", 0L));
        Check(await next.UpgradeScopedJobSuspendAsync() && !await next.UpgradeScopedJobSuspendAsync(),
            "the operator message protocol advances once after the active result-only owner stops");
        try
        {
            await current.TakeAsync(0, "old-owner", window);
            throw new InvalidOperationException("The result-only owner wrote to the operator protocol.");
        }
        catch (StoreFormatException e) when (e.Failure == Failure.Definite) { }
        var owner = (await next.TakeAsync(0, "operator-owner", window))!;
        var target = new Contract.Target.Ref(new(reference));
        var other = new Contract.BoundScope("b");
        var otherBirth = await next.AdmitJobRootAsync(other, "secondary", new("job-model", "type"), window);
        var second = await next.AdmitJobRootAsync(scope, "other-life", new("job-model", "type"), window);
        var secondTarget = new Contract.Target.Ref(new(second.Reference));
        var foreignTarget = new Contract.Target.Ref(new(otherBirth.Reference));
        var before = (await next.ReadJobRootCheckpointAsync(scope, reference))!;
        var denied = next.JobRootSuspensions((_, _) => false, window);
        var allowed = next.JobRootSuspensions((_, kind) => kind == Contract.Kinds.Suspend, window);
        Check((await denied.SendAsync(scope, target, "no-permission", Contract.Kinds.Suspend)).Outcome == Contract.AdmissionOutcome.Refused &&
            (await allowed.SendAsync(other, target, "other-tenant", Contract.Kinds.Suspend)).Outcome == Contract.AdmissionOutcome.Refused &&
            (await allowed.SendAsync(scope, target, "other-kind", Contract.Kinds.Resume)).Outcome == Contract.AdmissionOutcome.Refused &&
            (await allowed.SendAsync(scope, target, "unsupported", Contract.Kinds.Suspend, "payload")).Outcome == Contract.AdmissionOutcome.Refused &&
            await next.ReadDueRootSuspendAsync(scope, reference) is null, "permissions, exact tenant and supported shape are checked before insertion");
        var db = next.Database;
        var clocks = db.GetCollection<BsonDocument>("clocks");
        var clockId = new BsonDocument("_id", 0);
        var clockBefore = (await clocks.Find(clockId).SingleAsync())["clock"];
        await db.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "messages" },
            { "validator", new BsonDocument("operatorId", new BsonDocument("$ne", "rollback")) } });
        try
        {
            await allowed.SendAsync(scope, target, "rollback", Contract.Kinds.Suspend);
            throw new InvalidOperationException("Expected the physical inbox constraint to refuse the command.");
        }
        catch (MongoWriteException e) when (e.WriteError.Code == 121) { }
        finally
        {
            await db.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "messages" }, { "validator", new BsonDocument() } });
        }
        var receipt = new BsonDocument("_id", new BsonDocument("operatorOperation", FoundationIngressAddress.Operation(scope.Tenant, "rollback")));
        Check(!await db.GetCollection<BsonDocument>("meta").Find(receipt).AnyAsync() &&
            (await clocks.Find(clockId).SingleAsync())["clock"] == clockBefore && await next.ReadDueRootSuspendAsync(scope, reference) is null,
            "a physical inbox insertion failure rolls back the operation receipt and outside clock together");
        using var retryClient = MongoProvider.Client(connection, "suspend-retry");
        var otherStore = new MongoStore(retryClient, name, nextOptions);
        var retry = otherStore.JobRootSuspensions((_, _) => true, window);
        var race = await Task.WhenAll(allowed.SendAsync(scope, target, "operation", Contract.Kinds.Suspend).AsTask(),
            retry.SendAsync(scope, target, "operation", Contract.Kinds.Suspend).AsTask());
        Check(race.All(a => a.Actor == target.Actor) && race.Count(a => a.Outcome == Contract.AdmissionOutcome.Accepted) == 1 &&
            race.Count(a => a.Outcome == Contract.AdmissionOutcome.Duplicate) == 1,
            "two clients racing one operation pin one accepted occurrence to the original actor reference");
        var pinned = await retry.SendAsync(scope, secondTarget, "operation", Contract.Kinds.Suspend);
        Check(pinned.Outcome == Contract.AdmissionOutcome.Duplicate && pinned.Actor == target.Actor &&
            await otherStore.ReadDueRootSuspendAsync(scope, second.Reference) is null, "a retry at a second reference still returns the first target");
        var otherAccepted = await retry.SendAsync(other, foreignTarget, "operation", Contract.Kinds.Suspend);
        Check(otherAccepted.Outcome == Contract.AdmissionOutcome.Accepted && otherAccepted.Actor == foreignTarget.Actor &&
            await otherStore.ReadDueRootSuspendAsync(other, reference) is null, "each tenant owns its operation identity");
        var due = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        var delayed = await retry.SendAsync(scope, secondTarget, "reuse", Contract.Kinds.Suspend, options: new(due, TimeSpan.Zero));
        Check(delayed.Outcome == Contract.AdmissionOutcome.Accepted && await otherStore.ReadDueRootSuspendAsync(scope, second.Reference) is null,
            "a future occurrence can be admitted while its root creator is still pending and stays unavailable until due");
        await Task.Delay(20);
        await next.SweepAsync();
        var reused = await retry.SendAsync(scope, secondTarget, "reuse", Contract.Kinds.Suspend);
        var later = await next.ReadDueRootSuspendAsync(scope, second.Reference);
        var inbox = next.Database.GetCollection<BsonDocument>("messages");
        Check(reused.Outcome == Contract.AdmissionOutcome.Accepted && later?.Id == "reuse" &&
            await inbox.CountDocumentsAsync(new BsonDocument { { "rootRef", second.Reference }, { "operatorId", "reuse" } }) == 2,
            "receipt expiry permits a new due occurrence and preserves the still-pending delayed one");
        using var fresh = MongoProvider.Client(connection, "suspend-owner-cold");
        var cold = new MongoStore(fresh, name, nextOptions);
        var command = await cold.ReadDueRootSuspendAsync(scope, reference);
        var after = (await cold.ReadJobRootCheckpointAsync(scope, reference))!;
        Check(command is { Id: "operation", Kind: Kinds.Suspend, Due: null, Sender: null, TargetCreation: 1, Operator.Selection: null } &&
            command.Operator is not null && command.InboxKey.Scope == InboxKey.FoundationOccurrenceScope &&
            command.Target.Id == before.Image.Actor && command.StorageId != command.Id && before.Image.Revision == after.Image.Revision &&
            !after.Suspended && after.Image.Jobs == before.Image.Jobs && after.Image.NextJobGeneration == 2 && after.Journal == before.Journal &&
            (await cold.PollAsync(0, owner.Epoch, TimeSpan.Zero, 20)).Messages.Count == 0,
            "a fresh owner client recovers the exact admitted command; admission preserves the running checkpoint, job registry and journal");
        var expectedTickets = issued.Select(m => (m.InboxKey, m.JobGeneration)).ToArray();
        Check(before.Tickets.Select(m => (m.InboxKey, m.JobGeneration)).SequenceEqual(expectedTickets) &&
            after.Tickets.Select(m => (m.InboxKey, m.JobGeneration)).SequenceEqual(expectedTickets),
            "both physical checkpoints contain the originally issued ticket keys and generations");

        var zeroRoot = await next.AdmitJobRootAsync(scope, "due-zero-root", new("job-model", "type"), window);
        var zero = await allowed.SendAsync(scope, new Contract.Target.Ref(new(zeroRoot.Reference)), "due-zero", Contract.Kinds.Suspend, options: new(DueAt: 0));
        Check(zero.Outcome == Contract.AdmissionOutcome.Accepted && await cold.ReadDueRootSuspendAsync(scope, zeroRoot.Reference) is { Id: "due-zero", Due: 0 },
            "a fresh owner preserves an explicit zero due time");

        var futureRoot = await next.AdmitJobRootAsync(scope, "due-future-root", new("job-model", "type"), window);
        var server = await db.RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1));
        var futureDue = server["localTime"].AsBsonDateTime.MillisecondsSinceEpoch + 2500;
        var future = await allowed.SendAsync(scope, new Contract.Target.Ref(new(futureRoot.Reference)), "due-future", Contract.Kinds.Suspend, options: new(DueAt: futureDue));
        Check(future.Outcome == Contract.AdmissionOutcome.Accepted && await cold.ReadDueRootSuspendAsync(scope, futureRoot.Reference) is null,
            "the owner withholds a future command against server time");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Message? ready;
        while ((ready = await cold.ReadDueRootSuspendAsync(scope, futureRoot.Reference, timeout.Token)) is null)
            await Task.Delay(50, timeout.Token);
        Check(ready.Id == "due-future" && ready.Due == futureDue, "a fresh owner returns the original future due time when eligible");

        var exact = new BsonDocument { { "tenant", scope.Tenant }, { "rootRef", zeroRoot.Reference }, { "operatorId", "due-zero" } };
        foreach (var field in new[] { "payload", "sender", "replyTo", "start" })
        {
            var changed = await inbox.UpdateOneAsync(exact, Builders<BsonDocument>.Update.Set(field, "unexpected"));
            if (changed.ModifiedCount != 1) throw new InvalidOperationException("The stored envelope fixture did not change.");
            try
            {
                await cold.ReadDueRootSuspendAsync(scope, zeroRoot.Reference);
                throw new InvalidOperationException("Expected rejection of the inconsistent stored suspension field " + field);
            }
            catch (StoreException e) when (e.Failure == Failure.Definite) { }
            finally { await inbox.UpdateOneAsync(exact, Builders<BsonDocument>.Update.Set(field, BsonNull.Value)); }
        }
        Check(await cold.ReadDueRootSuspendAsync(scope, zeroRoot.Reference) is { Id: "due-zero", Due: 0, Operator: not null },
            "the cold owner refuses each inconsistent envelope field and reads the restored one-way command");
    }

    private static void Check(bool pass, string title)
    {
        if (!pass) throw new InvalidOperationException(title);
        Console.WriteLine("ok " + title);
    }
}
