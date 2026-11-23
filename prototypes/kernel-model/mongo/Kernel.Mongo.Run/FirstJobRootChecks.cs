using MongoDB.Bson;
using MongoDB.Driver;
using BoundScope = Kernel.Foundation.Contracts.BoundScope;

namespace Kernel.Mongo.Run;

internal static class FirstJobRootChecks
{
    internal static async Task RunAsync(string connection, MongoStoreOptions options)
    {
        var name = "kstartjob_" + Guid.NewGuid().ToString("N");
        var staged = options with { JobGenerations = true, ScopedJobRoots = true };
        using var client = MongoProvider.Client(connection, "job-checkpoint");
        using var restart = MongoProvider.Client(connection, "job-checkpoint-restart");
        var writer = new MongoStore(client, name, staged);
        var cold = new MongoStore(restart, name, staged);
        var scope = new BoundScope("a");
        var foreign = new BoundScope("b");
        var lease = TimeSpan.FromMinutes(1);
        try
        {
            await writer.CreateAsync(1);
            var spec = new StartSpec("job-model", "job-definition", "root-input");
            var receipt = await writer.AdmitJobRootAsync(scope, "op", spec, lease);
            var creator = (await cold.PendingJobRootAsync(scope, receipt.Reference))!;
            var id = creator.Target.Id;
            await using var worker = new Worker("compose", 0, new InMemoryStore(1, () => 1000), new WorkflowInstance([]));
            await using var activation = (await new DurableHost([worker]).ActivateAsync(0))!;
            var input = new TurnInput(worker, new Slot { Loaded = true }, id, [new Delivery(creator, 1)], 0, creator.Start, 1);
            var turn = new DurableTurn(input, foundation: true);
            turn.StartJob("initial", 41);
            turn.StartJob("second", 42);
            var row = new InstanceRow(id, spec.Model, spec.Definition, InstanceStatus.Running, "waiting", 1, null,
                Jobs: turn.Jobs(), Creation: 1, StartIntent: CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload));
            var outcome = turn.Seal(turn.Outcome(InstanceStatus.Running), row, "waiting", "[]");
            var commit = new TurnCommit(id, outcome.Row, outcome.Expected, outcome.Consumed.Select(d => d.Message.InboxKey).ToArray(),
                [], outcome.Sent, [], [], outcome.Journal) { FoundationHeader = new("job-type", new Dictionary<string, string> { ["stage"] = "waiting" }) };
            Check(outcome.Sent.Select(m => m.JobGeneration).SequenceEqual(new long[] { 0, 1 }) &&
                  outcome.Sent.All(m => m.HasIssuedTicket && m.SenderCreation == 1 && m.TargetCreation == 1),
                "the Foundation turn issued two authentic tickets in creation one with generations zero and one");
            var first = (await writer.TakeAsync(0, "owner", lease))!;
            var current = (await writer.RetakeAsync(0, "owner", first.Epoch, lease, false))!;
            var fence = new PartitionFence(0, current.Epoch, current.Position);
            Check(!(await writer.CommitFirstJobRootAsync(scope, receipt.Reference, creator, new(0, first.Epoch, first.Position), lease, commit)).Landed &&
                  !(await writer.CommitFirstJobRootAsync(foreign, receipt.Reference, creator, fence, lease, commit)).Landed,
                "stale ownership and a mismatched tenant leave the original creator ready");
            try
            {
                await writer.CommitFirstJobRootAsync(scope, receipt.Reference, creator, fence, lease, commit with { Unsubscribe = true });
                throw new InvalidOperationException("Unsupported effects were partially committed.");
            }
            catch (StoreException ex) when (ex.Failure == Failure.Definite && ex.Message.Contains("effects", StringComparison.Ordinal)) { }
            foreach (var invalidJournal in new[] { "{}", "truncated" })
            {
                try
                {
                    await writer.CommitFirstJobRootAsync(scope, receipt.Reference, creator, fence, lease, commit with { Journal = invalidJournal });
                    throw new InvalidOperationException("The invalid journal was accepted.");
                }
                catch (StoreException ex) when (ex.Failure == Failure.Definite && ex.Message.Contains("journal", StringComparison.Ordinal)) { }
            }
            Check(await cold.PendingJobRootAsync(scope, receipt.Reference) is not null && await cold.ReadJobRootCheckpointAsync(scope, receipt.Reference) is null &&
                  (await cold.ProjectedAsync(0)).Written == current.Position, "refusals preserve creator, checkpoint absence and journal position");

            var inbox = writer.Database.GetCollection<BsonDocument>("messages");
            var rootJobs = new BsonDocument { { "rootRef", receipt.Reference }, { "kind", Kinds.Job } };
            // Fail the second physical ticket insertion after the first ticket and checkpoint
            // have already entered the ordered transaction. Removing the fixture index permits a retry.
            var onlyOneJob = await inbox.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(new BsonDocument("kind", 1),
                new CreateIndexOptions<BsonDocument> { Name = "one_test_job", Unique = true, PartialFilterExpression = rootJobs }));
            try
            {
                await writer.CommitFirstJobRootAsync(scope, receipt.Reference, creator, fence, lease, commit);
                throw new InvalidOperationException("The injected second-ticket conflict should prevent the commit.");
            }
            catch (StoreException ex) when (ex.Failure == Failure.Definite) { }
            finally { await inbox.Indexes.DropOneAsync(onlyOneJob); }
            Check(await cold.PendingJobRootAsync(scope, receipt.Reference) is not null &&
                  await cold.ReadJobRootCheckpointAsync(scope, receipt.Reference) is null &&
                  await inbox.CountDocumentsAsync(rootJobs) == 0 && (await cold.ProjectedAsync(0)).Written == current.Position,
                "a physical failure on the second ticket rolls back the first ticket, creator consume, checkpoint and fence");
            await ProjectionAcrossBirthAsync(connection, name, staged, writer, id, async () =>
            {
                Check((await writer.CommitFirstJobRootAsync(scope, receipt.Reference, creator, fence, lease, commit)).Landed, "the first job-root checkpoint lands");
                var rows = await cold.ReadJournalAsync(0, 0, 10);
                Check(await cold.ProjectAsync(0, 0, rows.Single().Position, rows), "the trusted partition projector lands the scoped row");
            });
            var snapshot = (await cold.ReadJobRootCheckpointAsync(scope, receipt.Reference))!;
            Check(snapshot.Image is { Creation: 1, Revision: 1, State: "waiting", NextJobGeneration: 2, TypeData: "job-type" } &&
                  snapshot.Image.Labels["stage"] == "waiting" && snapshot.Image.Jobs == row.Jobs && snapshot.Image.StartIntent == row.StartIntent &&
                  snapshot.Journal is { Revision: 1, Entries: "[]" } && snapshot.Journal.Position == current.Position + 1 &&
                  snapshot.Tickets.Count == 2 && snapshot.Tickets.Zip(outcome.Sent).All(pair =>
                      pair.First.InboxKey == pair.Second.InboxKey && pair.First.Sender == id && pair.First.Target == creator.Target &&
                      pair.First.SenderCreation == 1 && pair.First.TargetCreation == 1 &&
                      pair.First.JobGeneration == pair.Second.JobGeneration && pair.First.Payload == pair.Second.Payload),
                "a fresh client recovers the scoped checkpoint, exact registry and both original issued ticket envelopes");
            Check((await cold.ReadViewsAsync([id])).Count == 0 &&
                  (await cold.ReadJournalOfAsync(0, id, 0)).Count == 0,
                "the trusted partition projector advances through scoped work without exposing it through ordinary actor reads");
            Check(await cold.PendingJobRootAsync(scope, receipt.Reference) is null &&
                  await cold.ReadJobRootCheckpointAsync(foreign, receipt.Reference) is null && (await cold.LoadAsync([id])).Count == 0 &&
                  (await cold.PollAsync(0, current.Epoch + 1, TimeSpan.Zero, 20)).Messages.Count == 0 &&
                  (await cold.ProjectedAsync(0)).Written == current.Position + 1,
                "the creator is consumed once; foreign and ordinary readers do not receive the scoped work");
            Check(!(await cold.CommitFirstJobRootAsync(scope, receipt.Reference, creator, fence with { Position = current.Position + 1 }, lease, commit)).Landed,
                "a replay cannot reuse the consumed creator or advance the journal");
            await writer.Database.GetCollection<BsonDocument>("nodes").UpdateOneAsync(new BsonDocument("_id", "owner"),
                Builders<BsonDocument>.Update.Set("leaseUntil", 0L));
            var successor = await cold.TakeAsync(0, "successor", lease);
            Check(successor is not null && successor.Epoch > current.Epoch &&
                  (await cold.ReadJobRootCheckpointAsync(scope, receipt.Reference))?.Tickets.Single(m => m.JobGeneration == 1).InboxKey == outcome.Sent[1].InboxKey,
                "a new owner finds the original generation-one job after takeover");
        }
        finally { await client.DropDatabaseAsync(name); }
    }

    private static async Task ProjectionAcrossBirthAsync(string connection, string name, MongoStoreOptions options, MongoStore writer, string id, Func<Task> commitAndProject)
    {
        const string ordinary = "ordinary-view";
        await writer.Database.GetCollection<BsonDocument>("views").InsertOneAsync(new BsonDocument
            { { "_id", ordinary }, { "partition", 0 }, { "revision", 4L }, { "commits", 2 } });
        var atViewRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readerWasReleased = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRead = new ManualResetEventSlim(false);
        using var reader = MongoProvider.Client(connection, "ordinary-views", command =>
        {
            if (command.CommandName == "find" && command.Command["find"] == "views")
            {
                atViewRead.TrySetResult();
                readerWasReleased.TrySetResult(releaseRead.Wait(TimeSpan.FromSeconds(30)));
            }
        });
        var store = new MongoStore(reader, name, options);
        var pending = Task.Run(async () => await store.ReadViewsAsync([ordinary, id]));
        try
        {
            await atViewRead.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await commitAndProject();
        }
        finally { releaseRead.Set(); }
        var result = await pending;
        Check(await readerWasReleased.Task && result is [{ Instance: ordinary, Revision: 4, Commits: 2 }],
            "an ordinary view reader spanning the scoped birth and projection returns only its original ordinary view");

        var requested = new List<string> { ordinary };
        using var changingCaller = MongoProvider.Client(connection, "changing-view-request", command =>
        {
            if (command.CommandName == "find" && command.Command["find"] == "instances")
                requested.Add(id);
        });
        var unchanged = await new MongoStore(changingCaller, name, options).ReadViewsAsync(requested);
        Check(requested.Contains(id) && unchanged is [{ Instance: ordinary, Revision: 4, Commits: 2 }],
            "an edit to the caller's collection during scope classification leaves the captured view request unchanged");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
