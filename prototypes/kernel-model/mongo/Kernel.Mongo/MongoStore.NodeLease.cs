using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo;

/// <summary>
/// K1b, the node lease on MongoDB (ownership-and-fencing.md, "The partition record" and "The node's heartbeat"; the
/// storage model's <c>nodes.leaseUntil</c> and <c>partitions.fenced</c>).
/// </summary>
public sealed partial class MongoStore
{

    // Even a batch with no journal rows must write every fenced document to conflict with a concurrent take.
    private static BsonElement FenceWrite() =>
        new("fenced", new BsonDocument("$add", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$fenced", 0L }), 1L }));

    /// <summary>A heartbeat records the issuing binary's readable kernel formats alongside primary time.
    /// A membership-only heartbeat keeps the existing lease unchanged.</summary>
    private UpdateDefinition<BsonDocument> NodeRenewal(TimeSpan? lease, bool register = false)
    {
        var fields = new BsonDocument
        {
            { "seenAt", NowMs },
            { "readableStoreSchema", new BsonArray { _format.StoreSchema } },
            { "readableMessageProtocol", new BsonArray { _format.MessageProtocol } },
            { "readableJournalCodec", new BsonArray { _format.JournalCodec } }
        };
        if (register) fields.Add("formatGrant", _grant);
        if (lease is { } duration)
            fields.Add("leaseUntil", new BsonDocument("$add", new BsonArray { NowMs, (long)duration.TotalMilliseconds }));
        return Builders<BsonDocument>.Update.Pipeline(new BsonDocument[] { new("$set", fields) });
    }

    /// <summary>
    /// A transaction that a write conflict with this process's own heartbeat (a single-document write to its node document)
    /// can abort: tried again while the driver labels the failure transient, a few times. Under the node lease a conflict
    /// still failing after the last attempt is a take that did not land: a transient failure, taken again at the next round.
    /// </summary>
    private async Task<T> RetriedAsync<T>(Func<IClientSessionHandle, Task<T>> body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var session = await _client.StartSessionAsync(cancellationToken: cancellationToken);
            session.StartTransaction(Transaction);
            try
            {
                var result = await body(session);
                await session.CommitTransactionAsync(cancellationToken);
                return result;
            }
            catch (MongoException e) when (e.HasErrorLabel("TransientTransactionError") && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= 5)
                    throw new StoreException(Failure.Transient, $"The node transaction still conflicted after {attempt} attempts.", e);
                await Task.Delay(Random.Shared.Next(5, 30), cancellationToken);
            }
        }
    }

    /// <summary>
    /// The take under the node lease, one transaction. 1: the owners of the set other than this node, then their node
    /// documents (a missing document counts as expired); 2: this node's own document; both under one deadline of a third
    /// of the lease (maxTimeMS on the reads, and the client's cancel). 3: one updateMany takes each document that is free,
    /// this node's, or owned by an expired owner, in ascending id; then the deaths, as before.
    /// </summary>
    private async Task<OwnedLease> NodeTakeAsync(IReadOnlyList<int> set, string node, TimeSpan lease, CancellationToken cancellationToken)
    {
        // The clocks are recorded once the transaction has committed: a retried attempt's documents are nobody's.
        var (taken, clocks) = await RetriedAsync(session => NodeTakeOnceAsync(session, set, node, lease, cancellationToken), cancellationToken);
        foreach (var (partition, epoch, clock) in clocks) Known(partition, epoch, clock);
        return taken;
    }

