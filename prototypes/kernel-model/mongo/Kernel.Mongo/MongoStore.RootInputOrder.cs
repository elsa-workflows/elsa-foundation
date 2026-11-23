using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo;

public sealed partial class MongoStore
{
    // Outside controls and owner-produced job results use different sequence clocks.
    // Touching the same birth in both insert transactions serializes their root-local order.
    private async Task<long> NextRootInputOrderAsync(IClientSessionHandle tx, string tenant, string reference, Message message, CancellationToken token)
    {
        var match = new BsonDocument { { "_id", BirthId(reference) }, { "tenant", tenant }, { "actor", message.Target.Id },
            { "creation", message.TargetCreation!.Value }, { "inputOrder", new BsonDocument("$not", new BsonDocument("$gte", long.MaxValue)) } };
        var updated = await _meta.FindOneAndUpdateAsync(tx, match, Builders<BsonDocument>.Update.Inc("inputOrder", 1L),
            new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After, Projection = new BsonDocument("inputOrder", 1) }, token);
        if (updated is null || updated["inputOrder"] is not BsonInt64 { Value: > 0 } order)
            throw new StoreException(Failure.Definite, "The scoped birth cannot assign a delivery order.");
        return order.Value;
    }

    private async Task CheckLegacyInputOrderAsync(IClientSessionHandle tx, CancellationToken token)
    {
        // One legacy family on a root is an ordered prefix. A mixed prefix cannot be
        // ordered from its physical sequence values. Check inside the marker transaction.
        BsonDocument[] pipeline =
        [
            new("$match", new BsonDocument { { "rootRef", new BsonDocument("$exists", true) }, { "done", false },
                { "kind", new BsonDocument("$in", new BsonArray { Kinds.Suspend, Kinds.JobFinished }) } }),
            new("$group", new BsonDocument { { "_id", new BsonDocument { { "tenant", "$tenant" }, { "root", "$rootRef" } } },
                { "kinds", new BsonDocument("$addToSet", "$kind") } }),
            new("$match", new BsonDocument("kinds", new BsonDocument("$size", 2))),
            new("$limit", 1)
        ];
        if (await _messages.Aggregate<BsonDocument>(tx, pipeline).AnyAsync(token))
            throw new StoreFormatException("A root has pending legacy suspensions and results. Drain its suspensions before upgrading.");
    }
}
