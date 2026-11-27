using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

/// <summary>The host's exact immutable publication and material selection for one authored entry node.</summary>
public sealed record EventStartRegistration(string Node, string Key, string ActivationId, string SourceReferenceId, string ArtifactId,
    string ExecutableSelection, Contract.StartSpec Template)
{
    /// <summary>The authored durable mode. An absent value uses Balanced.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; init; }

    /// <summary>Checks the registration's storage bounds before its upstream publication becomes authoritative.</summary>
    public static void ValidatePublication(string slot, IReadOnlyList<EventStartRegistration> registrations) =>
        RootEventStarts.ValidatePublication(slot, registrations);
}

public enum EventRegistrationResult { Applied, Duplicate, Stale, Conflict }

public sealed record EventStartTarget(string Tenant, string Id, string Slot, long Revision, string EventId, string Key,
    string? Payload, EventStartRegistration Registration);

/// <summary>A pending target and its position in the physical insertion order.</summary>
public sealed record EventStartScanItem(long Ordinal, EventStartTarget Target);

/// <summary>Trusted Event registration and selected-target relay bound to one physical store and tenant.</summary>
public interface IEventStartAdmissions
{
    Task<EventRegistrationResult> ApplyAsync(string slot, long revision, IReadOnlyList<EventStartRegistration> registrations, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventStartTarget>> ListAsync(string? after, CancellationToken cancellationToken);
    Task<Contract.Admission> AdmitAsync(string target, Func<EventStartTarget, Contract.StartSpec> start, ProtectRootStartAsync protect,
        CancellationToken cancellationToken);
    Task<bool> ConfirmDrainedAsync(string slot, long revision, CancellationToken cancellationToken);

    /// <summary>Prunes one bounded page of settled receipts past the supported replay window.</summary>
    Task<int> PruneSettledAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}

/// <summary>Bounded sweeps of accepted targets under a fixed provider insertion ceiling.</summary>
public interface IOrderedEventStartAdmissions : IEventStartAdmissions
{
    Task<long> ReadPendingTailAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<EventStartScanItem>> ReadPendingPageAsync(long after, long through, int limit, CancellationToken cancellationToken);
}

/// <summary>Provider-owned registrations and accepted event starts. Removing a registration leaves its selected targets intact.</summary>
internal static partial class RootEventStarts
{
    internal const string Slots = "FoundationEventStartSlots";
    internal const string Registrations = "FoundationEventStartRegistrations";
    internal const string Targets = "FoundationEventStartTargets";
    internal const string MatchIndex = "IX_FoundationEventStartRegistrations_Match";
    internal const string PendingIndex = "IX_FoundationEventStartTargets_Pending";
    internal const string SlotPendingIndex = "IX_FoundationEventStartTargets_SlotPending";
    internal const string MatchColumns = """("Tenant", "Key", "Slot", "Node")""";
    internal const string PendingColumns = """("Tenant", "Id") WHERE "Reference" IS NULL""";
    internal const string SlotPendingColumns = """("Tenant", "Slot", "Revision") WHERE "Reference" IS NULL""";
    internal const string ReceiptIndex = "IX_FoundationEventStartTargets_Receipt";
    internal const string ReceiptColumns = """("Tenant", "ReceiptUntil", "Id") WHERE "Reference" IS NOT NULL""";
    internal const string ScanIndex = "IX_FoundationEventStartTargets_Scan";
    internal const string ScanColumns = """("Tenant", "Ordinal") WHERE "Reference" IS NULL""";
    internal const string Source = "/*event-start-source*/";
    private const long ReplayWindowMilliseconds = 60 * 60 * 1000;
    private const int PageSize = 64;

    internal static string SlotColumns(bool sqlite)
    {
        var (text, number) = Types(sqlite);
        return $"""
            "Tenant" {text} NOT NULL, "Slot" {text} NOT NULL,
            "Revision" {number} NOT NULL CHECK ("Revision" > 0),
            "Fingerprint" {text} NOT NULL, PRIMARY KEY ("Tenant", "Slot")
            """;
    }

