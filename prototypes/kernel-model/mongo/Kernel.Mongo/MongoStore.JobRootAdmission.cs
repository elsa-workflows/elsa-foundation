using System.Security.Cryptography;
using BoundScope = Kernel.Foundation.Contracts.BoundScope;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo;

public sealed partial class MongoStore
{
    internal sealed record JobRootReceipt(string Reference, bool Duplicate);
    private sealed record RootEnvelope(string Operation, StartSpec Spec);
    private static BsonDocument BirthId(string reference) => new("birth", reference);
    // Internal generated-root ingress for the staged profile. A receipt and birth bind the
    // tenant and operation to one physical creator. The creator payload has one durable copy.
    internal async Task<JobRootReceipt> AdmitJobRootAsync(BoundScope scope, string operation, StartSpec spec,
        TimeSpan deduplicationWindow, CancellationToken cancellationToken = default)
    {
        if (!_options.ScopedJobRoots) throw new NotSupportedException("Qualified job roots require the staged provider.");
        if (!FoundationMetadataLimits.IsOutsideMessageId(operation) ||
            spec is { Parent: not null } or { Creation: not null } or { FoundationInlineTenant: not null } ||
            spec.ParentCreation != 0 || spec.Ephemeral || spec.ShortWait.HasValue || spec.History != History.Full ||
            !FoundationMetadataLimits.FitsName(spec.Model) || !FoundationMetadataLimits.FitsContent(spec.Definition, FoundationMetadataLimits.TypeDataBytes))
            throw new ArgumentException("Unsupported generated job-root start.");
        FoundationMetadataLimits.RequireContent(spec.Input, nameof(spec), FoundationMetadataLimits.StartContentBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(deduplicationWindow, TimeSpan.Zero);
        var digest = FoundationRootCreator.OperationDigest(operation);
        var receiptId = new BsonDocument { { "rootOperation", digest }, { "tenant", scope.Tenant } };
        var keyFilter = new BsonDocument
        {
            { "_id", "partitions" }, { "storeSchema", _format.StoreSchema }, { "messageProtocol", _format.MessageProtocol },
            { "journalCodec", _format.JournalCodec }, { "rootIssuer", new BsonDocument("$exists", false) }
        };
        // The issuer is installed once per database and survives caller restarts.
        await _meta.UpdateOneAsync(keyFilter, Builders<BsonDocument>.Update.Set("rootIssuer", RandomNumberGenerator.GetBytes(32)), cancellationToken: cancellationToken);
        var encoded = DurableKeyJson.Write(new RootEnvelope(operation, spec));
        var name = scope.Tenant + "_" + Guid.NewGuid().ToString("N");
        using var session = await _client.StartSessionAsync(cancellationToken: cancellationToken);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await session.WithTransactionAsync(async (tx, token) =>
                {
                    var marker = await _meta.Find(tx, PartitionMarker).SingleOrDefaultAsync(token);
                    var count = RequireVersions(marker, _format);
                    var actor = InstanceRef.In(InstanceRef.Spread(name, count), name);
                    var reference = FoundationRootCreator.ReferenceFor(marker!["rootIssuer"].AsBsonBinaryData.Bytes, scope.Tenant, new(actor.Id, 1));
                    var creator = FoundationRootCreator.Create(actor, operation, digest, spec, null, null);
                    var id = new BsonDocument("_id", receiptId);
                    var receipt = await _meta.Find(tx, new BsonDocument(id) { { "$expr", new BsonDocument("$gte", new BsonArray { "$until", NowMs }) } }).FirstOrDefaultAsync(token);
                    if (receipt is not null) return new JobRootReceipt(receipt["reference"].AsString, true);
                    await _meta.UpdateOneAsync(tx, id, Builders<BsonDocument>.Update.Pipeline(new BsonDocument[]
                        { new("$set", new BsonDocument { { "reference", reference }, { "until", new BsonDocument("$add", new BsonArray { NowMs, (long)deduplicationWindow.TotalMilliseconds }) } }) }),
                        new UpdateOptions { IsUpsert = true }, token);
                    var binding = new BsonDocument
                    {
                        { "_id", BirthId(reference) }, { "tenant", scope.Tenant }, { "actor", actor.Id }, { "creation", 1L },
                        { "partition", actor.Partition }, { "creator", creator.InboxKey.Id }, { "digest", digest },
                        { "intent", CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload) }
                    };
                    await _meta.InsertOneAsync(tx, binding, cancellationToken: token);
                    // The ordinary poll and key-based changes address ordinary inbox rows only.
                    var sequence = await NextOutsideSeq(actor.Partition, token, tx);
                    var message = new BsonDocument
                    {
                        { "_id", InboxId(creator.InboxKey) }, { "partition", actor.Partition }, { "target", actor.Id }, { "rootRef", reference },
                        { "seq", sequence }, { "seen", 0L }, { "trial", 0L }, { "attempts", 0 }, { "due", 0L },
                        { "keepUntil", BsonNull.Value }, { "done", false }, { "kind", Kinds.Start }, { "rootEnvelope", encoded }, { "tenant", scope.Tenant }
                    };
                    if (message.ToBson().Length > 16 * 1024 * 1024)
                        throw new StoreException(Failure.Definite, "The encoded root creator exceeds the MongoDB document budget.");
                    await _messages.InsertOneAsync(tx, message, cancellationToken: token);
                    return new JobRootReceipt(reference, false);
                }, Transaction, cancellationToken);
            }
            catch (MongoWriteException error) when (attempt < 4 && error.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // Concurrent inserts of a missing receipt may report a duplicate instead of a
                // transient transaction label. A fresh snapshot resolves the winning binding.
            }
        }
    }

    internal async Task<Message?> PendingJobRootAsync(BoundScope scope, string reference, CancellationToken token = default)
    {
        if (!_options.ScopedJobRoots) throw new NotSupportedException("Qualified job roots require the staged provider.");
        await RequireVersionsAsync(token);
        var birth = await _meta.Find(new BsonDocument { { "_id", BirthId(reference) }, { "tenant", scope.Tenant } }).FirstOrDefaultAsync(token);
        if (birth is null) return null;
        var id = new InboxKey(birth["creator"].AsString);
        var row = await _messages.Find(new BsonDocument { { "_id", InboxId(id) }, { "rootRef", reference }, { "tenant", scope.Tenant },
            { "target", birth["actor"] }, { "kind", Kinds.Start }, { "done", false } }).FirstOrDefaultAsync(token);
        if (row is null) return null;
        var body = DurableKeyJson.Read<RootEnvelope>(row["rootEnvelope"].AsString);
        return FoundationRootCreator.Create(new(birth["actor"].AsString), body.Operation, birth["digest"].AsString, body.Spec, null, null);
    }
}
