using MongoDB.Bson;
using MongoDB.Driver;
using BoundScope = Kernel.Foundation.Contracts.BoundScope;

namespace Kernel.Mongo.Run;

internal static class JobResultChecks
{
    internal static async Task RunAsync(string connection, string name, MongoStoreOptions options, MongoStore v3, BoundScope scope,
        string reference)
    {
        using var client = MongoProvider.Client(connection, "settlement");
        var staged = options with { ScopedJobResults = true };
        var store = new MongoStore(client, name, staged);
        var lease = TimeSpan.FromMinutes(1);
        var nodes = store.Database.GetCollection<BsonDocument>("nodes");
        try
        {
            await store.UpgradeScopedJobResultsAsync();
            throw new InvalidOperationException("The format-3 owner remained eligible during migration.");
        }
        catch (StoreFormatException e) when (e.Message.Contains("Live node", StringComparison.Ordinal)) { }
        await nodes.UpdateManyAsync(FilterDefinition<BsonDocument>.Empty, Builders<BsonDocument>.Update.Set("leaseUntil", 0L));
        Check(await store.UpgradeScopedJobResultsAsync() && !await store.UpgradeScopedJobResultsAsync(),
            "the coordinated stop advances the result format once with original pending job roots");
        try
        {
            await v3.ReadJobRootCheckpointAsync(scope, reference);
            throw new InvalidOperationException("The ticket-only provider opened the new result representation.");
        }
        catch (StoreFormatException e) when (e.Failure == Failure.Definite) { }
        var owner = (await store.TakeAsync(0, "result-owner", lease))!;
        var fence = new PartitionFence(0, owner.Epoch, owner.Position);
        using var bootstrap = MongoProvider.Client(connection, "cold-job-bootstrap");
        var before = (await new MongoStore(bootstrap, name, staged).ReadJobRootCheckpointAsync(scope, reference))!;
        Check(before.Tickets.Count == 2 && !before.Suspended && before.Results.Count == 0,
            "the result reader recovers both original tickets after the coordinated upgrade");

        // Drive a real Worker with the checkpoint and pending ticket reloaded from Mongo.
        // This owner runs the job locally. Only the settlement uses the staged physical provider.
        var image = before.Image;
        var row = new InstanceRow(image.Actor, image.Model, image.Definition, InstanceStatus.Running, image.State ?? "null",
            image.Revision, null, Jobs: image.Jobs, Suspended: before.Suspended, StateVersion: image.StateVersion,
            Creation: image.Creation, StartIntent: image.StartIntent)
        {
            FoundationNullState = image.State is null, NextJobGeneration = image.NextJobGeneration,
            FoundationInlineHeader = new(image.TypeData, image.Labels)
        };
        var pending = before.Tickets.Single(ticket => ticket.JobGeneration == 1);
        var memory = new InMemoryStore(1, () => 1700000000000);
        memory.Instances.Add(row.Id, row);
        memory.Messages.Add(pending.InboxKey, new(pending, 100, 0));
        var actor = new ExecutingJob();
        await using var worker = new Worker("executing", 0, memory, actor, new WorkerOptions { Monotonic = () => 0 });
        await using var active = (await new DurableHost([worker]).ActivateAsync(0))!;
        await PumpUntil(active, actor.Entered.Task);
        actor.Return.TrySetResult();
        await PumpUntil(active, actor.Result.Task);
        var result = await actor.Result.Task;
        Check(actor.Observed is { State: "waiting", StateVersion: 1, Creation: 1, Revision: 1, NextJobGeneration: 2 } observed &&
            observed.Model == image.Model && observed.Definition == image.Definition && observed.Jobs == image.Jobs &&
            observed.StartIntent == image.StartIntent && observed.FoundationInlineHeader?.TypeData == "job-type" &&
            observed.FoundationInlineHeader.Labels["stage"] == "waiting",
            "the executing Worker sees the fresh-client checkpoint state, creation, registry, generation counter and header");
        Check(actor.Entries == 1 && result.HasIssuedTicket && result.Read<JobFinished>() == new JobFinished("second", "84", null),
            "a real worker executes the reloaded positive-generation job once and produces a kernel-issued result");

        Check(!(await store.SettleSuspendedJobAsync(scope, reference, row.Revision, result, fence, lease)).Landed,
            "a running deliverable root refuses a standalone conversion and keeps its ticket");
        // Physical prerequisite fixture: the authenticated operator writer is a separate provider slice.
        // Install its already-committed suspended checkpoint, next revision and journal position together.
        var projected = before.Image with { Revision = row.Revision + 1 };
        using (var tx = await client.StartSessionAsync())
        {
            tx.StartTransaction();
            var db = store.Database;
            var suspend = await db.GetCollection<BsonDocument>("instances").UpdateOneAsync(tx,
                new BsonDocument { { "_id", row.Id }, { "rootRef", reference }, { "revision", row.Revision } },
                Builders<BsonDocument>.Update.Set("suspended", true).Set("revision", projected.Revision).Set("rootImage", DurableKeyJson.Write(projected)));
            var moved = await db.GetCollection<BsonDocument>("partitions").UpdateOneAsync(tx,
                new BsonDocument { { "_id", fence.Partition }, { "epoch", fence.Epoch }, { "position", fence.Position } },
                Builders<BsonDocument>.Update.Inc("position", 1L));
            Check(suspend.ModifiedCount == 1 && moved.ModifiedCount == 1, "fixture establishes the suspended checkpoint at its exact revision and fence");
            await db.GetCollection<BsonDocument>("journal").InsertOneAsync(tx, new BsonDocument
            {
                { "_id", new BsonDocument { { "p", fence.Partition }, { "n", fence.Position + 1 } } }, { "partition", fence.Partition },
                { "position", fence.Position + 1 }, { "instance", row.Id }, { "revision", projected.Revision }, { "entries", "[]" },
                { "rootRef", reference }, { "tenant", scope.Tenant }
            });
            await tx.CommitTransactionAsync();
        }
        fence = fence with { Position = fence.Position + 1 };
        Check(!(await store.SettleSuspendedJobAsync(new BoundScope("b"), reference, projected.Revision, result, fence, lease)).Landed &&
            !(await store.SettleSuspendedJobAsync(scope, reference, row.Revision, result, fence, lease)).Landed &&
            !(await store.SettleSuspendedJobAsync(scope, reference, projected.Revision, result, fence with { Epoch = fence.Epoch - 1 }, lease)).Landed,
            "the wrong tenant, stale parent revision and stale owner each leave the pending job untouched");
        Message Earlier(long creation, long generation)
        {
            var slot = new Slot { Loaded = true, Row = row with { Creation = creation } };
            var turn = new DurableTurn(new TurnInput(worker, slot, row.Id, [], 0, null, 1), foundation: true);
            turn.ReportFoundationJobResult(result.Read<JobFinished>(), generation);
            return turn.Seal(turn.Outcome(InstanceStatus.Running), slot.Row, slot.Row.State, null).Sent.Single();
        }
        var staleGeneration = Earlier(row.Creation, 0);
        var staleCreation = Earlier(row.Creation + 1, 1);
        Check(staleGeneration.HasIssuedTicket && staleCreation.HasIssuedTicket &&
            !(await store.SettleSuspendedJobAsync(scope, reference, projected.Revision, staleGeneration, fence, lease)).Landed &&
            !(await store.SettleSuspendedJobAsync(scope, reference, projected.Revision, staleCreation, fence, lease)).Landed,
            "an issued completion for the same logical key in a different generation or creation cannot settle the current ticket");
        try
        {
            await store.SettleSuspendedJobAsync(scope, reference, projected.Revision, result with { }, fence, lease);
            throw new InvalidOperationException("A copied result retained mint authority.");
        }
        catch (StoreException ex) when (ex.Failure == Failure.Definite) { }

        var inbox = store.Database.GetCollection<BsonDocument>("messages");
        // An existing ordinary finished message and fixture index force a failure on the result insert,
        // after the counted ticket delete. The ordinary row has a different inbox identity.
        var ordinary = new Message("ordinary-finished", pending.Target, Kinds.JobFinished, result.Payload);
        await store.AddAsync(ordinary);
        var index = await inbox.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(new BsonDocument("kind", 1),
            new CreateIndexOptions<BsonDocument> { Name = "one_result", Unique = true, PartialFilterExpression = new BsonDocument("kind", Kinds.JobFinished) }));
        try
        {
            await store.SettleSuspendedJobAsync(scope, reference, projected.Revision, result, fence, lease);
            throw new InvalidOperationException("The fixture conflict did not roll back settlement.");
        }
        catch (StoreException ex) when (ex.Failure == Failure.Definite) { }
        finally { await inbox.Indexes.DropOneAsync(index); }
        var retry = (await store.ReadJobRootCheckpointAsync(scope, reference))!;
        Check(retry.Suspended && retry.Tickets.Count == 2 && retry.Results.Count == 0 && retry.Journal.Position == fence.Position,
            "the failed result insertion leaves both source tickets and the exact parent checkpoint durable");
        Check((await store.SettleSuspendedJobAsync(scope, reference, projected.Revision, result, fence, lease)).Landed &&
            !(await store.SettleSuspendedJobAsync(scope, reference, projected.Revision, result, fence, lease)).Landed,
            "settlement consumes the exact ticket once and a repeated callback cannot create a second result");