    internal static string RegistrationColumns(bool sqlite)
    {
        var (text, _) = Types(sqlite);
        return $"""
            "Tenant" {text} NOT NULL, "Slot" {text} NOT NULL, "Node" {text} NOT NULL,
            "Key" {text} NOT NULL, "Authority" {text} NOT NULL,
            PRIMARY KEY ("Tenant", "Slot", "Node"),
            FOREIGN KEY ("Tenant", "Slot") REFERENCES "{Slots}" ("Tenant", "Slot")
            """;
    }

    internal static string TargetColumns(bool sqlite, bool retainsReceipts, bool orderedScan)
    {
        var (text, number) = Types(sqlite);
        var receiptColumn = retainsReceipts ? $""" "ReceiptUntil" {number} NOT NULL CHECK ("ReceiptUntil" >= 0), """ : "";
        var ordinalColumn = !orderedScan ? "" : sqlite
            ? """ "Ordinal" INTEGER PRIMARY KEY AUTOINCREMENT, """
            : """ "Ordinal" bigint GENERATED ALWAYS AS IDENTITY UNIQUE CHECK ("Ordinal" > 0), """;
        var targetKey = sqlite && orderedScan ? """UNIQUE ("Tenant", "Id")""" : """PRIMARY KEY ("Tenant", "Id")""";
        return $"""
            "Tenant" {text} NOT NULL, "Id" {text} NOT NULL, "Slot" {text} NOT NULL, "Node" {text} NOT NULL,
            "Revision" {number} NOT NULL CHECK ("Revision" > 0),
            "Key" {text} NOT NULL, "EventId" {text} NOT NULL, "Authority" {text}, "Payload" {text},
            "Reference" {text} UNIQUE REFERENCES "FoundationRootBirths" ("Reference"),
            "Intent" {text}, "BirthFingerprint" {text}, {receiptColumn}{ordinalColumn}{targetKey},
            CHECK (("Reference" IS NULL AND "Authority" IS NOT NULL AND "Intent" IS NULL AND "BirthFingerprint" IS NULL)
                OR ("Reference" IS NOT NULL AND "Authority" IS NULL AND "Payload" IS NULL AND "Intent" IS NOT NULL AND "BirthFingerprint" IS NOT NULL))
            """;
    }

    private static (string Text, string Number) Types(bool sqlite) => sqlite
        ? ("TEXT COLLATE BINARY", "INTEGER") : ("""text COLLATE "C" """, "bigint");

    private sealed record SlotState(bool SourceReady, long? Revision, string? Fingerprint);
    private sealed record RegistrationRow(string Node, string Key, string Authority);
    private sealed record MatchRow(bool SourceReady, string? Slot, string? Node, long? Revision, long? Bytes);
    private sealed record TargetRow(bool SourceReady, string? Id, string? Slot, long? Revision, string? EventId, string? Key,
        string? Payload, string? Authority, string? Reference, bool Birth);
    private sealed record SettlementRow(bool SourceReady, string? Id, string? Authority, string? Reference, string? Intent, bool Birth);

