using System.Collections.Concurrent;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo;

public sealed record MongoStoreOptions
{
    /// <summary>MUTATION, for the conformance suite only: the fence loses its epoch and position conditions.</summary>
    public bool BrokenFence { get; init; }

    /// <summary>
    /// The fence rides in the same bulk write as everything else, and the order keys are assigned from the partition's
    /// clock as this store last saw it at this epoch: two round trips instead of three. Falls back to the checked
    /// fence when this store instance did not take the partition at this epoch itself.
    /// </summary>
    public bool FenceInBatch { get; init; }

    // Release default stays at format 1. Only the staged provider qualification selects format 2.
    internal bool JobGenerations { get; init; }

}

/// <summary>
/// The kernel's storage contract on MongoDB. Needs a replica set (multi-document transactions) and MongoDB 8.0 (the
/// client-level bulk write, which sends writes to several collections in one round trip).
/// <para>
/// A commit is one transaction: the fence (a conditional update of the partition document, checked), one ordered bulk
/// write with everything else, then commitTransaction. The partition document also carries the partition's clock,
/// from which the order keys of every message the commit sends are cut, so a later send from the same sender always
/// gets a larger key whoever owns the partition and whatever its clock says.
/// </para>
/// </summary>
public sealed partial class MongoStore : IKernelStore
{
    public Func<long>? FoundationClock => null;

    private static readonly TransactionOptions Transaction = new(ReadConcern.Snapshot, ReadPreference.Primary, WriteConcern.WMajority);
    private static readonly BsonDocument NowMs = new("$toLong", "$$NOW");
    private static readonly BsonDocument NowUs = new("$multiply", new BsonArray { NowMs, 1000L });

    private readonly IMongoClient _client;
    private readonly MongoStoreOptions _options;
    private static readonly string[] DataCollections =
        ["partitions", "clocks", "instances", "messages", "subscriptions", "events", "journal", "nodes", "projections", "views"];
    private readonly IMongoCollection<BsonDocument> _meta, _partitions, _clocks, _instances, _messages, _subscriptions, _events, _journal, _nodes, _projections, _views;
    private readonly ConcurrentDictionary<(int Partition, long Epoch), long> _known = new();     // FenceInBatch: the clock at (partition, epoch)

    public MongoStore(IMongoClient client, string database, MongoStoreOptions? options = null)
    {
        _client = client;
        _options = options ?? new MongoStoreOptions();
        _format = _options.JobGenerations ? JobGenerationFormat : CurrentFormat;
        _grant = new BsonDocument
        {
            { "storeSchema", _format.StoreSchema }, { "messageProtocol", _format.MessageProtocol }, { "journalCodec", _format.JournalCodec }
        };
        // Reads use majority by default. The take reads leases from the primary, where a rolled-back renewal can only delay a take.
        var db = client.GetDatabase(database, new MongoDatabaseSettings { ReadConcern = ReadConcern.Majority, WriteConcern = WriteConcern.WMajority, ReadPreference = ReadPreference.Primary });
        Database = db;
        _meta = db.GetCollection<BsonDocument>("meta");
        _partitions = db.GetCollection<BsonDocument>("partitions");
        _clocks = db.GetCollection<BsonDocument>("clocks");
        _instances = db.GetCollection<BsonDocument>("instances");
        _messages = db.GetCollection<BsonDocument>("messages");
        _subscriptions = db.GetCollection<BsonDocument>("subscriptions");
        _events = db.GetCollection<BsonDocument>("events");
        _journal = db.GetCollection<BsonDocument>("journal");
        _nodes = db.GetCollection<BsonDocument>("nodes");
        _projections = db.GetCollection<BsonDocument>("projections");
        _views = db.GetCollection<BsonDocument>("views");
    }

    public bool PhysicalRetirementAvailable => true;
    public IMongoDatabase Database { get; }

