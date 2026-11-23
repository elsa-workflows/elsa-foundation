using Contract = Kernel.Foundation.Contracts;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo;

public sealed partial class MongoStore
{
    private sealed record RootSuspendWrite(string Tenant, string Reference, Message Occurrence, long Revision,
        FoundationHeaderChange Header, Func<Contract.ActorHeader, bool>? Withholds);

    // Compose the public actor's header from an authoritative read. The transaction
    // compares the occurrence, creation and revision again before publishing it.
    internal async ValueTask<Committed> ApplyJobRootControlAsync(Contract.BoundScope scope, string reference, InboxKey selected,
        long expected, PartitionFence fence, TimeSpan lease,
        Func<Contract.ActorHeader, InstanceRow, Message, CancellationToken, ValueTask<FoundationHeaderChange>> receive,
        Func<Contract.ActorHeader, bool>? withholds = null, CancellationToken cancellationToken = default)
    {
        if (!_options.ApplyJobSuspend) throw new NotSupportedException("Suspension application requires its checkpoint format.");
        var message = await ReadDueRootControlAsync(scope, reference, cancellationToken, withholds);
        if (message is null || message.InboxKey != selected || message.Target.Partition != fence.Partition)
            return new(false, 0, []);
        var checkpoint = await ReadJobRootCheckpointAsync(scope, reference, cancellationToken);
        if (checkpoint is null || checkpoint.Image.Revision != expected || checkpoint.Image.Creation != message.TargetCreation ||
            checkpoint.Image.Actor != message.Target.Id) return new(false, 0, []);
        var image = checkpoint.Image;
        var row = new InstanceRow(image.Actor, image.Model, image.Definition, InstanceStatus.Running, image.State ?? "null",
            expected, null, Jobs: image.Jobs, Suspended: checkpoint.Suspended, StateVersion: image.StateVersion,
            Creation: image.Creation, StartIntent: image.StartIntent)
            { FoundationNullState = image.State is null, NextJobGeneration = image.NextJobGeneration };
        var header = PublicRootHeader(scope.Tenant, reference, image);
        var change = await receive(header, row, message, cancellationToken);
        if (!FoundationMetadataLimits.FitsContent(change.TypeData, FoundationMetadataLimits.TypeDataBytes) ||
            change.Labels.Count > 4 || change.Labels.Any(p => !FoundationMetadataLimits.FitsName(p.Key, true) || !FoundationMetadataLimits.FitsName(p.Value, true)))
            throw new StoreException(Failure.Definite, "The suspension header exceeds the checkpoint bounds.");
        var write = new RootSuspendWrite(scope.Tenant, reference, message, expected,
            new(change.TypeData, new Dictionary<string, string>(change.Labels, StringComparer.Ordinal)), withholds);
        return await CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, lease, false, [], [], []),
            null, null, cancellationToken, write);
    }

    private async Task<bool> StageRootSuspensionWritesAsync(IClientSessionHandle tx, RootSuspendWrite write,
        List<BulkWriteModel> models, List<Check> checks, CancellationToken token)
    {
        var next = await NextRootControlAsync(tx, write.Tenant, write.Reference, token, write.Withholds);
        if (next != write.Occurrence) return false;
        var match = new BsonDocument { { "_id", next.Target.Id }, { "partition", next.Target.Partition },
            { "tenant", write.Tenant }, { "rootRef", write.Reference }, { "revision", write.Revision } };
        var stored = await _instances.Find(tx, match).FirstOrDefaultAsync(token);
        if (stored is null) return false;
        var image = DurableKeyJson.Read<JobRootImage>(stored["rootImage"].AsString);
        if (image.Actor != next.Target.Id || image.Creation != next.TargetCreation || image.Revision != write.Revision) return false;
        var changed = image with { Revision = checked(image.Revision + 1), TypeData = write.Header.TypeData,
            Labels = new(write.Header.Labels, StringComparer.Ordinal) };
        var replacement = new BsonDocument(stored)
        {
            ["revision"] = changed.Revision, ["rootImage"] = DurableKeyJson.Write(changed), ["suspended"] = next.Kind == Kinds.Suspend,
            ["journalRevision"] = stored.GetValue("journalRevision", write.Revision)
        };
        if (replacement.ToBson().Length > 16 * 1024 * 1024)
            throw new StoreException(Failure.Definite, "The suspension checkpoint exceeds the MongoDB document budget.");
        checks.Add(new(models.Count, false, 1, CheckKind.Count));
        models.Add(new BulkWriteReplaceOneModel<BsonDocument>(_instances.CollectionNamespace, match, replacement));
        var command = new BsonDocument { { "_id", InboxId(next.InboxKey) }, { "tenant", write.Tenant },
            { "rootRef", write.Reference }, { "targetCreation", image.Creation }, { "done", false } };
        checks.Add(new(models.Count, false, 1, CheckKind.Count));
        models.Add(new BulkWriteUpdateOneModel<BsonDocument>(_messages.CollectionNamespace, command,
            Builders<BsonDocument>.Update.Set("done", true)));
        return true;
    }
}
