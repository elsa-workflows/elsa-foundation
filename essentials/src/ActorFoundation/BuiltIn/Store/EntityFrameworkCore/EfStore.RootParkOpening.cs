using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // This qualification accepts the first Park of an existing generated root consuming
    // direct input. Park arrival, control, jobs and recovery have separate transitions.
    private async Task<PreparedRoot?> PrepareRootParkOpeningAsync(WorkerRoot root, TurnCommit turn,
        CancellationToken cancellationToken)
    {
        if (turn.Row is not { Status: InstanceStatus.Faulted, Suspended: false, Fault: { } fault } row ||
            turn.Expected is not > 0 || turn.Journal is null || turn.Done.Count == 0 ||
            turn.FoundationKept is not { } kept || kept.Count != turn.Done.Count ||
            turn.FoundationRootParkInputs is not { } envelopes || !SameInputs(envelopes, turn.Done) ||
            turn.Done.Any(key => key.Scope != InboxKey.FoundationOccurrenceScope || !root.Owns(key) ||
                PostgresRootAdmission.IsOperatorOccurrence(key) || !kept.TryGetValue(key, out var reason) ||
                reason != ParkKeepReason.Consumed) || turn.Done.Distinct().Count() != kept.Count ||
            turn.Consumed.Count != 0 || turn.Sent.Count != 0 || turn.Subscribed.Count != 0 ||
            turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 } ||
            turn != new TurnCommit(root.Actor, row, turn.Expected, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed,
                turn.Unsubscribed, turn.Journal, Unsubscribe: true, Held: turn.Held)
                { OpensPark = true, FoundationKept = kept, FoundationRootParkInputs = envelopes } ||
            row.FoundationDispatchHeader is not { } header)
            throw RootShape();

        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, cancellationToken);
        if (current?.Revision != turn.Expected || current.Status != InstanceStatus.Running || current.Suspended) return null;
        var expected = WorkerRow(root, current) with
        {
            Status = InstanceStatus.Faulted, Revision = checked(current.Revision + 1), Fault = fault,
            FoundationDispatchHeader = header
        };
        if (row != expected || header.Tenant != root.Tenant || header.Reference != root.Reference ||
            header.Header.TypeData != current.TypeData || header.Header.Labels.Count != current.Labels.Count ||
            current.Labels.Any(pair => !header.Header.Labels.TryGetValue(pair.Key, out var value) || pair.Value != value))
            throw RootShape();

        var journal = new TurnCommit(root.Actor, null, null, [], [], [], [], [], turn.Journal);
        return new(journal, new RootParkOpening(root, current.Revision, DurableKeyJson.Write(fault), envelopes));
    }

    private sealed record RootParkOpening(WorkerRoot Root, long Expected, string EncodedFault, IReadOnlyList<Message> Inputs)
        : QualifiedRootCheckpoint(checked(Expected + 1))
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            if (!db.UsesRootParkOpening) return false;
            var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" i
                SET "Status" = {(int)InstanceStatus.Faulted}, "Fault" = {EncodedFault}, "Revision" = {JournalRevision},
                    "FoundationWriteRevision" = {JournalRevision}, "FoundationParkAt" = floor(extract(epoch from statement_timestamp()) * 1000)::bigint
                FROM "FoundationRootBirths" b
                WHERE i."Id" = {Root.Actor} AND i."Partition" = {Root.Partition} AND NOT i."Retired"
                    AND NOT i."Suspended" AND i."Status" = {(int)InstanceStatus.Running} AND i."Revision" = {Expected}
                    AND i."FoundationParkAt" IS NULL AND i."FoundationTenant" = {Root.Tenant} AND i."FoundationReference" = {Root.Reference}
                    AND b."Actor" = i."Id" AND b."Partition" = i."Partition" AND b."Creation" = 1
                    AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
                """, cancellationToken);
            if (updated != 1) return false;

            var pending = await db.Messages.FromSqlRaw($$"""
                SELECT * FROM "Messages" WHERE "Target" = {0} AND "Partition" = {1} AND "InboxScope" = {2}
                    AND "EventMatch" IS NULL AND NOT "Done" AND "Due" <= {{RootClockMilliseconds}}
                ORDER BY "Seq" LIMIT {3} FOR UPDATE
                """, Root.Actor, Root.Partition, InboxKey.FoundationOccurrenceScope, Inputs.Count)
                .AsNoTracking().ToListAsync(cancellationToken);
            var inputs = pending.Select(row => DecodeRootInput(db, row, Root.Reference, null)).ToArray();
            if (!inputs.SequenceEqual(Inputs) || inputs.Any(input => input.TargetCreation != 1 || input.Operator is not null))
                return false;

            var keys = Inputs.Select(input => input.InboxKey.Id).ToArray();
            var kept = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Done" = true, "Trial" = 0, "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
                WHERE "Id" = ANY({keys}) AND "Target" = {Root.Actor} AND "Partition" = {Root.Partition}
                    AND "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "EventMatch" IS NULL AND NOT "Done"
                """, cancellationToken);
            if (kept != keys.Length) return false;
            var retained = await db.Messages.AsNoTracking()
                .Where(row => row.Target == Root.Actor && row.Partition == Root.Partition &&
                    row.InboxScope == InboxKey.FoundationOccurrenceScope && row.EventMatch == null && keys.Contains(row.Id))
                .OrderBy(row => row.Seq).ToArrayAsync(cancellationToken);
            var envelope = (MessageEntity row) => new
            {
                row.Id, row.Seq, row.Kind, row.Payload, row.Start, row.Sender, row.Due, row.KeepUntil, row.ReplyTo,
                row.JobGeneration, row.Seen, row.Attempts
            };
            if (!retained.Select(envelope).SequenceEqual(pending.Select(envelope))) return false;

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "Subscriptions" WHERE "Instance" = {Root.Actor} AND "Partition" = {Root.Partition}
                """, cancellationToken);
            return await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM "Instances" WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND "FoundationTenant" = {Root.Tenant} AND "FoundationReference" = {Root.Reference}
                        AND "Revision" = {JournalRevision} AND "FoundationWriteRevision" = {JournalRevision}
                        AND "Status" = {(int)InstanceStatus.Faulted} AND "Fault" = {EncodedFault} AND "FoundationParkAt" IS NOT NULL
                ) AND (SELECT count(*) FROM "Messages" WHERE "Id" = ANY({keys}) AND "Target" = {Root.Actor}
                        AND "Partition" = {Root.Partition} AND "InboxScope" = {InboxKey.FoundationOccurrenceScope}
                        AND "EventMatch" IS NULL AND "Done" AND "Trial" = 0 AND "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
                ) = {keys.Length}
                AND NOT EXISTS (SELECT 1 FROM "Subscriptions" WHERE "Instance" = {Root.Actor} AND "Partition" = {Root.Partition})
                AS "Value"
                """).SingleAsync(cancellationToken);

        }
    }
}
