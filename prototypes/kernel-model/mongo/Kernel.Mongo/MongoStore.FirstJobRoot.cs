using System.Text.Json;
using BoundScope = Kernel.Foundation.Contracts.BoundScope;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo;

public sealed partial class MongoStore
{
    internal sealed record JobRootImage(string Actor, long Creation, long Revision, string Model, string Definition,
        string? State, int StateVersion, string Jobs, long NextJobGeneration, string StartIntent, string TypeData, Dictionary<string, string> Labels);
    internal sealed record JobRootCheckpoint(JobRootImage Image, IReadOnlyList<Message> Tickets, JournalRow Journal)
    {
        internal IReadOnlyList<Message> Results { get; init; } = [];
        internal bool Suspended { get; init; }
    }
    private sealed record FirstJobWrite(string Tenant, string Reference, Message Creator, string Envelope, string Image);

    // Closed first-turn shape. The ordinary writer continues to refuse qualified writes.
    internal ValueTask<Committed> CommitFirstJobRootAsync(BoundScope scope, string reference, Message creator, PartitionFence fence,
        TimeSpan lease, TurnCommit turn, CancellationToken cancellationToken = default)
    {
        if (!_options.ScopedJobRoots) throw new NotSupportedException("Qualified job roots require the staged provider.");
        if (creator.Start is not { Creation: { Generation: 1, Previous: 0 }, Parent: null } start ||
            creator.Target.Partition != fence.Partition || turn.Row is not { } row || turn.FoundationHeader is not { } header ||
            turn.Row.Id != creator.Target.Id || turn.Instance != row.Id || turn.Expected is not null || turn.Consumed is not [var consumed] ||
            consumed != creator.InboxKey || turn.Done.Count != 0 || turn.Sent.Count is 0 or > FoundationMetadataLimits.MaxTurnSends || turn.Journal is null ||
            turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 } ||
            turn != new TurnCommit(row.Id, row, null, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed, turn.Unsubscribed,
                turn.Journal, Held: turn.Held) { FoundationHeader = header })
            throw new StoreException(Failure.Definite, "Unsupported first job-root turn or effects.");

