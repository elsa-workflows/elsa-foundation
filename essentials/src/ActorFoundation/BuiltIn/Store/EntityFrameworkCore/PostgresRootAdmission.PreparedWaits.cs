using System.Data;
using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    internal const int PreparedWaitAuthorityMarkerId = 6;

    private sealed record PreparedWaitInstallState(bool HasTable, bool HasRoot, bool HasMarker);

    internal static async Task<bool> MayCreatePreparedWaitAuthorityAsync(KernelDb db, CancellationToken token)
    {
        var state = await db.Database.SqlQuery<PreparedWaitInstallState>($"""
            SELECT pg_catalog.to_regclass('"FoundationPreparedWaitReceipts"') IS NOT NULL AS "HasTable",
                pg_catalog.to_regclass('"FoundationRootIssuer"') IS NOT NULL AS "HasRoot",
                EXISTS (SELECT 1 FROM ONLY "KernelStoreVersions" WHERE "Id" = {PreparedWaitAuthorityMarkerId}) AS "HasMarker"
            """).SingleAsync(token);
        if (!state.HasTable && !state.HasRoot && !state.HasMarker) return true;
        if (state is { HasTable: true, HasRoot: true, HasMarker: true }) return false;
        throw new StoreFormatException("The PostgreSQL prepared wait authority is incomplete.");
    }

    internal static async Task InstallPreparedWaitAuthorityAsync(KernelDb db, bool createAuthority, CancellationToken token)
    {
        var format = db.RequiredFormat;
        var table = RootEventAdmission.PreparedWaitReceiptTable;
        if (createAuthority)
        {
            await db.Database.ExecuteSqlRawAsync(Definition(table), token);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "KernelStoreVersions" ("Id", "StoreSchema", "MessageProtocol", "JournalCodec")
                VALUES ({PreparedWaitAuthorityMarkerId}, {format.StoreSchema}, {format.MessageProtocol}, {format.JournalCodec})
                """, token);
        }
        await RequirePreparedWaitAuthorityAsync(db, token);
        await EfStore.RequirePostgresTableDefinitionsAsync(db, table, Definition, token);

        static string Definition(string name) => $"""CREATE TABLE "{name}" ({RootEventAdmission.PreparedWaitReceiptColumns(sqlite: false)})""";
    }

    private static async Task RequirePreparedWaitAuthorityAsync(KernelDb db, CancellationToken token)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                LOCK TABLE ONLY "FoundationPreparedWaitReceipts" IN ACCESS SHARE MODE
                """, token);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.WrongObjectType)
        {
            throw new StoreFormatException("The PostgreSQL prepared wait receipt relation is missing.", error);
        }
        var format = db.RequiredFormat;
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM ONLY "KernelStoreVersions" version JOIN pg_catalog.pg_class marker ON marker.oid = version.tableoid
                JOIN pg_catalog.pg_class target ON target.oid = pg_catalog.to_regclass('"FoundationPreparedWaitReceipts"')
                    AND target.relnamespace = marker.relnamespace AND target.relkind = 'r' AND target.relpersistence = 'p'
                    AND NOT target.relrowsecurity AND NOT target.relforcerowsecurity
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE target.oid IN (i.inhparent, i.inhrelid))
                WHERE version."Id" = {PreparedWaitAuthorityMarkerId} AND version."StoreSchema" = {format.StoreSchema}
                    AND version."MessageProtocol" = {format.MessageProtocol} AND version."JournalCodec" = {format.JournalCodec}) AS "Value"
            """).SingleAsync(token))
            throw new StoreFormatException("The PostgreSQL prepared wait receipt relation does not belong to the installed root source.");
    }

    internal async Task<PreparedWaitReceipt> DeliverPreparedWaitsAsync(Contract.BoundScope scope, string operation, string sourceDigest,
        IReadOnlyList<PreparedWaitDelivery> deliveries, bool allowNew, CancellationToken token)
    {
        if (scope.IsHostIsolated) throw new ArgumentException("A prepared external wait source requires an ordinary tenant.", nameof(scope));
        var selected = RootEventAdmission.SnapshotPreparedWaitDeliveries(operation, sourceDigest, deliveries);
        for (var retry = 0; ; retry++)
        {
            try { return await DeliverAsync(); }
            catch (PostgresException error) when (retry < 2 &&
                error.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10 * (retry + 1)), token);
            }
        }

        async Task<PreparedWaitReceipt> DeliverAsync()
        {
            using var attempt = pressure?.Begin(scope.Tenant, RootAdmissionKind.Input);
            await using var db = create();
            if (!db.UsesPreparedWaitDelivery) throw new NotSupportedException("The selected store cannot admit a prepared wait source.");
            RequirePressureProfile(db);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", token);
            var pin = await PinRootNamespaceAsync(db, loadIssuer: false, token) ??
                throw new StoreFormatException("Prepared wait admission requires a pinned PostgreSQL source.");
            await db.Database.ExecuteSqlRawAsync("""
                LOCK TABLE ONLY "FoundationRootBirths", ONLY "Instances", ONLY "Subscriptions", ONLY "Messages" IN ACCESS SHARE MODE
                """, token);
            await RequirePreparedWaitAuthorityAsync(db, token);
            var operationLock = scope.Tenant + "\u001fprepared-waits:" + operation;
            await RootEventAdmission.LockPostgresKeysAsync(db,
                [operationLock, .. selected.Select(delivery => scope.Tenant + "_" + delivery.Key)], token);
            var now = await RootEventAdmission.ReadPostgresClockAsync(db, token);
            var receipt = await RootEventAdmission.DeliverPreparedWaitsAsync(db, scope.Tenant, operation, sourceDigest,
                selected, allowNew, now, deduplicationWindow, attempt, Bind, token);
            await transaction.CommitAsync(token);
            attempt?.Committed();
            return receipt;

            FormattableString Bind(FormattableString query) => WithAdmissionSource(query, pin, "/*event-source*/", 5,
                """('"FoundationPreparedWaitReceipts"'), ('"Subscriptions"'), ('"FoundationRootBirths"'), ('"Instances"'), ('"Messages"')""",
                requirePermanentDirectRelations: true);
        }
    }
}
