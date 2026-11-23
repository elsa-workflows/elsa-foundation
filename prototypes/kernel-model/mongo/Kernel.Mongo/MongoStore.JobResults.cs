using BoundScope = Kernel.Foundation.Contracts.BoundScope;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo;

public sealed partial class MongoStore
{
    private sealed record JobResultWrite(string Tenant, string Reference, long Revision, string Key, InboxKey Ticket, Message Result);

    // A withheld actor cannot take a callback in a model turn. This staged shape covers
    // durable suspension. Replacing the ticket leaves its registry entry and journal intact.
    internal ValueTask<Committed> SettleSuspendedJobAsync(BoundScope scope, string reference, long revision, Message result,
        PartitionFence fence, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        if (!_options.ScopedJobResults) throw new NotSupportedException("Job settlement requires the staged result format.");
        if (result is not { HasIssuedTicket: true, FoundationJobResult: true, FoundationHeldJobResult: false, Kind: Kinds.JobFinished,
                SenderCreation: > 0, Payload: not null } || result.TargetCreation != result.SenderCreation ||
            result.Sender != result.Target.Id || result.Target.Partition != fence.Partition || revision < 1 ||
            !FoundationMetadataLimits.FitsComposed(result))
            throw new StoreException(Failure.Definite, "A suspended job settlement needs an issued completion from its actor.");
        var key = result.Read<JobFinished>().Key;
        var creation = result.SenderCreation;
        if (result.InboxKey != InboxKey.JobResult(result.Target.Id, key, creation, result.JobGeneration))
            throw new StoreException(Failure.Definite, "The result key must derive from the completed ticket.");
        var ticket = InboxKey.Ticket(result.Target.Id, key, creation, result.JobGeneration);
        var settlement = new JobResultWrite(scope.Tenant, reference, revision, key, ticket, result);
        var turn = new TurnCommit(result.Target.Id, null, null, [ticket], [], [result], [], [], null);
        return CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, lease, false, [turn], [], []), null, settlement, cancellationToken);
    }

    private static BsonDocument JobTicketFilter(JobResultWrite write)
    {
        var result = write.Result;
        return new BsonDocument
        {
            { "_id", InboxId(write.Ticket) }, { "rootRef", write.Reference }, { "tenant", write.Tenant },
            { "partition", result.Target.Partition }, { "target", result.Target.Id }, { "sender", result.Target.Id },
            { "targetCreation", result.SenderCreation }, { "senderCreation", result.SenderCreation }, { "kind", Kinds.Job }, { "done", false },
            { "jobGeneration", result.JobGeneration == 0 ? new BsonDocument("$in", new BsonArray { BsonNull.Value, 0L }) : (BsonValue)result.JobGeneration }
        };
    }

    private async Task<bool> CanSettleJobAsync(IClientSessionHandle session, JobResultWrite write, CancellationToken token)
    {
        var message = write.Result;
        var birth = await _meta.Find(session, new BsonDocument { { "_id", BirthId(write.Reference) }, { "tenant", write.Tenant },
            { "actor", message.Target.Id }, { "creation", message.SenderCreation }, { "partition", message.Target.Partition } }).FirstOrDefaultAsync(token);
        if (birth is null) return false;
        var row = await _instances.Find(session, new BsonDocument { { "_id", message.Target.Id }, { "tenant", write.Tenant },
            { "rootRef", write.Reference }, { "revision", write.Revision }, { "suspended", true } }).FirstOrDefaultAsync(token);
        if (row is null) return false;
        var image = DurableKeyJson.Read<JobRootImage>(row["rootImage"].AsString);
        if (image.Actor != message.Target.Id || image.Creation != message.SenderCreation || image.Revision != write.Revision ||
            image.StartIntent != birth["intent"].AsString || !JobRegistry.Read(image.Jobs).Contains(new(write.Key, message.JobGeneration))) return false;
        return await _messages.Find(session, JobTicketFilter(write)).AnyAsync(token);
    }
}
