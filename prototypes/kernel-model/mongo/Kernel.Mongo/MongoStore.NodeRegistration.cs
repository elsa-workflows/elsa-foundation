using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo;

public sealed partial class MongoStore
{
    private static readonly BsonDocument CurrentGrant = new()
    {
        { "storeSchema", StoreSchema }, { "messageProtocol", MessageProtocol }, { "journalCodec", JournalCodec }
    };

    // The recurring heartbeat needs only this node document. The first registration, an expired
    // lease, or a revoked grant must contend with a format advance before renewing ownership.
    private async Task<BsonDocument> RenewRegisteredNodeAsync(IClientSessionHandle? session, string node,
        TimeSpan? lease, CancellationToken token)
    {
        var active = new BsonDocument { { "_id", node }, { "formatGrant", CurrentGrant } };
        if (lease.HasValue)
            active.Add("$expr", new BsonDocument("$gte", new BsonArray { "$leaseUntil", NowMs }));
        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            ReturnDocument = ReturnDocument.After, MaxTime = lease / 3
        };
        var change = NodeRenewal(lease);
        var renewed = session is null
            ? await _nodes.FindOneAndUpdateAsync(active, change, options, token)
            : await _nodes.FindOneAndUpdateAsync(session, active, change, options, token);
        if (renewed is not null) return renewed;

        return session is null ? await RetriedAsync(Register, token) : await Register(session);

        async Task<BsonDocument> Register(IClientSessionHandle registration)
        {
            // Flip a bit so even registrations of different new names write the migration marker.
            // A snapshot read or an update that matched without changing it provides no write conflict.
            // Take and retake validated their existing transaction snapshot on entry. A cold
            // standalone heartbeat checks its snapshot before interpreting any marker fields.
            if (session is null) await RequireVersionsAsync(registration, token);
            var changed = await _meta.UpdateOneAsync(registration, PartitionMarker,
                new BsonDocument("$bit", new BsonDocument("nodeRegistration", new BsonDocument("xor", 1L))),
                cancellationToken: token);
            if (changed.ModifiedCount != 1)
                throw new StoreFormatException("The kernel store version marker did not register the node.");
            options.IsUpsert = true;
            return await _nodes.FindOneAndUpdateAsync(registration, new BsonDocument("_id", node),
                NodeRenewal(lease, register: true), options, token);
        }
    }
}
