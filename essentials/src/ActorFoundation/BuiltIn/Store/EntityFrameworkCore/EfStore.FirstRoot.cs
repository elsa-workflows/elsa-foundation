using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // The generic load treats this as an unknown state family and leaves its bytes intact.
    private const string StableBodyMarker = "wire";
    private const string StableBodyFamily = StoredState.Family + "12:public-root-stable-body:";
    internal static string RootTerminationStateFamily(KernelDb db) => db.UsesRootStableBody ? StableBodyFamily : TerminationRootStateFamily;
    private bool? _rootStableBody;
    private bool RootStableBody => _rootStableBody ??= ReadRootStableBody();

    private bool ReadRootStableBody()
    {
        using var db = create();
        return db.UsesRootStableBody;
    }

    private const int RootContentBytes = 16 * 1024 * 1024 - 24 * 1024;

    private const string PublicRootState = StoredState.Family + "4:public-root:";
    internal const string TerminationRootStateFamily = StoredState.Family + "11:public-root-termination:";
    internal const string TimedRootStateFamily = StoredState.Family + "6:public-root-timed-jobs:";
    private static string RootStateFamily(KernelDb db) => db switch
    {
        { UsesRootValueProtocol: true } => StoredState.Family + "5:public-root-values:",
        { UsesRootTimedJobSettlement: true } => TimedRootStateFamily,
        { UsesRootTermination: true } => RootTerminationStateFamily(db),
        { UsesRootReplyProtocol: true } => StoredState.Family + "7:public-root-replies:",
        _ => PublicRootState
    };

    internal sealed record RootImage(CreationAddress Actor, string Model, string Definition, string TypeData,
        IReadOnlyDictionary<string, string> Labels, string? State, int StateVersion, long Revision)
    {
        internal string? Fault { get; init; }
        internal string? Jobs { get; init; }
        internal long? NextJobGeneration { get; init; }
        internal History History { get; init; }
        internal InstanceStatus Status { get; init; }
        internal bool Suspended { get; init; }
        internal long? TerminalRevision { get; init; }
        internal long? TerminalAt { get; init; }
        internal long AsOf { get; init; }
    }

    private sealed record RootHeader(string Model, string Definition, string TypeData, KeyValuePair<string, string>[] Labels, bool EscapedState);

    // DurableKeyJson writes JSON escapes of up to six bytes per admitted UTF-8 byte.
    // A tagged UTF-16 wire is smaller than that bound. Charge all four label wrappers
    // as well as the two bounded label strings in each wrapper.
    private static readonly int RootVariableHeaderBytes = 6 * HeaderLimits.VariableHeaderReserve +
        DurableKeyJson.Write(Enumerable.Repeat(new KeyValuePair<string, string>("", ""), HeaderLimits.MaxLabels).ToArray()).Length - 2;

    private static int RootTerminalHeaderBytes(string model, string definition) =>
        Encoding.UTF8.GetByteCount(DurableKeyJson.Write(new RootHeader(model, definition, "", [], false))) + RootVariableHeaderBytes;


    private abstract record QualifiedRootCheckpoint(long JournalRevision)
    {
        internal virtual long RevisionFor(string actor) => JournalRevision;
        internal virtual bool WritesJournal(string actor) => false;
        internal virtual ValueTask CheckBeforeWriteAsync(KernelDb db, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        internal abstract Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken);
    }

    private sealed record FirstRootCheckpoint(string Tenant, string Reference, Message Creator, InstanceRow Row, string Header,
        byte[]? Wire, long Growth) : QualifiedRootCheckpoint(1)
    {
        internal override async ValueTask CheckBeforeWriteAsync(KernelDb db, CancellationToken cancellationToken)
        {
            var (escape, state) = StoredState.EncodingParts(Row);
            var terminalHeader = db.UsesRootTermination && Row.Status == InstanceStatus.Running
                ? RootTerminalHeaderBytes(Creator.Start!.Model, Creator.Start.Definition) : 0;
            var reserve = Math.Max(0, terminalHeader - Encoding.UTF8.GetByteCount(Header));
            var bytes = db.UsesRootStableBody
                ? StableContentBytes(state, Header, Wire, reserve, Row.Status == InstanceStatus.Running ? Growth : 0)
                : await MeasureRootContentAsync(db, RootStateFamily(db).Length + escape.Length + reserve, state, Header,
                    Row.Jobs, cancellationToken);
            if (bytes > RootContentBytes) throw new StoreException(Failure.Definite, RootContentFailure);
        }

        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            // The existing transaction owns the fence. Return the exact deleted envelope for application-side
            // comparison. A missing bind or changed envelope returns false and rolls back all effects.
            var family = RootStateFamily(db);
            var fingerprint = CreationProtocol.Fingerprint(Creator.Start, Creator.Kind, Creator.Payload);
            var finished = Row.Status == InstanceStatus.Finished;
            var candidates = await db.Messages.FromSqlInterpolated(AddRootWire(db, $"""
                WITH consumed AS (
                    DELETE FROM "Messages" m USING "FoundationRootBirths" b
                    WHERE b."Creator" = m."Id" AND b."Actor" = m."Target" AND b."Partition" = m."Partition"
                        AND b."Reference" = {Reference} AND b."Tenant" = {Tenant} AND b."Creation" = 1
                        AND m."Id" = {Creator.InboxKey.Id} AND m."InboxScope" = 0 AND NOT m."Done"
                        AND m."KeepUntil" IS NULL AND m."EventMatch" IS NULL
                        AND m."Due" <= floor(extract(epoch from clock_timestamp()) * 1000)::bigint
                    RETURNING m.*
                ), bound AS (
                    UPDATE "Instances" i SET "FoundationReference" = b."Reference", "FoundationTenant" = b."Tenant",
                        "FoundationHeader" = {Header}, "State" = {family} || i."State", /*root-wire*/
                        "FoundationTerminalRevision" = CASE WHEN {finished} THEN {JournalRevision} ELSE NULL END,
                        "FoundationTerminalAt" = CASE WHEN {finished} THEN floor(extract(epoch from clock_timestamp()) * 1000)::bigint ELSE NULL END
                    FROM "FoundationRootBirths" b JOIN consumed m ON b."Creator" = m."Id" AND b."Actor" = m."Target"
                    WHERE b."Reference" = {Reference} AND b."Tenant" = {Tenant}
                        AND b."Actor" = {Creator.Target.Id} AND b."Creation" = 1 AND b."Creator" = {Creator.InboxKey.Id}
                        AND b."Fingerprint" = {fingerprint} AND i."Id" = b."Actor" AND i."Partition" = b."Partition"
                        AND i."Revision" = {JournalRevision} AND i."Status" = {(int)Row.Status} AND i."FoundationReference" IS NULL
                        AND (NOT {finished} OR NOT EXISTS (SELECT 1 FROM "Subscriptions" WHERE "Instance" = i."Id"))
                    RETURNING i."Id"
                )
                SELECT consumed.* FROM consumed JOIN bound ON bound."Id" = consumed."Target"
                """, Wire)).AsNoTracking().ToListAsync(cancellationToken);
            if (candidates.Count != 1) return false;
            var admitted = PostgresRootAdmission.DecodeCreator(candidates[0]);
            return admitted.Id == Creator.Id && admitted.Target == Creator.Target && admitted.InboxKey == Creator.InboxKey &&
                admitted.Start == Creator.Start && admitted.Payload == Creator.Payload && admitted.Due == Creator.Due &&
                admitted.KeepUntil == Creator.KeepUntil && admitted.ReplyTo == Creator.ReplyTo &&
                admitted.SenderCreation == Creator.SenderCreation;
        }
    }

    internal static Task InstallFirstRootCheckpointsAsync(KernelDb db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Instances" ADD COLUMN IF NOT EXISTS "FoundationReference" text COLLATE "C" REFERENCES "FoundationRootBirths" ("Reference"),
                                  ADD COLUMN IF NOT EXISTS "FoundationTenant" text COLLATE "C",
                                  ADD COLUMN IF NOT EXISTS "FoundationHeader" text,
                                  ADD COLUMN IF NOT EXISTS "FoundationWriteRevision" bigint,
                                  ADD COLUMN IF NOT EXISTS "FoundationTerminalRevision" bigint,
                                  ADD COLUMN IF NOT EXISTS "FoundationTerminalAt" bigint;
            CREATE OR REPLACE FUNCTION kernel_require_root_checkpoint_writer() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                IF TG_OP = 'UPDATE' AND NEW."FoundationWriteRevision" = OLD."Revision" + 1
                    AND NEW."Revision" = NEW."FoundationWriteRevision"
                    AND NEW."FoundationReference" = OLD."FoundationReference" AND NEW."FoundationTenant" = OLD."FoundationTenant"
                    AND NEW."Id" = OLD."Id" AND NEW."Partition" = OLD."Partition" AND NOT OLD."Retired" /*root-park-generation*/
                    AND ((OLD."Status" = 0 AND NOT NEW."Retired") OR /*root-park-continues*/ OR /*root-park-recover*/ OR
                        (/*root-retirement*/ AND NEW."Retired" AND NEW."State" = 'null' AND NEW."FoundationHeader" IS NULL /*root-retirement-wire*/
                            AND ((OLD."Status" <> 0 AND NEW."Status" = OLD."Status"
                                AND NEW."FoundationTerminalRevision" = OLD."FoundationTerminalRevision"
                                AND NEW."FoundationTerminalAt" = OLD."FoundationTerminalAt")
                            OR (/*root-atomic-retirement*/ AND OLD."Status" = 0 AND NEW."Status" = /*cancelled*/
                                AND OLD."FoundationTerminalRevision" IS NULL AND OLD."FoundationTerminalAt" IS NULL
                                AND NEW."FoundationTerminalRevision" = NEW."Revision" AND NEW."FoundationTerminalAt" IS NOT NULL))))
                THEN RETURN NEW; END IF;
                RAISE EXCEPTION 'Qualified actor checkpoint requires a qualified update' USING ERRCODE = '23514';
            END
            $fn$;
            CREATE OR REPLACE TRIGGER kernel_require_root_checkpoint_writer BEFORE UPDATE OR DELETE ON "Instances"
            FOR EACH ROW WHEN (OLD."FoundationReference" IS NOT NULL) EXECUTE FUNCTION kernel_require_root_checkpoint_writer();
            CREATE OR REPLACE FUNCTION kernel_require_root_journal_revision() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                IF EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = NEW."Instance" AND i."FoundationReference" IS NOT NULL
                    AND (i."Partition" <> NEW."Partition" OR NEW."Revision" <> i."Revision" + 1 OR i."Status" <> 0 OR i."Retired")
                    AND NOT (/*root-park-journal*/ OR (/*root-retirement*/ AND i."Retired" AND i."Partition" = NEW."Partition" AND NEW."Revision" = -1
                        AND NEW."Entries" = '["delete"]' AND NOT EXISTS (
                            SELECT 1 FROM "Journal" j WHERE j."Instance" = i."Id" AND j."Partition" = i."Partition" AND j."Revision" = -1))))
                THEN RAISE EXCEPTION 'Qualified actor journal requires the next running checkpoint revision' USING ERRCODE = '23514'; END IF;
                RETURN NEW;
            END
            $fn$;
            CREATE OR REPLACE TRIGGER kernel_require_root_journal_revision BEFORE INSERT ON "Journal"
            FOR EACH ROW EXECUTE FUNCTION kernel_require_root_journal_revision();
            """.Replace("/*root-park-continues*/", db.UsesRootParkArrivals ? """
                (OLD."Status" = 2 AND NEW."Status" = OLD."Status" AND NOT NEW."Retired"
                    AND OLD."FoundationParkAt" IS NOT NULL AND OLD."FoundationParkRevision" > 0
                    AND NEW."FoundationParkAt" = OLD."FoundationParkAt" AND NEW."FoundationParkRevision" = OLD."FoundationParkRevision"
                    AND NEW."State" = OLD."State" AND NEW."StateVersion" = OLD."StateVersion" AND NEW."FoundationHeader" = OLD."FoundationHeader"
                    AND NEW."Fault" IS NOT DISTINCT FROM OLD."Fault" AND NEW."Suspended" = OLD."Suspended"
                    AND NEW."History" IS NOT DISTINCT FROM OLD."History")
                """ : "false", StringComparison.Ordinal)
            .Replace("/*root-park-journal*/", db.UsesRootParkArrivals ? """
                (i."Status" = 2 AND NOT i."Retired" AND NEW."Partition" = i."Partition" AND NEW."Revision" = i."Revision" + 1
                    AND i."FoundationParkAt" IS NOT NULL AND i."FoundationParkRevision" > 0
                    AND (NEW."Entries" = '["parked"]' /*root-park-recover-journal*/))
                """ : "false", StringComparison.Ordinal)
            // Only a recover leaves Faulted for Running. It clears the Park mark and moves the release generation by one.
            .Replace("/*root-park-generation*/", db.UsesRootParkRecovery ? """
                AND (NEW."FoundationParkGeneration" = OLD."FoundationParkGeneration" OR (OLD."Status" = 2 AND NEW."Status" = 0
                    AND NEW."FoundationParkGeneration" = OLD."FoundationParkGeneration" + 1))
                """ : "", StringComparison.Ordinal)
            .Replace("/*root-park-recover*/", db.UsesRootParkRecovery ? """
                (OLD."Status" = 2 AND NEW."Status" = 0 AND NOT NEW."Retired" AND NOT OLD."Suspended" AND NOT NEW."Suspended"
                    AND OLD."FoundationParkAt" IS NOT NULL AND OLD."FoundationParkRevision" > 0
                    AND NEW."FoundationParkAt" IS NULL AND NEW."FoundationParkRevision" IS NULL AND NEW."Fault" IS NULL
                    AND NEW."State" = OLD."State" AND NEW."StateVersion" = OLD."StateVersion" AND NEW."FoundationHeader" = OLD."FoundationHeader"
                    AND NEW."History" IS NOT DISTINCT FROM OLD."History")
                """ : "false", StringComparison.Ordinal)
            .Replace("/*root-park-recover-journal*/", db.UsesRootParkRecovery
                // The raw command is formatted, so the literal brace is doubled.
                ? """OR NEW."Entries" LIKE '{{"Outcome":"recovered",%' OR NEW."Entries" LIKE '{{"Outcome":"refused",%'""" : "", StringComparison.Ordinal)
            .Replace("/*root-retirement*/", db.UsesRootRetirement ? "true" : "false", StringComparison.Ordinal)
            .Replace("/*root-atomic-retirement*/", db.UsesRootTerminateThenDelete ? "true" : "false", StringComparison.Ordinal)
            .Replace("/*root-retirement-wire*/", db.UsesRootRetirement ? """AND NEW."FoundationStateWire" IS NULL""" : "", StringComparison.Ordinal)
            .Replace("/*cancelled*/", FormattableString.Invariant($"{(int)InstanceStatus.Cancelled}"), StringComparison.Ordinal), cancellationToken);

    /// <summary>The manual first Running checkpoint of a generated root, with its creator as the sole input.
    /// The Worker qualifies immediate completion through its regular turn path.</summary>
    internal ValueTask<Committed> CommitFirstRootAsync(string tenant, string reference, Message creator,
        PartitionFence fence, TimeSpan lease, string? state, int stateVersion, string typeData,
        IReadOnlyDictionary<string, string> labels, string journal, CancellationToken cancellationToken = default) =>
        CommitPreparedRootAsync(fence, lease, PrepareFirstRoot(tenant, reference, creator, fence, state, stateVersion, typeData, labels, journal), cancellationToken);

    private PreparedRoot PrepareFirstRoot(string tenant, string reference, Message creator,
        PartitionFence fence, string? state, int stateVersion, string typeData,
        IReadOnlyDictionary<string, string> labels, string journal, RootJobState? jobState = null, InstanceStatus status = InstanceStatus.Running, long terminalGrowth = 0)
    {
        if (!postgres || Engine is not null) throw new NotSupportedException("First root checkpoints require PostgreSQL.");
        if (creator.Start is not { Parent: null, Creation: { Generation: 1, Previous: 0 } } start ||
            creator.TargetCreation != 1 || creator.Kind != Kinds.Start || creator.Target.Partition != fence.Partition ||
            creator.InboxKey != new InboxKey("root:" + creator.Target.Id) || creator.Payload != start.Input || creator.Sender is not null ||
            start.Creation.Intent != FoundationRootCreator.OperationDigest(creator.Id))
            throw new ArgumentException("First root checkpoint requires the admitted exact creator.", nameof(creator));
        var stable = RootStableBody;
        var (storedState, header) = EncodeRootContent(start.Model, start.Definition, stable && state is not null ? StableBodyMarker : state, stateVersion, typeData, labels, journal);
        var wire = stable && state is not null ? RootSelfContent.Encode(state) : null;
        // Model and definition are in the qualified header, including values that PostgreSQL text cannot carry.
        var row = new InstanceRow(creator.Target.Id, "", "", status, "null", 1, null,
            History: start.History, StateVersion: stateVersion, Jobs: jobState?.Jobs).WithFoundationState(storedState)
            with { NextJobGeneration = jobState?.NextGeneration };
        // BindCheckpointAsync consumes the exact, due creator before this same transaction lands.
        var turn = new TurnCommit(row.Id, row, null, [], [], [], [], [], journal);
        return new(turn, new FirstRootCheckpoint(tenant, reference, creator, row, header, wire, terminalGrowth));
    }

    private static (string? State, string Header) EncodeRootContent(string model, string definition, string? state,
        int stateVersion, string typeData, IReadOnlyDictionary<string, string> labels, string? journal)
    {
        if (stateVersion < 1 || !FoundationMetadataLimits.FitsContent(typeData, FoundationMetadataLimits.TypeDataBytes))
            throw new ArgumentException("Invalid qualified root checkpoint header.");

        var captured = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in labels)
        {
            if (captured.Count == 4 || key is null || value is null || Encoding.UTF8.GetByteCount(key) > 1024 ||
                Encoding.UTF8.GetByteCount(value) > 1024 || !captured.TryAdd(key, value))
                throw new ArgumentException("A root checkpoint accepts four ordinally distinct labels of at most 1,024 UTF-8 bytes per key and value.", nameof(labels));
        }

        if (journal is not null)
        {
            using var entries = JsonDocument.Parse(journal);
            if (entries.RootElement.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Qualified root checkpoint journal entries must form an array.", nameof(journal));
        }

        var escaped = state is not null && (state.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(state));
        var storedState = escaped ? DurableKeyJson.Write(state) : state;
        var header = DurableKeyJson.Write(new RootHeader(model, definition, typeData, captured.ToArray(), escaped));
        return (storedState, header);
    }

    private const string RootContentFailure = "The encoded PostgreSQL checkpoint exceeds its storage budget.";

    private static ValueTask<long> MeasureRootContentAsync(KernelDb db, int framing, string state, string header,
        string? jobs, CancellationToken cancellationToken, bool comparePriorSize = false)
    {
        // The known framing is ASCII. The remaining 24 KiB covers bounded identifiers and scalars.
        // ASCII stores the same way in all PostgreSQL encodings. For small non-ASCII values,
        // four bytes per code unit are enough to prove the row fits without querying the server.
        var units = (long)state.Length + header.Length + (jobs?.Length ?? 0);
        if (framing + 4 * units <= RootContentBytes)
            return new(framing + 4 * units);
        var utf8 = (long)Encoding.UTF8.GetByteCount(state) + Encoding.UTF8.GetByteCount(header)
            + (jobs is null ? 0 : Encoding.UTF8.GetByteCount(jobs));
        // Every non-ASCII UTF-16 code unit adds at least one byte to the UTF-8 count.
        // Each such unit can add up to three bytes in the database encoding.
        var bound = framing + units + 3 * (utf8 - units);
        if (utf8 == units || bound <= RootContentBytes)
            return new(bound);
        var asciiBytes = framing + Math.Max(0, 2 * units - utf8);
        if (!comparePriorSize && asciiBytes > RootContentBytes)
            return new(asciiBytes);
        return new(db.Database.SqlQuery<long>($"""
            SELECT {framing}::bigint + octet_length({state}) + octet_length({header}) + coalesce(octet_length({jobs}::text), 0) AS "Value"
            """).SingleAsync(cancellationToken));
    }

    private static FormattableString AddRootWire(KernelDb db, FormattableString sql, byte[]? wire) =>
        db.UsesRootStableBody ? FormattableStringFactory.Create(
            sql.Format.Replace("/*root-wire*/", $$""" "FoundationStateWire" = {{{sql.ArgumentCount}}}, """, StringComparison.Ordinal),
            [.. sql.GetArguments(), wire]) : sql;

    private static long StableContentBytes(string stored, string header, byte[]? wire, int reserve, long growth)
    {
        if (growth < 0 || growth > RootContentBytes) return (long)RootContentBytes + 1;
        return (long)StableBodyFamily.Length + stored.Length + Encoding.UTF8.GetByteCount(header) +
            (wire?.Length ?? 0) + reserve + growth;
    }

    private sealed record PhysicalRoot(string Id, long Creation, string State, byte[]? Wire, int StateVersion, long Revision, string Header,
        History History, InstanceStatus Status, bool Suspended, long? TerminalRevision, long? TerminalAt, long AsOf,
        string? Jobs, long? NextJobGeneration, string? Fault);

    internal async Task<RootImage?> ReadFirstRootAsync(string tenant, string reference, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        // The model's protection decision must see the same physical checkpoint as retirement.
        await using var transaction = await BeginPinnedRootOperationAsync(db, cancellationToken);
        var family = RootStateFamily(db);
        if (transaction is null && (db.UsesRootValueProtocol || db.UsesRootTimedJobIssuance || db.UsesRootTimedJobSettlement || db.UsesRootReplyProtocol || db.UsesRootStableBody))
            await RequireStoreVersionsAsync(db, cancellationToken);
        FormattableString query = $"""
            SELECT i."Id", b."Creation", i."State", /*root-wire*/ AS "Wire", i."StateVersion", i."Revision", i."FoundationHeader" AS "Header",
                i."History", i."Status", i."Suspended", i."FoundationTerminalRevision" AS "TerminalRevision", i."FoundationTerminalAt" AS "TerminalAt",
                floor(extract(epoch from statement_timestamp()) * 1000)::bigint AS "AsOf",
                i."Jobs", i."NextJobGeneration", i."Fault"
            FROM "Instances" i JOIN "FoundationRootBirths" b ON b."Reference" = i."FoundationReference"
                AND b."Actor" = i."Id" AND b."Tenant" = i."FoundationTenant"
            WHERE i."FoundationTenant" = {tenant} AND i."FoundationReference" = {reference} AND i."Partition" = b."Partition" AND NOT i."Retired"
            """;
        var physical = await db.Database.SqlQuery<PhysicalRoot>(FormattableStringFactory.Create(
            query.Format.Replace("/*root-wire*/", db.UsesRootStableBody ? """i."FoundationStateWire" """ : "NULL::bytea", StringComparison.Ordinal),
            query.GetArguments())).SingleOrDefaultAsync(cancellationToken);
        if (physical is null) return null;
        if (!physical.State.StartsWith(family, StringComparison.Ordinal))
            throw new StoreException(Failure.Definite, "Unsupported qualified actor checkpoint.");
        var header = DurableKeyJson.Read<RootHeader>(physical.Header);
        var logical = StoredState.Restore(new(physical.Id, header.Model, header.Definition, InstanceStatus.Running,
            physical.State[family.Length..], physical.Revision, null, Creation: physical.Creation));
        string? state;
        if (db.UsesRootStableBody)
        {
            if (header.EscapedState || logical.FoundationNullState != (physical.Wire is null) ||
                !logical.FoundationNullState && logical.State != StableBodyMarker)
                throw new StoreException(Failure.Definite, "Invalid PostgreSQL stable-body checkpoint.");
            try { state = physical.Wire is null ? null : RootSelfContent.Decode(physical.Wire); }
            catch (Exception error) when (error is FormatException or DecoderFallbackException)
            {
                throw new StoreException(Failure.Definite, "Invalid PostgreSQL stable-body encoding.", error);
            }
        }
        else state = logical.FoundationNullState ? null : header.EscapedState ? DurableKeyJson.Read<string>(logical.State) : logical.State;
        return new(new(physical.Id, physical.Creation), header.Model, header.Definition, header.TypeData,
            header.Labels.ToDictionary(StringComparer.Ordinal), state, physical.StateVersion, physical.Revision)
            {
                History = physical.History, Status = physical.Status, Suspended = physical.Suspended, TerminalRevision = physical.TerminalRevision,
                Fault = db.UsesRootParkArrivals && physical.Fault is not null ? DurableKeyJson.Read<string>(physical.Fault) : null,
                TerminalAt = physical.TerminalAt, AsOf = physical.AsOf, Jobs = physical.Jobs, NextJobGeneration = physical.NextJobGeneration
            };
    }
}