    /// <summary>
    /// The count the first open recorded, once every partition document of it exists; an open cut short between the record
    /// and the last document refuses (definite) until <see cref="CreateAsync"/> runs again at that count. Opening
    /// a counted host requires a readable marker even if store initialization was run by another host.
    /// </summary>
    public async ValueTask<int?> PartitionCountAsync(CancellationToken cancellationToken = default)
    {
        var count = await RequireVersionsAsync(cancellationToken);
        var rows = (int)await _partitions.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: cancellationToken);
        return rows == count ? count
            : throw new StoreException(Failure.Definite, $"Initialization incomplete: {rows} of {count} partition documents exist. Re-run CreateAsync at the same count.");
    }

    /// <summary>Collections, indexes and the partition documents. Collections are created here, not inside a transaction.</summary>
    public async Task CreateAsync(int partitions = Defaults.ServerPartitions)
    {
        var existing = await (await Database.ListCollectionNamesAsync()).ToListAsync();
        await OpenVersionsAsync(existing, partitions);
        foreach (var name in DataCollections)
            if (!existing.Contains(name)) await Database.CreateCollectionAsync(name);
        await _messages.Indexes.CreateManyAsync([
            new CreateIndexModel<BsonDocument>(new BsonDocument { { "partition", 1 }, { "done", 1 }, { "seq", 1 }, { "_id", 1 } }, new CreateIndexOptions { Name = "poll" }),
            new CreateIndexModel<BsonDocument>(new BsonDocument { { "partition", 1 }, { "trial", 1 } }, new CreateIndexOptions { Name = "trial" }),
            new CreateIndexModel<BsonDocument>(new BsonDocument { { "done", 1 }, { "keepUntil", 1 } }, new CreateIndexOptions { Name = "sweep" }),
        ]);
        await _subscriptions.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(new BsonDocument("key", 1)));
        // Merge with round 3: a terminate deletes every wait of its instance, a delete its instance's journal documents.
        await _subscriptions.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(new BsonDocument("instance", 1)));
        await _journal.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(new BsonDocument { { "partition", 1 }, { "instance", 1 } }));
        await _events.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(new BsonDocument("key", 1)));
        await _journal.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(new BsonDocument { { "partition", 1 }, { "position", 1 } }, new CreateIndexOptions { Unique = true }));
        for (var p = 0; p < partitions; p++)
        {
            await _partitions.UpdateOneAsync(new BsonDocument("_id", p), new BsonDocument("$setOnInsert", new BsonDocument
            {
                { "owner", BsonNull.Value }, { "epoch", 0L }, { "position", 0L }, { "clock", 0L }, { "now", 0L }, { "died", false },
            }), new UpdateOptions { IsUpsert = true });
            await _clocks.UpdateOneAsync(new BsonDocument("_id", p), new BsonDocument("$setOnInsert", new BsonDocument("clock", 0L)), new UpdateOptions { IsUpsert = true });
            await _projections.UpdateOneAsync(new BsonDocument("_id", p), new BsonDocument("$setOnInsert", new BsonDocument("position", 0L)), new UpdateOptions { IsUpsert = true });
        }
    }

    // ---- ownership ---------------------------------------------------------------------------------------------------

    /// <summary>Remember the commit clock read by this take for its partition and epoch.</summary>
    private void Known(int partition, long epoch, long clock)
    {
        if (!_options.FenceInBatch) return;
        foreach (var key in _known.Keys.Where(k => k.Partition == partition && k.Epoch < epoch)) _known.TryRemove(key, out _);
        _known[(partition, epoch)] = clock;
    }

    public async ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default) =>
        await NodeTakeAsync([partition], node, lease, cancellationToken) is { Taken: [var one] } set ? new Lease(one.Epoch, one.Position, set.Now) : null;

    public async ValueTask<OwnedLease> TakeAsync(IReadOnlyCollection<int> partitions, string node, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        var set = partitions.Distinct().Order().ToList();
        return set.Count == 0 ? new([], 0) : await NodeTakeAsync(set, node, lease, cancellationToken);
    }

    public async ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default)
    {
        _known.TryRemove((partition, epoch), out _);
        await _partitions.UpdateOneAsync(new BsonDocument { { "_id", partition }, { "epoch", epoch } }, new BsonDocument("$set", new BsonDocument("owner", BsonNull.Value)), cancellationToken: cancellationToken);
    }

    /// <summary>A release of a set, in one statement: each document still at its epoch is freed, and the rest are not written.</summary>
    public async ValueTask ReleaseAsync(IReadOnlyCollection<PartitionFence> owned, CancellationToken cancellationToken = default)
    {
        if (owned.Count <= 1)
        {
            foreach (var fence in owned) await ReleaseAsync(fence.Partition, fence.Epoch, cancellationToken);
            return;
        }
        foreach (var fence in owned) _known.TryRemove((fence.Partition, fence.Epoch), out _);
        var held = owned.OrderBy(f => f.Partition).Select(f => new BsonDocument { { "_id", f.Partition }, { "epoch", f.Epoch } });
        await _partitions.UpdateManyAsync(new BsonDocument("$or", new BsonArray(held)), new BsonDocument("$set", new BsonDocument("owner", BsonNull.Value)), cancellationToken: cancellationToken);
    }

    public ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, CancellationToken cancellationToken = default) =>
        new(RetakeInTransactionAsync(partition, node, epoch, lease, countDeaths: false, cancellationToken));

    // ---- the commit -----------------------------------------------------------------------------------------------------

    private BsonDocument Fence(PartitionFence fence) => _options.BrokenFence
        ? new BsonDocument("_id", fence.Partition)
        : new BsonDocument { { "_id", fence.Partition }, { "epoch", fence.Epoch }, { "position", fence.Position } };

    /// <summary>
    /// A commit over a set of virtual partitions fences every one of their documents in the one transaction, in ascending
    /// partition id; one fence that matches nothing aborts it, so nothing of the batch lands.
    /// </summary>
    public bool OwnedSetsAvailable => true;
    public bool SupportsJobTrialClear => true;

    /// <summary>Remedies of findings M2 and M3 (MONGO.md), switchable so that the stress test can show what they fix.</summary>
    public static bool AbortOnFailure { get; set; } = true;
    public static bool BackOffOnTransient { get; set; } = true;

    /// <summary>
    /// A count the commit checks. A fence must match. A count must match, or the batch is "not landed". In an owned batch a
    /// consume or done (Bound) and a mark (Mark) carry the fenced set's bound; when one touches fewer messages than it names,
    /// the store reads whether a key is outside the set. A mark that is short for any other reason is a message gone, as before.
    /// </summary>
    private enum CheckKind { Fence, Count, Bound, Mark }
    private readonly record struct Check(int Index, bool Delete, long Expected, CheckKind Kind, BsonDocument? Rewind = null);

    public async ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        StoreBatchRules.Validate(batch, ownedSets: true, supportsJobTrialClear: SupportsJobTrialClear);
        CreationProtocol.RejectUnsupported(batch, physicalRetirement: true);
        CommitClockFacts.RejectUnsupported(batch);
        if (!_options.JobGenerations && batch.Turns.Any(t => t.Row?.NextJobGeneration is not null || t.Sent.Any(m => m.JobGeneration != 0)))
            throw new StoreFormatException("Job generations require schema=2, message protocol=2.");
        if (batch.Window is not null) throw new NotSupportedException("MongoDB cannot enforce the requested commit window.");
        var sentAt = System.Diagnostics.Stopwatch.GetTimestamp();
        using var session = await _client.StartSessionAsync(cancellationToken: cancellationToken);
        try
        {
            session.StartTransaction(Transaction);
            if (_options.JobGenerations) await RequireVersionsAsync(session, cancellationToken);
            // Every fenced partition, in ascending id, with the sends whose order keys its clock cuts: the sends of the turns
            // in that partition. The one-partition form is the batch's partition and every send, as before.
            var (fences, owned) = (batch.Fenced, batch.Fences is not null);
            var sends = fences.ToDictionary(f => f.Partition, f => batch.Turns.Where(t => batch.PartitionOf(t) == f.Partition).Sum(t => t.Sent.Count));
            var models = new List<BulkWriteModel>();
            var checks = new List<Check>();
            var (clocks, known) = (new Dictionary<int, long>(), new Dictionary<int, long>());
            long now;

            if (_options.FenceInBatch && fences.All(f => _known.TryGetValue((f.Partition, f.Epoch), out var seen) && known.TryAdd(f.Partition, seen)))
            {
                // The owner is the only writer of its partition document at this epoch, so the clock is what it last saw.
                // The order keys are cut from it on this machine; the clock condition makes a wrong belief a refused fence.
                // A fence that matches nothing must stop the batch before anything else executes: otherwise the writes
                // behind it meet the rows of the commit that won (a journal position, a message id) and fail as definite
                // errors. So each fence is an upsert: matching nothing, it tries to insert the partition document again,
                // which is a duplicate key among the first k models, and an ordered bulk write stops at its first error.
                var micros = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
                foreach (var fence in fences)
                {
                    var clock = clocks[fence.Partition] = Math.Max(known[fence.Partition], micros) + sends[fence.Partition];
                    var filter = Fence(fence);
                    if (!_options.BrokenFence) filter["clock"] = known[fence.Partition];
                    checks.Add(new(models.Count, false, 1, CheckKind.Fence));
                    models.Add(new BulkWriteUpdateOneModel<BsonDocument>(_partitions.CollectionNamespace, filter, Builders<BsonDocument>.Update.Pipeline(new BsonDocument[]
                    {
                        new("$set", new BsonDocument
                        {
                            { "position", fence.Position + batch.PositionsIn(fence.Partition) }, { "clock", clock }, { "now", NowMs },
                            FenceWrite(),
                        }),
                    }), isUpsert: true));
                }
                now = 0;
            }
            else
            {
                // Each fence as its own statement, in ascending partition id, checked before anything else is sent. It also
                // renews the lease from the database's time and cuts the commit's order keys from the partition's clock.
                now = long.MaxValue;
                foreach (var fence in fences)
                {
                    BsonDocument[] update =
                    [
                        new("$set", new BsonDocument
                        {
                            { "position", fence.Position + batch.PositionsIn(fence.Partition) },
                            { "now", NowMs },
                            FenceWrite(),
                            { "clock", new BsonDocument("$add", new BsonArray { new BsonDocument("$max", new BsonArray { "$clock", NowUs }), (long)sends[fence.Partition] }) },
                        }),
                    ];
                    var held = await _partitions.FindOneAndUpdateAsync(session, Fence(fence), Builders<BsonDocument>.Update.Pipeline(update), new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After }, cancellationToken);
                    if (held is null)
                    {
                        await session.AbortTransactionAsync(cancellationToken);
                        return new Committed(false, 0, []);
                    }
                    (now, clocks[fence.Partition]) = (Math.Min(now, held["now"].AsInt64), held["clock"].AsInt64);
                }
            }

            // Each turn writes in the partition its address embeds (the batch's partition in the one-partition form), at
            // that partition's next position and with its order keys. An owned batch binds its consumes, dones and marks to
            // the fenced partitions' messages: an InboxKey does not name its partition.
            var (key, position, seqs) = (fences.ToDictionary(f => f.Partition, f => clocks[f.Partition] - sends[f.Partition]), fences.ToDictionary(f => f.Partition, f => f.Position), new List<long>());
            var epochs = fences.ToDictionary(f => f.Partition, f => f.Epoch);
            var bound = owned ? new BsonDocument("$in", new BsonArray(epochs.Keys)) : null;
            BsonDocument Keys(IEnumerable<InboxKey> ids, bool notDone, bool bind = true)
            {
                var filter = new BsonDocument("_id", new BsonDocument("$in", new BsonArray(ids.Select(InboxId))));
                if (notDone) filter["done"] = false;
                if (bind && bound is not null) filter["partition"] = bound;
                return filter;
            }
            var counted = owned ? CheckKind.Bound : CheckKind.Count;
            foreach (var turn in batch.Turns)
            {
                var home = batch.PartitionOf(turn);
                if (turn.Delete && turn.Row is not { Retired: true })
                {
                    // Round 3: an ended instance goes at its revision (a count that must be 1, as for an update), with its
                    // journal documents. All in the one bulk write: with the fence in the batch it stops at the fence first.
                    checks.Add(new(models.Count, true, 1, CheckKind.Count));
                    models.Add(new BulkWriteDeleteOneModel<BsonDocument>(_instances.CollectionNamespace, new BsonDocument { { "_id", turn.Instance }, { "revision", turn.Expected!.Value } }));

                }
                if (turn.Delete)
                    models.Add(new BulkWriteDeleteManyModel<BsonDocument>(_journal.CollectionNamespace, new BsonDocument { { "partition", home }, { "instance", turn.Instance }, { "revision", new BsonDocument("$gt", 0L) } }));
                if (turn.Row is { } row)
                {
                    var doc = ToDocument(row, home);
                    if (turn.Expected is not { } expected) models.Add(new BulkWriteInsertOneModel<BsonDocument>(_instances.CollectionNamespace, doc));
                    else
                    {
                        var match = new BsonDocument { { "_id", row.Id }, { "revision", expected }, { "retired", false } };
                        if (row.Retired) match["status"] = (int)row.Status; // ended status was checked by the marker shape
                        BsonDocument? rewind = null;
                        if (_options.JobGenerations && !row.Retired)
                        {
                            var regresses = row.NextJobGeneration is { } next ? new BsonDocument("$gt", next) : new BsonDocument("$type", "long");
                            rewind = new BsonDocument(match) { { "nextJobGeneration", regresses } };
                            match.Add("nextJobGeneration", new BsonDocument("$not", regresses));
                        }
                        checks.Add(new(models.Count, false, 1, CheckKind.Count, rewind));
                        models.Add(new BulkWriteReplaceOneModel<BsonDocument>(_instances.CollectionNamespace, match, doc));
                    }
                }
                if (turn.Consumed.Count > 0)
                {
                    checks.Add(new(models.Count, true, turn.Consumed.Count, counted));
                    models.Add(new BulkWriteDeleteManyModel<BsonDocument>(_messages.CollectionNamespace, Keys(turn.Consumed, notDone: true)));
                }
                if (turn.Done.Count > 0)
                {
                    checks.Add(new(models.Count, false, turn.Done.Count, counted));
                    models.Add(new BulkWriteUpdateManyModel<BsonDocument>(_messages.CollectionNamespace, Keys(turn.Done, notDone: true), new BsonDocument("$set", new BsonDocument("done", true))));
                }
                foreach (var message in turn.Sent)
                {
                    seqs.Add(++key[home]);
                    // Messages the owner hands over in memory to a fenced partition are written as seen at that partition's epoch.
                    var seen = batch.HandOver && epochs.TryGetValue(message.Target.Partition, out var mine) ? mine : 0;
                    models.Add(new BulkWriteInsertOneModel<BsonDocument>(_messages.CollectionNamespace, ToDocument(message, key[home], seen)));
                }
                foreach (var s in turn.Subscribed)
                    models.Add(new BulkWriteReplaceOneModel<BsonDocument>(_subscriptions.CollectionNamespace, new BsonDocument("_id", SubscriptionId(s)), ToDocument(s, home), isUpsert: true));
                foreach (var s in turn.Unsubscribed)
                    models.Add(new BulkWriteDeleteOneModel<BsonDocument>(_subscriptions.CollectionNamespace, new BsonDocument("_id", SubscriptionId(s))));
                if (turn.Unsubscribe)       // ended by force: every wait of the instance goes
                    models.Add(new BulkWriteDeleteManyModel<BsonDocument>(_subscriptions.CollectionNamespace, new BsonDocument("instance", turn.Instance)));
                if (turn.Held is { Count: > 0 } held)
                {
                    // Its turn ran: still there, not done, and no longer on trial. A count, like a consume; not bound, as in memory.
                    checks.Add(new(models.Count, false, held.Count, CheckKind.Count));
                    models.Add(new BulkWriteUpdateManyModel<BsonDocument>(_messages.CollectionNamespace, Keys(held, notDone: true, bind: false), new BsonDocument("$set", new BsonDocument("trial", 0L))));
                }
                if (turn.Journal is not null)
                {
                    var at = ++position[home];
                    models.Add(new BulkWriteInsertOneModel<BsonDocument>(_journal.CollectionNamespace, new BsonDocument
                    {
                        { "_id", new BsonDocument { { "p", home }, { "n", at } } }, { "partition", home }, { "position", at },
                        { "instance", turn.Instance }, { "revision", turn.Delete ? -1L : turn.Row?.Revision ?? 0L }, { "entries", turn.Journal },
                    }));
                }
            }
            // A mark records the epoch of its message's own partition: one value in the one-partition form, else a switch.
            BsonValue mark = epochs.Count == 1 ? batch.Epoch : new BsonDocument("$switch", new BsonDocument
            {
                { "branches", new BsonArray(epochs.Select(e => new BsonDocument { { "case", new BsonDocument("$eq", new BsonArray { "$partition", e.Key }) }, { "then", e.Value } })) },
            });
            void Marks(IReadOnlyList<InboxKey> ids, params string[] fields)
            {
                if (ids.Count == 0) return;
                var set = new BsonDocument(fields.Select(f => new BsonElement(f, mark)));
                if (owned) checks.Add(new(models.Count, false, ids.Distinct().Count(), CheckKind.Mark));
                models.Add(new BulkWriteUpdateManyModel<BsonDocument>(_messages.CollectionNamespace, Keys(ids, notDone: false),
                    epochs.Count == 1 ? new BsonDocument("$set", set) : Builders<BsonDocument>.Update.Pipeline(new BsonDocument[] { new("$set", set) })));
            }
            Marks(batch.Seen, "seen");
            Marks(batch.Trial, "seen", "trial");

            if (batch.ClearJobTrials.Count > 0)
            {
                // Match each pending ticket against the epoch of its own partition, also for a single fence.
                var filter = Keys(batch.ClearJobTrials, notDone: true, bind: false);
                filter["kind"] = Kinds.Job;
                filter["trial"] = new BsonDocument("$gt", 0L);
                filter["$or"] = new BsonArray(fences.Select(f => new BsonDocument { { "partition", f.Partition }, { "trial", f.Epoch } }));
                checks.Add(new(models.Count, false, batch.ClearJobTrials.Count, CheckKind.Count));
                models.Add(new BulkWriteUpdateManyModel<BsonDocument>(_messages.CollectionNamespace, filter,
                    new BsonDocument("$set", new BsonDocument("trial", 0L))) { Collation = Collation.Simple });
            }

            if (models.Count > 0)
            {
                ClientBulkWriteResult result;
                var fenceModels = checks.Count(c => c.Kind == CheckKind.Fence);
                try { result = await _client.BulkWriteAsync(session, models, new ClientBulkWriteOptions { IsOrdered = true, VerboseResult = true }, cancellationToken); }
                catch (ClientBulkWriteException e) when (e.WriteErrors.Any(w => w.Key < fenceModels && w.Value.Category == ServerErrorCategory.DuplicateKey))
                {
                    await Quietly(session.AbortTransactionAsync(CancellationToken.None));    // the server aborted it already
                    return new Committed(false, 0, []);
                }
                bool Matches(Check check) => (check.Delete ? result.DeleteResults[check.Index].DeletedCount : result.UpdateResults[check.Index].MatchedCount) == check.Expected;
                if (_options.JobGenerations && checks.All(c => c.Kind != CheckKind.Fence || Matches(c)))
                {
                    // A batch can touch a row once. A missed replacement leaves its prior image
                    // in this transaction, so a single read distinguishes rewinds from stale revisions.
                    var rewinds = new BsonArray(checks.Where(c => c.Rewind is not null && !Matches(c)).Select(c => c.Rewind!));
                    if (rewinds.Count > 0 && await _instances.Find(session, new BsonDocument("$or", rewinds)).AnyAsync(cancellationToken))
                        throw new StoreException(Failure.Definite, "A job generation counter cannot move backwards within its creation.");
                }
                // Only after every fence held: a bound statement that touched fewer messages than it named may name one outside
                // the fenced set, which is refused as definite, as the in-memory store refuses it.
                if (checks.All(c => c.Kind != CheckKind.Fence || Matches(c)) && checks.Any(c => c.Kind is CheckKind.Bound or CheckKind.Mark && !Matches(c)) &&
                    await OutsideAsync(session, batch, epochs.Keys, cancellationToken))
                {
                    await session.AbortTransactionAsync(cancellationToken);
                    throw new StoreException(Failure.Definite, "a mark, consume or done names a message outside the batch's fenced partitions");
                }
                if (checks.Any(c => c.Kind != CheckKind.Mark && !Matches(c)))
                {
                    // The fence (in the batch) or a count did not match: either way the owner resets.
                    await session.AbortTransactionAsync(cancellationToken);
                    return new Committed(false, 0, []);
                }
            }
            if (System.Diagnostics.Stopwatch.GetElapsedTime(sentAt) > batch.Lease)
            {
                // The commit-step deadline (the node lease): past the lease since the batch was sent, the owner's guard has
                // expired too; abort instead of committing.
                await session.AbortTransactionAsync(cancellationToken);
                return new Committed(false, 0, []);
            }
            await session.CommitTransactionAsync(cancellationToken);
            if (_options.FenceInBatch) foreach (var fence in fences) _known[(fence.Partition, fence.Epoch)] = clocks[fence.Partition];
            return new Committed(true, now, seqs);
        }
        catch (Exception e)
        {
            // Finding M2: the server does not notice a client that went away. A transaction whose connection broke stays
            // open, holding the partition document, until transactionLifetimeLimitSeconds. Abort it explicitly, on a
            // fresh connection, before telling the kernel anything; the kernel's next attempt would otherwise meet it.
            if (AbortOnFailure && session.IsInTransaction)
                using (var abort = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    await Quietly(session.AbortTransactionAsync(abort.Token));
            var failure = Classify(e);
            // Finding M3: the kernel tries a transient failure again at once. Beside a transaction that holds the
            // partition document every attempt fails the same way, so a short random pause goes first.
            if (BackOffOnTransient && failure.Failure == Failure.Transient) await Task.Delay(Random.Shared.Next(5, 30), CancellationToken.None);
            throw failure;
        }
    }

    /// <summary>After every fence held, in an owned batch: whether a key of its marks, consumes or dones names a message outside the fenced partitions (a message never changes partition).</summary>
    private async Task<bool> OutsideAsync(IClientSessionHandle session, CommitBatch batch, IEnumerable<int> fenced, CancellationToken cancellationToken)
    {
        var keys = batch.Seen.Concat(batch.Trial).Concat(batch.Turns.SelectMany(t => t.Consumed.Concat(t.Done))).Distinct().Select(InboxId);
        var filter = new BsonDocument { { "_id", new BsonDocument("$in", new BsonArray(keys)) }, { "partition", new BsonDocument("$nin", new BsonArray(fenced)) } };
        return await _messages.CountDocumentsAsync(session, filter, new CountOptions { Limit = 1 }, cancellationToken) > 0;
    }

    private static async Task Quietly(Task task)
    {
        try { await task; }
        catch (Exception) { }
    }

    /// <summary>
    /// What a failure means. Starting point: the driver's labels. TransientTransactionError: the transaction did not
    /// happen and may be tried again as it is. UnknownTransactionCommitResult: nobody knows. Anything else: definite.
    /// </summary>
    public static StoreException Classify(Exception e)
    {
        if (e is StoreException s) return s;
        for (var inner = e; inner is not null; inner = inner.InnerException)
            if (inner is MongoException m)
            {
                if (m.HasErrorLabel("TransientTransactionError")) return new StoreException(Failure.Transient, m.Message, e);
                if (m.HasErrorLabel("UnknownTransactionCommitResult")) return new StoreException(Failure.Unknown, m.Message, e);
                if (LabelsOnly) return new StoreException(Failure.Definite, m.Message, e);
                // Finding M4: "everything else is definite" is not safe. A killed operation (Interrupted, 11601), a
                // commitTransaction whose session was ended, a primary that stepped down: none of these is a property
                // of the batch, and a definite failure makes the kernel park an instance. Only errors that the same
                // batch would meet again are definite; everything else resets, which is always safe.
                return new StoreException(Deterministic(m) ? Failure.Definite : Failure.Unknown, m.Message, e);
            }
        return new StoreException(Failure.Unknown, e.Message, e);
    }

    /// <summary>The brief's starting point, kept so that the stress test can show finding M4.</summary>
    public static bool LabelsOnly { get; set; }

    // Duplicate key, document too large, document validation, bad value, type mismatch, key too long.
    private static readonly HashSet<int> DeterministicCodes = [11000, 11001, 10334, 17419, 121, 2, 14, 17280];

    private static bool Deterministic(MongoException m) => m switch
    {
        ClientBulkWriteException bulk => bulk.WriteErrors.Count > 0 && bulk.WriteErrors.Values.All(w => w.Category == ServerErrorCategory.DuplicateKey || DeterministicCodes.Contains(w.Code))
            || bulk.InnerException is MongoException inner && Deterministic(inner),
        MongoWriteException write => write.WriteError.Category == ServerErrorCategory.DuplicateKey || DeterministicCodes.Contains(write.WriteError.Code),
        MongoCommandException command => DeterministicCodes.Contains(command.Code),
        _ => false,
    };

    // ---- reads and adds -------------------------------------------------------------------------------------------------

    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) =>
        PollAsync(partition, new BsonDocument { { "partition", partition }, { "done", false }, { "seen", new BsonDocument("$lt", epoch) } }, horizon, limit, cancellationToken);

    /// <summary>One query over an owned set: each row is compared with the epoch of its own partition, all in seq order under the limit.</summary>
    public ValueTask<Polled> PollAsync(IReadOnlyCollection<PartitionFence> owned, TimeSpan horizon, int limit, CancellationToken cancellationToken = default)
    {
        StoreBatchRules.RequireDistinct(owned);
        if (owned.Count == 0) return new(new Polled([], 0));
        if (owned.Count == 1) return PollAsync(owned.First().Partition, owned.First().Epoch, horizon, limit, cancellationToken);
        var each = owned.OrderBy(f => f.Partition).Select(f => new BsonDocument { { "partition", f.Partition }, { "seen", new BsonDocument("$lt", f.Epoch) } });
        return PollAsync(owned.Min(f => f.Partition), new BsonDocument { { "$or", new BsonArray(each) }, { "done", false } }, horizon, limit, cancellationToken);
    }

    private async ValueTask<Polled> PollAsync(int anchor, BsonDocument rows, TimeSpan horizon, int limit, CancellationToken cancellationToken)
    {
        // One round trip: the store's time from a partition document, then the rows. $$NOW is one value for the whole command.
        rows["$expr"] = new BsonDocument("$lte", new BsonArray { "$due", new BsonDocument("$add", new BsonArray { NowMs, (long)horizon.TotalMilliseconds }) });
        BsonDocument[] pipeline =
        [
            new("$match", new BsonDocument("_id", anchor)),
            new("$project", new BsonDocument { { "_id", 0 }, { "now", NowMs } }),
            new("$unionWith", new BsonDocument
            {
                { "coll", _messages.CollectionNamespace.CollectionName },
                { "pipeline", new BsonArray
                {
                    new BsonDocument("$match", rows),
                    new BsonDocument("$sort", new BsonDocument { { "seq", 1 }, { "_id", 1 } }),
                    new BsonDocument("$limit", limit),
                } },
            }),
        ];
        var docs = await (await _partitions.AggregateAsync<BsonDocument>(pipeline, new AggregateOptions { BatchSize = limit + 1 }, cancellationToken)).ToListAsync(cancellationToken);
        return new Polled(docs.Skip(1).Select(ToStored).ToList(), docs[0]["now"].AsInt64);
    }

    public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default)
    {
        CreationProtocol.RejectUnsupported(message);
        Kinds.RequireApplicationSend(message.Kind);
        return InsertAsync(message.AsOrdinary(), cancellationToken);
    }

    public async ValueTask<EventAcceptance> AddEventAsync(EventDelivery delivery, CancellationToken cancellationToken = default)
    {
        CreationProtocol.RejectUnsupported(delivery.Message);
        await CheckAdmissionFormatAsync(delivery.Message, cancellationToken);
        // The existing per-partition outside-add clock is persistent, increasing and outside caller control.
        var seq = await NextOutsideSeq(delivery.Message.Target.Partition, cancellationToken);
        var message = delivery.At(seq);
        var fresh = ToDocument(message, seq, 0);
        var key = new BsonDocument("_id", fresh["_id"]);
        fresh.Remove("_id");
        var saved = await _messages.FindOneAndUpdateAsync(key, new BsonDocument("$setOnInsert", fresh),
            new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, cancellationToken);
        return new EventAcceptance(saved["seq"].AsInt64 == seq, new(saved["target"].AsString));
    }

    private async ValueTask<bool> InsertAsync(Message message, CancellationToken cancellationToken)
    {
        await CheckAdmissionFormatAsync(message, cancellationToken);
        var seq = await NextOutsideSeq(message.Target.Partition, cancellationToken);
        try
        {
            await _messages.InsertOneAsync(ToDocument(message, seq, 0), cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    private async ValueTask CheckAdmissionFormatAsync(Message message, CancellationToken cancellationToken)
    {
        await RequireVersionsAsync(cancellationToken);
        if (!_options.JobGenerations && message.JobGeneration != 0)
            throw new StoreFormatException("Job generations require schema=2, message protocol=2.");
    }

    private async ValueTask<long> NextOutsideSeq(int partition, CancellationToken cancellationToken)
    {
        // Nothing fences an outside add, so its key comes from the target partition's inbox clock: one document per
        // partition, apart from the partition document so that adds never conflict with the owner's commits.
        BsonDocument[] tick = [new("$set", new BsonDocument("clock", new BsonDocument("$add", new BsonArray { new BsonDocument("$max", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$clock", 0L }), NowUs }), 1L })))];
        var clock = await _clocks.FindOneAndUpdateAsync(new BsonDocument("_id", partition), Builders<BsonDocument>.Update.Pipeline(tick),
            new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, cancellationToken);
        return clock["clock"].AsInt64;
    }

    public async ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return [];
        var docs = await _instances.Find(new BsonDocument("_id", new BsonDocument("$in", new BsonArray(ids))), new FindOptions { BatchSize = ids.Count + 1 }).ToListAsync(cancellationToken);
        return docs.Select(d => StoredState.Restore(new InstanceRow(d["_id"].AsString, d["model"].AsString, d["definition"].AsString, (InstanceStatus)d["status"].AsInt32, d["state"].AsString,
            d["revision"].AsInt64, Str(d["parent"]), Str(d["fault"]), Str(d["jobs"]), (History)d.GetValue("history", 0).AsInt32, d.GetValue("suspended", false).AsBoolean,
            Str(d.GetValue("children", BsonNull.Value)), d.GetValue("stateVersion", 1).AsInt32, Retired: d["retired"].AsBoolean, CustomHandles: Str(d.GetValue("customHandles", BsonNull.Value)), IdentityOnly: false)
            { NextJobGeneration = d.TryGetValue("nextJobGeneration", out var next) ? next.AsInt64 : null })).ToList();
    }

    public async ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default)
    {
        try
        {
            await _events.InsertOneAsync(new BsonDocument
            {
                { "_id", new BsonDocument { { "k", @event.Key }, { "id", @event.Id } } }, { "key", @event.Key }, { "id", @event.Id },
                { "payload", (BsonValue?)@event.Payload ?? BsonNull.Value }, { "keepUntil", @event.KeepUntil },
            }, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey) { }
    }

    public async ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default)
    {
        var docs = await _subscriptions.Find(new BsonDocument("key", key)).Sort(new BsonDocument("instance", 1)).ToListAsync(cancellationToken);
        return docs.Select(d => new SubscriptionRow(d["key"].AsString, d["instance"].AsString, d["wait"].AsString,
            d.GetValue("start", BsonNull.Value) is { IsBsonNull: false } start ? Json.Read<StartSpec>(start.AsString) : null)).ToList();
    }

    public ValueTask<IReadOnlyList<EventRoute>> PublishedMatchesAsync(EventRow published, CancellationToken cancellationToken = default) =>
        FindMatches(new BsonDocument("_id", new BsonDocument { { "k", published.Key }, { "id", published.Id } }), null, cancellationToken);

    public ValueTask<IReadOnlyList<EventRoute>> KeptMatchesAsync(IReadOnlyList<SubscriptionRow> waits, CancellationToken cancellationToken = default)
    {
        // Until this provider supports qualified rows, only generation-zero tickets can match.
        waits = waits.Where(w => !CreationProtocol.Qualified(w)).ToArray();
        return waits.Count == 0 ? new ValueTask<IReadOnlyList<EventRoute>>([]) : FindMatches(new BsonDocument
        {
            { "key", new BsonDocument("$in", new BsonArray(waits.Select(s => s.Key).Distinct())) },
            { "$expr", new BsonDocument("$gte", new BsonArray { "$keepUntil", NowMs }) },
        }, waits, cancellationToken);
    }

    private async ValueTask<IReadOnlyList<EventRoute>> FindMatches(BsonDocument filter, IReadOnlyList<SubscriptionRow>? waits, CancellationToken token)
    {
        var pipeline = new List<BsonDocument>
        {
            new("$match", filter),
            new("$lookup", new BsonDocument { { "from", _subscriptions.CollectionNamespace.CollectionName }, { "localField", "key" },
                { "foreignField", "key" }, { "as", "wait" } }),
            new("$unwind", "$wait"),
        };
        if (waits is not null) pipeline.Add(new("$match", new BsonDocument("wait._id", new BsonDocument("$in", new BsonArray(waits.Select(SubscriptionId))))));
        pipeline.Add(new("$sort", new BsonDocument { { "id", 1 }, { "wait.instance", 1 }, { "wait.wait", 1 } }));
        var rows = await (await _events.AggregateAsync<BsonDocument>(pipeline, cancellationToken: token)).ToListAsync(token);
        return rows.Select(e =>
        {
            var w = e["wait"].AsBsonDocument;
            var spec = Str(w["start"]);
            return new EventRoute(new(e["key"].AsString, e["id"].AsString, Str(e["payload"]), e["keepUntil"].AsInt64),
                new(w["key"].AsString, w["instance"].AsString, w["wait"].AsString, spec is null ? null : Json.Read<StartSpec>(spec)));
        }).ToList();
    }

    public async ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0) return [];
        var filter = new BsonDocument { { "key", new BsonDocument("$in", new BsonArray(keys)) }, { "$expr", new BsonDocument("$gte", new BsonArray { "$keepUntil", NowMs }) } };
        var docs = await _events.Find(filter).Sort(new BsonDocument("id", 1)).ToListAsync(cancellationToken);
        return docs.Select(d => new EventRow(d["key"].AsString, d["id"].AsString, Str(d["payload"]), d["keepUntil"].AsInt64)).ToList();
    }

    public async ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default)
    {
        await RequireReadOnlyVersionsAsync(cancellationToken);
        var docs = await _journal.Find(new BsonDocument { { "partition", partition }, { "position", new BsonDocument("$gt", after) } }, new FindOptions { BatchSize = Math.Min(take, 10_000) + 1 })
            .Sort(new BsonDocument("position", 1)).Limit(take).ToListAsync(cancellationToken);
        return docs.Select(d => new JournalRow(d["partition"].AsInt32, d["position"].AsInt64, d["instance"].AsString, d["revision"].AsInt64, d["entries"].AsString)).ToList();
    }

    public async ValueTask<IReadOnlyList<JournalRow>> ReadJournalOfAsync(int partition, string instance, long after, CancellationToken cancellationToken = default)
    {
        await RequireReadOnlyVersionsAsync(cancellationToken);
        // A find may span multiple cursor batches. Keep them on the same committed snapshot.
        var docs = await _journal.WithReadConcern(ReadConcern.Snapshot).Find(new BsonDocument { { "partition", partition }, { "instance", instance }, { "position", new BsonDocument("$gt", after) } })
            .Sort(new BsonDocument("position", 1)).ToListAsync(cancellationToken);
        return docs.Select(d => new JournalRow(d["partition"].AsInt32, d["position"].AsInt64, d["instance"].AsString, d["revision"].AsInt64, d["entries"].AsString)).ToList();
    }

    public async ValueTask<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        await RequireVersionsAsync(cancellationToken);
        var expired = new BsonDocument("$expr", new BsonDocument("$lt", new BsonArray { "$keepUntil", NowMs }));
        var messages = await _messages.DeleteManyAsync(new BsonDocument { { "done", true }, { "keepUntil", new BsonDocument("$ne", BsonNull.Value) }, { "$expr", expired["$expr"] } }, cancellationToken);
        var events = await _events.DeleteManyAsync(expired, cancellationToken);
        return (int)(messages.DeletedCount + events.DeletedCount);
    }

    // ---- round 3's operations ---------------------------------------------------------------------------------------------

    public async ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default)
    {
        // The node's document carries the primary's time of its last beat; the count and the free partitions are read
        // against that same time.
        var beat = await RenewRegisteredNodeAsync(null, node, null, cancellationToken);
        return await MembershipAsync(beat["seenAt"].AsInt64, window, cancellationToken);
    }

    /// <summary>A subscription of kind start, upserted by its id, so registering it twice keeps one.</summary>
    public async ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default)
    {
        CreationProtocol.RejectUnsupported(subscription);
        await _subscriptions.ReplaceOneAsync(new BsonDocument("_id", SubscriptionId(subscription)), ToDocument(subscription, 0), new ReplaceOptions { IsUpsert = true }, cancellationToken);
    }

    public async ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default)
    {
        await RequireVersionsAsync(cancellationToken);
        // The projector's position first, then the partition's: every position up to the second is committed.
        var projected = await _projections.Find(new BsonDocument("_id", partition)).SingleAsync(cancellationToken);
        var written = await _partitions.Find(new BsonDocument("_id", partition)).Project(new BsonDocument("position", 1)).SingleAsync(cancellationToken);
        return new Projection(projected["position"].AsInt64, written["position"].AsInt64);
    }

    public async ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default)
    {
        // One transaction: the projector's own document moves only from where it was read (a conflict with another
        // projector is a write conflict, transient), then one bulk write of the view documents.
        using var session = await _client.StartSessionAsync(cancellationToken: cancellationToken);
        try
        {
            session.StartTransaction(Transaction);
            await RequireVersionsAsync(session, cancellationToken);
            var moved = await _projections.UpdateOneAsync(session, new BsonDocument { { "_id", partition }, { "position", from } }, new BsonDocument("$set", new BsonDocument("position", to)), cancellationToken: cancellationToken);
            if (moved.MatchedCount != 1)
            {
                await session.AbortTransactionAsync(cancellationToken);
                return false;
            }
            var models = new List<BulkWriteModel>();
            foreach (var change in ViewChange.From(rows))
            {
                var key = new BsonDocument("_id", change.Instance);
                if (change.Commits == 0)
                    models.Add(new BulkWriteDeleteOneModel<BsonDocument>(_views.CollectionNamespace, key));
                else
                {
                    var fields = new BsonDocument { { "partition", partition }, { "revision", change.Revision } };
                    var update = new BsonDocument("$set", fields);
                    if (change.Reset) fields["commits"] = change.Commits;
                    else update["$inc"] = new BsonDocument("commits", change.Commits);
                    models.Add(new BulkWriteUpdateOneModel<BsonDocument>(_views.CollectionNamespace, key, update, isUpsert: true));
                }
            }
            if (models.Count > 0) await _client.BulkWriteAsync(session, models, new ClientBulkWriteOptions { IsOrdered = true }, cancellationToken);
            await session.CommitTransactionAsync(cancellationToken);
            return true;
        }
        catch (Exception e)
        {
            if (AbortOnFailure && session.IsInTransaction)     // M2, as in a commit
                using (var abort = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    await Quietly(session.AbortTransactionAsync(abort.Token));
            var failure = Classify(e);
            if (BackOffOnTransient && failure.Failure == Failure.Transient) await Task.Delay(Random.Shared.Next(5, 30), CancellationToken.None);     // M3
            throw failure;
        }
    }

    public async ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default)
    {
        await RequireReadOnlyVersionsAsync(cancellationToken);
        if (instances.Count == 0) return [];
        var docs = await _views.Find(new BsonDocument("_id", new BsonDocument("$in", new BsonArray(instances)))).ToListAsync(cancellationToken);
        return docs.Select(d => new ViewRow(d["_id"].AsString, d["revision"].AsInt64, d["commits"].AsInt32)).ToList();
    }

    // ---- documents ------------------------------------------------------------------------------------------------------

    private static string? Str(BsonValue value) => value.IsBsonNull ? null : value.AsString;

    private static BsonDocument ToDocument(InstanceRow row, int partition) => new()
    {
        { "_id", row.Id }, { "nextJobGeneration", row.NextJobGeneration.GetValueOrDefault(), row.NextJobGeneration.HasValue }, { "partition", partition }, { "model", row.Model }, { "definition", row.Definition }, { "status", (int)row.Status },
        { "state", StoredState.Encode(row) }, { "jobs", (BsonValue?)row.Jobs ?? BsonNull.Value }, { "customHandles", (BsonValue?)row.CustomHandles ?? BsonNull.Value }, { "revision", row.Revision },
        { "parent", (BsonValue?)row.Parent ?? BsonNull.Value }, { "fault", (BsonValue?)row.Fault ?? BsonNull.Value },
        { "retired", row.Retired }, { "history", (int)row.History }, { "suspended", row.Suspended }, { "children", (BsonValue?)row.Children ?? BsonNull.Value }, { "stateVersion", row.StateVersion },
    };

    private static BsonDocument InboxId(InboxKey key)
    {
        BsonValue wire = key.HasIssuedOrigin && DurableKeyJson.HasUnpairedSurrogate(key.Id)
            ? new BsonBinaryData(DurableKeyJson.CodeUnits(key.Id)) : new BsonString(key.Id);
        var id = new BsonDocument { { "id", wire }, { "scope", key.Scope } };
        if (key.Match is not null) id.Add("match", key.Match);
        return id;
    }

    private static BsonDocument ToDocument(Message m, long seq, long seen) => new()
    {
        { "_id", InboxId(m.InboxKey) }, { "jobGeneration", m.JobGeneration, m.JobGeneration != 0 }, { "partition", m.Target.Partition }, { "seq", seq }, { "target", m.Target.Id }, { "kind", m.Kind },
        { "payload", (BsonValue?)m.Payload ?? BsonNull.Value }, { "sender", (BsonValue?)m.Sender ?? BsonNull.Value },
        { "start", m.Start is null ? BsonNull.Value : Json.Write(m.Start) }, { "due", m.Due ?? 0L }, { "seen", seen }, { "attempts", 0 }, { "trial", 0L },
        { "keepUntil", m.KeepUntil is { } keep ? keep : BsonNull.Value }, { "done", false }, { "replyTo", (BsonValue?)m.ReplyTo ?? BsonNull.Value },
    };

    private static Stored ToStored(BsonDocument d)
    {
        var due = d["due"].AsInt64;
        var key = d["_id"].AsBsonDocument;
        var wire = key["id"];
        var message = new Message(wire.IsBsonBinaryData ? DurableKeyJson.FromCodeUnits(wire.AsBsonBinaryData.Bytes) : wire.AsString, new InstanceRef(d["target"].AsString), d["kind"].AsString, Str(d["payload"]), Str(d["sender"]),
            d["start"].IsBsonNull ? null : Json.Read<StartSpec>(d["start"].AsString), due == 0 ? null : due, d["keepUntil"].IsBsonNull ? null : d["keepUntil"].AsInt64,
            Str(d.GetValue("replyTo", BsonNull.Value))) { JobGeneration = d.GetValue("jobGeneration", 0L).AsInt64 };
        return new Stored(message.WithInboxScope(key["scope"].ToInt64(), Str(key.GetValue("match", BsonNull.Value))), d["seq"].AsInt64, d["seen"].AsInt64, d["attempts"].AsInt32, d["trial"].AsInt64);
    }

    private static BsonDocument SubscriptionId(SubscriptionRow s) => new() { { "k", s.Key }, { "i", s.Instance }, { "w", s.Wait } };

    private static BsonDocument ToDocument(SubscriptionRow s, int partition) => new()
    {
        { "_id", SubscriptionId(s) }, { "key", s.Key }, { "instance", s.Instance }, { "wait", s.Wait }, { "partition", partition },
        { "start", s.Start is null ? BsonNull.Value : Json.Write(s.Start) },
    };
}
