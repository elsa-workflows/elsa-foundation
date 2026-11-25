using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    private const string OperatorPrefix = "!kernel-operator/1:";
    private const string HeldOperatorPrefix = "!kernel-held-operator/1:";
    private const string TerminateThenDeletePrefix = "!kernel-operator/2:";
    private const string RetentionDeletePrefix = "!kernel-operator/3:";
    private const string RetentionOperationPrefix = "~retention-";
    private sealed record StoredOperator(string Reference, string Operation, long Creation, string Kind,
        [property: JsonRequired] bool ExplicitZeroDue)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FoundationTerminalStamp? Terminal { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FoundationParkMark? Park { get; init; }
        // A recover, and a Terminate that names a parked root's Park, carry the ordinary admission window, which they give
        // each kept row they restore or clear.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? Window { get; init; }
        // S5b-2: the store's control that clears the next page of an open clearing.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Clear { get; init; }
    }
    internal const string OperatorOccurrencePrefix = "operator/";
    internal static bool IsStoredOperator(string? header) => header is not null &&
        (header.StartsWith(OperatorPrefix, StringComparison.Ordinal) || header.StartsWith(TerminateThenDeletePrefix, StringComparison.Ordinal) ||
         header.StartsWith(RetentionDeletePrefix, StringComparison.Ordinal) || header.StartsWith(HeldOperatorPrefix, StringComparison.Ordinal));
    internal static bool IsOperatorOccurrence(InboxKey key) => key.Scope == InboxKey.FoundationOccurrenceScope && key.Match is null &&
        key.Id.StartsWith(OperatorOccurrencePrefix, StringComparison.Ordinal);

    private static Task InstallOperatorReceiptsAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "FoundationOperatorReceipts" (
                "Tenant" text COLLATE "C" NOT NULL, "Operation" text COLLATE "C" NOT NULL,
                "Reference" text COLLATE "C" NOT NULL REFERENCES "FoundationRootBirths" ("Reference"),
                "Until" bigint NOT NULL, PRIMARY KEY ("Tenant", "Operation"));
            CREATE INDEX IF NOT EXISTS "IX_FoundationOperatorReceipts_Until" ON "FoundationOperatorReceipts" ("Until");
            CREATE INDEX IF NOT EXISTS "IX_FoundationRootOperators_Pending"
                ON "Messages" ("Partition", "Target", "Seq") INCLUDE ("Due")
                WHERE "InboxScope" = -4 AND "Id" LIKE 'operator/%' AND NOT "Done";
            """, token);

    // The trusted retention pass has no public operation id. Qualification derives it from the authenticated mark.
    internal async ValueTask<Contract.Admission> AdmitRetentionDeleteAsync(Contract.BoundScope scope, Contract.ActorRef actor,
        string selection, CancellationToken token)
    {
        var receipt = await AdmitRootOperatorAsync(scope.Tenant, actor.Id, "", Kinds.Delete, null, null, token, selection, retentionDelete: true);
        return receipt switch
        {
            { Duplicate: true } => new(Contract.AdmissionOutcome.Duplicate, new Contract.ActorRef(receipt.Reference)),
            { Duplicate: false } => new(Contract.AdmissionOutcome.Accepted, new Contract.ActorRef(receipt.Reference)),
            _ => new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection)
        };
    }

    internal sealed record OperatorReply(string Address, long Deadline);
    internal sealed class OperatorReplyExpired : Exception;
    internal sealed class OperatorReplyCapacity : Exception;
    internal const int HeldReplyLimit = 64;

    internal async Task<Receipt?> AdmitRootOperatorAsync(string tenant, string reference, string operation, string kind, long? due,
        TimeSpan? keepFor, CancellationToken token, string? selection = null, bool thenDelete = false, bool retentionDelete = false,
        OperatorReply? reply = null)
    {
        if (kind is not (Kinds.Suspend or Kinds.Resume or Kinds.Terminate or Kinds.Delete or Kinds.Recover) || !retentionDelete && !FoundationMetadataLimits.IsOutsideMessageId(operation))
            throw new ArgumentException("Expected a one-way exact-root operator command.");
        var window = keepFor is { } keep && keep < deduplicationWindow ? keep : deduplicationWindow;
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        var windowMs = (long)window.TotalMilliseconds;
        await using var db = create();
        if (!db.UsesRootOperatorProtocol || kind == Kinds.Terminate && !db.UsesRootTermination) return null;
        // The Park recovery profile admits Recover and no other operator command. No other profile admits Recover.
        var recover = kind == Kinds.Recover;
        // Recover is immediate: a delayed one could mature behind the cohort it releases.
        if (recover != db.UsesRootParkRecovery || recover && due is not null) return null;
        var deletion = kind == Kinds.Delete;
        if (deletion && !db.UsesRootRetirement || thenDelete && (kind != Kinds.Terminate || !db.UsesRootTerminateThenDelete) ||
            retentionDelete && (!deletion || !db.UsesRootRetention)) return null;
        var selected = deletion || recover || db.UsesRootTerminalSelection && kind == Kinds.Terminate;
        if (reply is not null && (!selected || !db.UsesRootOperatorReplies || retentionDelete ||
            !ValidOperatorReply(reply.Address, reply.Deadline))) return null;
        if (selected != (selection is not null)) return null;

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
        try
        {
            FoundationTerminalStamp? terminal = null;
            FoundationParkMark? park = null;
            if (selected)
            {
                await PinRootNamespaceAsync(db, token);
                var cursor = await ReadSelectionCursorAsync(db, token);
                if (deletion)
                {
                    terminal = PostgresRootSelection.OpenTerminal(cursor, selection, tenant, reference);
                    if (terminal is not { Revision: > 0 }) return null;
                }
                else if (recover)
                {
                    park = PostgresRootSelection.OpenPark(cursor, selection, tenant, reference);
                    if (park is not { Revision: > 0, At: > 0 }) return null;
                }
                // S5b: in a parked-operator set a Terminate may name the root's Park instead of its running selection.
                else if (db.UsesRootParkedOperators &&
                         PostgresRootSelection.OpenPark(cursor, selection, tenant, reference) is { Revision: > 0, At: > 0 } named)
                    park = named;
                else if (!PostgresRootSelection.MatchesRunning(cursor, selection, tenant, reference))
                    return null;
            }
            else
                await EfStore.RequireStoreVersionsAsync(db, token);
            // Serialize only held admissions for the same birth. NO KEY UPDATE remains compatible with receipt FKs.
            var address = reply is null ? await RootAddressAsync(db, tenant, reference, token) :
                await db.Database.SqlQuery<CreationAddress>($"""
                    SELECT "Actor" AS "Id", "Creation" FROM "FoundationRootBirths"
                    WHERE "Tenant" = {tenant} AND "Reference" = {reference} FOR NO KEY UPDATE
                    """).SingleOrDefaultAsync(token);
            if (address is null)
            {
                await tx.RollbackAsync(token);
                return null;
            }
            if (retentionDelete)
            {
                var identity = DurableKeyJson.Write(new { tenant, reference, address.Creation, Mark = terminal });
                operation = RetentionOperationPrefix + FoundationRootCreator.OperationDigest(identity);
            }
            var digest = FoundationRootCreator.OperationDigest(operation);

            // Lock the selected checkpoint before reading its receipt. If the command already
            // ended this actor, the receipt committed before the owner could consume it.
            var exactTerminal = terminal is not null && await LocksRootDeleteSelectionAsync(db, address, tenant, reference, terminal, token);
            // S5b-2: a Terminate is refused while its Park is being cleared.
            var exactPark = park is not null && await LocksRootParkSelectionAsync(db, address, tenant, reference, park, token) &&
                (recover || !await ClearsRootParkAsync(db, address, token));
            var selectedStatus = selected && !deletion && park is null
                ? await AdmissionStatusAsync(db, address, tenant, reference, token, requireCheckpoint: true)
                : null;
            var prior = await db.Database.SqlQuery<string>($"""
                SELECT "Reference" AS "Value" FROM "FoundationOperatorReceipts"
                WHERE "Tenant" = {tenant} AND "Operation" = {digest} AND "Until" >= floor(extract(epoch from clock_timestamp()) * 1000)::bigint
                """).SingleOrDefaultAsync(token);
            if (prior is not null)
            {
                await tx.CommitAsync(token);
                return selected && prior != reference ? null : new(prior, true);
            }
            if (!deletion && park is null)
                selectedStatus = selected ? selectedStatus : await AdmissionStatusAsync(db, address, tenant, reference, token);
            // S5b: a composed Park set admits a Terminate, or a Terminate and delete, for a parked root, held or not, only with the
            // selection of its Park. A running selection is refused there; a command admitted before the Park opened is refused
            // by the owner, which answers a held caller.
            var parked = selectedStatus == InstanceStatus.Faulted && db.UsesRootParkedOperators && kind == Kinds.Terminate &&
                await LocksParkedRootAsync(db, address, tenant, reference, token);
            var eligible = deletion ? exactTerminal : park is not null ? exactPark
                : !parked && (thenDelete ? selectedStatus is InstanceStatus.Running or InstanceStatus.Finished or InstanceStatus.Faulted or InstanceStatus.Cancelled
                    : selectedStatus == InstanceStatus.Running);
            if (!eligible)
            {
                await tx.RollbackAsync(token);
                return null;
            }
            if (reply is not null)
            {
                var withinDeadline = await db.Database.SqlQuery<bool>($"""
                    SELECT ({reply.Deadline} > floor(extract(epoch from clock_timestamp()) * 1000)::bigint) AS "Value"
                    """).SingleAsync(token);
                if (!withinDeadline) throw new OperatorReplyExpired();

                var atCapacity = await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "Messages"
                        WHERE "InboxScope" = {InboxKey.FoundationOccurrenceScope}
                            AND "Target" = {address.Id} AND "Partition" = {new InstanceRef(address.Id).Partition}
                            AND NOT "Done" AND "ReplyTo" IS NOT NULL AND "Id" LIKE 'operator/%'
                        OFFSET {HeldReplyLimit - 1} LIMIT 1) AS "Value"
                    """).SingleAsync(token);
                if (atCapacity) throw new OperatorReplyCapacity();
            }

            var receipt = await db.Database.SqlQuery<long>($"""
                INSERT INTO "FoundationOperatorReceipts" AS r ("Tenant","Operation","Reference","Until")
                VALUES ({tenant},{digest},{reference},floor(extract(epoch from clock_timestamp()) * 1000)::bigint + {windowMs})
                ON CONFLICT ("Tenant","Operation") DO UPDATE SET "Reference" = EXCLUDED."Reference",
                    "Until" = floor(extract(epoch from clock_timestamp()) * 1000)::bigint + {windowMs}
                WHERE r."Until" < floor(extract(epoch from clock_timestamp()) * 1000)::bigint RETURNING "Until" AS "Value"
                """).ToListAsync(token);
            if (receipt.Count == 0)
            {
                // A competing transaction recorded this operation while we qualified the requested root.
                prior = await db.Database.SqlQuery<string>($"""
                    SELECT "Reference" AS "Value" FROM "FoundationOperatorReceipts" WHERE "Tenant" = {tenant} AND "Operation" = {digest}
                    """).SingleAsync(token);
                await tx.CommitAsync(token);
                return selected && prior != reference ? null : new(prior, true);
            }
            var actor = new InstanceRef(address.Id);
            var id = $"{OperatorOccurrencePrefix}{reference}/{digest}/{Guid.NewGuid():N}";
            var prefix = retentionDelete ? RetentionDeletePrefix : thenDelete ? TerminateThenDeletePrefix : OperatorPrefix;
            var replyAddress = reply?.Address;
            var header = (replyAddress is null ? "" : HeldOperatorPrefix) + prefix +
                DurableKeyJson.Write(new StoredOperator(reference, operation, address.Creation, kind, due == 0)
                {
                    Terminal = terminal, Park = park, Window = park is not null ? (long)deduplicationWindow.TotalMilliseconds : null
                });
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Start", "Due", "KeepUntil", "Done", "Seen", "Attempts", "Trial", "ReplyTo")
                VALUES ({InboxKey.FoundationOccurrenceScope}, {id}, {actor.Partition}, {address.Id}, {kind}, {header}, {due ?? 0}, {receipt.Single()}, false, 0, 0, 0, {replyAddress})
                """, token);
            await tx.CommitAsync(token);
            return new(reference, false);
        }
        catch (PostgresException error) when (selected && IsUnavailableSelectedSchema(error))
        {
            throw new StoreFormatException("The qualified root store schema is unreadable.", error);
        }
    }

    private static async Task<bool> LocksRootDeleteSelectionAsync(KernelDb db, CreationAddress address, string tenant,
        string reference, FoundationTerminalStamp mark, CancellationToken token)
    {
        var rows = await db.Database.SqlQuery<long>($"""
            SELECT "Revision" AS "Value" FROM "Instances"
            WHERE "Id" = {address.Id} AND "FoundationTenant" = {tenant} AND "FoundationReference" = {reference}
                AND "Status" <> {(int)InstanceStatus.Running} AND NOT "Retired"
                AND "FoundationTerminalRevision" = {mark.Revision} AND "FoundationTerminalAt" = {mark.At}
            FOR SHARE
            """).ToListAsync(token);
        return rows.Count == 1;
    }

    // The checkpoint stays shared-locked until the receipt and occurrence commit, so the Park cannot move underneath them.
    // A parked root carries no terminal mark until it ends.
    private static async Task<bool> LocksRootParkSelectionAsync(KernelDb db, CreationAddress address, string tenant,
        string reference, FoundationParkMark mark, CancellationToken token) =>
        address.Creation == 1 && (await db.Database.SqlQuery<long>($"""
            SELECT "Revision" AS "Value" FROM "Instances"
            WHERE "Id" = {address.Id} AND "Partition" = {new InstanceRef(address.Id).Partition}
                AND "FoundationTenant" = {tenant} AND "FoundationReference" = {reference} AND NOT "Retired"
                AND "Status" = {(int)InstanceStatus.Faulted} AND "FoundationParkAt" = {mark.At} AND "FoundationParkRevision" = {mark.Revision}
                AND "FoundationTerminalRevision" IS NULL
            FOR SHARE
            """).ToListAsync(token)).Count == 1;

    private static async Task<bool> ClearsRootParkAsync(KernelDb db, CreationAddress address, CancellationToken token) =>
        await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {address.Id} AND "Partition" = {new InstanceRef(address.Id).Partition}
                AND "FoundationParkClearing" IS NOT NULL) AS "Value"
            """).SingleAsync(token);

    // The next control of an open clearing: the Terminate's operation and Park mark under a fresh occurrence.
    internal static (string Id, string Header) ClearControl(string reference, string operation, FoundationParkMark park, long window,
        bool thenDelete) =>
        ($"{OperatorOccurrencePrefix}{reference}/{FoundationRootCreator.OperationDigest(operation)}/{Guid.NewGuid():N}",
            (thenDelete ? TerminateThenDeletePrefix : OperatorPrefix) + DurableKeyJson.Write(new StoredOperator(reference, operation, 1, Kinds.Terminate, false)
                { Park = park, Window = window, Clear = true }));

    // A parked root in a composed Park set, whose running selection admission refuses. A Park keeps the row's Suspended
    // flag, so a root parked while suspended is found the same way.
    private static async Task<bool> LocksParkedRootAsync(KernelDb db, CreationAddress address, string tenant, string reference,
        CancellationToken token) =>
        address.Creation == 1 && (await db.Database.SqlQuery<long>($"""
            SELECT "Revision" AS "Value" FROM "Instances"
            WHERE "Id" = {address.Id} AND "Partition" = {new InstanceRef(address.Id).Partition}
                AND "FoundationTenant" = {tenant} AND "FoundationReference" = {reference} AND NOT "Retired"
                AND "Status" = {(int)InstanceStatus.Faulted} AND "FoundationParkAt" IS NOT NULL AND "FoundationTerminalRevision" IS NULL
            FOR SHARE
            """).ToListAsync(token)).Count == 1;

    private static bool IsUnavailableSelectedSchema(PostgresException error) =>
        error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn
            or PostgresErrorCodes.InvalidSchemaName or PostgresErrorCodes.WrongObjectType;

    private sealed record SelectedOperatorStore(int StoreSchema, int MessageProtocol, int JournalCodec, string Namespace);

    private const string ParkSources = """
        LOCK TABLE "FoundationRootIssuer", "FoundationRootBirths", "Instances", "Messages", "Partitions", "Nodes",
            "Journal", "Subscriptions", "FoundationDirectReceipts", "FoundationActorList", "FoundationRootReceipts", "Events"
        IN ACCESS SHARE MODE
        """;
    private const string ParkRecoverySources = """
        LOCK TABLE "FoundationRootIssuer", "FoundationRootBirths", "Instances", "Messages", "Partitions", "Nodes",
            "Journal", "Subscriptions", "FoundationDirectReceipts", "FoundationActorList", "FoundationRootReceipts", "Events",
            "FoundationOperatorReceipts"
        IN ACCESS SHARE MODE
        """;

    internal static async Task PinRootNamespaceAsync(KernelDb db, CancellationToken token)
    {
        try { await PinRootSourceAsync(db, token); }
        catch (PostgresException error) when (IsUnavailableSelectedSchema(error))
        {
            throw new StoreFormatException("The qualified root store schema is unreadable.", error);
        }
    }

    private static async Task PinRootSourceAsync(KernelDb db, CancellationToken token)
    {
        var marker = await db.Database.SqlQuery<SelectedOperatorStore>($"""
            SELECT v."StoreSchema", v."MessageProtocol", v."JournalCodec", ns.nspname AS "Namespace"
            FROM "KernelStoreVersions" v
            JOIN pg_catalog.pg_class c ON c.oid = v.tableoid
            JOIN pg_catalog.pg_namespace ns ON ns.oid = c.relnamespace
            WHERE v."Id" = {StoreVersionEntity.MarkerId}
            FOR SHARE OF v
            """).SingleOrDefaultAsync(token);
        EfStore.RequireSnapshotFormat(db.RequiredFormat, marker?.StoreSchema, marker?.MessageProtocol, marker?.JournalCodec);
        var source = marker!.Namespace;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_catalog.set_config('search_path', 'pg_catalog,' || pg_catalog.quote_ident({source}) || ',pg_temp', true)
            """, token);
        if (db.UsesRootParkOpening)
        {
            // Every Park set that admits operator commands, Recover or a composed one, pins their receipts too.
            var receipts = db.UsesRootOperatorProtocol;
            await db.Database.ExecuteSqlRawAsync(receipts ? ParkRecoverySources : ParkSources, token);
            var parkSources = await db.Database.SqlQuery<bool>($"""
                SELECT count(*) = {(receipts ? 14 : 13)} AS "Value"
                FROM (VALUES ('"KernelStoreVersions"'), ('"FoundationRootIssuer"'), ('"FoundationRootBirths"'),
                    ('"Instances"'), ('"Messages"'), ('"Partitions"'), ('"Nodes"'), ('"Journal"'), ('"Subscriptions"'),
                    ('"FoundationDirectReceipts"'), ('"FoundationActorList"'), ('"FoundationRootReceipts"'), ('"Events"'),
                    ('"FoundationOperatorReceipts"')) required(name)
                JOIN pg_catalog.pg_class c ON c.oid = pg_catalog.to_regclass(required.name) AND c.relkind = 'r'
                JOIN pg_catalog.pg_namespace ns ON ns.oid = c.relnamespace AND ns.nspname = {source}
                WHERE {receipts} OR required.name <> '"FoundationOperatorReceipts"'
                """).SingleAsync(token);
            if (!parkSources) throw new StoreFormatException("Park requires the source tables in the marker schema.");
            return;
        }
        await db.Database.ExecuteSqlRawAsync("""
            LOCK TABLE "FoundationRootIssuer", "FoundationRootBirths", "Instances", "FoundationOperatorReceipts", "Messages"
            IN ACCESS SHARE MODE
            """, token);
        if (db.UsesRootRetirement)
            await db.Database.ExecuteSqlRawAsync("""
                LOCK TABLE "Partitions", "Nodes", "Journal", "Subscriptions", "FoundationDirectReceipts", "FoundationActorList",
                    "FoundationRootReceipts" IN ACCESS SHARE MODE
                """, token);
        var sameSchema = await db.Database.SqlQuery<bool>($"""
            SELECT count(*) = {(db.UsesRootRetirement ? 13 : 6)} AS "Value"
            FROM (VALUES ('"KernelStoreVersions"'), ('"FoundationRootIssuer"'), ('"FoundationRootBirths"'),
                ('"Instances"'), ('"FoundationOperatorReceipts"'), ('"Messages"'), ('"Partitions"'), ('"Nodes"'), ('"Journal"'), ('"Subscriptions"'),
                ('"FoundationDirectReceipts"'), ('"FoundationActorList"'), ('"FoundationRootReceipts"')) required(name)
            JOIN pg_catalog.pg_class c ON c.oid = pg_catalog.to_regclass(required.name)
            JOIN pg_catalog.pg_namespace ns ON ns.oid = c.relnamespace AND ns.nspname = {source}
            WHERE {db.UsesRootRetirement} OR required.name NOT IN ('"Partitions"', '"Nodes"', '"Journal"', '"Subscriptions"', '"FoundationDirectReceipts"', '"FoundationActorList"', '"FoundationRootReceipts"')
            """).SingleAsync(token);
        if (!sameSchema)
            throw new StoreFormatException("Selected operator admission requires the source relations in one schema.");
    }

    // A candidate read. A subsequent owner must recheck the command and checkpoint at its fence before applying it.
    internal async Task<Message?> ReadDueRootOperatorAsync(string tenant, string reference, CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesRootOperatorProtocol) return null;
        await using var pinned = await EfStore.BeginPinnedRootOperationAsync(db, token);
        if (pinned is null) await EfStore.RequireStoreVersionsAsync(db, token);
        var row = await db.Messages.FromSqlInterpolated($"""
            SELECT m.* FROM "Messages" m JOIN "FoundationRootBirths" b ON b."Actor" = m."Target" AND b."Partition" = m."Partition"
            WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND b."Creation" = 1
                AND m."InboxScope" = {InboxKey.FoundationOccurrenceScope} AND m."Id" LIKE 'operator/%' AND NOT m."Done"
                AND m."Due" <= floor(extract(epoch from clock_timestamp()) * 1000)::bigint ORDER BY m."Seq" LIMIT 1
            """).AsNoTracking().SingleOrDefaultAsync(token);
        return row is null ? null : DecodeOperator(db, row, reference);
    }

    internal static Message DecodeOperator(KernelDb db, MessageEntity row, string reference) =>
        DecodeOperator(row, reference, db.UsesRootTermination, db.UsesRootRetirement, db.UsesRootTerminateThenDelete,
            db.UsesRootRetention, db.UsesRootOperatorReplies, db.UsesRootParkRecovery, db.UsesRootParkedOperators);

    internal static Message DecodeOperator(MessageEntity row, string reference, bool terminations, bool retirements, bool terminateThenDelete,
        bool retentionDeletes = false, bool operatorReplies = false, bool parkRecovery = false, bool parkedTerminate = false)
    {
        if (!IsOperatorOccurrence(new(row.Id, row.InboxScope)) || !IsStoredOperator(row.Start) || row.Kind is not (Kinds.Suspend or Kinds.Resume or Kinds.Terminate or Kinds.Delete or Kinds.Recover) ||
            (row.Kind == Kinds.Recover) != parkRecovery ||
            row.Kind == Kinds.Terminate && !terminations || row.Kind == Kinds.Delete && !retirements || row.EventMatch is not null || row.Payload is not null || row.Sender is not null ||
            row.JobGeneration != 0 || row.KeepUntil is not { } until)
            throw new StoreException(Failure.Definite, "Unexpected stored one-way operator envelope.");
        var held = row.Start!.StartsWith(HeldOperatorPrefix, StringComparison.Ordinal);
        if (held != (row.ReplyTo is not null) || held && (!operatorReplies || !ValidOperatorReply(row.ReplyTo!) ||
            row.Kind is not (Kinds.Terminate or Kinds.Delete)))
            throw new StoreException(Failure.Definite, "Unexpected stored operator reply route.");
        var (header, thenDelete, retentionDelete) = ReadOperatorHeader(row.Start!, reference, row.Id, row.Kind, row.Due,
            held, terminateThenDelete, retentionDeletes, parkedTerminate);
        var suffix = row.Id.LastIndexOf('/');
        return FoundationIngressAddress.Occurrence(header.Operation, row.Id[..suffix], row.Id[(suffix + 1)..],
            new(row.Target), header.Creation, row.Kind, null, row.Due != 0 || header.ExplicitZeroDue ? row.Due : null, until, row.ReplyTo, new(null)
            {
                QualifiedTerminal = header.Terminal, DeleteTarget = thenDelete ? reference : null,
                RootThenDelete = thenDelete, RetentionDelete = retentionDelete, RootHeldReply = held,
                RecoverMode = row.Kind == Kinds.Recover ? ParkRecoveryMode.Resume : null, RootParkMark = header.Park, RootParkWindow = header.Window,
                RootParkClear = header.Clear == true
            });
    }

    internal static (string Id, string Kind, long? Due) InspectOperatorHeader(KernelDb db, string? wire, string reference,
        string occurrence, string kind, long due, bool hasReply)
    {
        if (!db.UsesRootOperatorProtocol || !IsStoredOperator(wire) || kind is not (Kinds.Suspend or Kinds.Resume or Kinds.Terminate or Kinds.Delete or Kinds.Recover) ||
            (kind == Kinds.Recover) != db.UsesRootParkRecovery ||
            kind == Kinds.Terminate && !db.UsesRootTermination || kind == Kinds.Delete && !db.UsesRootRetirement)
            throw new StoreException(Failure.Definite, "Unexpected stored PostgreSQL operator envelope.");
        var held = wire!.StartsWith(HeldOperatorPrefix, StringComparison.Ordinal);
        if (held != hasReply || held && (!db.UsesRootOperatorReplies || kind is not (Kinds.Terminate or Kinds.Delete)))
            throw new StoreException(Failure.Definite, "Unexpected stored operator reply route.");
        var (header, _, _) = ReadOperatorHeader(wire, reference, occurrence, kind, due, held,
            db.UsesRootTerminateThenDelete, db.UsesRootRetention, db.UsesRootParkedOperators);
        return (header.Operation, kind, due != 0 || header.ExplicitZeroDue ? due : null);
    }

    private static (StoredOperator Header, bool ThenDelete, bool RetentionDelete) ReadOperatorHeader(string framing,
        string reference, string occurrence, string kind, long due, bool held, bool terminateThenDelete, bool retentionDeletes,
        bool parkedTerminate)
    {
        var wire = held ? framing[HeldOperatorPrefix.Length..] : framing;
        var thenDelete = wire.StartsWith(TerminateThenDeletePrefix, StringComparison.Ordinal);
        var retentionDelete = wire.StartsWith(RetentionDeletePrefix, StringComparison.Ordinal);
        var prefix = retentionDelete ? RetentionDeletePrefix : thenDelete ? TerminateThenDeletePrefix : OperatorPrefix;
        if (!wire.StartsWith(prefix, StringComparison.Ordinal))
            throw new StoreException(Failure.Definite, "Unexpected stored operator framing.");
        StoredOperator? header;
        try { header = DurableKeyJson.Read<StoredOperator>(wire[prefix.Length..]); }
        catch (JsonException error) { throw new StoreException(Failure.Definite, "Invalid stored operator framing.", error); }
        if (header is null) throw new StoreException(Failure.Definite, "Invalid stored operator framing.");
        if (header is not { Operation.Length: > 0 } || header.Reference != reference || header.Creation != 1 || header.Kind != kind ||
            (kind == Kinds.Delete ? header.Terminal is not { Revision: > 0 } : header.Terminal is not null) ||
            (kind == Kinds.Recover ? header.Park is not { Revision: > 0, At: > 0 } || header.Window is not >= 0 || due != 0 || header.ExplicitZeroDue ||
                held || thenDelete || retentionDelete
            : kind == Kinds.Terminate && parkedTerminate && header.Park is not null
                ? header.Park is not { Revision: > 0, At: > 0 } || header.Window is not >= 0 || thenDelete && !terminateThenDelete || retentionDelete ||
                    header.Clear is false || header.Clear == true && (held || header.ExplicitZeroDue || due != 0)
            : header.Park is not null || header.Window is not null) ||
            header.Clear is not null && (kind != Kinds.Terminate || header.Park is null || !parkedTerminate) ||
            thenDelete && (!terminateThenDelete || kind != Kinds.Terminate) ||
            retentionDelete && (!retentionDeletes || kind != Kinds.Delete || held) ||
            header.ExplicitZeroDue && due != 0 || !MatchesDirectOccurrence(reference, header.Operation, occurrence[OperatorOccurrencePrefix.Length..], retentionDelete))
            throw new StoreException(Failure.Definite, "The operator occurrence differs from its bound reference and operation.");
        return (header, thenDelete, retentionDelete);
    }

    private static bool ValidOperatorReply(string address, long? expectedDeadline = null) =>
        ReplyTo.Parse(address) is { Node.Length: > 0, Request.Length: > 0, Deadline: > 0, BeforeCommit: false } route &&
        (expectedDeadline is null || route.Deadline == expectedDeadline);

    internal async Task<InstanceStatus?> ReadOperatorTimeoutStatusAsync(string tenant, string reference, CancellationToken token)
    {
        await using var db = create();
        await using var pinned = await EfStore.BeginPinnedRootOperationAsync(db, token);
        return await db.Database.SqlQuery<InstanceStatus?>($"""
            SELECT i."Status" AS "Value"
            FROM "Instances" i JOIN "FoundationRootBirths" b ON b."Actor" = i."Id" AND b."Partition" = i."Partition"
            WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND b."Creation" = 1
                AND i."FoundationTenant" = b."Tenant" AND i."FoundationReference" = b."Reference"
            """).SingleOrDefaultAsync(token);
    }

    internal async Task<int> SweepOperatorReceiptsAsync(CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesRootOperatorProtocol) return 0;
        await EfStore.RequireStoreVersionsAsync(db, token);
        return await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM "FoundationOperatorReceipts" WHERE "Until" < floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            """, token);
    }
}
