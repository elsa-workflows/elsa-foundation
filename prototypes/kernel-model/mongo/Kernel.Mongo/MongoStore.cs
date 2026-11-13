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
public sealed class MongoStore : IKernelStore
{
    private static readonly TransactionOptions Transaction = new(ReadConcern.Snapshot, ReadPreference.Primary, WriteConcern.WMajority);
    private static readonly BsonDocument NowMs = new("$toLong", "$$NOW");
    private static readonly BsonDocument NowUs = new("$multiply", new BsonArray { NowMs, 1000L });

    private readonly IMongoClient _client;
    private readonly MongoStoreOptions _options;
    private readonly IMongoCollection<BsonDocument> _partitions, _clocks, _instances, _messages, _subscriptions, _events, _journal, _nodes, _projections, _views;
    private readonly ConcurrentDictionary<(int Partition, long Epoch), long> _known = new();     // FenceInBatch: the clock at (partition, epoch)

    public MongoStore(IMongoClient client, string database, MongoStoreOptions? options = null)
    {
        _client = client;
        _options = options ?? new MongoStoreOptions();
        // Every read outside a transaction is majority: it never sees a write that a failover could roll back.
        var db = client.GetDatabase(database, new MongoDatabaseSettings { ReadConcern = ReadConcern.Majority, WriteConcern = WriteConcern.WMajority, ReadPreference = ReadPreference.Primary });
        Database = db;
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

    public IMongoDatabase Database { get; }

    /// <summary>Collections, indexes and the partition documents. Collections are created here, not inside a transaction.</summary>
    public async Task CreateAsync(int partitions = Defaults.ServerPartitions)
    {
        var existing = await (await Database.ListCollectionNamesAsync()).ToListAsync();
        foreach (var name in new[] { "partitions", "clocks", "instances", "messages", "subscriptions", "events", "journal", "nodes", "projections", "views" })
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
                { "owner", BsonNull.Value }, { "epoch", 0L }, { "leaseUntil", 0L }, { "position", 0L }, { "clock", 0L }, { "now", 0L }, { "died", false },
            }), new UpdateOptions { IsUpsert = true });
            await _clocks.UpdateOneAsync(new BsonDocument("_id", p), new BsonDocument("$setOnInsert", new BsonDocument("clock", 0L)), new UpdateOptions { IsUpsert = true });
            await _projections.UpdateOneAsync(new BsonDocument("_id", p), new BsonDocument("$setOnInsert", new BsonDocument("position", 0L)), new UpdateOptions { IsUpsert = true });
        }
    }

    // ---- ownership ---------------------------------------------------------------------------------------------------

    public async ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        using var session = await _client.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction(Transaction);
        var filter = new BsonDocument
        {
            { "_id", partition },
            { "$or", new BsonArray { new BsonDocument("owner", BsonNull.Value), new BsonDocument("owner", node), new BsonDocument("$expr", new BsonDocument("$lt", new BsonArray { "$leaseUntil", NowMs })) } },
        };
        BsonDocument[] update =
        [
            new("$set", new BsonDocument
            {
                // Taken from another owner: only possible because its lease expired. That is a death.
                { "died", new BsonDocument("$and", new BsonArray { new BsonDocument("$ne", new BsonArray { "$owner", BsonNull.Value }), new BsonDocument("$ne", new BsonArray { "$owner", new BsonDocument("$literal", node) }) }) },
                { "owner", new BsonDocument("$literal", node) },
                { "epoch", new BsonDocument("$add", new BsonArray { "$epoch", 1L }) },
                { "now", NowMs },
                { "leaseUntil", new BsonDocument("$add", new BsonArray { NowMs, (long)lease.TotalMilliseconds }) },
            }),
        ];
        var doc = await _partitions.FindOneAndUpdateAsync(session, filter, Builders<BsonDocument>.Update.Pipeline(update), new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After }, cancellationToken);
        if (doc is null)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return null;
        }
        var epoch = doc["epoch"].AsInt64;
        // In the same transaction: one more attempt for every message the dead owner had on trial.
        if (doc["died"].AsBoolean)
            await _messages.UpdateManyAsync(session, new BsonDocument { { "partition", partition }, { "trial", epoch - 1 }, { "done", false } }, new BsonDocument("$inc", new BsonDocument("attempts", 1)), cancellationToken: cancellationToken);
        await session.CommitTransactionAsync(cancellationToken);
        if (_options.FenceInBatch)
        {
            foreach (var key in _known.Keys.Where(k => k.Partition == partition && k.Epoch < epoch)) _known.TryRemove(key, out _);
            _known[(partition, epoch)] = doc["clock"].AsInt64;
        }
        return new Lease(epoch, doc["position"].AsInt64, doc["now"].AsInt64);
    }

    public async ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default)
    {
        _known.TryRemove((partition, epoch), out _);
        await _partitions.UpdateOneAsync(new BsonDocument { { "_id", partition }, { "epoch", epoch } }, new BsonDocument("$set", new BsonDocument("owner", BsonNull.Value)), cancellationToken: cancellationToken);
    }

    // ---- the commit -----------------------------------------------------------------------------------------------------

    private BsonDocument Fence(CommitBatch batch) => _options.BrokenFence
        ? new BsonDocument("_id", batch.Partition)
        : new BsonDocument { { "_id", batch.Partition }, { "epoch", batch.Epoch }, { "position", batch.Position } };

    /// <summary>Remedies of findings M2 and M3 (MONGO.md), switchable so that the stress test can show what they fix.</summary>
    public static bool AbortOnFailure { get; set; } = true;
    public static bool BackOffOnTransient { get; set; } = true;

    public async ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        CommitClockFacts.RejectUnsupported(batch);
        using var session = await _client.StartSessionAsync(cancellationToken: cancellationToken);
        try
        {
            session.StartTransaction(Transaction);
            var sends = batch.Turns.Sum(s => s.Sent.Count);
            var leaseMs = (long)batch.Lease.TotalMilliseconds;
            var models = new List<BulkWriteModel>();
            var checks = new List<(int Index, bool Delete, long Expected, bool Fence)>();
            long now, clock;

            if (_options.FenceInBatch && _known.TryGetValue((batch.Partition, batch.Epoch), out var known))
            {
                // The owner is the only writer of its partition document at this epoch, so the clock is what it last saw.
                // The order keys are cut from it on this machine; the clock condition makes a wrong belief a refused fence.
                clock = Math.Max(known, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000) + sends;
                // A fence that matches nothing must stop the batch before anything else executes: otherwise the writes
                // behind it meet the rows of the commit that won (a journal position, a message id) and fail as definite
                // errors. So the fence is an upsert: matching nothing, it tries to insert the partition document again,
                // which is a duplicate key at index 0, and an ordered bulk write stops at its first error.
                var filter = Fence(batch);
                if (!_options.BrokenFence) filter["clock"] = known;
                models.Add(new BulkWriteUpdateOneModel<BsonDocument>(_partitions.CollectionNamespace, filter, Builders<BsonDocument>.Update.Pipeline(new BsonDocument[]
                {
                    new("$set", new BsonDocument
                    {
                        { "position", batch.Position + batch.Positions }, { "clock", clock }, { "now", NowMs },
                        { "leaseUntil", new BsonDocument("$add", new BsonArray { NowMs, leaseMs }) },
                    }),
                }), isUpsert: true));
                checks.Add((0, false, 1, true));
                now = 0;
            }
            else
            {
                // The fence as its own statement, checked before anything else is sent. It also renews the lease from the
                // database's time and cuts the commit's order keys from the partition's clock.
                BsonDocument[] update =
                [
                    new("$set", new BsonDocument
                    {
                        { "position", batch.Position + batch.Positions },
                        { "now", NowMs },
                        { "leaseUntil", new BsonDocument("$add", new BsonArray { NowMs, leaseMs }) },
                        { "clock", new BsonDocument("$add", new BsonArray { new BsonDocument("$max", new BsonArray { "$clock", NowUs }), (long)sends }) },
                    }),
                ];
                var fence = await _partitions.FindOneAndUpdateAsync(session, Fence(batch), Builders<BsonDocument>.Update.Pipeline(update), new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After }, cancellationToken);
                if (fence is null)
                {
                    await session.AbortTransactionAsync(cancellationToken);
                    return new Committed(false, 0, []);
                }
                (now, clock) = (fence["now"].AsInt64, fence["clock"].AsInt64);
            }

            var (key, position, seqs) = (clock - sends, batch.Position, new List<long>(sends));
            foreach (var turn in batch.Turns)
            {
                if (turn.Delete)
                {
                    // Round 3: an ended instance goes at its revision (a count that must be 1, as for an update), with its
                    // journal documents. All in the one bulk write: with the fence in the batch it stops at the fence first.
                    checks.Add((models.Count, true, 1, false));
                    models.Add(new BulkWriteDeleteOneModel<BsonDocument>(_instances.CollectionNamespace, new BsonDocument { { "_id", turn.Instance }, { "revision", turn.Expected!.Value } }));
                    models.Add(new BulkWriteDeleteManyModel<BsonDocument>(_journal.CollectionNamespace, new BsonDocument { { "partition", batch.Partition }, { "instance", turn.Instance }, { "revision", new BsonDocument("$gt", 0L) } }));
                }
                if (turn.Row is { } row)
                {
                    var doc = ToDocument(row, batch.Partition);
                    if (turn.Expected is not { } expected) models.Add(new BulkWriteInsertOneModel<BsonDocument>(_instances.CollectionNamespace, doc));
                    else
                    {
                        checks.Add((models.Count, false, 1, false));
                        models.Add(new BulkWriteReplaceOneModel<BsonDocument>(_instances.CollectionNamespace, new BsonDocument { { "_id", row.Id }, { "revision", expected } }, doc));
                    }
                }
                if (turn.Consumed.Count > 0)
                {
                    checks.Add((models.Count, true, turn.Consumed.Count, false));
                    models.Add(new BulkWriteDeleteManyModel<BsonDocument>(_messages.CollectionNamespace, new BsonDocument { { "_id", new BsonDocument("$in", new BsonArray(turn.Consumed)) }, { "done", false } }));
                }
                if (turn.Done.Count > 0)
                {
                    checks.Add((models.Count, false, turn.Done.Count, false));
                    models.Add(new BulkWriteUpdateManyModel<BsonDocument>(_messages.CollectionNamespace, new BsonDocument { { "_id", new BsonDocument("$in", new BsonArray(turn.Done)) }, { "done", false } }, new BsonDocument("$set", new BsonDocument("done", true))));
                }
                foreach (var message in turn.Sent)
                {
                    seqs.Add(++key);
                    var seen = batch.HandOver && message.Target.Partition == batch.Partition ? batch.Epoch : 0;
                    models.Add(new BulkWriteInsertOneModel<BsonDocument>(_messages.CollectionNamespace, ToDocument(message, key, seen)));
                }
                foreach (var s in turn.Subscribed)
                    models.Add(new BulkWriteReplaceOneModel<BsonDocument>(_subscriptions.CollectionNamespace, new BsonDocument("_id", SubscriptionId(s)), ToDocument(s, batch.Partition), isUpsert: true));
                foreach (var s in turn.Unsubscribed)
                    models.Add(new BulkWriteDeleteOneModel<BsonDocument>(_subscriptions.CollectionNamespace, new BsonDocument("_id", SubscriptionId(s))));
                if (turn.Unsubscribe)       // ended by force: every wait of the instance goes
                    models.Add(new BulkWriteDeleteManyModel<BsonDocument>(_subscriptions.CollectionNamespace, new BsonDocument("instance", turn.Instance)));
                if (turn.Held is { Count: > 0 } held)
                {
                    // Its turn ran: still there, not done, and no longer on trial. A count, like a consume.
                    checks.Add((models.Count, false, held.Count, false));
                    models.Add(new BulkWriteUpdateManyModel<BsonDocument>(_messages.CollectionNamespace, new BsonDocument { { "_id", new BsonDocument("$in", new BsonArray(held)) }, { "done", false } }, new BsonDocument("$set", new BsonDocument("trial", 0L))));
                }
                if (turn.Journal is not null)
                {
                    position++;
                    models.Add(new BulkWriteInsertOneModel<BsonDocument>(_journal.CollectionNamespace, new BsonDocument
                    {
                        { "_id", new BsonDocument { { "p", batch.Partition }, { "n", position } } }, { "partition", batch.Partition }, { "position", position },
                        { "instance", turn.Instance }, { "revision", turn.Delete ? -1L : turn.Row?.Revision ?? 0L }, { "entries", turn.Journal },
                    }));
                }
            }
            if (batch.Seen.Count > 0)
                models.Add(new BulkWriteUpdateManyModel<BsonDocument>(_messages.CollectionNamespace, new BsonDocument("_id", new BsonDocument("$in", new BsonArray(batch.Seen))), new BsonDocument("$set", new BsonDocument("seen", batch.Epoch))));
            if (batch.Trial.Count > 0)
                models.Add(new BulkWriteUpdateManyModel<BsonDocument>(_messages.CollectionNamespace, new BsonDocument("_id", new BsonDocument("$in", new BsonArray(batch.Trial))), new BsonDocument("$set", new BsonDocument { { "seen", batch.Epoch }, { "trial", batch.Epoch } })));

            if (models.Count > 0)
            {
                ClientBulkWriteResult result;
                try { result = await _client.BulkWriteAsync(session, models, new ClientBulkWriteOptions { IsOrdered = true, VerboseResult = true }, cancellationToken); }
                catch (ClientBulkWriteException e) when (checks is [{ Fence: true }, ..] && e.WriteErrors.TryGetValue(0, out var error) && error.Category == ServerErrorCategory.DuplicateKey)
                {
                    await Quietly(session.AbortTransactionAsync(CancellationToken.None));    // the server aborted it already
                    return new Committed(false, 0, []);
                }
                foreach (var check in checks)
                {
                    var count = check.Delete ? result.DeleteResults[check.Index].DeletedCount : result.UpdateResults[check.Index].MatchedCount;
                    if (count == check.Expected) continue;
                    // The fence (in the batch) or a count did not match: either way the owner resets.
                    await session.AbortTransactionAsync(cancellationToken);
                    return new Committed(false, 0, []);
                }
            }
            await session.CommitTransactionAsync(cancellationToken);
            if (_options.FenceInBatch) _known[(batch.Partition, batch.Epoch)] = clock;
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

    public async ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default)
    {
        // One round trip: the store's time from the partition document, then the rows. $$NOW is one value for the whole command.
        BsonDocument[] pipeline =
        [
            new("$match", new BsonDocument("_id", partition)),
            new("$project", new BsonDocument { { "_id", 0 }, { "now", NowMs } }),
            new("$unionWith", new BsonDocument
            {
                { "coll", _messages.CollectionNamespace.CollectionName },
                { "pipeline", new BsonArray
                {
                    new BsonDocument("$match", new BsonDocument
                    {
                        { "partition", partition }, { "done", false }, { "seen", new BsonDocument("$lt", epoch) },
                        { "$expr", new BsonDocument("$lte", new BsonArray { "$due", new BsonDocument("$add", new BsonArray { NowMs, (long)horizon.TotalMilliseconds }) }) },
                    }),
                    new BsonDocument("$sort", new BsonDocument { { "seq", 1 }, { "_id", 1 } }),
                    new BsonDocument("$limit", limit),
                } },
            }),
        ];
        var docs = await (await _partitions.AggregateAsync<BsonDocument>(pipeline, new AggregateOptions { BatchSize = limit + 1 }, cancellationToken)).ToListAsync(cancellationToken);
        return new Polled(docs.Skip(1).Select(ToStored).ToList(), docs[0]["now"].AsInt64);
    }

    public async ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default)
    {
        // Nothing fences an outside add, so its key comes from the target partition's inbox clock: one document per
        // partition, apart from the partition document so that adds never conflict with the owner's commits.
        BsonDocument[] tick = [new("$set", new BsonDocument("clock", new BsonDocument("$add", new BsonArray { new BsonDocument("$max", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$clock", 0L }), NowUs }), 1L })))];
        var clock = await _clocks.FindOneAndUpdateAsync(new BsonDocument("_id", message.Target.Partition), Builders<BsonDocument>.Update.Pipeline(tick),
            new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, cancellationToken);
        try
        {
            await _messages.InsertOneAsync(ToDocument(message, clock["clock"].AsInt64, 0), cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return [];
        var docs = await _instances.Find(new BsonDocument("_id", new BsonDocument("$in", new BsonArray(ids))), new FindOptions { BatchSize = ids.Count + 1 }).ToListAsync(cancellationToken);
        return docs.Select(d => new InstanceRow(d["_id"].AsString, d["model"].AsString, d["definition"].AsString, (InstanceStatus)d["status"].AsInt32, d["state"].AsString,
            d["revision"].AsInt64, Str(d["parent"]), Str(d["fault"]), Str(d["jobs"]), (History)d.GetValue("history", 0).AsInt32, d.GetValue("suspended", false).AsBoolean,
            Str(d.GetValue("children", BsonNull.Value)), d.GetValue("stateVersion", 1).AsInt32)).ToList();
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

    public async ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0) return [];
        var filter = new BsonDocument { { "key", new BsonDocument("$in", new BsonArray(keys)) }, { "$expr", new BsonDocument("$gte", new BsonArray { "$keepUntil", NowMs }) } };
        var docs = await _events.Find(filter).Sort(new BsonDocument("id", 1)).ToListAsync(cancellationToken);
        return docs.Select(d => new EventRow(d["key"].AsString, d["id"].AsString, Str(d["payload"]), d["keepUntil"].AsInt64)).ToList();
    }

    public async ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default)
    {
        var docs = await _journal.Find(new BsonDocument { { "partition", partition }, { "position", new BsonDocument("$gt", after) } }, new FindOptions { BatchSize = Math.Min(take, 10_000) + 1 })
            .Sort(new BsonDocument("position", 1)).Limit(take).ToListAsync(cancellationToken);
        return docs.Select(d => new JournalRow(d["partition"].AsInt32, d["position"].AsInt64, d["instance"].AsString, d["revision"].AsInt64, d["entries"].AsString)).ToList();
    }

    public async ValueTask<int> SweepAsync(CancellationToken cancellationToken = default)
    {
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
        var beat = await _nodes.FindOneAndUpdateAsync(new BsonDocument("_id", node), Builders<BsonDocument>.Update.Pipeline(new BsonDocument[] { new("$set", new BsonDocument("seenAt", NowMs)) }),
            new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, cancellationToken);
        var now = beat["seenAt"].AsInt64;
        var nodes = await _nodes.CountDocumentsAsync(new BsonDocument("seenAt", new BsonDocument("$gte", now - (long)window.TotalMilliseconds)), cancellationToken: cancellationToken);
        var free = await _partitions.Find(new BsonDocument("$or", new BsonArray { new BsonDocument("owner", BsonNull.Value), new BsonDocument("leaseUntil", new BsonDocument("$lt", now)) }))
            .Project(new BsonDocument("_id", 1)).ToListAsync(cancellationToken);
        return new Cluster((int)nodes, free.Select(d => d["_id"].AsInt32).Order().ToList(), now);
    }

    /// <summary>A subscription of kind start, upserted by its id, so registering it twice keeps one.</summary>
    public async ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default) =>
        await _subscriptions.ReplaceOneAsync(new BsonDocument("_id", SubscriptionId(subscription)), ToDocument(subscription, 0), new ReplaceOptions { IsUpsert = true }, cancellationToken);

    public async ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default)
    {
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
            var moved = await _projections.UpdateOneAsync(session, new BsonDocument { { "_id", partition }, { "position", from } }, new BsonDocument("$set", new BsonDocument("position", to)), cancellationToken: cancellationToken);
            if (moved.MatchedCount != 1)
            {
                await session.AbortTransactionAsync(cancellationToken);
                return false;
            }
            var models = new List<BulkWriteModel>();
            foreach (var view in rows.Where(r => r.Revision > 0).GroupBy(r => r.Instance))
                models.Add(new BulkWriteUpdateOneModel<BsonDocument>(_views.CollectionNamespace, new BsonDocument("_id", view.Key),
                    new BsonDocument { { "$set", new BsonDocument("partition", partition) }, { "$max", new BsonDocument("revision", view.Max(r => r.Revision)) }, { "$inc", new BsonDocument("commits", view.Count()) } }, isUpsert: true));
            foreach (var deleted in rows.Where(r => r.Revision < 0))
                models.Add(new BulkWriteDeleteOneModel<BsonDocument>(_views.CollectionNamespace, new BsonDocument("_id", deleted.Instance)));
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
        if (instances.Count == 0) return [];
        var docs = await _views.Find(new BsonDocument("_id", new BsonDocument("$in", new BsonArray(instances)))).ToListAsync(cancellationToken);
        return docs.Select(d => new ViewRow(d["_id"].AsString, d["revision"].AsInt64, d["commits"].AsInt32)).ToList();
    }

    // ---- documents ------------------------------------------------------------------------------------------------------

    private static string? Str(BsonValue value) => value.IsBsonNull ? null : value.AsString;

    private static BsonDocument ToDocument(InstanceRow row, int partition) => new()
    {
        { "_id", row.Id }, { "partition", partition }, { "model", row.Model }, { "definition", row.Definition }, { "status", (int)row.Status },
        { "state", row.State }, { "jobs", (BsonValue?)row.Jobs ?? BsonNull.Value }, { "revision", row.Revision },
        { "parent", (BsonValue?)row.Parent ?? BsonNull.Value }, { "fault", (BsonValue?)row.Fault ?? BsonNull.Value },
        { "history", (int)row.History }, { "suspended", row.Suspended }, { "children", (BsonValue?)row.Children ?? BsonNull.Value }, { "stateVersion", row.StateVersion },
    };

    private static BsonDocument ToDocument(Message m, long seq, long seen) => new()
    {
        { "_id", m.Id }, { "partition", m.Target.Partition }, { "seq", seq }, { "target", m.Target.Id }, { "kind", m.Kind },
        { "payload", (BsonValue?)m.Payload ?? BsonNull.Value }, { "sender", (BsonValue?)m.Sender ?? BsonNull.Value },
        { "start", m.Start is null ? BsonNull.Value : Json.Write(m.Start) }, { "due", m.Due ?? 0L }, { "seen", seen }, { "attempts", 0 }, { "trial", 0L },
        { "keepUntil", m.KeepUntil is { } keep ? keep : BsonNull.Value }, { "done", false }, { "replyTo", (BsonValue?)m.ReplyTo ?? BsonNull.Value },
    };

    private static Stored ToStored(BsonDocument d)
    {
        var due = d["due"].AsInt64;
        var message = new Message(d["_id"].AsString, new InstanceRef(d["target"].AsString), d["kind"].AsString, Str(d["payload"]), Str(d["sender"]),
            d["start"].IsBsonNull ? null : Json.Read<StartSpec>(d["start"].AsString), due == 0 ? null : due, d["keepUntil"].IsBsonNull ? null : d["keepUntil"].AsInt64,
            Str(d.GetValue("replyTo", BsonNull.Value)));
        return new Stored(message, d["seq"].AsInt64, d["seen"].AsInt64, d["attempts"].AsInt32, d["trial"].AsInt64);
    }

    private static BsonDocument SubscriptionId(SubscriptionRow s) => new() { { "k", s.Key }, { "i", s.Instance }, { "w", s.Wait } };

    private static BsonDocument ToDocument(SubscriptionRow s, int partition) => new()
    {
        { "_id", SubscriptionId(s) }, { "key", s.Key }, { "instance", s.Instance }, { "wait", s.Wait }, { "partition", partition },
        { "start", s.Start is null ? BsonNull.Value : Json.Write(s.Start) },
    };
}