    // A slot update and authored publisher take the same event-key locks. PostgreSQL guards the slot first so
    // a concurrent higher revision cannot replace the old key set before it has been locked.
    internal static async Task<EventRegistrationResult> ApplyAsync(KernelDb db, string tenant, string slot, long revision,
        IReadOnlyList<EventStartRegistration> registrations, Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        RequireTransaction(db, bind);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        var rows = EncodePublication(slot, registrations, tenant);
        if (!db.UsesRootEventStartReceipts && registrations.Any(registration => registration.Mode is not null))
            throw new ArgumentException("The selected Event profile does not store authored persistence modes.");
        var envelope = JsonSerializer.Serialize(rows);
        var fingerprint = FoundationRootCreator.OperationDigest(envelope);

        if (bind is not null)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended({tenant + "_" + slot} COLLATE "C", 558160543))
                """, token);
        var prior = await db.Database.SqlQuery<SlotState>(Bind($"""
            WITH source AS MATERIALIZED (/*event-start-source*/)
            SELECT source."SourceReady", s."Revision", s."Fingerprint" FROM source
            LEFT JOIN "FoundationEventStartSlots" s ON source."SourceReady" AND s."Tenant" = {tenant} AND s."Slot" = {slot}
            """, bind)).SingleAsync(token);
        if (!prior.SourceReady) throw SourceChanged();
        if (revision < prior.Revision) return EventRegistrationResult.Stale;
        if (revision == prior.Revision)
            return prior.Fingerprint == fingerprint ? EventRegistrationResult.Duplicate : EventRegistrationResult.Conflict;

        if (bind is not null)
        {
            var keys = await db.Database.SqlQuery<string>(Bind($"""
                WITH source AS MATERIALIZED (/*event-start-source*/)
                SELECT DISTINCT r."Key" AS "Value" FROM "FoundationEventStartRegistrations" r, source
                WHERE source."SourceReady" AND r."Tenant" = {tenant} AND r."Slot" = {slot}
                """, bind)).ToArrayAsync(token);
            var all = keys.Concat(rows.Select(row => row.Key)).Distinct(StringComparer.Ordinal).ToArray();
            if (all.Length != 0) await RootEventAdmission.LockPostgresKeysAsync(db, all, token);
        }
        if (await db.Database.ExecuteSqlInterpolatedAsync(Bind($"""
                WITH source AS MATERIALIZED (/*event-start-source*/)
                INSERT INTO "FoundationEventStartSlots" ("Tenant", "Slot", "Revision", "Fingerprint")
                SELECT {tenant}, {slot}, {revision}, {fingerprint} FROM source WHERE "SourceReady"
                ON CONFLICT ("Tenant", "Slot") DO UPDATE SET "Revision" = excluded."Revision", "Fingerprint" = excluded."Fingerprint"
                WHERE "FoundationEventStartSlots"."Revision" < excluded."Revision"
                """, bind), token) != 1) throw SourceChanged();
        await db.Database.ExecuteSqlInterpolatedAsync(Bind($"""
            WITH source AS MATERIALIZED (/*event-start-source*/)
            DELETE FROM "FoundationEventStartRegistrations" WHERE "Tenant" = {tenant} AND "Slot" = {slot}
                AND (SELECT "SourceReady" FROM source)
            """, bind), token);
        if (rows.Count == 0)
        {
            if (bind is not null && !await db.Database.SqlQuery<bool>(Bind($"""
                    WITH source AS MATERIALIZED (/*event-start-source*/)
                    SELECT "SourceReady" AS "Value" FROM source
                    """, bind)).SingleAsync(token)) throw SourceChanged();
            return EventRegistrationResult.Applied;
        }

        var insert = bind is not null
            ? (FormattableString)$"""
                WITH source AS MATERIALIZED (/*event-start-source*/)
                INSERT INTO "FoundationEventStartRegistrations" ("Tenant", "Slot", "Node", "Key", "Authority")
                SELECT {tenant}, {slot}, row."Node", row."Key", row."Authority"
                FROM jsonb_to_recordset({envelope}::jsonb) row("Node" text, "Key" text, "Authority" text), source
                WHERE source."SourceReady"
                """
            : $"""
                INSERT INTO "FoundationEventStartRegistrations" ("Tenant", "Slot", "Node", "Key", "Authority")
                SELECT {tenant}, {slot}, json_extract(row.value, '$.Node'), json_extract(row.value, '$.Key'), json_extract(row.value, '$.Authority')
                FROM json_each({envelope}) row
                """;
        if (await db.Database.ExecuteSqlInterpolatedAsync(Bind(insert, bind), token) != rows.Count) throw SourceChanged();
        return EventRegistrationResult.Applied;
    }

    internal static void ValidatePublication(string slot, IReadOnlyList<EventStartRegistration> registrations) =>
        EncodePublication(slot, registrations, null);

    private static List<RegistrationRow> EncodePublication(string slot, IReadOnlyList<EventStartRegistration> registrations, string? tenant)
    {
        RequireName(slot);
        if (registrations.Count > FoundationMetadataLimits.MaxTurnSends) throw new ArgumentException("Too many event starts for one publication.");
        var rows = new List<RegistrationRow>(registrations.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        long bytes = 0;
        foreach (var registration in registrations.OrderBy(item => item.Node, StringComparer.Ordinal))
        {
            Validate(registration);
            if (!names.Add(registration.Node)) throw new ArgumentException("A publication has more than one event start on the same node.");
            var authority = DurableKeyJson.Write(registration);
            bytes += Encoding.UTF8.GetByteCount(authority);
            if (bytes > FoundationMetadataLimits.MessageContentBytes)
                throw new ArgumentException("The publication event registrations exceed their storage budget.");
            rows.Add(new(registration.Node, tenant is null ? registration.Key : Qualify(tenant, registration.Key), authority));
        }
        return rows;
    }

    internal static async Task<(int Count, long Bytes)> CaptureAsync(KernelDb db, EventRow publication, long publicationUntil, int available, long availableBytes,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        RequireTransaction(db, bind);
        var tenant = publication.FoundationTenant!;
        var length = bind is not null ? """octet_length(r."Authority")::bigint""" : """length(CAST(r."Authority" AS BLOB))""";
        var quote = Bind($"""
            WITH source AS MATERIALIZED (/*event-start-source*/)
            , matches AS MATERIALIZED (
                SELECT r."Slot", r."Node", slots."Revision", /*authority-bytes*/ AS "Bytes"
                FROM "FoundationEventStartRegistrations" r
                JOIN "FoundationEventStartSlots" slots ON slots."Tenant" = r."Tenant" AND slots."Slot" = r."Slot"
                WHERE r."Tenant" = {tenant} AND r."Key" = {publication.Key} AND (SELECT "SourceReady" FROM source)
                ORDER BY r."Slot", r."Node" LIMIT {available + 1}
            )
            SELECT source."SourceReady", matches.* FROM source LEFT JOIN matches ON true
            ORDER BY matches."Slot", matches."Node"
            """, bind);
        quote = FormattableStringFactory.Create(quote.Format.Replace("/*authority-bytes*/", length, StringComparison.Ordinal), quote.GetArguments());
        var matches = await db.Database.SqlQuery<MatchRow>(quote).ToListAsync(token);
        if (!matches[0].SourceReady) throw SourceChanged();
        if (matches is [{ Slot: null }]) return default;
        var packedPayload = publication.Payload is { } body ? DurableKeyJson.Write(body) : null;
        var payloadBytes = packedPayload is null ? 0 : Encoding.UTF8.GetByteCount(packedPayload);
        var bytes = matches.Sum(match => match.Bytes!.Value + payloadBytes);
        if (matches.Count > available || bytes > availableBytes)
            throw new StoreException(Failure.Definite, "The authored event's start targets exceed the atomic delivery limit.");

        // The key lock (or SQLite writer) keeps the bounded quote current. Only matched rows load the registered authority.
        var identities = JsonSerializer.Serialize(matches.Select(match => new
        {
            match.Slot, match.Node, match.Revision,
            Id = TargetId(tenant, publication.Id, match.Slot!, match.Node!)
        }));
        var table = bind is not null
            ? """jsonb_to_recordset({0}::jsonb) selected("Slot" text, "Node" text, "Revision" bigint, "Id" text)"""
            : """
                (SELECT json_extract(value, '$.Slot') AS "Slot", json_extract(value, '$.Node') AS "Node",
                    json_extract(value, '$.Revision') AS "Revision", json_extract(value, '$.Id') AS "Id" FROM json_each({0})) selected
                """;
        var payload = bind is not null ? "{4}::text" : "{4}";
        var receiptColumn = db.UsesRootEventStartReceipts ? """, "ReceiptUntil" """ : "";
        var receiptValue = db.UsesRootEventStartReceipts ? ", {5}" : "";
        var insert = FormattableStringFactory.Create($$"""
            WITH source AS MATERIALIZED (/*event-start-source*/)
            INSERT INTO "FoundationEventStartTargets" ("Tenant", "Id", "Slot", "Node", "Revision", "Key", "EventId", "Authority", "Payload"{{receiptColumn}})
            SELECT r."Tenant", selected."Id", r."Slot", r."Node", selected."Revision", r."Key", {3}, r."Authority", {{payload}}{{receiptValue}}
            FROM {{table}} JOIN "FoundationEventStartRegistrations" r
                ON r."Tenant" = {1} AND r."Slot" = selected."Slot" AND r."Node" = selected."Node" AND r."Key" = {2}
            JOIN "FoundationEventStartSlots" slots ON slots."Tenant" = r."Tenant" AND slots."Slot" = r."Slot"
                AND slots."Revision" = selected."Revision"
            WHERE (SELECT "SourceReady" FROM source)
            """, db.UsesRootEventStartReceipts
                ? [identities, tenant, publication.Key, publication.Id, packedPayload!, publicationUntil]
                : [identities, tenant, publication.Key, publication.Id, packedPayload!]);
        if (await db.Database.ExecuteSqlInterpolatedAsync(Bind(insert, bind), token) != matches.Count) throw SourceChanged();
        return (matches.Count, bytes);
    }

    // The cursor belongs to one scan. A new sweep must restart from null so a later publication behind it is found.
    internal static async Task<IReadOnlyList<EventStartTarget>> ListAsync(KernelDb db, string tenant, string? after,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        RequireTransaction(db, bind);
        var size = bind is not null
            ? """octet_length("Authority")::bigint + coalesce(octet_length("Payload"), 0)"""
            : """length(CAST("Authority" AS BLOB)) + coalesce(length(CAST("Payload" AS BLOB)), 0)""";
        var query = Bind($"""
            WITH source AS MATERIALIZED (/*event-start-source*/), sized AS MATERIALIZED (
                SELECT "Id", /*target-bytes*/ AS "Bytes" FROM "FoundationEventStartTargets"
                WHERE "Tenant" = {tenant} AND "Id" > {after ?? ""} AND "Reference" IS NULL
                    AND (SELECT "SourceReady" FROM source)
                ORDER BY "Id" LIMIT {PageSize}
            ), quoted AS MATERIALIZED (
                SELECT "Id", sum("Bytes") OVER (ORDER BY "Id") AS "Through",
                    row_number() OVER (ORDER BY "Id") AS "Ordinal" FROM sized
            ), page AS MATERIALIZED (
                SELECT t."Id", t."Slot", t."Revision", t."EventId", t."Key", t."Payload", t."Authority"
                FROM quoted q JOIN "FoundationEventStartTargets" t ON t."Tenant" = {tenant} AND t."Id" = q."Id"
                WHERE q."Ordinal" = 1 OR q."Through" <= {FoundationMetadataLimits.MessageContentBytes}
            )
            SELECT source."SourceReady", page.*, CAST(NULL AS TEXT) AS "Reference", false AS "Birth"
            FROM source LEFT JOIN page ON true ORDER BY page."Id"
            """, bind);
        query = FormattableStringFactory.Create(query.Format.Replace("/*target-bytes*/", size, StringComparison.Ordinal), query.GetArguments());
        var rows = await db.Database.SqlQuery<TargetRow>(query).ToArrayAsync(token);
        if (!rows[0].SourceReady) throw SourceChanged();
        return rows[0].Id is null ? [] : rows.Select(row => Project(tenant, row)).ToArray();
    }

    internal static async Task<(EventStartTarget? Pending, string? Settled)> ReadWorkAsync(KernelDb db, string tenant, string target,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        RequireTransaction(db, bind);
        var row = await db.Database.SqlQuery<TargetRow>(Bind($"""
            WITH source AS MATERIALIZED (/*event-start-source*/)
            SELECT source."SourceReady", t."Id", t."Slot", t."Revision", t."EventId", t."Key", t."Payload", t."Authority",
                t."Reference", EXISTS (SELECT 1 FROM "FoundationRootBirths" b WHERE b."Tenant" = t."Tenant"
                    AND b."Reference" = t."Reference" AND b."Fingerprint" = t."BirthFingerprint" AND b."Creation" = 1) AS "Birth"
            FROM source LEFT JOIN "FoundationEventStartTargets" t ON source."SourceReady" AND t."Tenant" = {tenant} AND t."Id" = {target}
            """, bind)).SingleAsync(token);
        if (!row.SourceReady || row.Reference is not null && !row.Birth) throw SourceChanged();
        if (row.Id is null) throw Refused();
        return row.Reference is { } reference ? (null, reference) : (Project(tenant, row), null);
    }

    private static EventStartTarget Project(string tenant, TargetRow row)
    {
        var authority = ReadRegistration(row.Authority!);
        if (Qualify(tenant, authority.Key) != row.Key || row.Id != TargetId(tenant, row.EventId!, row.Slot!, authority.Node))
            throw SourceChanged();
        return new(tenant, row.Id!, row.Slot!, row.Revision!.Value, row.EventId!, authority.Key,
            row.Payload is { } body ? DurableKeyJson.Read<string>(body) : null, authority);
    }

    // Under the target lock, the prepared start may only use the admission metadata frozen with this exact target.
    // A fulfilled target proves exact birth independently of the ordinary (expiring) start receipt.
    internal static async Task<string?> CheckTargetAsync(KernelDb db, string tenant, string id, StartSpec spec,
        Func<FormattableString, FormattableString>? bind, bool lockRow, CancellationToken token)
    {
        RequireTransaction(db, bind);
        FormattableString query = $"""
            WITH source AS MATERIALIZED (/*event-start-source*/), selected AS MATERIALIZED (
                SELECT t."Tenant", t."Id", t."Authority", t."Reference", t."Intent", t."BirthFingerprint"
                FROM "FoundationEventStartTargets" t
                WHERE t."Tenant" = {tenant} AND t."Id" = {id} AND (SELECT "SourceReady" FROM source) /*lock-target*/
            )
            SELECT source."SourceReady", target."Id", target."Authority", target."Reference", target."Intent",
                EXISTS (SELECT 1 FROM "FoundationRootBirths" b
                    WHERE b."Tenant" = target."Tenant" AND b."Reference" = target."Reference"
                        AND b."Fingerprint" = target."BirthFingerprint" AND b."Creation" = 1) AS "Birth"
            FROM source LEFT JOIN selected target ON true
            """;
        query = FormattableStringFactory.Create(query.Format.Replace("/*lock-target*/", bind is not null && lockRow ? "FOR UPDATE OF t" : "",
            StringComparison.Ordinal), query.GetArguments());
        var row = await db.Database.SqlQuery<SettlementRow>(Bind(query, bind)).SingleAsync(token);
        if (!row.SourceReady) throw SourceChanged();
        if (row.Id is null) throw Refused();
        if (row.Reference is { } reference)
        {
            // PostgreSQL can refresh the locked target after the winner commits while its birth is still
            // outside this statement's earlier snapshot. Read the proof again while holding the target.
            if (!row.Birth && (bind is null || !lockRow ||
                (await ReadWorkAsync(db, tenant, id, bind, token)).Settled != reference)) throw SourceChanged();
            return row.Intent == Intent(spec) ? reference : throw Refused();
        }
        var registration = ReadRegistration(row.Authority!);
        if (registration.Template.Type != spec.Model || registration.Template.TypeData != spec.Definition ||
            (History)registration.Template.History != spec.History || spec.Ephemeral) throw Refused();
        return null;
    }

    internal static async Task SettleAsync(KernelDb db, string tenant, string id, StartSpec spec, PreparedRootStart stage,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        var intent = Intent(spec);
        FormattableString query = $"""
            WITH source AS MATERIALIZED (/*event-start-source*/)
            UPDATE "FoundationEventStartTargets"
            SET "Reference" = {stage.Reference}, "Intent" = {intent}, "BirthFingerprint" = {stage.Fingerprint},
                "Authority" = NULL, "Payload" = NULL /*event-start-receipt*/
            WHERE "Tenant" = {tenant} AND "Id" = {id} AND "Reference" IS NULL AND (SELECT "SourceReady" FROM source)
                AND EXISTS (SELECT 1 FROM "FoundationRootBirths" b WHERE b."Tenant" = {tenant} AND b."Reference" = {stage.Reference}
                    AND b."Actor" = {stage.Actor} AND b."Partition" = {stage.Partition} AND b."Creation" = 1
                    AND b."Creator" = {"root:" + stage.Actor} AND b."Fingerprint" = {stage.Fingerprint})
            """;
        var deadline = "";
        if (db.UsesRootEventStartReceipts)
        {
            var maximum = bind is null ? "max" : "greatest";
            deadline = FormattableString.Invariant($""", "ReceiptUntil" = {maximum}("ReceiptUntil", {Clock(bind)} + {ReplayWindowMilliseconds})""");
        }
        query = FormattableStringFactory.Create(query.Format.Replace("/*event-start-receipt*/", deadline, StringComparison.Ordinal), query.GetArguments());
        if (await db.Database.ExecuteSqlInterpolatedAsync(Bind(query, bind), token) != 1) throw SourceChanged();
    }

    internal static Task<int> PruneSettledAsync(KernelDb db, string tenant,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        RequireTransaction(db, bind);
        if (!db.UsesRootEventStartReceipts) return Task.FromResult(0);

        // A target's ID is bound to its publisher's origin, checkpoint revision and effect ordinal.
        // Capture ran in that fenced checkpoint, so even after its publication receipt expires a later turn
        // cannot recreate this entitlement. Missing targets are refused, including under the final birth lock.
        FormattableString query = $"""
            WITH source AS MATERIALIZED (/*event-start-source*/),
                cutoff AS MATERIALIZED (SELECT /*event-start-clock*/ AS "Now"),
                expired AS MATERIALIZED (
                    SELECT "Id" FROM "FoundationEventStartTargets"
                    WHERE "Tenant" = {tenant} AND "Reference" IS NOT NULL AND "ReceiptUntil" < (SELECT "Now" FROM cutoff)
                        AND (SELECT "SourceReady" FROM source)
                    ORDER BY "ReceiptUntil", "Id" LIMIT {PageSize}
                )
            DELETE FROM "FoundationEventStartTargets"
            WHERE "Tenant" = {tenant} AND "Reference" IS NOT NULL AND "Id" IN (SELECT "Id" FROM expired)
            """;
        query = FormattableStringFactory.Create(query.Format.Replace("/*event-start-clock*/", Clock(bind), StringComparison.Ordinal), query.GetArguments());
        return db.Database.ExecuteSqlInterpolatedAsync(Bind(query, bind), token);
    }

    private static string Clock(Func<FormattableString, FormattableString>? bind) =>
        bind is null ? "/*sqlite-root-clock*/" : "floor(extract(epoch from clock_timestamp()) * 1000)::bigint";

    internal static async Task<Contract.Admission> RelayAsync(string target, Func<EventStartTarget, Contract.StartSpec> start,
        Func<CancellationToken, Task<(EventStartTarget? Pending, string? Settled)>> read,
        Func<StartSpec, CancellationToken, Task<Contract.Admission?>> admit, CancellationToken caller)
    {
        if (!FoundationRootCreator.IsGeneratedReference(target)) return new(Contract.AdmissionOutcome.Refused);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(caller);
        budget.CancelAfter(RootPreparedStarts.OneWayBudget);
        try
        {
            var (pending, settled) = await read(budget.Token);
            if (settled is not null) return new(Contract.AdmissionOutcome.Duplicate, new Contract.ActorRef(settled));
            var selected = start(pending ?? throw SourceChanged());
            var spec = new StartSpec(selected.Type, selected.TypeData, selected.Input, Ephemeral: selected.Ephemeral,
                History: (History)selected.History);
            return await admit(spec, budget.Token) ?? Unavailable();
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !caller.IsCancellationRequested) { return Unavailable(); }
        catch (RootAdmissionRefused refusal) { return refusal.Admission; }
        catch (MetadataLimitExceeded) { return new(Contract.AdmissionOutcome.Refused); }

        static Contract.Admission Unavailable() => new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.NodeBusy,
            RetryAfter: TimeSpan.FromSeconds(1));
    }

    internal static async Task<bool> ConfirmDrainedAsync(KernelDb db, string tenant, string slot, long revision,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        RequireTransaction(db, bind);
        return await db.Database.SqlQuery<bool>(Bind($"""
            WITH source AS MATERIALIZED (/*event-start-source*/)
            SELECT "SourceReady" AND EXISTS (SELECT 1 FROM "FoundationEventStartSlots" WHERE "Tenant" = {tenant}
                    AND "Slot" = {slot} AND "Revision" > {revision})
                AND NOT EXISTS (SELECT 1 FROM "FoundationEventStartTargets" WHERE "Tenant" = {tenant}
                    AND "Slot" = {slot} AND "Revision" = {revision} AND "Reference" IS NULL) AS "Value"
            FROM source
            """, bind)).SingleAsync(token);
    }

    internal static string Operation(string target) => "~prepared-event-start/1:" + target;

    private static string TargetId(string tenant, string publication, string slot, string node) =>
        FoundationRootCreator.OperationDigest(DurableKeyJson.Write(new { tenant, publication, slot, node }));
    private static string Intent(StartSpec spec) => FoundationRootCreator.OperationDigest(DurableKeyJson.Write(spec));
    private static string Qualify(string tenant, string key) => tenant + "_" + key;
    private static RootAdmissionRefused Refused() => new(Contract.RefusalReason.TargetMismatch, TimeSpan.Zero);
    private static StoreFormatException SourceChanged() => new("The selected event-start source changed or is incomplete.");

    private static EventStartRegistration ReadRegistration(string authority)
    {
        var registration = DurableKeyJson.Read<EventStartRegistration>(authority);
        Validate(registration);
        return registration;
    }

    private static void Validate(EventStartRegistration registration)
    {
        RequireName(registration.Node);
        RequireName(registration.ActivationId);
        RequireName(registration.SourceReferenceId);
        RequireName(registration.ArtifactId);
        if (!registration.Key.StartsWith("event:", StringComparison.Ordinal) || registration.Key.Length == "event:".Length ||
            !FoundationMetadataLimits.FitsLiveWaitName(registration.Key) ||
            !FoundationMetadataLimits.FitsContent(registration.ExecutableSelection, FoundationMetadataLimits.TypeDataBytes) ||
            string.IsNullOrEmpty(registration.ExecutableSelection) || registration.Mode is not (null or "Durable") ||
            registration.Template is not { Input: null, Ephemeral: false } template || !Enum.IsDefined(template.History) ||
            !FoundationMetadataLimits.FitsName(template.Type) ||
            !FoundationMetadataLimits.FitsContent(template.TypeData, FoundationMetadataLimits.TypeDataBytes))
            throw new ArgumentException("The event start registration does not carry a bounded authored authority.");
    }

    private static void RequireName(string value)
    {
        if (!FoundationMetadataLimits.FitsLiveWaitName(value)) throw new ArgumentException("The event start identity is invalid.");
    }

    private static void RequireTransaction(KernelDb db, Func<FormattableString, FormattableString>? bind)
    {
        if (!db.UsesRootEventStarts || db.Database.CurrentTransaction is null || db.Database.IsNpgsql() != (bind is not null))
            throw new StoreFormatException("Event starts require the selected provider transaction and profile.");
    }

    private static FormattableString Bind(FormattableString query, Func<FormattableString, FormattableString>? bind) =>
        bind?.Invoke(query) ?? EfStore.UseSqliteRootClock(FormattableStringFactory.Create(
            query.Format.Replace(Source, """SELECT true AS "SourceReady" """, StringComparison.Ordinal), query.GetArguments()));
}
