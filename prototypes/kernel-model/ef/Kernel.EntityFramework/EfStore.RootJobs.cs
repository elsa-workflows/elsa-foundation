using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record RootJobState(string? Jobs, long? NextGeneration);

    internal const long RootJobScope = -6;
    internal Func<string, string>? RootJobHandle { get; private set; }
    internal bool RootJobsEnabled => RootJobHandle is not null;

    internal async Task EnableRootJobsAsync(CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesRootJobProtocol || !db.Database.IsNpgsql())
            throw new StoreFormatException("Untimed generated-root jobs require the PostgreSQL job protocol.");
        await RequireStoreVersionsAsync(db, token);
        var key = await db.Database.SqlQueryRaw<byte[]>("""SELECT "Key" AS "Value" FROM "FoundationRootIssuer" """).SingleAsync(token);
        lock (_workerBinding)
        {
            if (_ordinaryRoots is not null)
                throw new InvalidOperationException("The PostgreSQL owner is already bound.");
            RootJobHandle ??= seed => FoundationSelfSend.Handle(key, seed);
        }
    }

    private static string? JobActor(InboxKey key)
    {
        if (!key.HasIssuedOrigin || key.Scope <= 0 || key.Match is not null || !key.Id.StartsWith('c')) return null;
        var split = key.Id.IndexOf('_');
        if (split < 2 || !int.TryParse(key.Id.AsSpan(1, split - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var length) ||
            length <= 0 || length > key.Id.Length - split - 1) return null;
        var actor = key.Id.Substring(split + 1, length);
        var origin = CreationProtocol.Origin(actor, 1);
        return key.Scope == origin.Length + 1 && key.Id.StartsWith(origin + "!", StringComparison.Ordinal) ? actor : null;
    }

    private static string StoredJobId(InboxKey key) =>
        Convert.ToHexString(SHA256.HashData(IssuedMailKey.Encode(key.Id)));

    private static Message EncodeRootJob(Message message, WorkerRoot root, string model)
    {
        if (!message.HasIssuedTicket || RootUntimedJob.Read(message, root.Actor, model) is null) throw RootShape();
        return (new Message(StoredJobId(message.InboxKey), message.Target, Kinds.Job, message.Payload, root.Actor)
            { JobGeneration = message.JobGeneration }).WithInboxScope(RootJobScope).AsKernelTicket();
    }

    private static Message DecodeRootJob(MessageEntity row)
    {
        if (row.Kind != Kinds.Job || row.Payload is null || row.EventMatch is not null || row.Start is not null ||
            row.Sender != row.Target || row.Due != 0 || row.KeepUntil is not null || row.ReplyTo is not null ||
            row.JobGeneration is < 0 or long.MaxValue || Json.Read<JobTicket>(row.Payload) is not { Key.Length: > 0 } ticket)
            throw RootShape();
        var key = InboxKey.Ticket(row.Target, ticket.Key, 1, row.JobGeneration);
        var message = (new Message(key.Id, new InstanceRef(row.Target), row.Kind, row.Payload, row.Sender,
            TargetCreation: 1, SenderCreation: 1) { JobGeneration = row.JobGeneration }).WithInboxScope(key.Scope);
        return row.Id == StoredJobId(key) && RootUntimedJob.Read(message, row.Target, ticket.Model) is not null ? message : throw RootShape();
    }

    // A job completion takes its original row, independently of the ordinary input FIFO.
    private static async Task<bool> ConsumeRootJobAsync(KernelDb db, CreationAddress actor, Message original, CancellationToken token)
    {
        if (!db.UsesRootJobProtocol || !original.HasIssuedTicket) return false;
        var id = StoredJobId(original.InboxKey);
        var home = new InstanceRef(actor.Id).Partition;
        var rows = await db.Messages.FromSqlInterpolated($"""
            SELECT * FROM "Messages" WHERE "InboxScope" = {RootJobScope} AND "Id" = {id}
                AND "Target" = {actor.Id} AND "Partition" = {home} AND NOT "Done" FOR UPDATE
            """).AsNoTracking().ToListAsync(token);
        if (rows is not [var stored] || DecodeRootJob(stored) != original.WithoutTicketAuthority()) return false;
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "Messages" WHERE "InboxScope" = {RootJobScope} AND "Id" = {id}
                AND "Target" = {actor.Id} AND "Partition" = {home} AND NOT "Done"
            """, token) == 1;
    }

    private static void ValidateRootJobChange(RootImage? before, InstanceRow row, Message[] inputs, IReadOnlyList<Message> sends,
        RootJobCompletion? completion)
    {
        var previous = JobRegistry.Read(before?.Jobs);
        var current = JobRegistry.Read(row.Jobs);
        var generation = before?.NextJobGeneration ?? 0;
        var consumed = inputs.Where(message => message.Kind == Kinds.Job).ToArray();
        var started = sends.Where(message => message.Kind == Kinds.Job).ToArray();
        if (consumed.Length != 0)
        {
            if (inputs is not [var input] || previous is not [var old] ||
                RootUntimedJob.Read(input, row.Id, row.Model)?.Key != old.Key || input.JobGeneration != old.Generation ||
                completion is not { Original.HasIssuedTicket: true, Result.HasIssuedTicket: true } ||
                completion.Original.WithoutTicketAuthority() != input ||
                !RootUntimedJob.MatchesCompletion(completion.Original, completion.Result, old.Key)) throw RootShape();
            previous.Clear();
        }
        else if (completion is not null) throw RootShape();
        if (started.Length != 0)
        {
            if (started is not [var message] || previous.Count != 0 || row.Status != InstanceStatus.Running ||
                message.JobGeneration != generation || RootUntimedJob.Read(message, row.Id, row.Model) is not { } ticket) throw RootShape();
            previous.Add(new(ticket.Key, generation));
            generation = checked(generation + 1);
        }
        if (!current.SequenceEqual(previous) || row.NextJobGeneration != generation ||
            row.Status == InstanceStatus.Finished && current.Count != 0) throw RootShape();
    }
}
