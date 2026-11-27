using System.Security.Cryptography;
using System.Text;
using Kernel.Durable;
using Kernel.Foundation.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal const string RetentionPolicyTable = "FoundationRetentionPolicies";

    private static readonly ActorRetentionPolicy DefaultRetentionPolicy = new(0, new());

    private sealed record RetentionPolicyRow(string ActorType, long Revision, long? FinishedTicks, long? CancelledTicks,
        long? FaultedTicks, byte[] Authentication);

    private sealed record PostgresPolicyRow(string? ActorType, long? Revision, long? FinishedTicks, long? CancelledTicks,
        long? FaultedTicks, byte[]? Authentication, byte[] Key, long Relation, long AsOf);

    internal static string RetentionPolicyColumns(bool sqlite)
    {
        var ordinal = sqlite ? "BINARY" : "\"C\"";
        var tenant = sqlite ? """
            length("Tenant") BETWEEN 1 AND 64 AND instr("Tenant", char(0)) = 0 AND "Tenant" NOT GLOB '*[^a-z0-9-]*'
            """ : """ length("Tenant") BETWEEN 1 AND 64 AND "Tenant" !~ '[^a-z0-9-]' """;
        var integer = sqlite ? "INTEGER" : "bigint";
        var blob = sqlite ? "BLOB" : "bytea";
        return $"""
            "Tenant" TEXT COLLATE {ordinal} NOT NULL PRIMARY KEY CHECK ({tenant}),
            "ActorType" TEXT COLLATE {ordinal} NOT NULL CHECK (length("ActorType") BETWEEN 1 AND {6 * FoundationMetadataLimits.NameBytes + 2}),
            "Revision" {integer} NOT NULL CHECK ("Revision" > 0),
            "FinishedTicks" {integer} CHECK ("FinishedTicks" >= 0),
            "CancelledTicks" {integer} CHECK ("CancelledTicks" >= 0),
            "FaultedTicks" {integer} CHECK ("FaultedTicks" >= 0),
            "Authentication" {blob} NOT NULL CHECK (length("Authentication") = 32)
            """;
    }

    internal static Task InstallRootRetentionPolicyAsync(KernelDb db, CancellationToken token)
    {
        var sql = $"""CREATE TABLE IF NOT EXISTS "{RetentionPolicyTable}" ({RetentionPolicyColumns(sqlite: false)})""";
        return db.Database.ExecuteSqlRawAsync(sql, token);
    }

    internal async Task<ActorRetentionPolicy> ReadRootRetentionPolicyAsync(BoundScope scope, string actorType, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var type = EncodeRetentionActorType(actorType);
        await using var db = create();
        RequireRetentionPolicies(db);
        if (db.UsesSqliteRootRuntime)
            return await ReadSqliteRootSnapshotAsync(db, async _ =>
            {
                var key = await SqliteRootAdmission.ReadRootIssuerKeyAsync(db, token);
                return await ReadSqliteRetentionPolicyAsync(db, scope.Tenant, type, key, token);
            }, token);

        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var marker = await PostgresRootAdmission.PinRetentionPolicySourceAsync(db, token);
        var row = await ReadPostgresRetentionPolicyAsync(db, marker, scope.Tenant, token);
        return DecodePostgresRetentionPolicy(scope.Tenant, type, row);
    }

    internal async Task<ActorRetentionPolicyChange> TryApplyRootRetentionPolicyAsync(BoundScope scope, string actorType,
        long expectedRevision, ActorRetentionWindows proposed, long automaticNewlyEligibleLimit, string? confirmation, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(automaticNewlyEligibleLimit);
        var after = RetentionMillis.From(proposed);
        var type = EncodeRetentionActorType(actorType);
        var tenant = scope.Tenant;
        await using var db = create();
        RequireRetentionPolicies(db);
        if (db.UsesSqliteRootRuntime)
        {
            await db.Database.OpenConnectionAsync(token);
            token.ThrowIfCancellationRequested();
            await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
            await using var enlistment = await db.Database.UseTransactionAsync(transaction, token);
            await RequireSqliteOperationalFormatAsync(db, token);
            var key = await SqliteRootAdmission.ReadRootIssuerKeyAsync(db, token);
            var current = await ReadSqliteRetentionPolicyAsync(db, tenant, type, key, token);
            var at = await ReadSqliteRetentionClockAsync(db, token);
            var change = await EvaluatePolicyChangeAsync(current, expectedRevision, proposed, automaticNewlyEligibleLimit, at,
                new RetentionApproval(tenant, type, key), confirmation, before => ReadSqliteRetentionQuoteAtAsync(db, tenant, type, null, before, after, at, token));
            if (change.Outcome == ActorRetentionPolicyChangeOutcome.Applied)
            {
                var row = EncodeRetentionPolicy(tenant, type, change.Current, key);
                await WriteSqliteRetentionPolicyAsync(db, tenant, expectedRevision, row, token);
                token.ThrowIfCancellationRequested();
                await transaction.CommitAsync(token);
            }
            return change;
        }

        await using var postgresTransaction = await db.Database.BeginTransactionAsync(token);
        var marker = await PostgresRootAdmission.PinRetentionPolicySourceAsync(db, token);
        // Serialize policy CAS even while the tenant has no row. An unchanged policy never locks the actor projection.
        await db.Database.ExecuteSqlRawAsync("""LOCK TABLE "FoundationRetentionPolicies" IN SHARE ROW EXCLUSIVE MODE""", token);
        var stored = await ReadPostgresRetentionPolicyAsync(db, marker, tenant, token);
        var effective = DecodePostgresRetentionPolicy(tenant, type, stored);
        var result = await EvaluatePolicyChangeAsync(effective, expectedRevision, proposed, automaticNewlyEligibleLimit, stored.AsOf,
            new RetentionApproval(tenant, type, stored.Key), confirmation, async before =>
            {
                // Owner commits update this projection transactionally. Wait for existing writers and keep the latest
                // committed projection stable until the policy is applied or refused.
                await db.Database.ExecuteSqlRawAsync("""LOCK TABLE "FoundationActorList" IN SHARE MODE""", token);
                return await ReadPostgresRetentionQuoteAsync(db, tenant, EncodeListFilter(type), null, before, after, null, token, marker);
            });
        if (result.Outcome == ActorRetentionPolicyChangeOutcome.Applied)
        {
            var row = EncodeRetentionPolicy(tenant, type, result.Current, stored.Key);
            await WritePostgresRetentionPolicyAsync(db, marker, stored.Relation, tenant, expectedRevision, row, token);
            token.ThrowIfCancellationRequested();
            await postgresTransaction.CommitAsync(token);
        }
        return result;
    }

    private void RequireRetentionPolicies(KernelDb db)
    {
        if (Engine is not null || !db.UsesRootRetentionPolicy || !db.UsesSqliteRootRuntime && !postgres)
            throw new NotSupportedException("Durable retention policies require a qualified PostgreSQL or SQLite root store.");
    }

    private static string EncodeRetentionActorType(string actorType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);
        if (!FoundationMetadataLimits.FitsName(actorType))
            throw new ArgumentException("Invalid root retention actor type.", nameof(actorType));
        return DurableKeyJson.Write(actorType);
    }

    private static async Task<ActorRetentionPolicyChange> EvaluatePolicyChangeAsync(ActorRetentionPolicy current, long expected,
        ActorRetentionWindows proposed, long automaticLimit, long at, RetentionApproval approval, string? confirmation, Func<RetentionMillis, Task<ActorRetentionQuote>> quote)
    {
        var impact = current.Windows == proposed
            ? new ActorRetentionQuote(0, at)
            : await quote(RetentionMillis.From(current.Windows));
        if (current.Revision != expected) return new(ActorRetentionPolicyChangeOutcome.Stale, current, impact);
        if (current.Windows == proposed) return new(ActorRetentionPolicyChangeOutcome.Unchanged, current, impact);
        var maximum = Math.Max(automaticLimit, approval.ReviewedCount(confirmation, current.Revision, proposed));
        if (impact.NewlyEligible > maximum)
            return new(ActorRetentionPolicyChangeOutcome.OverLimit, current, impact, approval.Seal(current.Revision, proposed, impact));
        return new(ActorRetentionPolicyChangeOutcome.Applied, new(checked(expected + 1), proposed), impact);
    }

    private static RetentionPolicyRow EncodeRetentionPolicy(string tenant, string type, ActorRetentionPolicy policy, byte[] key)
    {
        var finished = policy.Windows.Finished?.Ticks;
        var cancelled = policy.Windows.Cancelled?.Ticks;
        var faulted = policy.Windows.Faulted?.Ticks;
        return new(type, policy.Revision, finished, cancelled, faulted,
            AuthenticateRetentionPolicy(tenant, type, policy.Revision, finished, cancelled, faulted, key));
    }

    private static ActorRetentionPolicy DecodeRetentionPolicy(string tenant, string type, RetentionPolicyRow? row, byte[] key)
    {
        if (row is null) return DefaultRetentionPolicy;
        if (!string.Equals(row.ActorType, type, StringComparison.Ordinal) || row.Revision <= 0 ||
            row.FinishedTicks is < 0 || row.CancelledTicks is < 0 || row.FaultedTicks is < 0 ||
            !CryptographicOperations.FixedTimeEquals(row.Authentication,
                AuthenticateRetentionPolicy(tenant, type, row.Revision, row.FinishedTicks, row.CancelledTicks, row.FaultedTicks, key)))
            throw new StoreFormatException("The tenant retention policy is invalid or unauthenticated.");
        return new(row.Revision, new(ToWindow(row.FinishedTicks), ToWindow(row.CancelledTicks), ToWindow(row.FaultedTicks)));
    }

    private static TimeSpan? ToWindow(long? ticks) => ticks is { } finite ? TimeSpan.FromTicks(finite) : null;

    private static byte[] AuthenticateRetentionPolicy(string tenant, string type, long revision, long? finishedTicks,
        long? cancelledTicks, long? faultedTicks, byte[] key) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(DurableKeyJson.Write(new
        {
            Domain = "elsa-preview/retention-policy/1", Tenant = tenant, Type = type, Revision = revision,
            FinishedTicks = finishedTicks, CancelledTicks = cancelledTicks, FaultedTicks = faultedTicks
        })));

    private static Task<long> ReadSqliteRetentionClockAsync(KernelDb db, CancellationToken token) =>
        db.Database.SqlQueryRaw<long>("SELECT " + SqliteCommitDialect.MillisecondClock + """ AS "Value" """).SingleAsync(token);

    private static async Task<ActorRetentionPolicy> ReadSqliteRetentionPolicyAsync(KernelDb db, string tenant, string type,
        byte[] key, CancellationToken token)
    {
        var row = await db.Database.SqlQuery<RetentionPolicyRow>($"""
            SELECT "ActorType", "Revision", "FinishedTicks", "CancelledTicks", "FaultedTicks", "Authentication"
            FROM "FoundationRetentionPolicies" WHERE "Tenant" = {tenant}
            """).SingleOrDefaultAsync(token);
        return DecodeRetentionPolicy(tenant, type, row, key);
    }

    private static async Task<PostgresPolicyRow> ReadPostgresRetentionPolicyAsync(KernelDb db, long marker, string tenant,
        CancellationToken token, string? requiredLock = null)
    {
        try
        {
            return await db.Database.SqlQuery<PostgresPolicyRow>($"""
                SELECT r."ActorType", r."Revision", r."FinishedTicks", r."CancelledTicks", r."FaultedTicks",
                    r."Authentication", issuer."Key", policy.oid::bigint AS "Relation",
                    floor(extract(epoch from statement_timestamp()) * 1000)::bigint AS "AsOf"
                FROM pg_catalog.pg_class v
                JOIN pg_catalog.pg_class policy ON policy.oid = pg_catalog.to_regclass('"FoundationRetentionPolicies"')
                    AND policy.relnamespace = v.relnamespace AND policy.relkind = 'r'
                JOIN "FoundationRootIssuer" issuer ON issuer."Singleton" AND issuer."Partitions" > 0
                    AND length(issuer."Key") = 32
                JOIN pg_catalog.pg_class source ON source.oid = issuer.tableoid
                    AND source.relnamespace = v.relnamespace AND source.relkind = 'r'
                LEFT JOIN "FoundationRetentionPolicies" r ON r."Tenant" = {tenant} AND r.tableoid = policy.oid
                WHERE v.oid = pg_catalog.to_regclass('"KernelStoreVersions"') AND v.oid::bigint = {marker}
                    AND ({requiredLock}::text IS NULL OR EXISTS (SELECT 1 FROM pg_catalog.pg_locks l
                        WHERE l.pid = pg_catalog.pg_backend_pid() AND l.locktype = 'relation'
                            AND l.relation = policy.oid AND l.mode = {requiredLock} AND l.granted))
                """).SingleOrDefaultAsync(token)
                ?? throw new StoreFormatException("The PostgreSQL retention policy source is missing or changed.");
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            throw new StoreFormatException("The PostgreSQL retention policy source is unreadable.", error);
        }
    }

    private static ActorRetentionPolicy DecodePostgresRetentionPolicy(string tenant, string type, PostgresPolicyRow row) =>
        DecodeRetentionPolicy(tenant, type, row.Revision is { } revision
            ? new(row.ActorType!, revision, row.FinishedTicks, row.CancelledTicks, row.FaultedTicks, row.Authentication!)
            : null, row.Key);

    private static async Task WriteSqliteRetentionPolicyAsync(KernelDb db, string tenant, long expected, RetentionPolicyRow row,
        CancellationToken token)
    {
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "FoundationRetentionPolicies" ("Tenant", "ActorType", "Revision", "FinishedTicks", "CancelledTicks",
                "FaultedTicks", "Authentication")
            SELECT {tenant}, {row.ActorType}, {row.Revision}, {row.FinishedTicks}, {row.CancelledTicks}, {row.FaultedTicks}, {row.Authentication}
            WHERE {expected} = 0 OR EXISTS (SELECT 1 FROM "FoundationRetentionPolicies" prior
                WHERE prior."Tenant" = {tenant} AND prior."Revision" = {expected} AND prior."ActorType" = {row.ActorType})
            ON CONFLICT ("Tenant") DO UPDATE SET "Revision" = excluded."Revision", "FinishedTicks" = excluded."FinishedTicks",
                "CancelledTicks" = excluded."CancelledTicks", "FaultedTicks" = excluded."FaultedTicks", "Authentication" = excluded."Authentication"
            WHERE "FoundationRetentionPolicies"."Revision" = {expected}
            """, token);
        if (affected != 1) throw new StoreFormatException("The SQLite retention policy changed during activation.");
    }

    private static async Task WritePostgresRetentionPolicyAsync(KernelDb db, long marker, long policy, string tenant, long expected,
        RetentionPolicyRow row, CancellationToken token)
    {
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "FoundationRetentionPolicies" AS current ("Tenant", "ActorType", "Revision", "FinishedTicks", "CancelledTicks",
                "FaultedTicks", "Authentication")
            SELECT {tenant}, {row.ActorType}, {row.Revision}, {row.FinishedTicks}, {row.CancelledTicks}, {row.FaultedTicks}, {row.Authentication}
            WHERE EXISTS (SELECT 1 FROM pg_catalog.pg_class v
                JOIN pg_catalog.pg_class relation ON relation.oid = pg_catalog.to_regclass('"FoundationRetentionPolicies"')
                    AND relation.oid::bigint = {policy} AND relation.relnamespace = v.relnamespace
                WHERE v.oid = pg_catalog.to_regclass('"KernelStoreVersions"') AND v.oid::bigint = {marker})
                AND ({expected} = 0 OR EXISTS (SELECT 1 FROM "FoundationRetentionPolicies" prior
                    WHERE prior."Tenant" = {tenant} AND prior."Revision" = {expected} AND prior."ActorType" = {row.ActorType}))
            ON CONFLICT ("Tenant") DO UPDATE SET "Revision" = excluded."Revision", "FinishedTicks" = excluded."FinishedTicks",
                "CancelledTicks" = excluded."CancelledTicks", "FaultedTicks" = excluded."FaultedTicks", "Authentication" = excluded."Authentication"
            WHERE current."Revision" = {expected}
            """, token);
        if (affected != 1) throw new StoreFormatException("The PostgreSQL retention policy source changed during activation.");
    }
}
