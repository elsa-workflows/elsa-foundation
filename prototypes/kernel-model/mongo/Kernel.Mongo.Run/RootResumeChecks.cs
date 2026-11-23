using Contract = Kernel.Foundation.Contracts;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo.Run;

internal static class RootResumeChecks
{
    internal static async Task RunAsync(string connection, string name, MongoStoreOptions priorOptions, MongoStore prior,
        Contract.BoundScope scope, string reference, MongoStore.JobRootCheckpoint held, TimeSpan lease)
    {
        using var client = MongoProvider.Client(connection, "resume-owner");
        var options = priorOptions with { ScopedJobResume = true };
        var store = new MongoStore(client, name, options);
        var target = new Contract.Target.Ref(new(reference));
        Check((await prior.JobRootControls((_, _) => true, lease).SendAsync(scope, target, "resume", Contract.Kinds.Resume)).Outcome == Contract.AdmissionOutcome.Refused,
            "the previous protocol refuses resume before creating a receipt");
        try
        {
            await store.UpgradeRootResumeAsync();
            throw new InvalidOperationException("The prior live owner cannot dispatch resume.");
        }
        catch (StoreFormatException e) when (e.Message.Contains("Live node", StringComparison.Ordinal)) { }
        await store.Database.GetCollection<BsonDocument>("nodes").UpdateOneAsync(new BsonDocument("_id", "result-successor"),
            Builders<BsonDocument>.Update.Set("leaseUntil", 0L));
        try
        {
            await store.UpgradeRootResumeAsync();
            throw new InvalidOperationException("A mixed legacy prefix was assigned an invented order.");
        }
        catch (StoreFormatException e) when (e.Message.Contains("pending legacy", StringComparison.Ordinal)) { }
        var actor = new Kernel.WorkflowActor.WorkflowActor([new NativeExecutionModel(new WorkflowRegistry())]);
        await using (var legacy = new Worker("legacy-prefix", 0, prior, new WorkflowInstance([])))
        {
            await using var legacyActive = await new DurableHost([legacy]).ActivateAsync(0) ?? throw new InvalidOperationException("Legacy owner did not take its partition.");
            var pending = (await prior.ReadDueRootControlAsync(scope, reference))!;
            var oldSnapshot = (await prior.ReadJobRootCheckpointAsync(scope, reference))!;
            Check(oldSnapshot.Image.Revision == held.Image.Revision && oldSnapshot.Results.Single().InboxKey == held.Results.Single().InboxKey &&
                pending is { Id: "second-suspension", Kind: Kinds.Suspend }, "the mixed-prefix refusal preserves the earlier marker and both pending input families");
            Check((await prior.ApplyJobRootControlAsync(scope, reference, pending.InboxKey, held.Image.Revision, legacy.Fences.Single(), lease,
                (header,row,control,token) => ActorAdapter.StageRootControlAsync(actor, legacy, header, row, control, token))).Landed,
                "the previous owner drains the pending legacy suspension with the real actor");
        }
        await store.Database.GetCollection<BsonDocument>("nodes").UpdateOneAsync(new BsonDocument("_id", "legacy-prefix"),
            Builders<BsonDocument>.Update.Set("leaseUntil", 0L));
        Check(await store.UpgradeRootResumeAsync() && !await store.UpgradeRootResumeAsync(), "only the coordinated start enables the resume protocol");
        try
        {
            await prior.ReadDueRootControlAsync(scope, reference);
            throw new InvalidOperationException("The suspension-only owner read the new operator protocol.");
        }
        catch (StoreFormatException e) when (e.Failure == Failure.Definite) { }
        var ingress = store.JobRootControls((_, kind) => kind is Contract.Kinds.Resume or Contract.Kinds.Suspend, lease);
        var accepted = await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Resume);
        Check(accepted.Outcome == Contract.AdmissionOutcome.Accepted && accepted.Actor == target.Actor &&
            (await ingress.SendAsync(new Contract.BoundScope("b"), target, "resume", Contract.Kinds.Resume)).Outcome == Contract.AdmissionOutcome.Refused,
            "the resume is tenant-bound and pinned to the intended root");
        await using var worker = new Worker("resume-owner", 0, store, new WorkflowInstance([]));
        await using var active = await new DurableHost([worker]).ActivateAsync(0) ?? throw new InvalidOperationException("Resume owner did not take its partition.");
        var fence = worker.Fences.Single();
        ValueTask<FoundationHeaderChange> Receive(Contract.ActorHeader header, InstanceRow row, Message control, CancellationToken token) =>
            ActorAdapter.StageRootControlAsync(actor, worker, header, row, control, token);
        var revision = held.Image.Revision + 1;
        var selected = (await store.ReadDueRootControlAsync(scope, reference, withholds: actor.Withholds))!;
        Check(selected is { Id: "resume", Kind: Kinds.Resume, TargetCreation: 1, Operator: not null }, "the cold decoder restores the exact resume kind and creation");
        ValueTask<Committed> Apply(PartitionFence at, long expected) =>
            store.ApplyJobRootControlAsync(scope, reference, selected.InboxKey, expected, at, lease, Receive, actor.Withholds);
        var db = store.Database;
        var inbox = db.GetCollection<BsonDocument>("messages");
        var resultFilter = new BsonDocument { { "rootRef", reference }, { "tenant", scope.Tenant }, { "kind", Kinds.JobFinished } };
        var durableResult = await inbox.Find(resultFilter).SingleAsync();
        await db.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "messages" },
            { "validator", new BsonDocument("$nor", new BsonArray { new BsonDocument { { "operatorId", "resume" }, { "done", true } } }) } });
        try
        {
            await Apply(fence, revision);
            throw new InvalidOperationException("A failed done mark committed a resume.");
        }
        catch (StoreException e) when (e.Failure == Failure.Definite) { }
        finally { await db.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "messages" }, { "validator", new BsonDocument() } }); }
        using var restart = MongoProvider.Client(connection, "cold-after-resume");
        var cold = new MongoStore(restart, name, options);
        var unchanged = (await cold.ReadJobRootCheckpointAsync(scope, reference))!;
        Check(unchanged.Suspended && unchanged.Image.Revision == revision && unchanged.Image.TypeData == held.Image.TypeData &&
            unchanged.Journal == held.Journal && unchanged.Results.Single().InboxKey == held.Results.Single().InboxKey &&
            (await cold.ReadDueRootControlAsync(scope, reference, withholds: actor.Withholds))?.InboxKey == selected.InboxKey,
            "the failed resume leaves the same suspended header, exact result and pending command");
        Check(!(await Apply(fence with { Epoch = fence.Epoch - 1 }, revision)).Landed && !(await Apply(fence, revision - 1)).Landed &&
            (await Apply(fence, revision)).Landed && !(await Apply(fence, revision)).Landed, "the resume lands once at its exact epoch and revision");
        var resumed = (await cold.ReadJobRootCheckpointAsync(scope, reference))!;
        var h = resumed.Image;
        var header = new Contract.ActorHeader(target.Actor, h.Model, scope.Tenant, Contract.ActorStatus.Running, null, null, null, h.TypeData, h.Labels);
        Check(!resumed.Suspended && !actor.Withholds(header) && new Kernel.WorkflowActor.WorkflowActor([]).Withholds(header) &&
            h.Revision == revision + 1 && h.TypeData != unchanged.Image.TypeData && h.Jobs == held.Image.Jobs && h.NextJobGeneration == held.Image.NextJobGeneration &&
            resumed.Journal == held.Journal && resumed.Tickets.Single().InboxKey == held.Tickets.Single().InboxKey &&
            resumed.Results.Single().InboxKey == held.Results.Single().InboxKey && resumed.Results[0].JobGeneration == 1 && resumed.Results[0].FoundationJobResult &&
            durableResult.Equals(await inbox.Find(resultFilter).SingleAsync()) && (await cold.ProjectedAsync(0)).Written == fence.Position,
            "the supported model is eligible after a cold resume with its unchanged sibling, authenticated generation-one result and journal");
        var later = await ingress.SendAsync(scope, target, "later-suspend", Contract.Kinds.Suspend);
        Check(later.Outcome == Contract.AdmissionOutcome.Accepted && await cold.ReadDueRootControlAsync(scope, reference, withholds: actor.Withholds) is null,
            "a newly admitted suspension cannot pass the older deliverable job result on a running root");

        var oldActor = new Kernel.WorkflowActor.WorkflowActor([]);
        var olderControl = (await cold.ReadDueRootControlAsync(scope, reference, withholds: oldActor.Withholds))!;
        Check(olderControl is { Id: "later-suspend", Kind: Kinds.Suspend } && oldActor.Withholds(header),
            "an owner missing the resumed workflow model can select the later control while the result is withheld");
        var becameAvailable = false;
        bool OwnerWithholds(Contract.ActorHeader current) => (becameAvailable ? actor : oldActor).Withholds(current);
        async ValueTask<FoundationHeaderChange> OnDispatch(Contract.ActorHeader current, InstanceRow row, Message control, CancellationToken token)
        {
            var change = await ActorAdapter.StageRootControlAsync(oldActor, worker, current, row, control, token);
            becameAvailable = true;
            return change;
        }
        Check(!(await store.ApplyJobRootControlAsync(scope, reference, olderControl.InboxKey, h.Revision, fence, lease, OnDispatch, OwnerWithholds)).Landed &&
            becameAvailable && (await cold.ReadJobRootCheckpointAsync(scope, reference)) is { Suspended: false } retried &&
            retried.Image.Revision == h.Revision && retried.Image.TypeData == h.TypeData && durableResult.Equals(await inbox.Find(resultFilter).SingleAsync()),
            "the fenced recheck refuses a control whose earlier result became deliverable during dispatch");
        ValueTask<FoundationHeaderChange> OldReceive(Contract.ActorHeader current, InstanceRow row, Message control, CancellationToken token) =>
            ActorAdapter.StageRootControlAsync(oldActor, worker, current, row, control, token);
        Check((await store.ApplyJobRootControlAsync(scope, reference, olderControl.InboxKey, h.Revision, fence, lease, OldReceive, oldActor.Withholds)).Landed &&
            (await cold.ReadJobRootCheckpointAsync(scope, reference)) is { Suspended: true } final &&
            final.Image.Revision == h.Revision + 1 && final.Journal == resumed.Journal && durableResult.Equals(await inbox.Find(resultFilter).SingleAsync()),
            "the withholding owner applies the control through its public actor and leaves the generation-one result intact");
    }

    private static void Check(bool pass, string title)
    {
        if (!pass) throw new InvalidOperationException(title);
        Console.WriteLine("ok " + title);
    }
}
