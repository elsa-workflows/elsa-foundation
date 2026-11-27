using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

internal static partial class RootEventStarts
{
    internal static async Task InstallPostgresAsync(KernelDb db, CancellationToken token)
    {
        var receipts = db.UsesRootEventStartReceipts;
        var scan = db.UsesRootEventStartScan;
        var publicStarts = db.UsesRootPublicEventStarts;
        var receiptIndex = receipts ? $"""CREATE INDEX IF NOT EXISTS "{ReceiptIndex}" ON "{Targets}" {ReceiptColumns};""" : "";
        var scanIndex = scan ? $"""CREATE INDEX IF NOT EXISTS "{ScanIndex}" ON "{Targets}" {ScanColumns};""" : "";
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE TABLE IF NOT EXISTS "{Slots}" ({SlotColumns(sqlite: false)});
            CREATE TABLE IF NOT EXISTS "{Registrations}" ({RegistrationColumns(sqlite: false)});
            CREATE INDEX IF NOT EXISTS "{MatchIndex}" ON "{Registrations}" {MatchColumns};
            CREATE TABLE IF NOT EXISTS "{Targets}" ({TargetColumns(sqlite: false, receipts, scan)});
            CREATE INDEX IF NOT EXISTS "{PendingIndex}" ON "{Targets}" {PendingColumns};
            CREATE INDEX IF NOT EXISTS "{SlotPendingIndex}" ON "{Targets}" {SlotPendingColumns};
            {receiptIndex}
            {scanIndex}
            """, token);
        if (publicStarts)
            await db.Database.ExecuteSqlRawAsync($"""
                CREATE TABLE IF NOT EXISTS "{RootPublicEventStarts.Registrations}" ({RootPublicEventStarts.RegistrationColumns(sqlite: false)});
                CREATE TABLE IF NOT EXISTS "{RootPublicEventStarts.Targets}" ({RootPublicEventStarts.TargetColumns(sqlite: false)});
                """, token);
        var transaction = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Installing event starts requires the install transaction.");
        const string savepoint = "kernel_event_start_definitions";
        var suffix = Guid.NewGuid().ToString("N");
        var slots = "kernel_event_slots_" + suffix;
        var registrations = "kernel_event_regs_" + suffix;
        var targets = "kernel_event_targets_" + suffix;
        var publicRegistrations = "kernel_public_event_reg_" + suffix;
        var publicTargets = "kernel_public_event_target_" + suffix;
        var compareReceipts = receipts ? $"""CREATE INDEX "{targets}_r" ON "{targets}" {ReceiptColumns};""" : "";
        var compareScan = scan ? $"""CREATE INDEX "{targets}_o" ON "{targets}" {ScanColumns};""" : "";
        await transaction.CreateSavepointAsync(savepoint, token);
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE TABLE "{slots}" ({SlotColumns(sqlite: false)});
            CREATE TABLE "{registrations}" ({RegistrationColumns(sqlite: false)});
            CREATE INDEX "{registrations}_m" ON "{registrations}" {MatchColumns};
            CREATE TABLE "{targets}" ({TargetColumns(sqlite: false, receipts, scan)});
            CREATE INDEX "{targets}_p" ON "{targets}" {PendingColumns};
            CREATE INDEX "{targets}_s" ON "{targets}" {SlotPendingColumns};
            {compareReceipts}
            {compareScan}
            """, token);
        if (publicStarts)
            await db.Database.ExecuteSqlRawAsync($"""
                CREATE TABLE "{publicRegistrations}" ({RootPublicEventStarts.RegistrationColumns(sqlite: false)});
                CREATE TABLE "{publicTargets}" ({RootPublicEventStarts.TargetColumns(sqlite: false)});
                """, token);

        var same = await db.Database.SqlQuery<bool>($"""
            WITH pairs(expected, installed) AS (
                VALUES (pg_catalog.to_regclass({slots}), pg_catalog.to_regclass('"FoundationEventStartSlots"')),
                       (pg_catalog.to_regclass({registrations}), pg_catalog.to_regclass('"FoundationEventStartRegistrations"')),
                       (pg_catalog.to_regclass({targets}), pg_catalog.to_regclass('"FoundationEventStartTargets"')),
                       (pg_catalog.to_regclass({publicRegistrations}), pg_catalog.to_regclass('"FoundationPublicEventStarts"')),
                       (pg_catalog.to_regclass({publicTargets}), pg_catalog.to_regclass('"FoundationPublicEventTargets"'))
            ), shapes AS (
                SELECT c.oid,
                    array(SELECT ROW(a.attname, a.atttypid, a.attnotnull, a.attcollation, a.atttypmod, a.atthasdef, a.attidentity)::text
                        FROM pg_catalog.pg_attribute a WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
                        ORDER BY a.attnum) AS columns,
                    array(SELECT ROW(k.convalidated, pg_catalog.pg_get_constraintdef(k.oid))::text
                        FROM pg_catalog.pg_constraint k WHERE k.conrelid = c.oid ORDER BY 1) AS constraints,
                    array(SELECT ROW(i.indisunique, i.indisvalid, i.indisready,
                            substring(pg_catalog.pg_get_indexdef(i.indexrelid) FROM ' USING .*$'))::text
                        FROM pg_catalog.pg_index i WHERE i.indrelid = c.oid ORDER BY 1) AS indexes
                FROM pg_catalog.pg_class c JOIN pg_catalog.pg_class marker ON marker.oid = pg_catalog.to_regclass('"KernelStoreVersions"')
                WHERE c.relkind = 'r' AND c.relpersistence = 'p' AND c.relnamespace = marker.relnamespace
                    AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE c.oid IN (i.inhparent, i.inhrelid))
                    AND c.oid IN (SELECT expected FROM pairs UNION SELECT installed FROM pairs)
            )
            SELECT count(*) = {(publicStarts ? 5 : 3)} AND bool_and(expected.columns = installed.columns AND expected.constraints = installed.constraints
                AND expected.indexes = installed.indexes) AS "Value"
            FROM pairs JOIN shapes expected ON expected.oid = pairs.expected JOIN shapes installed ON installed.oid = pairs.installed
            """).SingleAsync(token);
        await transaction.RollbackToSavepointAsync(savepoint, token);
        if (!same) throw new StoreFormatException("The installed event start definitions differ from the selected profile.");
    }
}