    private async Task<(OwnedLease, List<(int, long, long)>)> NodeTakeOnceAsync(IClientSessionHandle session, IReadOnlyList<int> set, string node, TimeSpan lease, CancellationToken cancellationToken)
    {
        var ids = new BsonDocument("$in", new BsonArray(set));
        var expired = new BsonArray();
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(lease / 3);
            var bounded = new FindOptions { MaxTime = lease / 3 };
            try
            {
                await RequireVersionsAsync(session, deadline.Token);
                var owners = (await _partitions.Find(session, new BsonDocument { { "_id", ids }, { "owner", new BsonDocument("$nin", new BsonArray { BsonNull.Value, node }) } }, bounded)
                    .Project(new BsonDocument("owner", 1)).ToListAsync(deadline.Token)).Select(d => d["owner"].AsString).Distinct(StringComparer.Ordinal).ToList();
                if (owners.Count > 0)
                {
                    // Read the primary's current leases. The transaction snapshot was opened by the version read;
                    // comparing a lease from that older snapshot to a later clock could evict a continuously live owner.
                    // The partition update still conflicts with a change of owner after the snapshot.
                    var live = (await _nodes.WithReadConcern(ReadConcern.Local).Find(new BsonDocument { { "_id", new BsonDocument("$in", new BsonArray(owners)) }, { "$expr", new BsonDocument("$gte", new BsonArray { "$leaseUntil", NowMs }) } }, bounded)
                        .Project(new BsonDocument("_id", 1)).ToListAsync(deadline.Token)).Select(d => d["_id"].AsString).ToHashSet(StringComparer.Ordinal);
                    foreach (var owner in owners.Where(o => !live.Contains(o))) expired.Add(owner);
                }
                await RenewRegisteredNodeAsync(session, node, lease, deadline.Token);
            }
            catch (Exception e) when (Deadline(e, deadline, cancellationToken))
            {
                throw new StoreException(Failure.Transient, "The take's read of the node documents and its own document's write outlasted a third of the lease.", e);
            }
        }
        var takeable = new BsonDocument
        {
            { "_id", ids },
            { "$or", new BsonArray { new BsonDocument("owner", BsonNull.Value), new BsonDocument("owner", node), new BsonDocument("owner", new BsonDocument("$in", expired)) } },
        };
        await _partitions.UpdateManyAsync(session, takeable, Builders<BsonDocument>.Update.Pipeline(new BsonDocument[] { new("$set", TakeSet(node)) }), cancellationToken: cancellationToken);
        var docs = await _partitions.Find(session, new BsonDocument { { "_id", ids }, { "owner", node } }).Sort(new BsonDocument("_id", 1)).ToListAsync(cancellationToken);
        if (docs.Where(d => d["died"].AsBoolean).Select(d => new BsonDocument { { "partition", d["_id"] }, { "trial", d["epoch"].AsInt64 - 1 }, { "done", false } }).ToList() is { Count: > 0 } dead)
            await _messages.UpdateManyAsync(session, new BsonDocument("$or", new BsonArray(dead)), new BsonDocument("$inc", new BsonDocument("attempts", 1)), cancellationToken: cancellationToken);
        return (new OwnedLease([.. docs.Select(d => new PartitionFence(d["_id"].AsInt32, d["epoch"].AsInt64, d["position"].AsInt64))], docs.Count > 0 ? docs[0]["now"].AsInt64 : 0),
            [.. docs.Select(d => (d["_id"].AsInt32, d["epoch"].AsInt64, d["clock"].AsInt64))]);
    }

    // A bound of a third of the lease ran out: the client's cancel of the deadline, or the server's maxTimeMS (MaxTimeMSExpired).
    private static bool Deadline(Exception e, CancellationTokenSource? deadline, CancellationToken caller) =>
        !caller.IsCancellationRequested && (deadline?.IsCancellationRequested == true || e is MongoExecutionTimeoutException);

    /// <summary>The take's $set records a death, the new owner and epoch, and the primary's time.</summary>
    private static BsonDocument TakeSet(string node) => new()
    {
        { "died", new BsonDocument("$and", new BsonArray { new BsonDocument("$ne", new BsonArray { "$owner", BsonNull.Value }), new BsonDocument("$ne", new BsonArray { "$owner", new BsonDocument("$literal", node) }) }) },
        { "owner", new BsonDocument("$literal", node) },
        { "epoch", new BsonDocument("$add", new BsonArray { "$epoch", 1L }) },
        { "now", NowMs },
    };

    /// <summary>The retake, counting a death on the previous epoch's unfinished trial messages when asked (#4837, a pending partition).</summary>
    public ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, bool countDeaths, CancellationToken cancellationToken = default) =>
        new(RetakeInTransactionAsync(partition, node, epoch, lease, countDeaths, cancellationToken));

    /// <summary>
    /// The retake in one session transaction: under the node lease this node's document first, also when the retake is
    /// refused; then findOneAndUpdate on owner and epoch; with <paramref name="countDeaths"/> the attempts updateMany.
    /// </summary>
    private async Task<Lease?> RetakeInTransactionAsync(int partition, string node, long epoch, TimeSpan lease, bool countDeaths, CancellationToken cancellationToken)
    {
        var set = new BsonDocument { { "epoch", new BsonDocument("$add", new BsonArray { "$epoch", 1L }) }, { "now", NowMs } };
        var doc = await RetriedAsync(async session =>
        {
            await RequireVersionsAsync(session, cancellationToken);
            await RenewRegisteredNodeAsync(session, node, lease, cancellationToken);
            var taken = await _partitions.FindOneAndUpdateAsync(session, new BsonDocument { { "_id", partition }, { "owner", node }, { "epoch", epoch } },
                Builders<BsonDocument>.Update.Pipeline(new BsonDocument[] { new("$set", set) }), new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After }, cancellationToken);
            if (taken is not null && countDeaths)
                await _messages.UpdateManyAsync(session, new BsonDocument { { "partition", partition }, { "trial", epoch }, { "done", false } }, new BsonDocument("$inc", new BsonDocument("attempts", 1)), cancellationToken: cancellationToken);
            return taken;
        }, cancellationToken);
        if (doc is null) return null;
        Known(partition, doc["epoch"].AsInt64, doc["clock"].AsInt64);
        return new Lease(doc["epoch"].AsInt64, doc["position"].AsInt64, doc["now"].AsInt64);
    }

    /// <summary>
    /// A recurring heartbeat renews its live registered node with one document write. Cold registration and
    /// an expired lease take the registration transaction. The renewal has a deadline of a third of the lease;
    /// the existing reads of owned partitions and membership follow it.
    /// </summary>
    public async ValueTask<NodeBeat> NodeHeartbeatAsync(string node, TimeSpan lease, TimeSpan window, CancellationToken cancellationToken = default)
    {
        BsonDocument beat;
        List<BsonDocument> owned;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(lease / 3);
            try { beat = await RenewRegisteredNodeAsync(null, node, lease, deadline.Token); }
            catch (OperationCanceledException e) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new StoreException(Failure.Unknown, "The heartbeat outlasted a third of the lease.", e);
            }
            owned = await _partitions.Find(new BsonDocument("owner", node), new FindOptions { MaxTime = lease / 3 }).Project(new BsonDocument("epoch", 1)).Sort(new BsonDocument("_id", 1)).ToListAsync(cancellationToken);
        }
        catch (Exception e) when (Deadline(e, null, cancellationToken))
        {
            throw new StoreException(Failure.Unknown, "The heartbeat outlasted a third of the lease.", e);
        }
        var now = beat["seenAt"].AsInt64;
        return new NodeBeat([.. owned.Select(d => new OwnedEpoch(d["_id"].AsInt32, d["epoch"].AsInt64))], now, await MembershipAsync(now, window, cancellationToken));
    }

    /// <summary>
    /// The nodes heard within the window and partitions that are unowned or whose owner has no live node record.
    /// </summary>
    private async Task<Cluster> MembershipAsync(long now, TimeSpan window, CancellationToken cancellationToken)
    {
        var nodes = await _nodes.CountDocumentsAsync(new BsonDocument("seenAt", new BsonDocument("$gte", now - (long)window.TotalMilliseconds)), cancellationToken: cancellationToken);
        var live = (await _nodes.Find(new BsonDocument("leaseUntil", new BsonDocument("$gte", now))).Project(new BsonDocument("_id", 1)).ToListAsync(cancellationToken)).Select(d => d["_id"].AsString).ToHashSet(StringComparer.Ordinal);
        List<int> free = [.. (await _partitions.Find(FilterDefinition<BsonDocument>.Empty).Project(new BsonDocument("owner", 1)).ToListAsync(cancellationToken))
            .Where(d => d["owner"].IsBsonNull || !live.Contains(d["owner"].AsString)).Select(d => d["_id"].AsInt32).Order()];
        return new Cluster((int)nodes, free, now);
    }
}
