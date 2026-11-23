using Contract = Kernel.Foundation.Contracts;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo;

public sealed partial class MongoStore
{
    internal Contract.IIngress JobRootSuspensions(Func<Contract.BoundScope, string, bool> authorize, TimeSpan deduplication)
    {
        if (!_options.ScopedJobSuspend) throw new NotSupportedException("Suspension admission requires the staged operator protocol.");
        ArgumentNullException.ThrowIfNull(authorize);
        ArgumentOutOfRangeException.ThrowIfLessThan(deduplication, TimeSpan.Zero);
        return new SuspendIngress(this, authorize, deduplication);
    }

    private sealed class SuspendIngress(MongoStore store, Func<Contract.BoundScope, string, bool> authorize, TimeSpan window) : Contract.IIngress
    {
        public ValueTask<Contract.Admission> SendAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
            string? payload = null, Contract.AddOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scope);
            cancellationToken.ThrowIfCancellationRequested();
            if (kind != Contract.Kinds.Suspend || target is not Contract.Target.Ref reference || !authorize(scope, kind) ||
                !FoundationMetadataLimits.IsOutsideMessageId(id) || payload is not null)
                return new(new Contract.Admission(Contract.AdmissionOutcome.Refused));
            var keep = options?.KeepFor is { } requested && requested < window ? requested : window;
            ArgumentOutOfRangeException.ThrowIfLessThan(keep, TimeSpan.Zero);
            return new(store.AdmitSuspendAsync(scope.Tenant, reference.Actor.Id, id, options?.DueAt, keep, cancellationToken));
        }

        public ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind, string? payload,
            Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This staged ingress supports one-way reference suspension only.");

        public ValueTask SetStartAsync(Contract.BoundScope scope, string key, string wait, Contract.StartSpec? spec, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This staged ingress supports one-way reference suspension only.");
    }

    private async Task<Contract.Admission> AdmitSuspendAsync(string tenant, string reference, string id, long? due,
        TimeSpan keep, CancellationToken cancellationToken)
    {
        var operation = FoundationIngressAddress.Operation(tenant, id);
        var receipt = new BsonDocument("_id", new BsonDocument("operatorOperation", operation));
        using var session = await _client.StartSessionAsync(cancellationToken: cancellationToken);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await session.WithTransactionAsync(async (tx, token) =>
                {
                    await RequireVersionsAsync(tx, token);
                    var birth = await _meta.Find(tx, new BsonDocument { { "_id", BirthId(reference) }, { "tenant", tenant } }).FirstOrDefaultAsync(token);
                    if (birth is null) return new Contract.Admission(Contract.AdmissionOutcome.Refused);
                    var prior = await _meta.Find(tx, new BsonDocument(receipt)
                        { { "$expr", new BsonDocument("$gte", new BsonArray { "$until", NowMs }) } }).FirstOrDefaultAsync(token);
                    if (prior is not null)
                        return new Contract.Admission(Contract.AdmissionOutcome.Duplicate, new Contract.ActorRef(prior["reference"].AsString));
                    var actor = new InstanceRef(birth["actor"].AsString);
                    var creation = birth["creation"].AsInt64;
                    var exact = new BsonDocument { { "tenant", tenant }, { "rootRef", reference } };
                    var live = await _instances.Find(tx, new BsonDocument(exact) { { "_id", actor.Id } }).AnyAsync(token);
                    if (!live && !await _messages.Find(tx, new BsonDocument(exact)
                        { { "_id", InboxId(new(birth["creator"].AsString)) }, { "target", actor.Id }, { "kind", Kinds.Start }, { "done", false } }).AnyAsync(token))
                        return new Contract.Admission(Contract.AdmissionOutcome.Refused);
                    var seq = await NextOutsideSeq(actor.Partition, token, tx);
                    var saved = await _meta.FindOneAndUpdateAsync(tx, receipt, Builders<BsonDocument>.Update.Pipeline(new BsonDocument[]
                        { new("$set", new BsonDocument { { "reference", reference }, { "until", new BsonDocument("$add", new BsonArray { NowMs, (long)keep.TotalMilliseconds }) } }) }),
                        new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, token);
                    // Outside sequence clocks are per partition. Include the partition so an expired
                    // operation can be reused against another root while its earlier command is pending.
                    var occurrence = FormattableString.Invariant($"{actor.Partition}:{seq}");
                    var message = FoundationIngressAddress.Occurrence(id, "operator/" + operation, occurrence, actor, creation,
                        Kinds.Suspend, null, due, saved["until"].AsInt64, null, new(null));
                    var doc = ToDocument(message, seq, 0).Add("rootRef", reference).Add("tenant", tenant)
                        .Add("targetCreation", creation).Add("operatorId", id);
                    if (due.HasValue) doc.Add("dueSpecified", true);
                    await _messages.InsertOneAsync(tx, doc, cancellationToken: token);
                    return new Contract.Admission(Contract.AdmissionOutcome.Accepted, new Contract.ActorRef(reference));
                }, Transaction, cancellationToken);
            }
            catch (MongoWriteException e) when (attempt < 4 && e.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // Concurrent first admissions of one operation can collide before a snapshot sees its receipt.
            }
        }
    }

    // A scoped read for the owner. Its result is a candidate: any applying turn must
    // recheck this occurrence and the checkpoint at the partition fence.
    internal async Task<Message?> ReadDueRootSuspendAsync(Contract.BoundScope scope, string reference, CancellationToken cancellationToken = default)
    {
        if (!_options.ScopedJobSuspend) throw new NotSupportedException("Suspension reads require the staged operator protocol.");
        using var session = await _client.StartSessionAsync(cancellationToken: cancellationToken);
        return await session.WithTransactionAsync(async (tx, token) =>
        {
            await RequireVersionsAsync(tx, token);
            return await NextRootSuspensionAsync(tx, scope.Tenant, reference, token);
        }, Transaction, cancellationToken);
    }

    private async Task<Message?> NextRootSuspensionAsync(IClientSessionHandle tx, string tenant, string reference, CancellationToken token)
    {
        var birth = await _meta.Find(tx, new BsonDocument { { "_id", BirthId(reference) }, { "tenant", tenant } }).FirstOrDefaultAsync(token);
        if (birth is null) return null;
        var doc = await _messages.Find(tx, new BsonDocument
        {
            { "tenant", tenant }, { "rootRef", reference }, { "target", birth["actor"] }, { "targetCreation", birth["creation"] },
            { "partition", birth["partition"] }, { "kind", Kinds.Suspend }, { "done", false }, { "operatorId", new BsonDocument("$exists", true) },
            { "$expr", new BsonDocument("$lte", new BsonArray { "$due", NowMs }) }
        }).Sort(new BsonDocument("seq", 1)).FirstOrDefaultAsync(token);
        if (doc is null) return null;
        bool Empty(string field) => doc.TryGetValue(field, out var value) && value.IsBsonNull;
        var hasDue = doc.TryGetValue("dueSpecified", out var specified);
        if (!Empty("payload") || !Empty("sender") || !Empty("replyTo") || !Empty("start") ||
            doc.Contains("senderCreation") || doc.Contains("jobGeneration") || doc.Contains("resultOf") || doc.Contains("rootEnvelope") ||
            !doc["operatorId"].IsString || !FoundationMetadataLimits.IsOutsideMessageId(doc["operatorId"].AsString) ||
            !doc.TryGetValue("due", out var due) || !due.IsInt64 || (hasDue ? !specified.Equals(BsonBoolean.True) : due.AsInt64 != 0) ||
            !doc.TryGetValue("keepUntil", out var keep) || !keep.IsInt64)
            throw new StoreException(Failure.Definite, "The stored suspension envelope is inconsistent with one-way outside admission.");
        var operation = doc["operatorId"].AsString;
        var actor = new InstanceRef(birth["actor"].AsString);
        var seq = doc["seq"].AsInt64;
        var command = FoundationIngressAddress.Occurrence(operation, "operator/" + FoundationIngressAddress.Operation(tenant, operation),
            FormattableString.Invariant($"{actor.Partition}:{seq}"), actor, birth["creation"].AsInt64,
            Kinds.Suspend, null, hasDue ? due.AsInt64 : null, keep.AsInt64, null, new(null));
        if (!doc["_id"].Equals(InboxId(command.InboxKey)))
            throw new StoreException(Failure.Definite, "The stored suspension occurrence conflicts with its bound operation.");
        return command;
    }
}
