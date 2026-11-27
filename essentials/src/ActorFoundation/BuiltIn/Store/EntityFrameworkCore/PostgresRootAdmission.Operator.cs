using System.Text.Json;
using System.Text.Json.Serialization;
using Kernel.Durable;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    internal const string OperatorPrefix = "!kernel-operator/1:";
    internal const string HeldOperatorPrefix = "!kernel-held-operator/1:";
    internal const string TerminateThenDeletePrefix = "!kernel-operator/2:";
    internal const string RetentionDeletePrefix = "!kernel-operator/3:";
    private const string RetentionOperationPrefix = "~retention-";
    private sealed record StoredOperator(string Reference, string Operation, long Creation, string Kind,
        [property: JsonRequired] bool ExplicitZeroDue)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FoundationTerminalStamp? Terminal { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FoundationParkMark? Park { get; init; }
        // A recover, and a Terminate or Delete that names a parked root's Park, carry the ordinary admission window, which they give
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
        OperatorReply? reply = null, bool requireExactTarget = false)
    {
        if (kind is not (Kinds.Suspend or Kinds.Resume or Kinds.Terminate or Kinds.Delete or Kinds.Recover) || !retentionDelete && !FoundationMetadataLimits.IsOutsideMessageId(operation))
            throw new ArgumentException("Expected a one-way exact-root operator command.");
        var window = keepFor is { } keep && keep < deduplicationWindow ? keep : deduplicationWindow;
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        var windowMs = (long)window.TotalMilliseconds;
        using var attempt = retentionDelete ? null : pressure?.Begin(tenant, RootAdmissionPressure.Operator(kind));
        await using var db = create();
        RequirePressureProfile(db);
        if (!db.UsesRootOperatorProtocol || kind == Kinds.Terminate && !db.UsesRootTermination) return null;
        // The standalone recovery profile admits only Recover. The composition also admits the root operator chain.
        var recover = kind == Kinds.Recover;
        // Recover is immediate: a delayed one could mature behind the cohort it releases.
        if (recover ? !db.UsesRootParkRecovery || due is not null || reply is not null
            : db.UsesRootParkRecovery && !db.UsesRootComposedParkRecovery) return null;
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
            long? retentionMarker = null;
            EfStore.RetentionAdmissionPolicy? retentionPolicy = null;
            if (selected)
            {
                if (retentionDelete && db.UsesRootRetentionPolicy)
                {
                    retentionMarker = await PinRootCommitNamespaceAsync(db, writesJournal: false, token);
                    retentionPolicy = await EfStore.ReadRetentionAdmissionPolicyAsync(db, tenant, retentionMarker, now: null, token);
                }
                else
                    await PinRootNamespaceAsync(db, token);
                var cursor = await ReadSelectionCursorAsync(db, token);
                if (deletion)
                {
                    terminal = PostgresRootSelection.OpenTerminal(cursor, selection, tenant, reference);
                    // S5b-3b: in a parked-operator set a Delete may name the root's Park instead of its terminal mark.
                    if (terminal is null && !retentionDelete && db.UsesRootParkedOperators &&
                        PostgresRootSelection.OpenPark(cursor, selection, tenant, reference) is { Revision: > 0, At: > 0 } parkedDelete)
                        park = parkedDelete;
                    else if (terminal is not { Revision: > 0 }) return null;
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
            var address = reply is null || attempt?.IsBusy == true ? await RootAddressAsync(db, tenant, reference, token) :
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
                operation = RetentionOperationFor(tenant, reference, address.Creation, terminal!, retentionPolicy?.Policy.Revision);
            }
            var digest = FoundationRootCreator.OperationDigest(operation);

            Task<string?> LiveReceiptAsync() => db.Database.SqlQuery<string>($"""
                SELECT "Reference" AS "Value" FROM "FoundationOperatorReceipts"
                WHERE "Tenant" = {tenant} AND "Operation" = {digest} AND "Until" >= floor(extract(epoch from clock_timestamp()) * 1000)::bigint
                    AND ({retentionMarker}::bigint IS NULL OR pg_catalog.to_regclass('"KernelStoreVersions"')::oid::bigint = {retentionMarker})
                """).SingleOrDefaultAsync(token);

            async Task<Receipt?> ReplayAsync(string prior)
            {
                await tx.CommitAsync(token);
                return (selected || requireExactTarget) && prior != reference ? null : new(prior, true);
            }
            if (attempt?.IsBusy == true)
            {
                if (await LiveReceiptAsync() is { } winner) return await ReplayAsync(winner);
                attempt.RequireCapacity();
            }

            // Lock the selected checkpoint before reading its receipt. If the command already
            // ended this actor, the receipt committed before the owner could consume it.
            var terminalTarget = terminal is null ? null :
                await LockRootDeleteSelectionAsync(db, address, tenant, reference, terminal, retentionMarker, token);
            var exactTerminal = terminalTarget is not null &&
                (retentionPolicy is null || retentionPolicy.Allows(terminalTarget.Header, terminalTarget.Status, terminal!.At));
            // S5b-2: a Terminate, or a Delete, that names the Park is refused while its Park is being cleared.
            var exactPark = park is not null && await LocksRootParkSelectionAsync(db, address, tenant, reference, park, recover, token) &&
                (recover && !db.UsesRootComposedParkRecovery || !await ClearsRootParkAsync(db, address, token));
            var selectedStatus = selected && !deletion && park is null
                ? await AdmissionStatusAsync(db, address, tenant, reference, token, requireCheckpoint: true)
                : null;
            var prior = await LiveReceiptAsync();
            if (prior is not null) return await ReplayAsync(prior);
            if (!deletion && park is null)
                selectedStatus = selected ? selectedStatus : await AdmissionStatusAsync(db, address, tenant, reference, token);
            // S5b: a composed Park set admits a Terminate, or a Terminate and delete, for a parked root, held or not, only with the
            // selection of its Park. A running selection is refused there; a command admitted before the Park opened is refused
            // by the owner, which answers a held caller.
            var parked = selectedStatus == InstanceStatus.Faulted && db.UsesRootParkedOperators && kind == Kinds.Terminate &&
                await LocksParkedRootAsync(db, address, tenant, reference, token);
            var eligible = terminal is not null ? exactTerminal : park is not null ? exactPark
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

            var actor = new InstanceRef(address.Id);
            try { attempt?.Require(actor.Partition); }
            catch (RootAdmissionRefused)
            {
                if (await LiveReceiptAsync() is { } winner) return await ReplayAsync(winner);
                throw;
            }
            var receipt = await db.Database.SqlQuery<long>($"""
                INSERT INTO "FoundationOperatorReceipts" AS r ("Tenant","Operation","Reference","Until")
                SELECT {tenant},{digest},{reference},floor(extract(epoch from clock_timestamp()) * 1000)::bigint + {windowMs}
                WHERE {retentionMarker}::bigint IS NULL OR pg_catalog.to_regclass('"KernelStoreVersions"')::oid::bigint = {retentionMarker}
                ON CONFLICT ("Tenant","Operation") DO UPDATE SET "Reference" = EXCLUDED."Reference",
                    "Until" = floor(extract(epoch from clock_timestamp()) * 1000)::bigint + {windowMs}
                WHERE r."Until" < floor(extract(epoch from clock_timestamp()) * 1000)::bigint RETURNING "Until" AS "Value"
                """).ToListAsync(token);
            if (receipt.Count == 0)
            {
                // A competing transaction recorded this operation while we qualified the requested root.
                prior = await db.Database.SqlQuery<string>($"""
                    SELECT "Reference" AS "Value" FROM "FoundationOperatorReceipts" WHERE "Tenant" = {tenant} AND "Operation" = {digest}
                        AND ({retentionMarker}::bigint IS NULL OR pg_catalog.to_regclass('"KernelStoreVersions"')::oid::bigint = {retentionMarker})
                    """).SingleOrDefaultAsync(token);
                return prior is not null ? await ReplayAsync(prior) : null;
            }
            var replyAddress = reply?.Address;
            var (id, header) = EncodeOperator(reference, operation, digest, address.Creation, kind, due, terminal, park,
                park is not null ? (long)deduplicationWindow.TotalMilliseconds : null, thenDelete, retentionDelete, replyAddress);
            if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Start", "Due", "KeepUntil", "Done", "Seen", "Attempts", "Trial", "ReplyTo")
                SELECT {InboxKey.FoundationOccurrenceScope}, {id}, {actor.Partition}, {address.Id}, {kind}, {header}, {due ?? 0}, {receipt.Single()}, false, 0, 0, 0, {replyAddress}
                WHERE {retentionMarker}::bigint IS NULL OR pg_catalog.to_regclass('"KernelStoreVersions"')::oid::bigint = {retentionMarker}
                """, token) != 1) return null;
            await tx.CommitAsync(token);
            attempt?.Committed();
            return new(reference, false);
        }
        catch (PostgresException error) when (selected && IsUnavailableSelectedSchema(error))
        {
            throw new StoreFormatException("The qualified root store schema is unreadable.", error);
        }
    }

    internal static (string Id, string Header) EncodeOperator(string reference, string operation, string digest, long creation,
        string kind, long? due, FoundationTerminalStamp? terminal = null, FoundationParkMark? park = null, long? window = null,
        bool thenDelete = false, bool retentionDelete = false, string? replyAddress = null)
    {
        var occurrence = $"{OperatorOccurrencePrefix}{reference}/{digest}/{Guid.NewGuid():N}";
        var prefix = retentionDelete ? RetentionDeletePrefix : thenDelete ? TerminateThenDeletePrefix : OperatorPrefix;
        var header = (replyAddress is null ? "" : HeldOperatorPrefix) + prefix +
            DurableKeyJson.Write(new StoredOperator(reference, operation, creation, kind, due == 0)
            {
                Terminal = terminal, Park = park, Window = window
            });
        return (occurrence, header);
    }

    internal static string RetentionOperationFor(string tenant, string reference, long creation, FoundationTerminalStamp terminal,
        long? policyRevision = null) =>
        RetentionOperationPrefix + FoundationRootCreator.OperationDigest(policyRevision is { } revision
            ? DurableKeyJson.Write(new { tenant, reference, Creation = creation, Mark = terminal, PolicyRevision = revision })
            : DurableKeyJson.Write(new { tenant, reference, Creation = creation, Mark = terminal }));

    private static Task<EfStore.RetentionAdmissionTarget?> LockRootDeleteSelectionAsync(KernelDb db, CreationAddress address, string tenant,
        string reference, FoundationTerminalStamp mark, long? marker, CancellationToken token) =>
        db.Database.SqlQuery<EfStore.RetentionAdmissionTarget>($"""
            SELECT CASE WHEN {marker}::bigint IS NOT NULL THEN i."FoundationHeader" END AS "Header", i."Status"
            FROM "Instances" i
            WHERE i."Id" = {address.Id} AND i."FoundationTenant" = {tenant} AND i."FoundationReference" = {reference}
                AND i."Status" <> {(int)InstanceStatus.Running} AND NOT i."Retired"
                AND i."FoundationTerminalRevision" = {mark.Revision} AND i."FoundationTerminalAt" = {mark.At}
                AND ({marker}::bigint IS NULL OR EXISTS (SELECT 1 FROM pg_catalog.pg_class v
                    JOIN pg_catalog.pg_class source ON source.oid = i.tableoid AND source.relnamespace = v.relnamespace
                    WHERE v.oid::bigint = {marker} AND v.oid = pg_catalog.to_regclass('"KernelStoreVersions"')))
            FOR SHARE OF i
            """).SingleOrDefaultAsync(token);

    // The checkpoint stays shared-locked until the receipt and occurrence commit, so the Park cannot move underneath them.
    // A parked root carries no terminal mark until it ends.
    private static async Task<bool> LocksRootParkSelectionAsync(KernelDb db, CreationAddress address, string tenant,
        string reference, FoundationParkMark mark, bool requireUnsuspended, CancellationToken token) =>
        address.Creation == 1 && (await db.Database.SqlQuery<long>($"""
            SELECT "Revision" AS "Value" FROM "Instances"
            WHERE "Id" = {address.Id} AND "Partition" = {new InstanceRef(address.Id).Partition}
                AND "FoundationTenant" = {tenant} AND "FoundationReference" = {reference} AND NOT "Retired"
                AND "Status" = {(int)InstanceStatus.Faulted} AND "FoundationParkAt" = {mark.At} AND "FoundationParkRevision" = {mark.Revision}
                AND "FoundationTerminalRevision" IS NULL AND (NOT {requireUnsuspended} OR NOT "Suspended")
            FOR SHARE
            """).ToListAsync(token)).Count == 1;

    private static async Task<bool> ClearsRootParkAsync(KernelDb db, CreationAddress address, CancellationToken token) =>
        await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {address.Id} AND "Partition" = {new InstanceRef(address.Id).Partition}
                AND "FoundationParkClearing" IS NOT NULL) AS "Value"
            """).SingleAsync(token);

    // The next control of an open clearing: the Terminate's, or the Delete's, kind, operation and Park mark under a fresh occurrence.
    internal static (string Id, string Header) ClearControl(string reference, string operation, string kind, FoundationParkMark park,
        long window, bool thenDelete) =>
        ($"{OperatorOccurrencePrefix}{reference}/{FoundationRootCreator.OperationDigest(operation)}/{Guid.NewGuid():N}",
            (thenDelete ? TerminateThenDeletePrefix : OperatorPrefix) + DurableKeyJson.Write(new StoredOperator(reference, operation, 1, kind, false)
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

    internal static Task PinRootNamespaceAsync(KernelDb db, CancellationToken token, bool writesJournal = false) =>
        PinRootNamespaceAsync(db, false, token, writesJournal);

    internal static async Task<uint?> PinRootCommitNamespaceAsync(KernelDb db, bool writesJournal, CancellationToken token) =>
        (await PinRootNamespaceAsync(db, loadIssuer: false, token, writesJournal))?.Marker;

    internal static Task<uint> PinRetentionPolicySourceAsync(KernelDb db, CancellationToken token)
    {
        var pin = new NpgsqlBatchCommand("""
            SELECT v.tableoid,
                pg_catalog.set_config('search_path', 'pg_catalog,' || pg_catalog.quote_ident(ns.nspname) || ',pg_temp', true)
            FROM "KernelStoreVersions" v
            JOIN pg_catalog.pg_class marker ON marker.oid = v.tableoid
            JOIN pg_catalog.pg_namespace ns ON ns.oid = marker.relnamespace
            WHERE v."Id" = $1
            """);
        pin.Parameters.Add(new NpgsqlParameter<int> { TypedValue = StoreVersionEntity.MarkerId });
        return ReadGuardedAdmissionAsync<uint>(db, [pin], async (reader, marker, ct) =>
        {
            if (!await reader.ReadAsync(ct) || reader.GetFieldValue<uint>(0) != marker)
                throw new StoreFormatException("The retention policy's physical store changed.");
            return marker;
        }, token);
    }

    internal static async Task<uint> PinPlaybackReadSourcesAsync(KernelDb db, CancellationToken token) =>
        (await PinRootNamespaceAsync(db, false, token, writesJournal: true))?.Marker
            ?? throw new StoreFormatException("Playback requires a qualified physical root source.");

    private static async Task<PinnedRootSource?> PinRootNamespaceAsync(KernelDb db, bool loadIssuer, CancellationToken token,
        bool writesJournal = false)
    {
        EfStore.RefuseSqliteShippedRuntime(db);
        try
        {
            if (db.UsesRootParkOpening)
                return await PinRootParkSourcesAsync(db, loadIssuer, writesJournal && db.UsesRootSequencedJournal, token);
            await PinRootSourceAsync(db, token);
            return null;
        }
        catch (PostgresException error) when (IsUnavailableSelectedSchema(error))
        {
            throw new StoreFormatException("The qualified root store schema is unreadable.", error);
        }
    }

    private static async Task PinRootSourceAsync(KernelDb db, CancellationToken token)
    {
        var namespaceQuery = new NpgsqlBatchCommand("""
            SELECT ns.nspname
            FROM "KernelStoreVersions" v
            JOIN pg_catalog.pg_class c ON c.oid = v.tableoid
            JOIN pg_catalog.pg_namespace ns ON ns.oid = c.relnamespace
            WHERE v."Id" = $1
            """);
        namespaceQuery.Parameters.Add(new NpgsqlParameter<int> { TypedValue = StoreVersionEntity.MarkerId });
        var source = await ReadGuardedAdmissionAsync(db, namespaceQuery, static reader => reader.GetString(0), token)
            ?? throw new StoreFormatException("The kernel store version marker schema is unreadable.");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_catalog.set_config('search_path', 'pg_catalog,' || pg_catalog.quote_ident({source}) || ',pg_temp', true)
            """, token);
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

    internal sealed record PinnedMarkSource(uint Marker, uint Namespace, uint Messages);

    private sealed record MarkSourceRead(NpgsqlBatchCommand Bindings, NpgsqlBatchCommand? Fence)
    {
        internal List<EfStore.WorkerRoot> Roots { get; } = [];
        internal PinnedMarkSource? Source { get; set; }
        internal long? FenceTime { get; set; }
    }

    internal static async Task<(IReadOnlyList<EfStore.WorkerRoot> Roots, PinnedMarkSource Source, long? FenceTime)> PinRootMarkBindingsAsync(
        KernelDb db, int? partition, string[] actors, string[] references, IReadOnlyList<PartitionFence>? checkedFences, CancellationToken token)
    {
        var bindings = new NpgsqlBatchCommand("""
            SELECT marker.oid, marker.relnamespace, messages.oid, b."Tenant", b."Reference", b."Actor", b."Partition", b."Fingerprint"
            FROM pg_catalog.pg_class marker
            JOIN pg_catalog.pg_class messages ON messages.oid = pg_catalog.to_regclass('"Messages"')
                AND messages.relnamespace = marker.relnamespace AND messages.relkind = 'r'
            LEFT JOIN "FoundationRootBirths" b ON ($1::integer IS NULL OR b."Partition" = $1) AND b."Creation" = 1
                AND (b."Actor" = ANY($2) OR b."Reference" = ANY($3))
            WHERE marker.oid = pg_catalog.to_regclass('"KernelStoreVersions"')
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_class birth
                    WHERE birth.oid = pg_catalog.to_regclass('"FoundationRootBirths"')
                        AND birth.relnamespace = marker.relnamespace AND birth.relkind = 'r')
            """);
        bindings.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = (object?)partition ?? DBNull.Value });
        bindings.Parameters.Add(new NpgsqlParameter<string[]> { TypedValue = actors });
        bindings.Parameters.Add(new NpgsqlParameter<string[]> { TypedValue = references });
        var read = new MarkSourceRead(bindings, checkedFences is null ? null : LockMarkFences(checkedFences));
        try
        {
            await PinRootParkSourcesAsync(db, loadIssuer: false, pinSequencedJournal: false, token, read);
        }
        catch (PostgresException error) when (IsUnavailableSelectedSchema(error))
        {
            throw new StoreFormatException("The qualified root store schema is unreadable.", error);
        }
        return (read.Roots, read.Source!, read.FenceTime);
    }

    // In checked mode an otherwise unchanged fence needs only the ordered row locks. All mark DML
    // is sent after the checked result and runs while these exact epoch/position rows remain locked.
    private static NpgsqlBatchCommand LockMarkFences(IReadOnlyList<PartitionFence> fences)
    {
        var command = new NpgsqlBatchCommand("""
            WITH marker AS MATERIALIZED (
                SELECT oid, relnamespace FROM pg_catalog.pg_class
                WHERE oid = pg_catalog.to_regclass('"KernelStoreVersions"')
            ), locked AS MATERIALIZED (
                SELECT p."Id"
                FROM unnest($1::integer[], $2::bigint[], $3::bigint[]) AS wanted(id, epoch, position)
                JOIN "Partitions" p ON p."Id" = wanted.id AND p."Epoch" = wanted.epoch AND p."Position" = wanted.position
                JOIN pg_catalog.pg_class source ON source.oid = p.tableoid AND source.relkind = 'r'
                JOIN marker ON marker.relnamespace = source.relnamespace
                ORDER BY p."Id" FOR UPDATE OF p
            )
            SELECT marker.oid, marker.relnamespace, CASE WHEN (SELECT count(*) FROM locked) = cardinality($1)
                THEN (extract(epoch from clock_timestamp()) * 1000)::bigint END
            FROM marker
            """);
        command.Parameters.Add(new NpgsqlParameter<int[]> { TypedValue = fences.Select(fence => fence.Partition).ToArray() });
        command.Parameters.Add(new NpgsqlParameter<long[]> { TypedValue = fences.Select(fence => fence.Epoch).ToArray() });
        command.Parameters.Add(new NpgsqlParameter<long[]> { TypedValue = fences.Select(fence => fence.Position).ToArray() });
        return command;
    }

    private static Task<PinnedRootSource> PinRootParkSourcesAsync(KernelDb db, bool loadIssuer, bool pinSequencedJournal,
        CancellationToken token, MarkSourceRead? markRead = null)
    {
        // Every Park set that admits operator commands, Recover or a composed one, pins their receipts too.
        var receipts = db.UsesRootOperatorProtocol;
        var childLedger = db.UsesRootChildren;
        var lockTables = receipts ? ParkRecoverySources : ParkSources;
        if (childLedger)
            lockTables = lockTables.Replace("IN ACCESS SHARE MODE", ", \"FoundationRootChildCascades\" IN ACCESS SHARE MODE", StringComparison.Ordinal);

        var pin = new NpgsqlBatchCommand("""
            SELECT pg_catalog.set_config('search_path', 'pg_catalog,' || pg_catalog.quote_ident(ns.nspname) || ',pg_temp', true),
                c.oid, c.relnamespace
            FROM "KernelStoreVersions" v
            JOIN pg_catalog.pg_class c ON c.oid = v.tableoid
            JOIN pg_catalog.pg_namespace ns ON ns.oid = c.relnamespace
            WHERE v."Id" = $1
            """);
        pin.Parameters.Add(new NpgsqlParameter<int> { TypedValue = StoreVersionEntity.MarkerId });
        var journalReady = pinSequencedJournal ? ", " + EfStore.PostgresSequencedJournalReady("marker.oid") +
            (db.UsesRootPlayback ? " AND " + EfStore.PostgresPlaybackReady("marker.oid", db.UsesRootPlaybackDescendants) : "") : "";
        var validate = new NpgsqlBatchCommand($$"""
            SELECT marker.oid, marker.relnamespace, (
                SELECT count(*) = $1
                FROM (VALUES ('"KernelStoreVersions"'), ('"FoundationRootIssuer"'), ('"FoundationRootBirths"'),
                    ('"Instances"'), ('"Messages"'), ('"Partitions"'), ('"Nodes"'), ('"Journal"'), ('"Subscriptions"'),
                    ('"FoundationDirectReceipts"'), ('"FoundationActorList"'), ('"FoundationRootReceipts"'), ('"Events"'),
                    ('"FoundationOperatorReceipts"'), ('"FoundationRootChildCascades"')) required(name)
                JOIN pg_catalog.pg_class c ON c.oid = pg_catalog.to_regclass(required.name) AND c.relkind = 'r'
                    AND c.relnamespace = marker.relnamespace
                WHERE ($2 OR required.name <> '"FoundationOperatorReceipts"')
                    AND ($3 OR required.name <> '"FoundationRootChildCascades"')){{journalReady}}
            FROM pg_catalog.pg_class marker WHERE marker.oid = pg_catalog.to_regclass('"KernelStoreVersions"')
            """);
        validate.Parameters.Add(new NpgsqlParameter<int> { TypedValue = (receipts ? 14 : 13) + (childLedger ? 1 : 0) });
        validate.Parameters.Add(new NpgsqlParameter<bool> { TypedValue = receipts });
        validate.Parameters.Add(new NpgsqlParameter<bool> { TypedValue = childLedger });

        // Resolve lock targets after setting the local search path. The batch contains only reads and locks.
        // Before sending any business SQL, confirm the locked relations belong to the same physical marker.
        List<NpgsqlBatchCommand> commands = [pin, new(lockTables)];
        // Keep trigger state stable until the fenced write commits. ROW EXCLUSIVE permits other
        // writers but conflicts with ALTER TABLE ... ENABLE/DISABLE TRIGGER.
        if (pinSequencedJournal)
        {
            var tables = """LOCK TABLE "Journal", "FoundationSequencedJournal" """;
            if (db.UsesRootPlayback)
                tables += """, "FoundationJournalPlayback", "FoundationJournalPlaybackInvalid" """;
            if (db.UsesRootPlaybackDescendants)
                tables += """, "FoundationJournalPlaybackScopes", "FoundationJournalPlaybackReceipts" """;
            commands.Add(new(tables + " IN ROW EXCLUSIVE MODE"));
        }
        commands.Add(validate);
        if (loadIssuer) commands.Add(new("""SELECT issuer."Key", issuer."Partitions", marker.oid """ + RootIssuerSource));
        if (markRead is not null)
        {
            commands.Add(markRead.Bindings);
            if (markRead.Fence is { } fence) commands.Add(fence);
        }
        return ReadGuardedAdmissionAsync<PinnedRootSource>(db, commands, async (reader, marker, ct) =>
        {
            if (!await reader.ReadAsync(ct) || reader.GetFieldValue<uint>(1) != marker)
                throw new StoreFormatException("The kernel store version marker schema is unreadable.");
            var schema = reader.GetFieldValue<uint>(2);
            if (!await reader.NextResultAsync(ct) || !await reader.ReadAsync(ct) ||
                reader.GetFieldValue<uint>(0) != marker || reader.GetFieldValue<uint>(1) != schema || !reader.GetBoolean(2))
                throw new StoreFormatException(childLedger
                    ? "The child profile requires its source tables in the marker schema."
                    : "Park requires the source tables in the marker schema.");
            if (pinSequencedJournal && !reader.GetBoolean(3))
                throw new StoreFormatException("The installed sequenced journal projection is incomplete.");
            if (markRead is not null)
            {
                if (!await reader.NextResultAsync(ct) || !await reader.ReadAsync(ct))
                    throw new StoreFormatException("The qualified root mark source is unreadable.");
                var inbox = reader.GetFieldValue<uint>(2);
                do
                {
                    if (reader.GetFieldValue<uint>(0) != marker || reader.GetFieldValue<uint>(1) != schema || reader.GetFieldValue<uint>(2) != inbox)
                        throw new StoreFormatException("The qualified root mark source changed its physical schema.");
                    if (!reader.IsDBNull(3))
                        markRead.Roots.Add(new(reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetString(7)));
                }
                while (await reader.ReadAsync(ct));
                markRead.Source = new(marker, schema, inbox);
                if (markRead.Fence is not null)
                {
                    if (!await reader.NextResultAsync(ct) || !await reader.ReadAsync(ct) ||
                        reader.GetFieldValue<uint>(0) != marker || reader.GetFieldValue<uint>(1) != schema)
                        throw new StoreFormatException("The qualified root mark fence changed its physical schema.");
                    if (!reader.IsDBNull(2)) markRead.FenceTime = reader.GetInt64(2);
                }
            }
            if (!loadIssuer) return new(marker, schema, null);
            if (!await reader.NextResultAsync(ct) || !await reader.ReadAsync(ct))
                throw new StoreFormatException(RootIssuerMissing);
            if (reader.GetFieldValue<uint>(2) != marker)
                throw new StoreFormatException("The kernel store version marker changed its physical table.");
            return new(marker, schema, ReadIssuer(reader));
        }, token);
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
            db.UsesRootRetention, db.UsesRootOperatorReplies, db.UsesRootParkRecovery, db.UsesRootParkedOperators, db.UsesRootComposedParkRecovery);

    internal static Message DecodeOperator(MessageEntity row, string reference, bool terminations, bool retirements, bool terminateThenDelete,
        bool retentionDeletes = false, bool operatorReplies = false, bool parkRecovery = false, bool parkedTerminate = false,
        bool composedRecovery = false)
    {
        if (!IsOperatorOccurrence(new(row.Id, row.InboxScope)) || !IsStoredOperator(row.Start) ||
            !CanReadOperatorKind(row.Kind, terminations, retirements, parkRecovery, parkedTerminate, composedRecovery) ||
            row.EventMatch is not null || row.Payload is not null || row.Sender is not null || row.JobGeneration != 0 || row.KeepUntil is not { } until)
            throw new StoreException(Failure.Definite, "Unexpected stored one-way operator envelope.");
        var held = row.Start!.StartsWith(HeldOperatorPrefix, StringComparison.Ordinal);
        if (held != (row.ReplyTo is not null) || held && (!operatorReplies || !ValidOperatorReply(row.ReplyTo!) ||
            row.Kind is not (Kinds.Terminate or Kinds.Delete)))
            throw new StoreException(Failure.Definite, "Unexpected stored operator reply route.");
        var (header, thenDelete, retentionDelete) = ReadOperatorHeader(row.Start!, reference, row.Id, row.Kind, row.Due,
            held, terminateThenDelete, retentionDeletes, parkedTerminate, parkRecovery && !composedRecovery);
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
        if (!db.UsesRootOperatorProtocol || !IsStoredOperator(wire) ||
            !CanReadOperatorKind(kind, db.UsesRootTermination, db.UsesRootRetirement, db.UsesRootParkRecovery, db.UsesRootParkedOperators,
                db.UsesRootComposedParkRecovery))
            throw new StoreException(Failure.Definite, "Unexpected stored PostgreSQL operator envelope.");
        var held = wire!.StartsWith(HeldOperatorPrefix, StringComparison.Ordinal);
        if (held != hasReply || held && (!db.UsesRootOperatorReplies || kind is not (Kinds.Terminate or Kinds.Delete)))
            throw new StoreException(Failure.Definite, "Unexpected stored operator reply route.");
        var (header, _, _) = ReadOperatorHeader(wire, reference, occurrence, kind, due, held,
            db.UsesRootTerminateThenDelete, db.UsesRootRetention, db.UsesRootParkedOperators,
            db.UsesRootParkRecovery && !db.UsesRootComposedParkRecovery);
        return (header.Operation, kind, due != 0 || header.ExplicitZeroDue ? due : null);
    }

    private static bool CanReadOperatorKind(string kind, bool terminations, bool retirements, bool parkRecovery, bool parkedTerminate, bool composedRecovery) =>
        kind switch
        {
            Kinds.Suspend or Kinds.Resume => !parkRecovery || composedRecovery,
            Kinds.Terminate => terminations && (!parkRecovery || parkedTerminate),
            Kinds.Delete => retirements && (!parkRecovery || composedRecovery),
            Kinds.Recover => parkRecovery,
            _ => false
        };

    private static (StoredOperator Header, bool ThenDelete, bool RetentionDelete) ReadOperatorHeader(string framing,
        string reference, string occurrence, string kind, long due, bool held, bool terminateThenDelete, bool retentionDeletes,
        bool parkedTerminate, bool standaloneRecovery)
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
        if (standaloneRecovery && kind == Kinds.Terminate && header is not { Park: not null, Clear: null })
            throw new StoreException(Failure.Definite, "The recovery composition requires a Park-selected Terminate.");
        if (header is not { Operation.Length: > 0 } || header.Reference != reference || header.Creation != 1 || header.Kind != kind ||
            (kind == Kinds.Delete && !(parkedTerminate && header.Park is not null) ? header.Terminal is not { Revision: > 0 } : header.Terminal is not null) ||
            (kind == Kinds.Recover ? header.Park is not { Revision: > 0, At: > 0 } || header.Window is not >= 0 || due != 0 || header.ExplicitZeroDue ||
                held || thenDelete || retentionDelete
            : kind is (Kinds.Terminate or Kinds.Delete) && parkedTerminate && header.Park is not null
                ? header.Park is not { Revision: > 0, At: > 0 } || header.Window is not >= 0 || thenDelete && !terminateThenDelete || retentionDelete ||
                    header.Clear is false || header.Clear == true && (held || header.ExplicitZeroDue)
            : header.Park is not null || header.Window is not null) ||
            header.Clear is not null && (kind is not (Kinds.Terminate or Kinds.Delete) || header.Park is null || !parkedTerminate) ||
            thenDelete && (!terminateThenDelete || kind != Kinds.Terminate) ||
            retentionDelete && (!retentionDeletes || kind != Kinds.Delete || held) ||
            header.ExplicitZeroDue && due != 0 || !MatchesDirectOccurrence(reference, header.Operation, occurrence[OperatorOccurrencePrefix.Length..], retentionDelete))
            throw new StoreException(Failure.Definite, "The operator occurrence differs from its bound reference and operation.");
        return (header, thenDelete, retentionDelete);
    }

    internal static bool ValidOperatorReply(string address, long? expectedDeadline = null) =>
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
