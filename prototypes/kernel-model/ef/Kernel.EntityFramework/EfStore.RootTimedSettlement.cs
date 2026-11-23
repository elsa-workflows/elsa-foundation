using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal const string TimedJobPublicationClockQuery = "root-timed-job-publication-clock";

    internal async ValueTask<long> ReadRootTimedJobPublicationTimeAsync(CancellationToken token)
    {
        await using var db = create();
        if (!RootTimedJobSettlementEnabled || !db.UsesRootTimedJobSettlement) throw RootShape();
        return await db.Database.SqlQueryRaw<long>($"""
            SELECT {RootClockMilliseconds} AS "Value" /* {TimedJobPublicationClockQuery} */
            """).SingleAsync(token);
    }

    private static string? DeadlineActor(InboxKey key)
    {
        if (key.Scope != -1 || key.Match is not null || !key.Id.StartsWith('c')) return null;
        var split = key.Id.IndexOf('_');
        if (split < 2 || !int.TryParse(key.Id.AsSpan(1, split - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var length) ||
            length <= 0 || length > key.Id.Length - split - 1) return null;
        var actor = key.Id.Substring(split + 1, length);
        var prefix = CreationProtocol.Origin(actor, 1) + "#job-deadline:";
        return key.Id.StartsWith(prefix, StringComparison.Ordinal) &&
            long.TryParse(key.Id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var generation) &&
            key.Id == DurableTurn.FoundationJobDeadlineId(actor, 1, generation) ? actor : null;
    }

    private static Message DecodeRootTimedRow(MessageEntity row)
    {
        if (row.Kind == Kinds.Job) return DecodeRootJob(row, timed: true);
        if (row.Payload is null || row.EventMatch is not null || row.Start is not null || row.Sender != row.Target ||
            row.KeepUntil is not null || row.ReplyTo is not null || row.JobGeneration is < 0 or long.MaxValue) throw RootShape();
        var message = row.Kind switch
        {
            Kinds.JobDeadline when row.Due > 0 && Json.Read<JobDeadline>(row.Payload) is { Key.Length: > 0 } timer =>
                RootJobTicket.Deadline(row.Target, timer.Key, row.JobGeneration, row.Due),
            Kinds.JobFinished when row.Due == 0 && Json.Read<JobFinished>(row.Payload) is { Key.Length: > 0 } outcome =>
                RootJobTicket.RecordedResult(row.Target, outcome.Key, row.JobGeneration, row.Payload),
            _ => throw RootShape()
        };
        return row.Id == StoredJobId(message.InboxKey) && row.Payload == message.Payload ? message : throw RootShape();
    }

    private async Task<PreparedRoot?> PrepareRootTimedTurnAsync(WorkerRoot root, TurnCommit turn, InstanceRow row, string? state,
        RootImage current, PartitionFence fence, Message[] wires, CancellationToken token)
    {
        if (turn.Done.Count != 0 || turn.FoundationSelfRecalls is not null || turn.FoundationRootOperator is not null ||
            row.Suspended || current.Suspended || turn.Consumed.Count is < 1 or > 2 ||
            turn.Consumed.Distinct().Count() != turn.Consumed.Count ||
            JobRegistry.Read(current.Jobs) is not [var job] ||
            row.NextJobGeneration != current.NextJobGeneration) throw RootShape();
        var ids = turn.Consumed.Select(StoredJobId).ToArray();
        await using var db = create();
        var rows = await db.Messages.AsNoTracking().Where(message => message.Target == root.Actor && message.Partition == root.Partition &&
            message.InboxScope == RootTimedJobScope && !message.Done && ids.Contains(message.Id)).OrderBy(message => message.Seq).ToListAsync(token);
        var inputs = rows.Select(DecodeRootTimedRow).ToArray();
        if (!SameInputs(inputs, turn.Consumed)) return null;

        var completion = turn.FoundationRootJobCompletion;
        if (inputs is [var recorded])
        {
            if (!recorded.FoundationJobResult || recorded.JobGeneration != job.Generation || recorded.Read<JobFinished>().Key != job.Key ||
                row.Jobs is not null || wires.Length != 0 || completion is not null) throw RootShape();
        }
        else
        {
            if (inputs is not [var deadline, var original] || RootJobTicket.Read(original, row.Id, row.Model, timed: true) is not { } ticket ||
                original.JobGeneration != job.Generation || ticket.Key != job.Key || deadline != RootJobTicket.Deadline(row.Id, ticket) ||
                row.Status != InstanceStatus.Running || state != current.State || row.StateVersion != current.StateVersion ||
                row.Jobs != current.Jobs || turn.FoundationHeader is not null || turn.Journal is not null ||
                turn.Sent is not [var emitted]) throw RootShape();
            var payload = Json.Write(new JobFinished(ticket.Key, null, "timeout"));
            if (completion is not null)
            {
                if (completion is not { Original.HasIssuedTicket: true, Result.HasIssuedTicket: true, PublishedAt: >= 0 } ||
                    completion.PublishedAt >= ticket.Deadline || completion.Original.WithoutTicketAuthority() != original ||
                    !RootJobTicket.MatchesCompletion(completion.Original, completion.Result, ticket.Key)) throw RootShape();
                payload = completion.Result.Payload!;
            }
            if (emitted != RootJobTicket.RecordedResult(row.Id, ticket.Key, job.Generation, payload)) throw RootShape();
        }

        var header = row.FoundationDispatchHeader!.Header;
        var prepared = PrepareRootTurn(root.Tenant, root.Reference, current, inputs, fence, row.Status, state, row.StateVersion,
            header.TypeData, header.Labels, turn.Journal, jobState: new(row.Jobs, row.NextJobGeneration), jobCompletion: completion, timedJob: true);
        return prepared with { JournalTurn = prepared.JournalTurn! with { Sent = wires } };
    }

    private static async Task<bool> ConsumeRootTimedInputsAsync(KernelDb db, CreationAddress actor, Message[] expected,
        RootJobCompletion? completion, CancellationToken token)
    {
        if (!db.UsesRootTimedJobSettlement) return false;
        var ids = expected.Select(message => StoredJobId(message.InboxKey)).ToArray();
        var home = new InstanceRef(actor.Id).Partition;
        var rows = await db.Messages.FromSqlInterpolated($"""
            SELECT * FROM "Messages" WHERE "Target" = {actor.Id} AND "Partition" = {home}
                AND "InboxScope" = {RootTimedJobScope} AND "Id" = ANY({ids}) AND NOT "Done"
            ORDER BY "Seq" FOR UPDATE
            """).AsNoTracking().ToListAsync(token);
        if (!rows.Select(DecodeRootTimedRow).SequenceEqual(expected)) return false;
        if (expected is [{ Due: { } deadline }, _] && completion is null &&
            !await db.Database.SqlQueryRaw<bool>($"""SELECT {RootClockMilliseconds} >= {"{0}"} AS "Value" """, deadline).SingleAsync(token))
            return false;
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "Messages" WHERE "Target" = {actor.Id} AND "Partition" = {home}
                AND "InboxScope" = {RootTimedJobScope} AND "Id" = ANY({ids}) AND NOT "Done"
            """, token) == expected.Length;
    }
}
