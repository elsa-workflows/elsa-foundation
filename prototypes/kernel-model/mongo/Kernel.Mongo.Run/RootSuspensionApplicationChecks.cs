using Contract = Kernel.Foundation.Contracts;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo.Run;

internal static class RootSuspensionApplicationChecks
{
    internal static async Task<PartitionFence> RunAsync(string connection, string name, MongoStoreOptions options, MongoStore store,
        Contract.BoundScope scope, string reference, MongoStore.JobRootCheckpoint before, PartitionFence fence, TimeSpan lease, Worker owner)
    {
        using var fresh = MongoProvider.Client(connection, "silent-checkpoint-reader");
        var cold = new MongoStore(fresh, name, options);
        var ingress = store.JobRootSuspensions((_, kind) => kind == Contract.Kinds.Suspend, lease);
        var accepted = await ingress.SendAsync(scope, new Contract.Target.Ref(new(reference)), "suspension", Contract.Kinds.Suspend);
        var command = (await cold.ReadDueRootSuspendAsync(scope, reference))!;
        var inbox = cold.Database.GetCollection<BsonDocument>("messages");
        var exact = new BsonDocument { { "tenant", scope.Tenant }, { "rootRef", reference }, { "operatorId", command.Id } };
        var admitted = await inbox.Find(exact).SingleAsync();
        var second = await ingress.SendAsync(scope, new Contract.Target.Ref(new(reference)), "second-suspension", Contract.Kinds.Suspend);
        var secondFilter = new BsonDocument(exact) { ["operatorId"] = "second-suspension" };
        var secondAdmitted = await inbox.Find(secondFilter).SingleAsync();
        var secondId = secondAdmitted["_id"].AsBsonDocument;
        var secondKey = new InboxKey(secondId["id"].AsString, secondId["scope"].AsInt64);
        var operations = new BsonArray(new[] { command.Id, "second-suspension" }.Select(id =>
            new BsonDocument("operatorOperation", FoundationIngressAddress.Operation(scope.Tenant, id))));
        var meta = cold.Database.GetCollection<BsonDocument>("meta");
        Task<List<BsonDocument>> Receipts() => meta.Find(new BsonDocument("_id", new BsonDocument("$in", operations)))
            .Sort(new BsonDocument("_id", 1)).ToListAsync();
        var admittedReceipts = await Receipts();
        var workflow = new Kernel.WorkflowActor.WorkflowActor([new NativeExecutionModel(new WorkflowRegistry())]);
        var deliveries = 0;
        ValueTask<FoundationHeaderChange> Receive(Contract.ActorHeader header, InstanceRow row, Message message, CancellationToken token)
        {
            if (message != command || header.Ref.Id != reference || header.Tenant != scope.Tenant || row.Id != before.Image.Actor ||
                workflow.Withholds(header) || row.Jobs != before.Image.Jobs || row.NextJobGeneration != before.Image.NextJobGeneration)
                throw new InvalidOperationException("The staged owner received an incorrect checkpoint or delivery.");
            deliveries++;
            return ActorAdapter.StageRootSuspensionAsync(workflow, owner, header, row, message, token);
        }
        var revision = before.Image.Revision;
        ValueTask<Committed> Apply(PartitionFence current, long expected) =>
            store.ApplyJobRootSuspensionAsync(scope, reference, command.InboxKey, expected, current, lease, Receive);
        var moved = owner.Fences.Single();
        var outOfOrder = await store.ApplyJobRootSuspensionAsync(scope, reference, secondKey, revision, moved, lease, Receive);
        Check(second.Outcome == Contract.AdmissionOutcome.Accepted && secondKey != command.InboxKey &&
            admitted["seq"].AsInt64 < secondAdmitted["seq"].AsInt64 && admittedReceipts.Count == 2 &&
            admittedReceipts.All(r => r["reference"].AsString == reference) && !outOfOrder.Landed && deliveries == 0 &&
            admitted.Equals(await inbox.Find(exact).SingleAsync()) && secondAdmitted.Equals(await inbox.Find(secondFilter).SingleAsync()) &&
            (await cold.ReadJobRootCheckpointAsync(scope, reference))!.Image.Revision == revision,
            "selecting the later of two admitted suspends refuses before actor dispatch or either physical write");
        Check(moved.Epoch > fence.Epoch, "the Mongo-backed owner has taken a newer epoch");
        Check(accepted.Outcome == Contract.AdmissionOutcome.Accepted && !(await Apply(fence, revision)).Landed &&
            (await cold.ReadDueRootSuspendAsync(scope, reference))?.InboxKey == command.InboxKey &&
            !(await cold.ReadJobRootCheckpointAsync(scope, reference))!.Suspended,
            "the former owner's staged suspension leaves the same occurrence and running checkpoint for its successor");
        fence = moved;

        // Reject the final done mark. The prior header update shares the transaction.
        var db = store.Database;
        await db.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "messages" },
            { "validator", new BsonDocument("$nor", new BsonArray { new BsonDocument { { "operatorId", "suspension" }, { "done", true } } }) } });
        try
        {
            await Apply(fence, revision);
            throw new InvalidOperationException("The failed done mark left a partial suspension.");
        }
        catch (StoreException e) when (e.Failure == Failure.Definite) { }
        finally { await db.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "messages" }, { "validator", new BsonDocument() } }); }
        Check((await cold.ReadJobRootCheckpointAsync(scope, reference)) is { Suspended: false } unchanged &&
            unchanged.Image.Revision == revision && unchanged.Image.TypeData == before.Image.TypeData &&
            unchanged.Image.Labels.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(before.Image.Labels.OrderBy(p => p.Key, StringComparer.Ordinal)) &&
            unchanged.Journal == before.Journal && await cold.ReadDueRootSuspendAsync(scope, reference) is not null,
            "a failure in the retained occurrence's done mark rolls back the header and revision");
        Check(!(await Apply(fence, revision + 1)).Landed && (await Apply(fence, revision)).Landed, "only the selected revision commits the public suspension");
        var previousDispatches = deliveries;
        Check(!(await Apply(fence, revision)).Landed && deliveries == previousDispatches && (await cold.ReadDueRootSuspendAsync(scope, reference))?.InboxKey == secondKey &&
            (await ingress.SendAsync(scope, new Contract.Target.Ref(new(reference)), "suspension", Contract.Kinds.Suspend)).Outcome == Contract.AdmissionOutcome.Duplicate,
            "replaying the exact applied occurrence makes no second delivery and admission keeps its original receipt");
        var retained = await inbox.Find(exact).SingleAsync();
        Check(!admitted["done"].AsBoolean && retained["done"].AsBoolean && retained["_id"] == admitted["_id"] &&
            retained["keepUntil"] == admitted["keepUntil"], "the original retained occurrence stays under its exact key and deadline with done set");
        Check(secondAdmitted.Equals(await inbox.Find(secondFilter).SingleAsync()) && admittedReceipts.SequenceEqual(await Receipts()),
            "applying the first suspend preserves the second physical occurrence and both original operation receipts");
        var after = (await cold.ReadJobRootCheckpointAsync(scope, reference))!;
        var h = after.Image;
        var header = new Contract.ActorHeader(new(reference), h.Model, scope.Tenant, Contract.ActorStatus.Running, null, null, null, h.TypeData, h.Labels);
        Check(after.Suspended && workflow.Withholds(header) && h.TypeData != before.Image.TypeData && h.Labels["definition"] == "job-definition" &&
            h.Revision == revision + 1 && h.State == before.Image.State && h.Jobs == before.Image.Jobs && h.NextJobGeneration == before.Image.NextJobGeneration &&
            after.Tickets.Select(m => (m.InboxKey, m.JobGeneration)).SequenceEqual(before.Tickets.Select(m => (m.InboxKey, m.JobGeneration))) &&
            after.Journal == before.Journal && (await cold.ProjectedAsync(0)).Written == fence.Position,
            "the actual WorkflowActor withholds on its cold header while both original tickets and the earlier journal survive the silent revision");
        return fence;
    }

    private static void Check(bool pass, string text)
    {
        if (!pass) throw new InvalidOperationException(text);
        Console.WriteLine("ok " + text);
    }
}
