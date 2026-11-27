using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static long ParkStorageScope(Message input) => input.FoundationJobResult ? RootTimedJobScope :
        input.InboxKey.Scope == InboxKey.FoundationOccurrenceScope ? InboxKey.FoundationOccurrenceScope : RootSelfScope;

    private static string ParkStorageId(Message input) => input.FoundationJobResult ? StoredJobId(input.InboxKey) : input.InboxKey.Id;

    // The fenced Park path keeps direct or qualified private inputs and can open on an authenticated timed-job result.
    private async Task<PreparedRoot?> PrepareRootParkOpeningAsync(WorkerRoot root, TurnCommit turn, bool parkedOperators,
        CancellationToken cancellationToken)
    {
        var opening = turn.OpensPark;
        var retry = _ordinaryRoots?.RecoversInlineInitialRoots == true && RootSelfIssuer is { } inlineIssuer &&
            turn.Row is { } initial && turn.FoundationRootParkInputs is [var retryInput] && FoundationInlineRetry.Matches(inlineIssuer, initial, retryInput);
        var reason = turn.Journal is null || retry ? ParkKeepReason.Arrived : ParkKeepReason.Consumed;
        var repark = !opening && reason == ParkKeepReason.Consumed;
        // S5a: a composed Park set opens the Park without the operator command whose own turn failed. The command keeps its
        // row and FIFO place, unmarked, so the owner polls it again on the parked root.
        var split = parkedOperators && opening && turn.Done.Any(PostgresRootAdmission.IsOperatorOccurrence);
        string[] returned = split ? turn.Done.Where(PostgresRootAdmission.IsOperatorOccurrence).Select(key => key.Id).ToArray() : [];
        if (split) turn = WithoutOperators(turn);
        // A recorded timed result is already durably published. If its actor callback faults,
        // keep that exact result beside the same job registration in the Park commit.
        var recorded = opening && RootTimedJobSettlementEnabled && turn.Row is { } resultTarget &&
            turn.FoundationRootParkInputs is [var result] && RootJobTicket.IsRecordedResult(result, resultTarget);
        // A Park keeps the row's Suspended flag, as the in-memory reference does. Only a composed set with operators can
        // suspend a root, so only there can a Park open or continue on a suspended one.
        if (turn.Row is not { Status: InstanceStatus.Faulted, Fault: { } fault } row || row.Suspended && !parkedOperators ||
            turn.Expected is not > 0 || opening && !retry && reason != ParkKeepReason.Consumed || turn.Done.Count == 0 && !split ||
            turn.FoundationKept is not { } kept || kept.Count != turn.Done.Count ||
            turn.FoundationRootParkInputs is not { } envelopes || !SameInputs(envelopes, turn.Done) ||
            (!recorded && envelopes.Any(input => input.InboxKey.Scope != InboxKey.FoundationOccurrenceScope &&
                (_ordinaryRoots?.KeepsPrivateParkInputs != true || RootSelfIssuer is null ||
                    !FoundationSelfSend.IsSelfInput(input, root.Actor, 1)))) ||
            turn.Done.Any(key => !root.Owns(key) ||
                PostgresRootAdmission.IsOperatorOccurrence(key) || !kept.TryGetValue(key, out var actual) ||
                actual != reason) || turn.Done.Distinct().Count() != kept.Count ||
            turn.Consumed.Count != 0 || turn.Sent.Count != 0 || turn.Subscribed.Count != 0 ||
            turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 } ||
            turn != new TurnCommit(root.Actor, row, turn.Expected, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed,
                turn.Unsubscribed, turn.Journal, Unsubscribe: opening || repark, Held: turn.Held)
                { OpensPark = opening, FoundationKept = kept, FoundationRootParkInputs = envelopes,
                    FoundationRootChildSends = turn.FoundationRootChildSends } ||
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

        var childEffects = turn.FoundationRootChildSends is null ? null :
            PrepareRootChildEffects(root, row, turn.FoundationRootChildSends);
        if (turn.FoundationRootChildSends is not null &&
            (!opening || childEffects is not { Starts.Length: 0, Inputs.Length: 0, Notice: not null })) throw RootShape();
        var journal = turn.Journal is null ? null : new TurnCommit(root.Actor, null, null, [], [], [], [], [], turn.Journal);
        return new(journal, new RootParkOpening(root, current.Revision, opening, reason, DurableKeyJson.Write(fault), envelopes, returned,
            row.Suspended, current.Jobs, current.NextJobGeneration, row.Model, RootSelfIssuer)
            { ChildEffects = childEffects, RetriesInitialTurn = retry });
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

    // Whether a Park keep marks pending Park commands unseen, and so whether their Park facts let the owner forget a
    // copy read ahead of the keep. Composed parked-operator sets only: shipped formats keep their behaviour, where a stale
    // Recover fails its commit and the next owner reads it again.
    internal static bool RereadsParkFacts(KernelDb db) => db.UsesRootParkedOperators;

    // A stored operator header that names a Park.
    private const string ParkSelectedOperator = "%\"Park\":{%";

    private sealed record LockedParkCheckpoint(long At, long? ParkAt, long? OpenRevision, byte[] StateDigest, byte[] HeaderDigest, int StateVersion);

    private sealed record RootParkOpening(WorkerRoot Root, long Expected, bool Opening, ParkKeepReason Reason, string EncodedFault,
        IReadOnlyList<Message> Inputs, string[] Returned, bool Suspended, string? Jobs, long? NextJobGeneration, string Model,
        FoundationSelfIssuer? SelfIssuer)
        : QualifiedRootCheckpoint(checked(Expected + 1))
    {
        internal RootChildEffects? ChildEffects { get; init; }
        internal bool RetriesInitialTurn { get; init; }

        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            if (!db.UsesRootParkOpening || !Opening && !db.UsesRootParkArrivals || RetriesInitialTurn && !db.UsesRootInlinePromotion)
                return false;
            if (db.UsesSqliteRootRuntime) return await BindSqliteParkOpeningAsync(db, this, cancellationToken);
            var recorded = Inputs is [{ FoundationJobResult: true }];
            if (recorded && (!Opening || !db.UsesRootTimedJobSettlement)) return false;
            var priorStatus = Opening ? InstanceStatus.Running : InstanceStatus.Faulted;
            FormattableString read = $"""
                SELECT floor(extract(epoch from statement_timestamp()) * 1000)::bigint AS "At", i."FoundationParkAt" AS "ParkAt",
                    /*open-revision*/ AS "OpenRevision", sha256(convert_to(i."State", 'UTF8')) AS "StateDigest",
                    sha256(convert_to(i."FoundationHeader", 'UTF8')) AS "HeaderDigest", i."StateVersion"
                FROM "Instances" i JOIN "FoundationRootBirths" b
                    ON b."Actor" = i."Id" AND b."Partition" = i."Partition" AND b."Creation" = 1
                    AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
                    AND (NOT {RetriesInitialTurn} OR b."Fingerprint" = {Root.Fingerprint})
                WHERE i."Id" = {Root.Actor} AND i."Partition" = {Root.Partition} AND NOT i."Retired"
                    AND i."Suspended" = {Suspended} AND i."Status" = {(int)priorStatus} AND i."Revision" = {Expected}
                    AND (i."FoundationParkAt" IS NULL) = {Opening} /*no-clearing*/
                    AND ({Opening} OR i."Fault" = {EncodedFault})
                    AND i."Jobs" IS NOT DISTINCT FROM {Jobs} AND i."NextJobGeneration" IS NOT DISTINCT FROM {NextJobGeneration}
                    AND i."FoundationTenant" = {Root.Tenant} AND i."FoundationReference" = {Root.Reference}
                FOR UPDATE OF i
                """;
            var openRevisionColumn = db.RecordsRootParkRevision ? """i."FoundationParkRevision" """ : "NULL::bigint";
            var locked = await db.Database.SqlQuery<LockedParkCheckpoint>(FormattableStringFactory.Create(
                read.Format.Replace("/*open-revision*/", openRevisionColumn, StringComparison.Ordinal)
                    // S5b-2: nothing is kept while the Park is being cleared.
                    .Replace("/*no-clearing*/", db.UsesRootParkedOperators ? """AND i."FoundationParkClearing" IS NULL""" : "", StringComparison.Ordinal),
                read.GetArguments()))
                .ToListAsync(cancellationToken);
            if (locked is not [var prior] || db.RecordsRootParkRevision &&
                (Opening ? prior.OpenRevision is not null : prior.OpenRevision is not > 0 || prior.OpenRevision > Expected)) return false;

            var parkAt = prior.ParkAt ?? prior.At;
            var openRevision = prior.OpenRevision ?? JournalRevision;
            FormattableString write = $"""
                UPDATE "Instances" SET "Status" = {(int)InstanceStatus.Faulted}, "Fault" = {EncodedFault},
                    "Revision" = {JournalRevision}, "FoundationWriteRevision" = {JournalRevision}, "FoundationParkAt" = {parkAt}
                    /*open-revision*/ /*park-job-result*/ /*park-live-job*/
                WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition} AND "Revision" = {Expected}
                """;
            if (db.RecordsRootParkRevision)
                write = FormattableStringFactory.Create(
                    write.Format.Replace("/*open-revision*/", $$""", "FoundationParkRevision" = {{{write.ArgumentCount}}}""", StringComparison.Ordinal),
                    [.. write.GetArguments(), openRevision]);
            var keys = Inputs.Select(ParkStorageId).ToArray();
            var pinsResult = recorded && RecordsConsumedRootJob(db);
            var live = Opening && !recorded && Jobs is not null
                ? await ReadLiveRootJobAsync(db, Root.Actor, Root.Partition, Model, Jobs, cancellationToken) : null;
            if (live is not null) await ProtectLiveJobAsync(db, Root.Actor, Root.Partition, live, cancellationToken);
            write = WithLiveParkJob(db, WithParkJobResult(db, write, pinsResult ? keys[0] : null), live?.Proof);
            if (!pinsResult && await db.Database.ExecuteSqlInterpolatedAsync(write, cancellationToken) != 1) return false;

            // A different input can mature between dispatch and Park. Lock the dispatched identities.
            var keptInputs = new List<(long Scope, string[] Ids, MessageEntity[] Pending)>();
            foreach (var group in Inputs.GroupBy(ParkStorageScope).OrderBy(group => group.Key))
            {
                var ids = group.Select(ParkStorageId).ToArray();
                var pending = await KeepPostgresInputsAsync(db, group.Key, ids, group.ToArray(), prior.At, pinsResult, cancellationToken);
                if (pending is null) return false;
                keptInputs.Add((group.Key, ids, pending));
            }
            if (pinsResult && await db.Database.ExecuteSqlInterpolatedAsync(write, cancellationToken) != 1) return false;
            if (Returned.Length != 0 && await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Seen" = 0
                WHERE "Id" = ANY({Returned}) AND "Id" LIKE 'operator/%' AND "Target" = {Root.Actor} AND "Partition" = {Root.Partition}
                    AND "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "EventMatch" IS NULL AND NOT "Done"
                """, cancellationToken) != Returned.Length) return false;
            // S5b: a keep, of either reason, changes the kept set, so a pending command that carries Park facts (a Recover
            // or a Terminate that names a Park) is read again with fresh ones. The owner forgets the copy it read ahead of
            // this keep only where its facts say so, by the same predicate.
            if (RereadsParkFacts(db))
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "Seen" = 0, "Trial" = 0
                    WHERE "Target" = {Root.Actor} AND "Partition" = {Root.Partition} AND "InboxScope" = {InboxKey.FoundationOccurrenceScope}
                        AND "EventMatch" IS NULL AND NOT "Done" AND "Id" LIKE 'operator/%' AND "Start" LIKE {ParkSelectedOperator}
                    """, cancellationToken);
            if (db.UsesRootChildren)
                await db.Database.ExecuteSqlRawAsync($"""
                    UPDATE "Messages" m SET "Seen" = 0, "Trial" = 0
                    WHERE m."Target" = {"{0}"} AND m."Partition" = {"{1}"} AND m."InboxScope" = {InboxKey.FoundationOccurrenceScope}
                        AND m."EventMatch" IS NULL AND NOT m."Done" AND {RootChildNoticePredicate("m")}
                    """, [Root.Actor, Root.Partition], cancellationToken);
            if (Reason == ParkKeepReason.Consumed)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM "Subscriptions" WHERE "Instance" = {Root.Actor} AND "Partition" = {Root.Partition}
                    """, cancellationToken);

            foreach (var (scope, ids, pending) in keptInputs)
            {
                var retained = await ReadPostgresParkInputsAsync(db, scope, ids, prior.At, kept: true, cancellationToken);
                if (!retained.Select(Envelope).SequenceEqual(pending.Select(Envelope))) return false;
            }

            var stored = await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM "Instances" WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND "FoundationTenant" = {Root.Tenant} AND "FoundationReference" = {Root.Reference}
                        AND "Revision" = {JournalRevision} AND "FoundationWriteRevision" = {JournalRevision}
                        AND "Status" = {(int)InstanceStatus.Faulted} AND "Fault" = {EncodedFault} AND "FoundationParkAt" = {parkAt}
                        AND "Suspended" = {Suspended} AND "StateVersion" = {prior.StateVersion} AND sha256(convert_to("State", 'UTF8')) = {prior.StateDigest}
                        AND sha256(convert_to("FoundationHeader", 'UTF8')) = {prior.HeaderDigest}
                        AND "Jobs" IS NOT DISTINCT FROM {Jobs} AND "NextJobGeneration" IS NOT DISTINCT FROM {NextJobGeneration}
                )
                AND NOT EXISTS (SELECT 1 FROM "Subscriptions" WHERE "Instance" = {Root.Actor} AND "Partition" = {Root.Partition})
                AS "Value"
                """).SingleAsync(cancellationToken);
            if (db.RecordsRootParkRevision && !await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition}
                    AND "Revision" = {JournalRevision} AND "FoundationParkRevision" = {openRevision}) AS "Value"
                """).SingleAsync(cancellationToken)) return false;
            return stored && (ChildEffects is null || await ChildEffects.BindAsync(db, Root.Tenant, cancellationToken)) &&
                (!db.UsesRootParkInspection || await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "FoundationActorList"
                    WHERE "Tenant" = {Root.Tenant} AND "Reference" = {Root.Reference}
                        AND "Status" = {(int)InstanceStatus.Faulted} AND "ParkAt" = {parkAt} AND "ParkFault" = {EncodedFault})
                AS "Value"
                """).SingleAsync(cancellationToken));
        }

        private async Task<MessageEntity[]?> KeepPostgresInputsAsync(KernelDb db, long scope, string[] ids, Message[] expected, long now,
            bool pinsResult, CancellationToken token)
        {
            var pending = await ReadPostgresParkInputsAsync(db, scope, ids, now, kept: false, token);
            var decoded = pending.Select(row => DecodeRootInput(db, row, Root.Reference, SelfIssuer, timed: scope == RootTimedJobScope));
            if (!ParkInputsMatch(decoded, expected)) return null;

            if (pinsResult) await ReleaseProtectedTimedJobsAsync(db, Root.Actor, ids, token);
            return await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Done" = true, "Trial" = 0, "FoundationKeptBy" = {(short)Reason}
                WHERE "Target" = {Root.Actor} AND "Partition" = {Root.Partition} AND "InboxScope" = {scope}
                    AND "Id" = ANY({ids}) AND "EventMatch" IS NULL AND NOT "Done" AND "FoundationKeptBy" IS NULL
                """, token) == ids.Length ? pending : null;
        }

        private Task<MessageEntity[]> ReadPostgresParkInputsAsync(KernelDb db, long scope, string[] ids, long now, bool kept,
            CancellationToken token)
        {
            var source = scope == RootSelfScope ? PostgresPrivateInboxView : "\"Messages\"";
            var state = kept ? """AND m."Trial" = 0 AND m."FoundationKeptBy" = {6} ORDER BY m."Seq" """ :
                """AND m."FoundationKeptBy" IS NULL ORDER BY m."Seq" FOR UPDATE OF m""";
            var sql = $$"""
                SELECT * FROM {{source}} m WHERE m."Target" = {0} AND m."Partition" = {1} AND m."InboxScope" = {2}
                    AND m."Id" = ANY({3}) AND m."EventMatch" IS NULL AND m."Due" <= {4} AND m."Done" = {5} {{state}}
                """;
            object[] args = [Root.Actor, Root.Partition, scope, ids, now, kept];
            if (kept) args = [.. args, (short)Reason];
            return db.Messages.FromSqlInterpolated(FormattableStringFactory.Create(sql, args)).AsNoTracking().ToArrayAsync(token);
        }

        private static object Envelope(MessageEntity row) => new
        {
            row.Id, row.Seq, row.Kind, row.Payload, row.Start, row.Sender, row.Due, row.KeepUntil, row.ReplyTo,
            row.JobGeneration, row.Seen, row.Attempts
        };
    }

    private static Task<bool> VerifyRootParkProjectionAsync(KernelDb db, string tenant, string reference, long revision,
        string fault, CancellationToken token) => db.Database.SqlQuery<bool>($"""
        SELECT EXISTS (SELECT 1 FROM "FoundationActorList" WHERE "Tenant" = {tenant} AND "Reference" = {reference}
            AND "Status" = {(int)InstanceStatus.Faulted} AND "ParkRevision" = {revision} AND "ParkFault" = {fault}
            AND "ParkAt" > 0 AND "EndedAt" IS NULL AND "TerminalRevision" IS NULL) AS "Value"
        """).SingleAsync(token);

    private static bool ParkInputsMatch(IEnumerable<Message> decoded, IEnumerable<Message> expected) =>
        decoded.OrderBy(input => input.InboxKey.Id, StringComparer.Ordinal)
            .SequenceEqual(expected.OrderBy(input => input.InboxKey.Id, StringComparer.Ordinal));
}
