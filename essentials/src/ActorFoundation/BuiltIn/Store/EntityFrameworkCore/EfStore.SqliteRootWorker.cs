using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;

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

    internal WorkerRoots BindSqliteWorkerNode()
    {
        using var db = create();
        if (!db.UsesSqliteRootRuntime || postgres || Engine is not null || !OwnedSetsAvailable)
            throw new NotSupportedException("A SQLite root node requires the shipped context and owned commits.");
        return InstallWorkerScope(new(null, [], discover: true));
    }

    // The discovered birth join already limits creators to their exact tenant/reference envelope.
    private static readonly string SqliteOrdinaryRootPollFilter = $"""
        AND m."EventMatch" IS NULL AND m."ReplyTo" IS NULL AND (m."InboxScope" = 0 OR (
            m."InboxScope" = {InboxKey.FoundationOccurrenceScope} AND m."Id" NOT GLOB 'operator/*' AND EXISTS (
                SELECT 1 FROM "Instances" i
                WHERE i."Id" = m."Target" AND i."Partition" = m."Partition"
                    AND i."FoundationReference" = b."Reference" AND i."FoundationTenant" = b."Tenant"
                    AND NOT i."Retired" AND i."FoundationParkAt" IS NULL
                    AND (NOT i."Suspended" OR i."Status" <> {(int)InstanceStatus.Running}))))
        """;

    private static Task<List<WorkerRoot>> ResolveSqliteWorkerRootsAsync(KernelDb db, int? partition, string[] actors,
        string[] references, CancellationToken token) =>
        db.Database.SqlQuery<WorkerRoot>($"""
            SELECT "Tenant", "Reference", "Actor", "Partition", "Fingerprint" FROM "FoundationRootBirths"
            WHERE ({partition} IS NULL OR "Partition" = {partition}) AND "Creation" = 1
                AND ("Actor" IN (SELECT value FROM json_each({Json.Write(actors)}))
                    OR "Reference" IN (SELECT value FROM json_each({Json.Write(references)})))
            """).ToListAsync(token);

    private async Task<Message?> ReadSqlitePendingCreatorAsync(string tenant, string reference, CancellationToken token)
    {
        await using var db = create();
        await RequireSqliteOperationalFormatAsync(db, token);
        var row = await ReadSqliteCreatorAsync(db, tenant, reference, fingerprint: null, token);
        return row is null ? null : PostgresRootAdmission.DecodeCreator(row);
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
                AND m."EventMatch" IS NULL AND m."ReplyTo" IS NULL
                AND ({fingerprint} IS NULL OR b."Fingerprint" = {fingerprint} AND m."Due" <= /*sqlite-root-clock*/)
            """;
        return db.Messages.FromSqlInterpolated(UseSqliteRootClock(sql)).AsNoTracking().SingleOrDefaultAsync(token);
    }

    private static async Task<bool> BindSqliteFirstRootAsync(KernelDb db, FirstRootCheckpoint checkpoint, CancellationToken token)
    {
        var creator = checkpoint.Creator;
        var fingerprint = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload);
        if (fingerprint is null) return false;
        var pending = await ReadSqliteCreatorAsync(db, checkpoint.Tenant, checkpoint.Reference, fingerprint, token);
        if (pending is null || !checkpoint.MatchesCreator(PostgresRootAdmission.DecodeCreator(pending))) return false;
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "Messages" WHERE "Id" = {pending.Id} AND "InboxScope" = 0
                AND "Target" = {pending.Target} AND "Partition" = {pending.Partition} AND NOT "Done"
            """, token) != 1) return false;

        var finished = checkpoint.Row.Status == InstanceStatus.Finished;
        var family = RootStatePrefix(db);
        var revision = checkpoint.JournalRevision;
        FormattableString update = $"""
            UPDATE "Instances" SET "FoundationReference" = {checkpoint.Reference}, "FoundationTenant" = {checkpoint.Tenant},
                "FoundationHeader" = {checkpoint.Header}, "FoundationStateWire" = {checkpoint.Wire}, "State" = {family} || "State",
                "FoundationTerminalRevision" = CASE WHEN {finished} THEN {revision} END,
                "FoundationTerminalAt" = CASE WHEN {finished} THEN /*sqlite-root-clock*/ END
            WHERE "Id" = {pending.Target} AND "Partition" = {pending.Partition} AND "Revision" = {revision}
                AND "Status" = {(int)checkpoint.Row.Status} AND "FoundationReference" IS NULL
                AND (NOT {finished} OR NOT EXISTS (SELECT 1 FROM "Subscriptions" WHERE "Instance" = {pending.Target}))
            """;
        return await db.Database.ExecuteSqlInterpolatedAsync(UseSqliteRootClock(update), token) == 1;
    }

    private static async Task<bool> BindSqliteRootTurnAsync(KernelDb db, RootTurnCheckpoint turn, CancellationToken token)
    {
        if (turn.Status is not (InstanceStatus.Running or InstanceStatus.Finished) || turn.Current.Suspended ||
            turn.OperatorSuspension is not null || turn.OperatorTermination || turn.JobState is not null ||
            turn.JobCompletion is not null || turn.TimedJob || turn.Values is not null || turn.SettledJobs is not null ||
            turn.Issue is not null || turn.Recalls is not null)
            throw new NotSupportedException("The staged SQLite root worker supports ordinary checkpoints.");

        var family = RootStatePrefix(db);
        var finished = turn.Status == InstanceStatus.Finished;
        var terminalHeader = finished ? 0 : RootTerminalHeaderBytes(turn.Current.Model, turn.Current.Definition);
        var reserve = Math.Max(0, terminalHeader - Encoding.UTF8.GetByteCount(turn.Header));
        if (StableContentBytes(family, turn.State, turn.Header, turn.Wire, null, reserve, finished ? 0 : turn.Growth) > RootContentBytes)
            throw new StoreException(Failure.Definite, "The encoded SQLite root checkpoint exceeds its storage budget.");

        var actor = turn.Current.Actor;
        var home = new InstanceRef(actor.Id).Partition;
        var prior = await db.Database.SqlQuery<string>($"""
            SELECT i."FoundationHeader" AS "Value" FROM "Instances" i JOIN "FoundationRootBirths" b
                ON b."Reference" = i."FoundationReference" AND b."Tenant" = i."FoundationTenant"
                AND b."Actor" = i."Id" AND b."Partition" = i."Partition"
            WHERE b."Reference" = {turn.Reference} AND b."Tenant" = {turn.Tenant} AND b."Creation" = {actor.Creation}
                AND i."Id" = {actor.Id} AND i."Partition" = {home} AND i."Revision" = {turn.Current.Revision}
                AND i."Status" = {(int)InstanceStatus.Running} AND NOT i."Retired" AND NOT i."Suspended"
                AND i."FoundationParkAt" IS NULL AND i."FoundationTerminalAt" IS NULL AND i."FoundationTerminalRevision" IS NULL
                AND i."Jobs" IS NULL AND i."NextJobGeneration" IS NOT DISTINCT FROM {turn.Current.NextJobGeneration}
            """).SingleOrDefaultAsync(token);
        if (prior is null) return false;
        var header = DurableKeyJson.Read<RootHeader>(prior);
        if (header.Model != turn.Current.Model || header.Definition != turn.Current.Definition) return false;

        if (!await SettleSqliteRootInputsAsync(db, turn.Reference, actor,
            turn.Inputs.Select(input => input.InboxKey).ToArray(), turn.Inputs, token)) return false;

        var revision = turn.JournalRevision;
        FormattableString update = $"""
            UPDATE "Instances" SET "State" = {family + turn.State}, "FoundationHeader" = {turn.Header},
                "FoundationStateWire" = {turn.Wire}, "StateVersion" = {turn.StateVersion}, "Status" = {(int)turn.Status},
                "Revision" = {revision}, "FoundationWriteRevision" = {revision},
                "FoundationTerminalRevision" = CASE WHEN {finished} THEN {revision} END,
                "FoundationTerminalAt" = CASE WHEN {finished} THEN /*sqlite-root-clock*/ END
            WHERE "Id" = {actor.Id} AND "Partition" = {home} AND "Revision" = {turn.Current.Revision}
                AND "FoundationTenant" = {turn.Tenant} AND "FoundationReference" = {turn.Reference}
                AND (NOT {finished} OR NOT EXISTS (SELECT 1 FROM "Subscriptions" WHERE "Instance" = {actor.Id}))
            """;
        return await db.Database.ExecuteSqlInterpolatedAsync(UseSqliteRootClock(update), token) == 1;
    }

    private static async Task<bool> SettleSqliteRootInputsAsync(KernelDb db, string reference, CreationAddress actor,
        IReadOnlyList<InboxKey> expected, Message[]? expectedEnvelopes, CancellationToken token)
    {
        if (expected.Any(key => key.Scope != InboxKey.FoundationOccurrenceScope || key.Match is not null ||
            PostgresRootAdmission.IsOperatorOccurrence(key))) return false;
        if (expected.Count == 0) return true;
        var home = new InstanceRef(actor.Id).Partition;
        FormattableString ready = $"""
            SELECT * FROM "Messages" WHERE "Target" = {actor.Id} AND "Partition" = {home}
                AND "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "EventMatch" IS NULL AND NOT "Done"
                AND "ReplyTo" IS NULL AND "Due" <= /*sqlite-root-clock*/ AND "Id" NOT GLOB 'operator/*'
            ORDER BY "Seq" LIMIT {expected.Count}
            """;
        var rows = await db.Messages.FromSqlInterpolated(UseSqliteRootClock(ready)).AsNoTracking().ToListAsync(token);
        var messages = rows.Select(row => PostgresRootAdmission.DecodeDirect(row, reference)).ToArray();
        if (!SameInputs(messages, expected) || messages.Any(message => message.TargetCreation != actor.Creation) ||
            expectedEnvelopes is not null && !messages.SequenceEqual(expectedEnvelopes)) return false;
        var ids = Json.Write(expected.Select(key => key.Id).ToArray());
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" SET "Done" = 1, "Payload" = NULL, "Start" = NULL, "ReplyTo" = NULL
            WHERE "Target" = {actor.Id} AND "Partition" = {home} AND "InboxScope" = {InboxKey.FoundationOccurrenceScope}
                AND "Id" IN (SELECT value FROM json_each({ids})) AND "EventMatch" IS NULL AND "KeepUntil" IS NOT NULL
                AND NOT "Done" AND "ReplyTo" IS NULL
            """, token) == expected.Count;
    }

    private static Task<List<long>> ReadSqliteFinishedRevisionAsync(KernelDb db, WorkerRoot root, CancellationToken token) =>
        db.Database.SqlQuery<long>($"""
            SELECT i."Revision" AS "Value" FROM "Instances" i JOIN "FoundationRootBirths" b
                ON b."Actor" = i."Id" AND b."Partition" = i."Partition"
                AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
            WHERE b."Tenant" = {root.Tenant} AND b."Reference" = {root.Reference} AND b."Actor" = {root.Actor}
                AND b."Creation" = 1 AND i."Partition" = {root.Partition} AND NOT i."Retired"
                AND i."Status" = {(int)InstanceStatus.Finished} AND i."FoundationTerminalAt" IS NOT NULL
                AND i."FoundationTerminalRevision" = i."Revision"
            """).ToListAsync(token);

    private static FormattableString UseSqliteRootClock(FormattableString sql) =>
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
