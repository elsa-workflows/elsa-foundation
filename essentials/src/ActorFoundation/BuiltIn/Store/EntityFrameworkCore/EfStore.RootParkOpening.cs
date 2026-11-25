using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // Qualified generated roots keep only ordinary direct inputs here. The same fenced
    // path covers opening and continuing the Park without entering the actor model.
    private async Task<PreparedRoot?> PrepareRootParkOpeningAsync(WorkerRoot root, TurnCommit turn, bool parkedOperators,
        CancellationToken cancellationToken)
    {
        var opening = turn.OpensPark;
        var reason = turn.Journal is null ? ParkKeepReason.Arrived : ParkKeepReason.Consumed;
        var repark = !opening && reason == ParkKeepReason.Consumed;
        // S5a: a composed Park set opens the Park without the operator command whose own turn failed. The command keeps its
        // row and FIFO place, unmarked, so the owner polls it again on the parked root.
        var split = parkedOperators && opening && turn.Done.Any(PostgresRootAdmission.IsOperatorOccurrence);
        string[] returned = split ? turn.Done.Where(PostgresRootAdmission.IsOperatorOccurrence).Select(key => key.Id).ToArray() : [];
        if (split) turn = WithoutOperators(turn);
        // A Park keeps the row's Suspended flag, as the in-memory reference does. Only a composed set with operators can
        // suspend a root, so only there can a Park open or continue on a suspended one.
        if (turn.Row is not { Status: InstanceStatus.Faulted, Fault: { } fault } row || row.Suspended && !parkedOperators ||
            turn.Expected is not > 0 || opening && reason != ParkKeepReason.Consumed || turn.Done.Count == 0 && !split ||
            turn.FoundationKept is not { } kept || kept.Count != turn.Done.Count ||
            turn.FoundationRootParkInputs is not { } envelopes || !SameInputs(envelopes, turn.Done) ||
            turn.Done.Any(key => key.Scope != InboxKey.FoundationOccurrenceScope || !root.Owns(key) ||
                PostgresRootAdmission.IsOperatorOccurrence(key) || !kept.TryGetValue(key, out var actual) ||
                actual != reason) || turn.Done.Distinct().Count() != kept.Count ||
            turn.Consumed.Count != 0 || turn.Sent.Count != 0 || turn.Subscribed.Count != 0 ||
            turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 } ||
            turn != new TurnCommit(root.Actor, row, turn.Expected, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed,
                turn.Unsubscribed, turn.Journal, Unsubscribe: opening || repark, Held: turn.Held)
                { OpensPark = opening, FoundationKept = kept, FoundationRootParkInputs = envelopes } ||
            row.FoundationDispatchHeader is not { } header)
            throw RootShape();

        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, cancellationToken);
        var priorStatus = opening ? InstanceStatus.Running : InstanceStatus.Faulted;
        if (current?.Revision != turn.Expected || current.Status != priorStatus || current.Suspended != row.Suspended) return null;
        var expected = WorkerRow(root, current) with
        {
            Status = InstanceStatus.Faulted, Revision = checked(current.Revision + 1), Fault = opening ? fault : current.Fault,
            FoundationDispatchHeader = header
        };
        if (row != expected || header.Tenant != root.Tenant || header.Reference != root.Reference ||
            header.Header.TypeData != current.TypeData || header.Header.Labels.Count != current.Labels.Count ||
            current.Labels.Any(pair => !header.Header.Labels.TryGetValue(pair.Key, out var value) || pair.Value != value))
            throw RootShape();

        var journal = turn.Journal is null ? null : new TurnCommit(root.Actor, null, null, [], [], [], [], [], turn.Journal);
        return new(journal, new RootParkOpening(root, current.Revision, opening, reason, DurableKeyJson.Write(fault), envelopes, returned,
            row.Suspended));
    }

    private static TurnCommit WithoutOperators(TurnCommit turn)
    {
        bool Ordinary(InboxKey key) => !PostgresRootAdmission.IsOperatorOccurrence(key);
        var command = turn.FoundationRootOperator;
        if (turn.FoundationKept is not { } kept || turn.FoundationRootParkInputs is not { } inputs ||
            command is not null && (Ordinary(command.InboxKey) || turn.Done is not [var only] || only != command.InboxKey)) throw RootShape();
        return turn with
        {
            Done = turn.Done.Where(Ordinary).ToArray(), FoundationRootOperator = null,
            FoundationKept = kept.Where(entry => Ordinary(entry.Key)).ToDictionary(),
            FoundationRootParkInputs = inputs.Where(input => Ordinary(input.InboxKey)).ToArray()
        };
    }

    private sealed record LockedParkCheckpoint(long At, long? ParkAt, long? OpenRevision, byte[] StateDigest, byte[] HeaderDigest, int StateVersion);

    private sealed record RootParkOpening(WorkerRoot Root, long Expected, bool Opening, ParkKeepReason Reason, string EncodedFault,
        IReadOnlyList<Message> Inputs, string[] Returned, bool Suspended)
        : QualifiedRootCheckpoint(checked(Expected + 1))
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            if (!db.UsesRootParkOpening || !Opening && !db.UsesRootParkArrivals) return false;
            var priorStatus = Opening ? InstanceStatus.Running : InstanceStatus.Faulted;
            FormattableString read = $"""
                SELECT floor(extract(epoch from statement_timestamp()) * 1000)::bigint AS "At", i."FoundationParkAt" AS "ParkAt",
                    /*open-revision*/ AS "OpenRevision", sha256(convert_to(i."State", 'UTF8')) AS "StateDigest",
                    sha256(convert_to(i."FoundationHeader", 'UTF8')) AS "HeaderDigest", i."StateVersion"
                FROM "Instances" i JOIN "FoundationRootBirths" b
                    ON b."Actor" = i."Id" AND b."Partition" = i."Partition" AND b."Creation" = 1
                    AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
                WHERE i."Id" = {Root.Actor} AND i."Partition" = {Root.Partition} AND NOT i."Retired"
                    AND i."Suspended" = {Suspended} AND i."Status" = {(int)priorStatus} AND i."Revision" = {Expected}
                    AND (i."FoundationParkAt" IS NULL) = {Opening}
                    AND ({Opening} OR i."Fault" = {EncodedFault})
                    AND i."FoundationTenant" = {Root.Tenant} AND i."FoundationReference" = {Root.Reference}
                FOR UPDATE OF i
                """;
            var openRevisionColumn = db.RecordsRootParkRevision ? """i."FoundationParkRevision" """ : "NULL::bigint";
            var locked = await db.Database.SqlQuery<LockedParkCheckpoint>(FormattableStringFactory.Create(
                read.Format.Replace("/*open-revision*/", openRevisionColumn, StringComparison.Ordinal), read.GetArguments()))
                .ToListAsync(cancellationToken);
            if (locked is not [var prior] || db.RecordsRootParkRevision &&
                (Opening ? prior.OpenRevision is not null : prior.OpenRevision is not > 0 || prior.OpenRevision > Expected)) return false;

            var parkAt = prior.ParkAt ?? prior.At;
            var openRevision = prior.OpenRevision ?? JournalRevision;
            FormattableString write = $"""
                UPDATE "Instances" SET "Status" = {(int)InstanceStatus.Faulted}, "Fault" = {EncodedFault},
                    "Revision" = {JournalRevision}, "FoundationWriteRevision" = {JournalRevision}, "FoundationParkAt" = {parkAt}
                    /*open-revision*/
                WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition} AND "Revision" = {Expected}
                """;
            if (db.RecordsRootParkRevision)
                write = FormattableStringFactory.Create(
                    write.Format.Replace("/*open-revision*/", $$""", "FoundationParkRevision" = {{{write.ArgumentCount}}}""", StringComparison.Ordinal),
                    [.. write.GetArguments(), openRevision]);
            if (await db.Database.ExecuteSqlInterpolatedAsync(write, cancellationToken) != 1) return false;

            var keys = Inputs.Select(input => input.InboxKey.Id).ToArray();
            // A different input can mature between dispatch and Park. Lock the dispatched identities.
            var pending = await db.Messages.FromSqlInterpolated($"""
                SELECT * FROM "Messages" WHERE "Target" = {Root.Actor} AND "Partition" = {Root.Partition}
                    AND "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "Id" = ANY({keys})
                    AND "EventMatch" IS NULL AND NOT "Done" AND "Due" <= {prior.At} ORDER BY "Seq" FOR UPDATE
                """).AsNoTracking().ToListAsync(cancellationToken);
            var inputs = pending.Select(row => DecodeRootInput(db, row, Root.Reference, null)).ToArray();
            if (inputs.Any(input => input.TargetCreation != 1 || input.Operator is not null) ||
                !inputs.OrderBy(input => input.InboxKey.Id, StringComparer.Ordinal)
                    .SequenceEqual(Inputs.OrderBy(input => input.InboxKey.Id, StringComparer.Ordinal)))
                return false;

            var kept = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Done" = true, "Trial" = 0, "FoundationKeptBy" = {(short)Reason}
                WHERE "Id" = ANY({keys}) AND "Target" = {Root.Actor} AND "Partition" = {Root.Partition}
                    AND "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "EventMatch" IS NULL AND NOT "Done"
                """, cancellationToken);
            if (kept != keys.Length || Returned.Length != 0 && await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Seen" = 0
                WHERE "Id" = ANY({Returned}) AND "Id" LIKE 'operator/%' AND "Target" = {Root.Actor} AND "Partition" = {Root.Partition}
                    AND "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "EventMatch" IS NULL AND NOT "Done"
                """, cancellationToken) != Returned.Length) return false;
            if (Reason == ParkKeepReason.Consumed)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM "Subscriptions" WHERE "Instance" = {Root.Actor} AND "Partition" = {Root.Partition}
                    """, cancellationToken);

            var retained = await db.Messages.AsNoTracking()
                .Where(row => row.Target == Root.Actor && row.Partition == Root.Partition &&
                    row.InboxScope == InboxKey.FoundationOccurrenceScope && row.EventMatch == null && keys.Contains(row.Id))
                .OrderBy(row => row.Seq).ToArrayAsync(cancellationToken);
            var envelope = (MessageEntity row) => new
            {
                row.Id, row.Seq, row.Kind, row.Payload, row.Start, row.Sender, row.Due, row.KeepUntil, row.ReplyTo,
                row.JobGeneration, row.Seen, row.Attempts
            };
            if (!retained.Select(envelope).OrderBy(row => row.Id, StringComparer.Ordinal)
                .SequenceEqual(pending.Select(envelope).OrderBy(row => row.Id, StringComparer.Ordinal))) return false;

            var stored = await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM "Instances" WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND "FoundationTenant" = {Root.Tenant} AND "FoundationReference" = {Root.Reference}
                        AND "Revision" = {JournalRevision} AND "FoundationWriteRevision" = {JournalRevision}
                        AND "Status" = {(int)InstanceStatus.Faulted} AND "Fault" = {EncodedFault} AND "FoundationParkAt" = {parkAt}
                        AND "Suspended" = {Suspended} AND "StateVersion" = {prior.StateVersion} AND sha256(convert_to("State", 'UTF8')) = {prior.StateDigest}
                        AND sha256(convert_to("FoundationHeader", 'UTF8')) = {prior.HeaderDigest}
                ) AND (SELECT count(*) FROM "Messages" WHERE "Id" = ANY({keys}) AND "Target" = {Root.Actor}
                        AND "Partition" = {Root.Partition} AND "InboxScope" = {InboxKey.FoundationOccurrenceScope}
                        AND "EventMatch" IS NULL AND "Done" AND "Trial" = 0 AND "FoundationKeptBy" = {(short)Reason}
                ) = {keys.Length}
                AND NOT EXISTS (SELECT 1 FROM "Subscriptions" WHERE "Instance" = {Root.Actor} AND "Partition" = {Root.Partition})
                AS "Value"
                """).SingleAsync(cancellationToken);
            if (db.RecordsRootParkRevision && !await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition}
                    AND "Revision" = {JournalRevision} AND "FoundationParkRevision" = {openRevision}) AS "Value"
                """).SingleAsync(cancellationToken)) return false;
            return stored && (!db.UsesRootParkInspection || await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "FoundationActorList"
                    WHERE "Tenant" = {Root.Tenant} AND "Reference" = {Root.Reference}
                        AND "Status" = {(int)InstanceStatus.Faulted} AND "ParkAt" = {parkAt} AND "ParkFault" = {EncodedFault})
                AS "Value"
                """).SingleAsync(cancellationToken));
        }
    }
}