        using var reconnect = MongoProvider.Client(connection, "settlement-successor");
        var cold = new MongoStore(reconnect, name, staged);
        await nodes.UpdateOneAsync(new BsonDocument("_id", "result-owner"), Builders<BsonDocument>.Update.Set("leaseUntil", 0L));
        var successor = await cold.TakeAsync(0, "result-successor", lease);
        var after = (await cold.ReadJobRootCheckpointAsync(scope, reference))!;
        var restored = after.Results.Single();
        var delivered = new Delivery(restored, 1);
        Check(successor is not null && successor.Epoch > fence.Epoch && successor.Position == fence.Position &&
            after.Tickets is [{ JobGeneration: 0 }] && after.Image.Jobs == row.Jobs && after.Image.Revision == projected.Revision &&
            after.Journal == retry.Journal && after.Suspended && !delivered.DiscardedInput && !delivered.IsObsoleteJob(row) &&
            restored is { FoundationJobResult: true, FoundationHeldJobResult: false, JobGeneration: 1, SenderCreation: 1, TargetCreation: 1 } &&
            restored.InboxKey == InboxKey.JobResult(row.Id, "second", row.Creation, 1) && restored.Read<JobFinished>() == result.Read<JobFinished>(),
            "a fresh owner recovers an authentic result for the current generation and the untouched sibling and registry");
        Check(await cold.ReadJobRootCheckpointAsync(new BoundScope("b"), reference) is null &&
            (await cold.PollAsync(0, successor!.Epoch, TimeSpan.Zero, 10)).Messages is [var single] &&
            single.Message.Id == ordinary.Id && new Delivery(single.Message, single.Seq).SuppliedCompletion,
            "ordinary polling sees only the outside completion and the foreign scope cannot read the result");
    }

    private sealed class ExecutingJob : IActorType
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Return = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<Message> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Entries;
        internal InstanceRow? Observed;
        public bool Withholds(InstanceRow row, ITurnServices services) => false;
        public TurnOutcome? Command(TurnInput input) => null;
        public async ValueTask<object?> ExecuteJobAsync(string model, IJob job, CancellationToken token)
        {
            Entries++;
            Entered.TrySetResult();
            await Return.Task.WaitAsync(token);
            return Json.Read<int>(job.Arguments!) * 2;
        }
        public ValueTask<TurnOutcome> TurnAsync(TurnInput input)
        {
            Observed = input.Row;
            var completed = input.Taken.Single();
            if (!completed.IsWorkerJobCompletion || completed.Original?.Kind != Kinds.Job)
                throw new InvalidOperationException("Expected an entered worker job's completed callback.");
            var turn = new DurableTurn(input, foundation: true);
            turn.ReportFoundationJobResult(completed.Message.Read<JobFinished>(), completed.Message.JobGeneration);
            var outcome = turn.Seal(turn.Outcome(InstanceStatus.Running), input.Row!, input.State, null);
            Result.TrySetResult(outcome.Sent.Single());
            return ValueTask.FromResult(outcome);
        }
        public TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> inputs, StartSpec? spec, string error) =>
            throw new InvalidOperationException(error);
    }

    private static async Task PumpUntil(IScopeActivation active, Task condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition.IsCompleted)
        {
            deadline.Token.ThrowIfCancellationRequested();
            await active.WokenAsync(null);
            await active.RunAsync();
            await Task.Yield();
        }
        await condition;
    }

    private static void Check(bool pass, string title)
    {
        if (!pass) throw new InvalidOperationException(title);
        Console.WriteLine("ok " + title);
    }
}
