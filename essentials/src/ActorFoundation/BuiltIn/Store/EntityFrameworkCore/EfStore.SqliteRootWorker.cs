using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private void RequireQualifiedRootWriter()
    {
        if (Engine is null && postgres) return;
        using var db = create();
        if (Engine is not null || !db.UsesSqliteRootRuntime || !OwnedCommit)
            throw new NotSupportedException("Qualified checkpoints require PostgreSQL or the owned SQLite root writer.");
    }

    internal WorkerRoots BindSqliteWorkerNode(Func<Contract.ActorHeader, bool>? withholds = null)
    {
        using var db = create();
        if (!db.UsesSqliteRootRuntime || postgres || Engine is not null || !OwnedSetsAvailable)
            throw new NotSupportedException("A SQLite root node requires the shipped context and owned commits.");
        // Ended roots settle their physical FIFO without a model, including actors that withhold by header alone.
        return InstallWorkerScope(new(null, [], discover: true) { AuthoredWithholds = withholds, SettlesEndedMailbox = true });
    }

    // The discovered birth join already limits creators to their exact tenant/reference envelope.
    private static string SqliteRootPollScope(bool includeParkArrivals, bool recovery, bool authoredCohort)
    {
        var withheld = authoredCohort ? $"""
            OR (m."InboxScope" = {InboxKey.FoundationOccurrenceScope} AND m."Id" GLOB 'operator/*' AND {SqliteCohortDecision})
            """ : "";
        var cohort = recovery
            ? $"""AND (i."FoundationParkAt" IS NOT NULL {withheld} OR {NoEarlierReleasedCohort})"""
            : "";
        return $"""
        AND m."EventMatch" IS NULL AND (m."InboxScope" = 0 OR (
            m."InboxScope" IN ({RootTimedJobScope}, {InboxKey.FoundationOccurrenceScope}/*root-self-scope*/) AND EXISTS (
                SELECT 1 FROM "Instances" i
                WHERE i."Id" = m."Target" AND i."Partition" = m."Partition"
                    AND i."FoundationReference" = b."Reference" AND i."FoundationTenant" = b."Tenant"
                    AND NOT i."Retired" AND (i."FoundationParkAt" IS NULL OR
                        m."InboxScope" = {InboxKey.FoundationOccurrenceScope} AND m."Id" GLOB 'operator/*'
                        {(includeParkArrivals ? """OR (m."InboxScope" IN (/*park-input-scopes*/) AND i."FoundationParkClearing" IS NULL AND m."ReplyTo" IS NULL)""" : "")})
                    {cohort})
            AND (m."Id" NOT GLOB 'operator/*' OR ({SqliteSupportedOperator("m.", recovery)}))))
        """;
    }

    // Unsupported stored operator kinds stay pending and do not block the current operator FIFO.
    private static string SqliteSupportedOperator(string columnPrefix, bool recovery = false)
    {
        var address = $"""{columnPrefix}"ReplyTo" """;
        var header = $"""{columnPrefix}"Start" """;
        var wire = $"(CASE WHEN {address} IS NULL THEN {header} ELSE substr({header}, {PostgresRootAdmission.HeldOperatorPrefix.Length + 1}) END)";
        var payload = $"substr({wire}, {PostgresRootAdmission.OperatorPrefix.Length + 1})";
        var recover = recovery ? $"""
            OR ({columnPrefix}"Kind" = '{Kinds.Recover}' AND {address} IS NULL AND {wire} GLOB '{PostgresRootAdmission.OperatorPrefix}*')
            """ : "";
        return $"""
        {columnPrefix}"Id" GLOB 'operator/*'
        AND ({address} IS NULL OR {header} GLOB '{PostgresRootAdmission.HeldOperatorPrefix}*')
        AND (({columnPrefix}"Kind" = '{Kinds.Terminate}' AND
                ({wire} GLOB '{PostgresRootAdmission.OperatorPrefix}*' OR {wire} GLOB '{PostgresRootAdmission.TerminateThenDeletePrefix}*'))
            OR ({columnPrefix}"Kind" = '{Kinds.Delete}' AND
                ({wire} GLOB '{PostgresRootAdmission.OperatorPrefix}*' OR
                    ({address} IS NULL AND {wire} GLOB '{PostgresRootAdmission.RetentionDeletePrefix}*'))){recover})
        AND json_valid({payload})
        """;
    }

    internal static Task<bool> SqliteHeldOperatorCapacityReachedAsync(KernelDb db, string actor, int partition, CancellationToken token) =>
        db.Database.SqlQuery<bool>(WithSqliteOperatorPredicate(db, $"""
            SELECT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {actor} AND "Partition" = {partition}
                AND "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND NOT "Done" AND "ReplyTo" IS NOT NULL
                AND (/*supported-operator*/) LIMIT 1 OFFSET {PostgresRootAdmission.HeldReplyLimit - 1}) AS "Value"
            """)).SingleAsync(token);

    private static FormattableString WithSqliteOperatorPredicate(KernelDb db, FormattableString sql) =>
        FormattableStringFactory.Create(
            sql.Format.Replace("/*supported-operator*/", SqliteSupportedOperator("", db.UsesRootParkRecovery), StringComparison.Ordinal), sql.GetArguments());

    private static Task<List<WorkerRoot>> ResolveSqliteWorkerRootsAsync(KernelDb db, int? partition, string[] actors,
        string[] references, CancellationToken token) =>
        db.Database.SqlQuery<WorkerRoot>($"""
            SELECT "Tenant", "Reference", "Actor", "Partition", "Fingerprint" FROM "FoundationRootBirths"
            WHERE ({partition} IS NULL OR "Partition" = {partition}) AND "Creation" = 1
                AND ("Actor" IN (SELECT value FROM json_each({Json.Write(actors)}))
                    OR "Reference" IN (SELECT value FROM json_each({Json.Write(references)})))
            """).ToListAsync(token);

    private sealed record SqliteWorkerLoad(string Tenant, string Reference, string Actor, int Partition, string Fingerprint,
        string? Id, long Creation, string? State, byte[]? Wire, int? StateVersion, long? Revision, string? Header,
        History? History, InstanceStatus? Status, bool? Suspended, long? TerminalRevision, long? TerminalAt, long AsOf,
        string? Jobs, long? NextJobGeneration, string? Fault, long? ParkAt, string? Parent, string? Children);

    private async Task<IReadOnlyList<InstanceRow>> LoadSqliteWorkerRootsAsync(KernelDb db, WorkerRoots scope,
        IReadOnlyCollection<string> ids, CancellationToken token)
    {
        await db.Database.OpenConnectionAsync(token);
        await RequireSqliteOperationalFormatAsync(db, token);
        var actors = ids.Distinct(StringComparer.Ordinal).ToArray();
        if (actors.Length == 0) return [];
        // One bounded lookup ties each requested birth to that life's checkpoint. A pending or retired life
        // still has its birth and returns no checkpoint. A missing birth is a malformed worker request.
        var rows = await db.Database.SqlQuery<SqliteWorkerLoad>(UseSqliteRootClock($"""
            SELECT b."Tenant", b."Reference", b."Actor", b."Partition", b."Fingerprint",
                i."Id", b."Creation", i."State", i."FoundationStateWire" AS "Wire", i."StateVersion", i."Revision",
                i."FoundationHeader" AS "Header", i."History", i."Status", i."Suspended",
                i."FoundationTerminalRevision" AS "TerminalRevision", i."FoundationTerminalAt" AS "TerminalAt",
                /*sqlite-root-clock*/ AS "AsOf", i."Jobs", i."NextJobGeneration", i."Fault", i."FoundationParkAt" AS "ParkAt",
                i."Parent", i."Children"
            FROM json_each({Json.Write(actors)}) request
            JOIN "FoundationRootBirths" b ON b."Actor" = request.value AND b."Creation" = 1
            LEFT JOIN "Instances" i ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                AND i."FoundationReference" = b."Reference" AND i."FoundationTenant" = b."Tenant" AND NOT i."Retired"
            ORDER BY request.key
            """)).ToListAsync(token);
        if (rows.Count != actors.Length) throw RootShape();
        var result = new List<InstanceRow>(rows.Count);
        foreach (var row in rows)
        {
            if (!scope.Includes(row.Partition) || new InstanceRef(row.Actor).Partition != row.Partition) throw RootShape();
            if (row.Id is not { } id) continue;
            if (row is not { State: { } state, StateVersion: { } version, Revision: { } revision, Header: { } header,
                    History: { } history, Status: { } status, Suspended: { } suspended }) throw RootShape();
            var binding = new WorkerRoot(row.Tenant, row.Reference, row.Actor, row.Partition, row.Fingerprint);
            var physical = new PhysicalRoot(id, row.Creation, state, row.Wire, version, revision, header, history, status,
                suspended, row.TerminalRevision, row.TerminalAt, row.AsOf, row.Jobs, row.NextJobGeneration, row.Fault, row.ParkAt,
                row.Parent, row.Children);
            result.Add(WorkerRow(binding, DecodePhysicalRoot(db, physical)));
        }
        return result;
    }

    private async Task<Message?> ReadSqlitePendingCreatorAsync(string tenant, string reference, CancellationToken token)
    {
        await using var db = create();
        await RequireSqliteOperationalFormatAsync(db, token);
        var row = await ReadSqliteCreatorAsync(db, tenant, reference, fingerprint: null, token);
        return row is null ? null : DecodeSqliteCreator(row);
    }

    private static Message DecodeSqliteCreator(MessageEntity row)
    {
        if (row.ReplyTo is not null && (row.Due != 0 ||
            ReplyTo.Parse(row.ReplyTo) is not { Node.Length: > 0, Request.Length: > 0, Deadline: > 0 }))
            throw new StoreException(Failure.Definite, "Invalid SQLite held creator reply route.");
        return PostgresRootAdmission.DecodeCreator(row);
    }

    private static Task<MessageEntity?> ReadSqliteCreatorAsync(KernelDb db, string tenant, string reference, string? fingerprint,
        CancellationToken token)
    {
        // A bind adds the readiness and fingerprint check to the same identity used by the prepare read.
        FormattableString sql = $"""
            SELECT m.* FROM "Messages" m JOIN "FoundationRootBirths" b
                ON b."Creator" = m."Id" AND b."Actor" = m."Target" AND b."Partition" = m."Partition"
            WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND b."Creation" = 1
                AND m."InboxScope" = 0 AND NOT m."Done" AND m."KeepUntil" IS NULL
                AND m."EventMatch" IS NULL
                AND ({fingerprint} IS NULL OR b."Fingerprint" = {fingerprint} AND m."Due" <= /*sqlite-root-clock*/)
            """;
        return db.Messages.FromSqlInterpolated(UseSqliteRootClock(sql)).AsNoTracking().SingleOrDefaultAsync(token);
    }

    private static async Task<bool> BindSqliteFirstRootAsync(KernelDb db, FirstRootCheckpoint checkpoint, CancellationToken token)
    {
        var creator = checkpoint.Creator;
        var fingerprint = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload);
        if (fingerprint is null) return false;
        var parked = checkpoint.Row is { Status: InstanceStatus.Faulted, Fault: not null };
        var pending = parked
            ? await ReadSqliteCreatorAsync(db, checkpoint.Tenant, checkpoint.Reference, fingerprint, token)
            : await ConsumeSqliteCreatorAsync(db, checkpoint, fingerprint, token);
        if (pending is null || !checkpoint.MatchesCreator(DecodeSqliteCreator(pending))) return false;
        if (parked && !await KeepSqliteParkInputsAsync(db, pending.Target, pending.Partition, 0, [pending.Id], ParkKeepReason.Consumed, token))
            return false;

        var ended = !parked && db.RootStateFamily.Ends(checkpoint.Row.Status);
        var family = RootStatePrefix(db);
        var revision = checkpoint.JournalRevision;
        FormattableString update = $"""
            UPDATE "Instances" SET "FoundationReference" = {checkpoint.Reference}, "FoundationTenant" = {checkpoint.Tenant},
                "FoundationHeader" = {checkpoint.Header}, "FoundationStateWire" = {checkpoint.Wire}, "State" = {family} || "State",
                "FoundationTerminalRevision" = CASE WHEN {ended} THEN {revision} END,
                "FoundationTerminalAt" = CASE WHEN {ended} THEN /*sqlite-root-clock*/ END,
                "FoundationParkAt" = CASE WHEN {parked} THEN /*sqlite-root-clock*/ END,
                "FoundationParkRevision" = CASE WHEN {parked} THEN {revision} END
            WHERE "Id" = {pending.Target} AND "Partition" = {pending.Partition} AND "Revision" = {revision}
                AND "Status" = {(int)checkpoint.Row.Status} AND "FoundationReference" IS NULL
                AND (NOT {ended} OR NOT EXISTS (SELECT 1 FROM "Subscriptions" WHERE "Instance" = {pending.Target}))
            """;
        return await db.Database.ExecuteSqlInterpolatedAsync(UseSqliteRootClock(update), token) == 1 &&
            (!parked || await VerifySqliteParkProjectionAsync(db, checkpoint.Tenant, checkpoint.Reference, revision, checkpoint.Row.Fault!, token));
    }

    private static async Task<MessageEntity?> ConsumeSqliteCreatorAsync(KernelDb db, FirstRootCheckpoint checkpoint, string fingerprint,
        CancellationToken token)
    {
        var creator = checkpoint.Creator;
        // This runs under the write fence. A changed returned envelope refuses the checkpoint and rolls back its deletion.
        var rows = await db.Messages.FromSqlInterpolated(UseSqliteRootClock($"""
            DELETE FROM "Messages" AS m
            WHERE m."Id" = {creator.InboxKey.Id} AND m."Target" = {creator.Target.Id} AND m."Partition" = {creator.Target.Partition}
                AND m."InboxScope" = 0 AND NOT m."Done" AND m."KeepUntil" IS NULL AND m."EventMatch" IS NULL
                AND m."Due" <= /*sqlite-root-clock*/
                AND EXISTS (SELECT 1 FROM "FoundationRootBirths" b
                    WHERE b."Tenant" = {checkpoint.Tenant} AND b."Reference" = {checkpoint.Reference}
                        AND b."Creation" = 1 AND b."Fingerprint" = {fingerprint}
                        AND b."Creator" = m."Id" AND b."Actor" = m."Target" AND b."Partition" = m."Partition")
            RETURNING *
            """)).AsNoTracking().ToListAsync(token);
        return rows is [var message] ? message : null;
    }

    private static async Task<bool> BindSqliteRootTurnAsync(KernelDb db, RootTurnCheckpoint turn, CancellationToken token)
    {
        var timedFault = turn.Status == InstanceStatus.Faulted && db.RootStateFamily.Ends(turn.Status);
        var terminated = turn.OperatorTermination && db.UsesRootTermination && turn.Status == InstanceStatus.Cancelled;
        if (turn.Status is not (InstanceStatus.Running or InstanceStatus.Finished) && !timedFault && !terminated ||
            turn.Current.Suspended && !terminated || turn.OperatorSuspension is not null ||
            !turn.TimedJob && turn.JobCompletion is not null || turn.Values is not null ||
            (turn.Issue is not null || turn.Recalls is not null) && !db.UsesRootSelfProtocol)
            throw new NotSupportedException("The staged SQLite root worker supports ordinary and timed job checkpoints.");

        var family = RootStatePrefix(db);
        var ended = turn.Status != InstanceStatus.Running;
        var terminalHeader = ended ? 0 : RootTerminalHeaderBytes(turn.Current.Model, turn.Current.Definition);
        var reserve = Math.Max(0, terminalHeader - Encoding.UTF8.GetByteCount(turn.Header));
        var jobs = turn.JobState is null ? turn.Current.Jobs : turn.JobState.Jobs;
        var nextGeneration = turn.JobState is null ? turn.Current.NextJobGeneration : turn.JobState.NextGeneration;
        if (StableContentBytes(family, turn.State, turn.Header, turn.Wire, jobs, reserve, ended ? 0 : turn.Growth) > RootContentBytes)
            throw new StoreException(Failure.Definite, "The encoded SQLite root checkpoint exceeds its storage budget.");

        var actor = turn.Current.Actor;
        var home = new InstanceRef(actor.Id).Partition;
        var prior = await db.Database.SqlQuery<string>($"""
            SELECT i."FoundationHeader" AS "Value" FROM "Instances" i JOIN "FoundationRootBirths" b
                ON b."Reference" = i."FoundationReference" AND b."Tenant" = i."FoundationTenant"
                AND b."Actor" = i."Id" AND b."Partition" = i."Partition"
            WHERE b."Reference" = {turn.Reference} AND b."Tenant" = {turn.Tenant} AND b."Creation" = {actor.Creation}
                AND i."Id" = {actor.Id} AND i."Partition" = {home} AND i."Revision" = {turn.Current.Revision}
                AND i."Status" = {(int)InstanceStatus.Running} AND NOT i."Retired" AND i."Suspended" = {turn.Current.Suspended}
                AND i."FoundationParkAt" IS NULL AND i."FoundationTerminalAt" IS NULL AND i."FoundationTerminalRevision" IS NULL
                AND i."Jobs" IS NOT DISTINCT FROM {turn.Current.Jobs}
                AND i."NextJobGeneration" IS NOT DISTINCT FROM {turn.Current.NextJobGeneration}
            """).SingleOrDefaultAsync(token);
        if (prior is null) return false;
        var header = DurableKeyJson.Read<RootHeader>(prior);
        if (header.Model != turn.Current.Model || header.Definition != turn.Current.Definition) return false;

        var settled = turn.TimedJob
            ? await ConsumeRootTimedInputsAsync(db, actor, turn.Inputs, turn.JobCompletion, token)
            : await SettleSqliteRootInputsAsync(db, turn.Reference, actor,
                turn.Inputs.Select(input => input.InboxKey).ToArray(), turn.Inputs, turn.Current.Suspended || turn.WithheldInputs,
                token, issue: turn.Issue);
        if (!settled) return false;
        if (turn.Recalls is { Length: > 0 } recalls)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "Messages" WHERE "InboxScope" = {RootSelfScope}
                    AND "Id" IN (SELECT value FROM json_each({Json.Write(recalls)}))
                    AND "Target" = {actor.Id} AND "Sender" = {actor.Id} AND "Partition" = {home}
                        AND ("Kind" = {RootSelfWire} OR {db.UsesRootPrivateParkInputs} AND "Kind" = {RootSelfTimerWire})
                        AND "EventMatch" IS NULL AND "KeepUntil" IS NULL AND NOT "Done"
                """, token);
        if (turn.SettledJobs is { Length: > 0 } jobIds && !await DeleteRootTimedRowsAsync(db, actor, jobIds, token)) return false;

        if (ended && !await ClearTerminalRootSelfAsync(db, actor, home, turn.Current.Revision, turn.Issue, includeImmediate: false, token))
            return false;
        var revision = turn.JournalRevision;
        FormattableString update = $"""
            UPDATE "Instances" SET "State" = {family + turn.State}, "FoundationHeader" = {turn.Header},
                "FoundationStateWire" = {turn.Wire}, "StateVersion" = {turn.StateVersion}, "Status" = {(int)turn.Status},
                "Revision" = {revision}, "FoundationWriteRevision" = {revision},
                "Jobs" = {jobs}, "NextJobGeneration" = {nextGeneration},
                "FoundationTerminalRevision" = CASE WHEN {ended} THEN {revision} END,
                "FoundationTerminalAt" = CASE WHEN {ended} THEN /*sqlite-root-clock*/ END
            WHERE "Id" = {actor.Id} AND "Partition" = {home} AND "Revision" = {turn.Current.Revision}
                AND "FoundationTenant" = {turn.Tenant} AND "FoundationReference" = {turn.Reference}
                AND (NOT {ended} OR NOT EXISTS (SELECT 1 FROM "Subscriptions" WHERE "Instance" = {actor.Id}))
            """;
        return await db.Database.ExecuteSqlInterpolatedAsync(UseSqliteRootClock(update), token) == 1;
    }

    private static async Task<bool> SettleSqliteRootInputsAsync(KernelDb db, string reference, CreationAddress actor,
        IReadOnlyList<InboxKey> expected, Message[]? expectedEnvelopes, bool suspended, CancellationToken token, bool parked = false,
        FoundationSelfIssuer? issue = null)
    {
        if (expected.Any(key => key.Match is not null ||
            key.Scope != InboxKey.FoundationOccurrenceScope && (issue is null || SelfActor(key) != actor.Id))) return false;
        if (expected.Count == 0) return true;
        var home = new InstanceRef(actor.Id).Partition;
        var includePrivate = issue is not null || db.UsesRootPrivateParkInputs && parked && !suspended;
        FormattableString ready = $"""
            SELECT m.* FROM "Messages" m WHERE "Target" = {actor.Id} AND "Partition" = {home}
                AND ("InboxScope" = {InboxKey.FoundationOccurrenceScope} OR {includePrivate} AND "InboxScope" = {RootSelfScope})
                AND "EventMatch" IS NULL AND NOT "Done"
                AND "Due" <= /*sqlite-root-clock*/ AND (NOT {suspended} OR "Id" GLOB 'operator/*')
                AND (NOT {parked && db.UsesRootParkArrivals} OR "ReplyTo" IS NULL OR "Id" GLOB 'operator/*')
                AND ("Id" NOT GLOB 'operator/*' OR (/*supported-operator*/)) /*released-cohort*/
            ORDER BY "Seq" LIMIT {expected.Count}
            """;
        var cohort = db.UsesRootParkRecovery && !parked && !suspended
            ? $"""AND EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = m."Target" AND i."Partition" = m."Partition" {RootReleasedCohortFilter(db)})"""
            : "";
        var eligible = WithSqliteOperatorPredicate(db, UseSqliteRootClock(FormattableStringFactory.Create(
            ready.Format.Replace("/*released-cohort*/", cohort, StringComparison.Ordinal), ready.GetArguments())));
        var rows = await db.Messages.FromSqlInterpolated(eligible).AsNoTracking().ToListAsync(token);
        if (issue is null && rows.Any(row => row.InboxScope == RootSelfScope))
            return false;
        var messages = rows.Select(row => DecodeRootInput(db, row, reference, issue)).ToArray();
        if (!SameInputs(messages, expected) || messages.Any(message => message.TargetCreation != actor.Creation) ||
            expectedEnvelopes is not null && !messages.SequenceEqual(expectedEnvelopes)) return false;
        var self = expected.Where(key => key.Scope == -1).Select(key => key.Id).ToArray();
        if (self.Length > 0 && await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "Messages" WHERE "InboxScope" = {RootSelfScope}
                AND "Id" IN (SELECT value FROM json_each({Json.Write(self)})) AND "Target" = {actor.Id}
                AND "Partition" = {home} AND "EventMatch" IS NULL AND "KeepUntil" IS NULL AND NOT "Done"
            """, token) != self.Length) return false;
        var outside = expected.Where(key => key.Scope == InboxKey.FoundationOccurrenceScope).Select(key => key.Id).ToArray();
        if (outside.Length == 0) return true;
        var ids = Json.Write(outside);
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" SET "Done" = 1, "Payload" = NULL, "Start" = NULL, "ReplyTo" = NULL
            WHERE "Target" = {actor.Id} AND "Partition" = {home} AND "InboxScope" = {InboxKey.FoundationOccurrenceScope}
                AND "Id" IN (SELECT value FROM json_each({ids})) AND "EventMatch" IS NULL AND "KeepUntil" IS NOT NULL
                AND NOT "Done"
            """, token) == outside.Length;
    }

    private static Task<List<long>> ReadSqliteTerminalRevisionAsync(KernelDb db, WorkerRoot root, CancellationToken token) =>
        db.Database.SqlQuery<long>($"""
            SELECT i."Revision" AS "Value" FROM "Instances" i JOIN "FoundationRootBirths" b
                ON b."Actor" = i."Id" AND b."Partition" = i."Partition"
                AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
            WHERE b."Tenant" = {root.Tenant} AND b."Reference" = {root.Reference} AND b."Actor" = {root.Actor}
                AND b."Creation" = 1 AND i."Partition" = {root.Partition} AND NOT i."Retired"
                AND (i."Status" = {(int)InstanceStatus.Finished} OR
                    (({db.RootStateFamily.EndsFaulted} AND i."Status" = {(int)InstanceStatus.Faulted} OR
                        {db.RootStateFamily.EndsCancelled} AND i."Status" = {(int)InstanceStatus.Cancelled})
                        AND instr(i."State", {db.RootStateFamily.Prefix}) = 1 AND i."FoundationParkAt" IS NULL))
                AND i."FoundationTerminalAt" IS NOT NULL
                AND i."FoundationTerminalRevision" = i."Revision"
            """).ToListAsync(token);

    internal static FormattableString UseSqliteRootClock(FormattableString sql) =>
        FormattableStringFactory.Create(
            sql.Format.Replace("/*sqlite-root-clock*/", SqliteCommitDialect.MillisecondClock, StringComparison.Ordinal), sql.GetArguments());

    private static Task<int> MarkSqliteRootAsync(KernelDb db, WorkerRoot root, long epoch, bool trial, long scope, string[] ids,
        CancellationToken token) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" SET "Seen" = {epoch}, "Trial" = CASE WHEN {trial} THEN {epoch} ELSE "Trial" END
            WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition} AND "InboxScope" = {scope}
                AND "Id" IN (SELECT value FROM json_each({Json.Write(ids)})) AND "EventMatch" IS NULL AND NOT "Done"
            """, token);
}
