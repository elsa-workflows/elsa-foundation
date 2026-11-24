using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record LockedRoot(string Header, long ContentBytes, int HeaderBytes);

    private sealed record RootTurnCheckpoint(string Tenant, string Reference, RootImage Current, InstanceStatus Status,
        string State, string Header, byte[]? Wire, long Growth, int StateVersion, Message[] Inputs, FoundationSelfIssuer? Issue, string[]? Recalls,
        bool? OperatorSuspension, bool OperatorTermination, RootJobState? JobState, RootJobCompletion? JobCompletion, bool TimedJob,
        IReadOnlyList<FoundationValue>? Values) : QualifiedRootCheckpoint(checked(Current.Revision + 1))
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            var family = RootStateFamily(db);
            var jobs = JobState is null ? Current.Jobs : JobState.Jobs;
            var terminalHeader = db.UsesRootTermination && Status == InstanceStatus.Running
                ? RootTerminalHeaderBytes(Current.Model, Current.Definition) : 0;
            var reserve = Math.Max(0, terminalHeader - System.Text.Encoding.UTF8.GetByteCount(Header));
            var projectedBytes = db.UsesRootStableBody ? StableContentBytes(State, Header, Wire, reserve, Status == InstanceStatus.Running ? Growth : 0)
                : await MeasureRootContentAsync(db, family.Length + reserve, State, Header, jobs, cancellationToken,
                    comparePriorSize: true);
            var overBudget = projectedBytes > RootContentBytes;

            var home = new InstanceRef(Current.Actor.Id).Partition;
            var locked = await db.Database.SqlQuery<LockedRoot>($"""
                SELECT i."FoundationHeader" AS "Header", CASE WHEN {overBudget}
                    THEN octet_length(i."State")::bigint + octet_length(i."FoundationHeader") + coalesce(octet_length(i."Jobs"), 0)
                    ELSE 0 END AS "ContentBytes",
                    CASE WHEN {overBudget} THEN octet_length(i."FoundationHeader") ELSE 0 END AS "HeaderBytes"
                FROM "Instances" i JOIN "FoundationRootBirths" b
                    ON b."Reference" = i."FoundationReference" AND b."Tenant" = i."FoundationTenant"
                    AND b."Actor" = i."Id" AND b."Partition" = i."Partition"
                WHERE b."Reference" = {Reference} AND b."Tenant" = {Tenant} AND b."Creation" = {Current.Actor.Creation}
                    AND i."Id" = {Current.Actor.Id} AND i."Partition" = {home} AND i."Revision" = {Current.Revision}
                    AND i."Status" = {(int)InstanceStatus.Running} AND NOT i."Retired" AND i."Suspended" = {Current.Suspended}
                    AND i."FoundationTerminalAt" IS NULL AND i."FoundationTerminalRevision" IS NULL
                    AND i."Jobs" IS NOT DISTINCT FROM {Current.Jobs} AND i."NextJobGeneration" IS NOT DISTINCT FROM {Current.NextJobGeneration}
                FOR UPDATE OF i
                """).ToListAsync(cancellationToken);
            if (locked is not [var prior]) return false;
            var old = DurableKeyJson.Read<RootHeader>(prior.Header);
            if (old.Model != Current.Model || old.Definition != Current.Definition) return false;

            // Compare an old over-budget row under the exact locked revision. The
            // physical write and its projection may each stay put or shrink.
            var contentBytes = projectedBytes - reserve;
            var priorProjection = prior.ContentBytes + Math.Max(0, terminalHeader - prior.HeaderBytes);
            if (overBudget && (db.UsesRootStableBody || contentBytes > Math.Max(RootContentBytes, prior.ContentBytes) ||
                projectedBytes > Math.Max(RootContentBytes, priorProjection)))
                throw new StoreException(Failure.Definite, RootContentFailure);

            if (OperatorSuspension is not null && !db.UsesRootOperatorProtocol || OperatorTermination && !db.UsesRootTermination)
                return false;
            if (TimedJob)
            {
                if (!await ConsumeRootTimedInputsAsync(db, Current.Actor, Inputs, JobCompletion, cancellationToken)) return false;
            }
            else if (Inputs is [var job] && job.Kind == Kinds.Job)
            {
                if (Current.Suspended || JobCompletion is not { } completed ||
                    !await ConsumeRootJobAsync(db, Current.Actor, completed.Original, cancellationToken)) return false;
            }
            else if (!await SettleRootInputsAsync(db, Reference, Current.Actor, Inputs.Select(input => input.InboxKey).ToArray(), Inputs, Issue,
                         Current.Suspended, cancellationToken)) return false;
            if (Recalls is { Length: > 0 })
            {
                // A recall is idempotent. Only an existing private row addressed by this creation changes.
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM "Messages" WHERE "InboxScope" = {RootSelfScope} AND "Id" = ANY({Recalls})
                        AND "Target" = {Current.Actor.Id} AND "Sender" = {Current.Actor.Id} AND "Partition" = {home}
                        AND "Kind" = {RootSelfWire} AND "EventMatch" IS NULL AND "KeepUntil" IS NULL AND NOT "Done"
                    """, cancellationToken);
            }
            var ended = Status is InstanceStatus.Finished or InstanceStatus.Faulted or InstanceStatus.Cancelled;
            var nextJobGeneration = JobState is null ? Current.NextJobGeneration : JobState.NextGeneration;
            // This preview cannot create or discharge qualified waits. A terminal commit may fulfil
            // UnsubscribeAll here only when the exact root has no physical subscriptions.
            var encoded = family + State;
            var updated = await db.Database.ExecuteSqlInterpolatedAsync(AddRootWire(db, $"""
                UPDATE "Instances" SET "State" = {encoded}, "FoundationHeader" = {Header}, "StateVersion" = {StateVersion}, /*root-wire*/
                    "Status" = {(int)Status}, "Revision" = {JournalRevision}, "FoundationWriteRevision" = {JournalRevision},
                    "Suspended" = {OperatorSuspension ?? Current.Suspended},
                    "Jobs" = {jobs}, "NextJobGeneration" = {nextJobGeneration},
                    "FoundationTerminalRevision" = CASE WHEN {ended} THEN {JournalRevision} ELSE NULL END,
                    "FoundationTerminalAt" = CASE WHEN {ended} THEN floor(extract(epoch from clock_timestamp()) * 1000)::bigint ELSE NULL END
                WHERE "Id" = {Current.Actor.Id} AND "Partition" = {home} AND "FoundationTenant" = {Tenant}
                    AND "FoundationReference" = {Reference} AND "Revision" = {Current.Revision}
                    AND (NOT {ended} OR NOT EXISTS (SELECT 1 FROM "Subscriptions"
                        WHERE "Instance" = {Current.Actor.Id}))
                """, Wire), cancellationToken);
            return updated == 1 && (Values is not { Count: > 0 } values ||
                await WriteRootValuesAsync(db, Current.Actor, values, cancellationToken));
        }
    }

    // Both a model turn and a terminal no-turn disposition settle the same due FIFO prefix.
    private static async Task<bool> SettleRootInputsAsync(KernelDb db, string reference, CreationAddress actor, IReadOnlyList<InboxKey> expected,
        Message[]? expectedEnvelopes, FoundationSelfIssuer? issue, bool suspended, CancellationToken cancellationToken)
    {
        var home = new InstanceRef(actor.Id).Partition;
        var source = issue is null ? "\"Messages\"" : "\"FoundationRootMessages\"";
        var operatorOnly = suspended ? """AND "Id" LIKE 'operator/%'""" : "";
        // The settled prefix follows the poll's order, which puts a pending released cohort first.
        var cohort = db.UsesRootParkRecovery
            ? $"""AND EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = m."Target" AND i."Partition" = m."Partition" {RootReleasedCohortFilter})""" : "";
        var rows = await db.Messages.FromSqlRaw($$"""
            SELECT * FROM {{source}} m WHERE "Target" = {0} AND "Partition" = {1}
                AND "InboxScope" IN ({2}, {3}) AND "EventMatch" IS NULL
                AND NOT "Done" AND "Due" <= {{RootClockMilliseconds}} {{operatorOnly}} {{cohort}}
            ORDER BY "Seq" LIMIT {4} FOR UPDATE OF m
            """, actor.Id, home, InboxKey.FoundationOccurrenceScope, RootSelfScope, expected.Count).AsNoTracking().ToListAsync(cancellationToken);
        if (!db.UsesRootOperatorProtocol && rows.Any(row => PostgresRootAdmission.IsOperatorOccurrence(new(row.Id, row.InboxScope))))
            return false;
        var messages = rows.Select(row => DecodeRootInput(db, row, reference, issue)).ToArray();
        if (!SameInputs(messages, expected) || messages.Any(m => m.TargetCreation != actor.Creation) ||
            expectedEnvelopes is not null && !messages.SequenceEqual(expectedEnvelopes)) return false;

        var consumed = expected.Where(k => k.Scope == -1).Select(k => k.Id).ToArray();
        if (consumed.Length > 0 && await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "Messages" WHERE "InboxScope" = {RootSelfScope} AND "Id" = ANY({consumed})
                 AND "Target" = {actor.Id} AND "Partition" = {home} AND NOT "Done" AND "EventMatch" IS NULL AND "KeepUntil" IS NULL
            """, cancellationToken) != consumed.Length) return false;
        var ids = expected.Where(k => k.Scope == InboxKey.FoundationOccurrenceScope).Select(k => k.Id).ToArray();
        if (ids.Length == 0) return true;
        var settled = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" SET "Done" = true, "Payload" = NULL, "Start" = NULL, "ReplyTo" = NULL
            WHERE "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "Id" = ANY({ids}) AND "EventMatch" IS NULL
                AND NOT "Done" AND "Target" = {actor.Id} AND "Partition" = {home} AND "KeepUntil" IS NOT NULL
            """, cancellationToken);
        return settled == ids.Length;
    }

    private static Message DecodeRootInput(KernelDb db, MessageEntity row, string reference, FoundationSelfIssuer? issue, bool jobs = false, bool timed = false) =>
        row.InboxScope switch
        {
            RootJobScope => jobs ? DecodeRootJob(row) : throw RootShape(),
            RootTimedJobScope => timed ? DecodeRootTimedRow(row) : throw RootShape(),
            RootSelfScope => DecodeRootSelf(row, issue),
            _ => PostgresRootAdmission.IsOperatorOccurrence(new(row.Id, row.InboxScope))
                ? PostgresRootAdmission.DecodeOperator(db, row, reference) : PostgresRootAdmission.DecodeDirect(row, reference, db.UsesRootReplyProtocol)
        };

    internal ValueTask<Committed> CommitRootTurnAsync(string tenant, string reference, RootImage current,
        IReadOnlyList<Message> inputs, PartitionFence fence, TimeSpan lease, InstanceStatus status, string? state, int stateVersion,
        string typeData, IReadOnlyDictionary<string, string> labels, string? journal, CancellationToken cancellationToken = default) =>
        CommitPreparedRootAsync(fence, lease, PrepareRootTurn(tenant, reference, current, inputs, fence, status, state, stateVersion, typeData, labels, journal), cancellationToken);

    private PreparedRoot PrepareRootTurn(string tenant, string reference, RootImage current,
        IReadOnlyList<Message> inputs, PartitionFence fence, InstanceStatus status, string? state, int stateVersion,
        string typeData, IReadOnlyDictionary<string, string> labels, string? journal, string[]? recalls = null, bool? operatorSuspension = null,
        RootJobState? jobState = null, RootJobCompletion? jobCompletion = null, bool timedJob = false,
        IReadOnlyList<FoundationValue>? values = null, bool operatorTermination = false, long terminalGrowth = 0)
    {
        if (!postgres || Engine is not null) throw new NotSupportedException("Qualified root turns require PostgreSQL.");
        if (current is not { Revision: >= 1, Status: InstanceStatus.Running, Actor.Creation: 1 } ||
            new InstanceRef(current.Actor.Id).Partition != fence.Partition ||
            status is not (InstanceStatus.Running or InstanceStatus.Finished) && !(timedJob && status == InstanceStatus.Faulted) && !(operatorTermination && status == InstanceStatus.Cancelled))
            throw new ArgumentException("Invalid running root or target disposition.", nameof(current));
        if (status != InstanceStatus.Running && journal is null)
            throw new ArgumentException("A terminal public root requires its journal entry.", nameof(journal));
        var accepted = inputs.ToArray();
        if (accepted.Length == 0 || accepted.Any(message => !QualifiedInput(message) ||
            message.InboxKey.Match is not null || message.Target.Id != current.Actor.Id || message.TargetCreation != current.Actor.Creation) ||
            accepted.Select(message => message.InboxKey).Distinct().Count() != accepted.Length ||
            operatorSuspension is null && !operatorTermination && accepted.Any(message => message.Operator is not null) ||
            operatorTermination && accepted is not [{ Kind: Kinds.Terminate, Operator: not null }])
            throw new ArgumentException("The turn requires distinct qualified inputs for its exact actor creation.", nameof(inputs));
        var stable = RootStableBody;
        var (stored, header) = EncodeRootContent(current.Model, current.Definition, stable && state is not null ? StableBodyMarker : state, stateVersion, typeData, labels, journal);
        var wire = stable && state is not null ? RootSelfContent.Encode(state) : null;
        var image = new InstanceRow(current.Actor.Id, "", "", status, "null", checked(current.Revision + 1), null)
            .WithFoundationState(stored);
        var root = new RootTurnCheckpoint(tenant, reference, current, status, StoredState.Encode(image),
            header, wire, terminalGrowth, stateVersion, accepted, RootSelfIssuer, recalls, operatorSuspension, operatorTermination, jobState, jobCompletion, timedJob, values);
        var turn = new TurnCommit(image.Id, null, null, [], [], [], [], [], journal);
        return new(turn, root);

        bool QualifiedInput(Message message) => timedJob || message.InboxKey.Scope == InboxKey.FoundationOccurrenceScope ||
            jobState is not null && accepted.Length == 1 && RootJobTicket.Read(message, current.Actor.Id, current.Model) is not null ||
            RootSelfIssuer is { } issue && issue.Allows(message.Due) && FoundationSelfSend.IsSelfInput(message, current.Actor.Id, current.Actor.Creation);
    }
}