        try
        {
            using var entriesJson = JsonDocument.Parse(turn.Journal);
            if (entriesJson.RootElement.ValueKind != JsonValueKind.Array)
                throw new StoreException(Failure.Definite, "First job-root journal entries must form an array.");
        }
        catch (JsonException)
        {
            throw new StoreException(Failure.Definite, "First job-root journal entries must be valid JSON.");
        }
        var spec = start with { Creation = null };
        var digest = FoundationRootCreator.OperationDigest(creator.Id);
        var canonical = FoundationRootCreator.Create(creator.Target, creator.Id, digest, spec, null, null);
        if (canonical != creator || start.Ephemeral || start.History != History.Full || row.StateVersion < 1 ||
            row.FoundationNullState && row.State != "null" || row.Jobs is null ||
            row != new InstanceRow(row.Id, start.Model, start.Definition, InstanceStatus.Running, row.State, 1, null,
                Jobs: row.Jobs, StateVersion: row.StateVersion, Creation: 1, StartIntent: CreationProtocol.Fingerprint(start, creator.Kind, creator.Payload))
                { NextJobGeneration = turn.Sent.Count, FoundationNullState = row.FoundationNullState } ||
            !FoundationMetadataLimits.FitsContent(header.TypeData, FoundationMetadataLimits.TypeDataBytes) ||
            header.Labels.Count > 4 || header.Labels.Any(p => !FoundationMetadataLimits.FitsName(p.Key, true) || !FoundationMetadataLimits.FitsName(p.Value, true)))
            throw new StoreException(Failure.Definite, "Invalid first job-root checkpoint or header.");
        var tickets = turn.Sent.ToArray();
        var entries = JobRegistry.Read(row.Jobs);
        if (entries.Count != tickets.Length || entries.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count() != tickets.Length)
            throw new StoreException(Failure.Definite, "The job registry does not match the issued tickets.");
        for (var generation = 0; generation < tickets.Length; generation++)
        {
            var m = tickets[generation];
            if (!m.HasIssuedTicket || m.Kind != Kinds.Job || m.Target != creator.Target || m.Sender != row.Id ||
                m.SenderCreation != 1 || m.TargetCreation != 1 || m.JobGeneration != generation || m.Start is not null ||
                m.Due is not null || m.KeepUntil is not null || m.ReplyTo is not null || !FoundationMetadataLimits.FitsComposed(m) ||
                m.Read<JobTicket>() is not { Deadline: null, Timer: null } job || job.Model != row.Model || !FoundationMetadataLimits.FitsName(job.Key, true) ||
                entries[generation] != new JobRegistration(job.Key, generation) ||
                m.InboxKey != InboxKey.Ticket(row.Id, job.Key, 1, generation))
                throw new StoreException(Failure.Definite, "A first job-root ticket does not match its issuer, registry or generation.");
        }
        var image = new JobRootImage(row.Id, 1, 1, row.Model, row.Definition, row.FoundationNullState ? null : row.State,
            row.StateVersion, row.Jobs, tickets.Length, row.StartIntent!, header.TypeData, new(header.Labels, StringComparer.Ordinal));
        var scopeWrite = new FirstJobWrite(scope.Tenant, reference, canonical, DurableKeyJson.Write(new RootEnvelope(creator.Id, spec)), DurableKeyJson.Write(image));
        var frozen = turn with { Consumed = [canonical.InboxKey], Done = [], Sent = tickets, Subscribed = [], Unsubscribed = [], Held = null };
        var batch = new CommitBatch(fence.Partition, fence.Epoch, fence.Position, lease, false, [frozen], [], []);
        return CommitAsync(batch, scopeWrite, null, cancellationToken);
    }

    private static BsonDocument CreatorFilter(FirstJobWrite scope) => new()
    {
        { "_id", InboxId(scope.Creator.InboxKey) }, { "tenant", scope.Tenant }, { "rootRef", scope.Reference },
        { "target", scope.Creator.Target.Id }, { "kind", Kinds.Start }, { "done", false }
    };

    private async Task<bool> HasJobRootCreatorAsync(IClientSessionHandle session, FirstJobWrite scope, CancellationToken token)
    {
        var binding = new BsonDocument
        {
            { "_id", BirthId(scope.Reference) }, { "tenant", scope.Tenant }, { "actor", scope.Creator.Target.Id },
            { "creation", 1L }, { "creator", scope.Creator.InboxKey.Id }, { "digest", scope.Creator.Start!.Creation!.Intent },
            { "intent", CreationProtocol.Fingerprint(scope.Creator.Start, scope.Creator.Kind, scope.Creator.Payload) }
        };
        if (!await _meta.Find(session, binding).AnyAsync(token)) return false;
        var doc = await _messages.Find(session, CreatorFilter(scope)).Project(new BsonDocument("rootEnvelope", 1)).FirstOrDefaultAsync(token);
        return doc?.GetValue("rootEnvelope", BsonNull.Value) is BsonString body && body.Value == scope.Envelope;
    }

    internal async Task<JobRootCheckpoint?> ReadJobRootCheckpointAsync(BoundScope scope, string reference, CancellationToken cancellationToken = default)
    {
        if (!_options.ScopedJobRoots) throw new NotSupportedException("Qualified job roots require the staged provider.");
        using var session = await _client.StartSessionAsync(cancellationToken: cancellationToken);
        return await session.WithTransactionAsync(async (tx, token) =>
        {
            await RequireVersionsAsync(tx, token);
            var birth = await _meta.Find(tx, new BsonDocument { { "_id", BirthId(reference) }, { "tenant", scope.Tenant } }).FirstOrDefaultAsync(token);
            if (birth is null) return null;
            var bound = new BsonDocument { { "_id", birth["actor"] }, { "tenant", scope.Tenant }, { "rootRef", reference } };
            var row = await _instances.Find(tx, bound).FirstOrDefaultAsync(token);
            if (row is null) return null;
            var image = DurableKeyJson.Read<JobRootImage>(row["rootImage"].AsString);
            if (image.Actor != birth["actor"].AsString || image.Creation != birth["creation"].AsInt64 ||
                image.Revision != row["revision"].AsInt64 || image.StartIntent != birth["intent"].AsString)
                throw new StoreException(Failure.Definite, "The scoped checkpoint conflicts with its birth.");
            var jobs = JobRegistry.Read(image.Jobs);
            var expected = jobs.ToDictionary(e => InboxKey.Ticket(image.Actor, e.Key, image.Creation, e.Generation));
            var docs = await _messages.Find(tx, new BsonDocument { { "partition", birth["partition"] },
                { "rootRef", reference }, { "tenant", scope.Tenant }, { "target", image.Actor },
                { "kind", _options.ScopedJobResults ? new BsonDocument("$in", new BsonArray { Kinds.Job, Kinds.JobFinished }) : (BsonValue)Kinds.Job },
                { "done", false } }).Sort(new BsonDocument("seq", 1)).ToListAsync(token);
            var tickets = new List<Message>();
            var results = new List<Message>();
            foreach (var doc in docs)
            {
                var msg = ToStored(doc).Message;
                bool result = msg.Kind == Kinds.JobFinished;
                var key = result ? msg.Read<JobFinished>().Key : msg.Read<JobTicket>().Key;
                var ticket = InboxKey.Ticket(image.Actor, key, image.Creation, msg.JobGeneration);
                if (!expected.Remove(ticket) || msg.Sender != image.Actor || msg.SenderCreation != image.Creation || msg.TargetCreation != image.Creation ||
                    msg.InboxKey != (result ? InboxKey.JobResult(image.Actor, key, image.Creation, msg.JobGeneration) : ticket) ||
                    result && !doc.GetValue("resultOf", BsonNull.Value).Equals(InboxId(ticket)))
                    throw new StoreException(Failure.Definite, "The pending ticket or result conflicts with the job checkpoint.");
                if (result) results.Add(msg with { FoundationJobResult = true });
                else tickets.Add(msg);
            }
            if (expected.Count != 0)
                throw new StoreException(Failure.Definite, "The job checkpoint is missing a pending ticket or result.");
            var journalRevision = _options.ApplyJobSuspend ? row.GetValue("journalRevision", image.Revision).AsInt64 : image.Revision;
            if (journalRevision < 1 || journalRevision > image.Revision)
                throw new StoreException(Failure.Definite, "The journal revision exceeds the scoped checkpoint.");
            var journal = await _journal.Find(tx, new BsonDocument { { "partition", birth["partition"] }, { "instance", image.Actor },
                { "revision", journalRevision }, { "tenant", scope.Tenant }, { "rootRef", reference } }).SingleAsync(token);
            return new JobRootCheckpoint(image, tickets, new JournalRow(birth["partition"].AsInt32, journal["position"].AsInt64,
                image.Actor, journalRevision, journal["entries"].AsString))
                { Results = results, Suspended = row.GetValue("suspended", false).AsBoolean };
        }, Transaction, cancellationToken);
    }
}
