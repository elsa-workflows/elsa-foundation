using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

internal static partial class RootEventStarts
{
    private const int PublicAuthorityMarkerId = 4;
    private const int PublicAuthorityTables = 6;
    private sealed record PublicInstallState(int Relations, bool RootSchema, bool HasMarker, bool ExactMarker);

    // The base format can be seeded before root installation. Authority commits with its relations under the root install
    // lock. Existing root relations also prevent a pre-marker installation from mistaking data loss for an empty store.
    internal static async Task<bool> MayCreatePostgresPublicAuthorityAsync(KernelDb db, CancellationToken token)
    {
        var format = db.RequiredFormat;
        var expectedRelations = PublicAuthorityTables + (db.UsesRootPublicEventHost ? 1 : 0) + (db.UsesRootPagedPublicEvents ? 2 : 0);
        var state = await db.Database.SqlQuery<PublicInstallState>($"""
            SELECT (SELECT count(*)::int FROM (VALUES ('"FoundationPublicEventStarts"'), ('"FoundationPublicEventTargets"'),
                    ('"FoundationPublicEventPublications"'), ('"FoundationPublicEventLiveWaits"'), ('"FoundationPublicEventLiveReceipts"'),
                    ('"FoundationPublicEventHorizons"'), (CASE WHEN {db.UsesRootPublicEventHost} THEN '"FoundationPublicEventHost"' END),
                    (CASE WHEN {db.UsesRootPagedPublicEvents} THEN '"FoundationPublicEventDispatch"' END),
                    (CASE WHEN {db.UsesRootPagedPublicEvents} THEN '"FoundationPagedEventStarts"' END))
                    AS authority(name) WHERE pg_catalog.to_regclass(name) IS NOT NULL) AS "Relations",
                (pg_catalog.to_regclass('"FoundationRootIssuer"') IS NOT NULL OR
                    pg_catalog.to_regclass('"FoundationRootBirths"') IS NOT NULL OR
                    pg_catalog.to_regclass('"FoundationRootReceipts"') IS NOT NULL) AS "RootSchema",
                EXISTS (SELECT 1 FROM "KernelStoreVersions" WHERE "Id" = {PublicAuthorityMarkerId}) AS "HasMarker",
                EXISTS (SELECT 1 FROM "KernelStoreVersions" WHERE "Id" = {PublicAuthorityMarkerId}
                    AND "StoreSchema" = {format.StoreSchema} AND "MessageProtocol" = {format.MessageProtocol}
                    AND "JournalCodec" = {format.JournalCodec}) AS "ExactMarker"
            """).SingleAsync(token);
        if (state is { HasMarker: true, ExactMarker: false })
            throw new StoreFormatException("The PostgreSQL public Event authority belongs to another store format.");
        if (state.Relations == expectedRelations) return false;
        if (state is { Relations: 0, RootSchema: false, HasMarker: false }) return true;
        throw new StoreFormatException("The initialized PostgreSQL public Event store is missing an authoritative relation.");
    }

    internal static async Task InstallPostgresAsync(KernelDb db, bool createPublicAuthority, CancellationToken token)
    {
        var receipts = db.UsesRootEventStartReceipts;
        var scan = db.UsesRootEventStartScan;
        var publicStarts = db.UsesRootPublicEventStarts;
        var hostAuthority = db.UsesRootPublicEventHost;
        var dispatch = db.UsesRootPagedPublicEvents;
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
        if (createPublicAuthority)
            await db.Database.ExecuteSqlRawAsync($"""
                CREATE TABLE "{RootPublicEventStarts.Registrations}" ({RootPublicEventStarts.RegistrationColumns(sqlite: false)});
                CREATE INDEX "{RootPublicEventStarts.CatchUpIndex}" ON "{RootPublicEventStarts.Registrations}" {RootPublicEventStarts.CatchUpColumns};
                CREATE TABLE "{RootPublicEventStarts.Targets}" ({RootPublicEventStarts.TargetColumns(sqlite: false)});
                CREATE TABLE "{RootPublicEventPublications.Table}" ({RootPublicEventPublications.Columns(sqlite: false)});
                CREATE TABLE "{RootPublicEventLiveWaits.Table}" ({RootPublicEventLiveWaits.Columns(sqlite: false)});
                CREATE INDEX "{RootPublicEventLiveWaits.CatchUpIndex}" ON "{RootPublicEventLiveWaits.Table}" {RootPublicEventLiveWaits.CatchUpColumns};
                CREATE TABLE "{RootPublicEventLiveWaits.Receipts}" ({RootPublicEventLiveWaits.ReceiptColumns(sqlite: false)});
                CREATE TABLE "{RootPublicEventPublications.Horizons}" ({RootPublicEventPublications.HorizonColumns(sqlite: false)});
                CREATE UNIQUE INDEX "{RootPublicEventPublications.ReplayIndex}" ON "{RootPublicEventPublications.Table}" {RootPublicEventPublications.ReplayColumns};
                CREATE INDEX "{RootPublicEventPublications.RetainedIndex}" ON "{RootPublicEventPublications.Table}" {RootPublicEventPublications.RetainedColumns};
                CREATE INDEX "{RootPublicEventPublications.CleanupIndex}" ON "{RootPublicEventPublications.Table}" {RootPublicEventPublications.CleanupColumns};
                """, token);
        else if (publicStarts)
            await db.Database.ExecuteSqlRawAsync("""
                LOCK TABLE ONLY "FoundationPublicEventStarts", ONLY "FoundationPublicEventTargets", ONLY "FoundationPublicEventPublications",
                    ONLY "FoundationPublicEventLiveWaits", ONLY "FoundationPublicEventLiveReceipts", ONLY "FoundationPublicEventHorizons" IN ACCESS SHARE MODE
                """, token);
        if (hostAuthority)
        {
            if (createPublicAuthority)
                await db.Database.ExecuteSqlRawAsync($"""
                    CREATE TABLE "{RootPublicEventHost.Table}" ({RootPublicEventHost.Columns(sqlite: false)})
                    """, token);
            else
                await db.Database.ExecuteSqlRawAsync("""
                    LOCK TABLE ONLY "FoundationPublicEventHost" IN ACCESS SHARE MODE
                    """, token);
        }
        if (dispatch)
        {
            if (createPublicAuthority)
                await db.Database.ExecuteSqlRawAsync($"""
                    CREATE TABLE "{RootPublicEventDispatch.Table}" ({RootPublicEventDispatch.Columns(sqlite: false)});
                    CREATE TABLE "{StagedTargets}" ({StagedTargetColumns(sqlite: false)});
                    CREATE INDEX "{StagedSlotIndex}" ON "{StagedTargets}" {StagedSlotColumns};
                    CREATE INDEX "{RootPublicEventStarts.PendingTargetIndex}" ON "{RootPublicEventStarts.Targets}" {RootPublicEventStarts.PendingTargetColumns};
                    """, token);
            else
                await db.Database.ExecuteSqlRawAsync("""
                    LOCK TABLE ONLY "FoundationPublicEventDispatch", ONLY "FoundationPagedEventStarts" IN ACCESS SHARE MODE
                    """, token);
        }
        var transaction = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Installing event starts requires the install transaction.");
        const string savepoint = "kernel_event_start_definitions";
        var suffix = Guid.NewGuid().ToString("N");
        var slots = "kernel_event_slots_" + suffix;
        var registrations = "kernel_event_regs_" + suffix;
        var targets = "kernel_event_targets_" + suffix;
        var publicRegistrations = "kernel_public_event_reg_" + suffix;
        var publicTargets = "kernel_public_event_target_" + suffix;
        var publicPublications = "kernel_public_event_row_" + suffix;
        var publicLive = "kernel_public_event_live_" + suffix;
        var publicLiveReceipts = "kernel_public_event_live_rcpt_" + suffix;
        var publicHorizons = "kernel_public_event_horizon_" + suffix;
        var publicHost = "kernel_public_event_host_" + suffix;
        var publicDispatch = "kernel_public_event_dispatch_" + suffix;
        var staged = "kernel_paged_event_starts_" + suffix;
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
                CREATE INDEX "{publicRegistrations}_c" ON "{publicRegistrations}" {RootPublicEventStarts.CatchUpColumns};
                CREATE TABLE "{publicTargets}" ({RootPublicEventStarts.TargetColumns(sqlite: false)});
                CREATE TABLE "{publicPublications}" ({RootPublicEventPublications.Columns(sqlite: false)});
                CREATE TABLE "{publicLive}" ({RootPublicEventLiveWaits.Columns(sqlite: false)});
                CREATE INDEX "{publicLive}_c" ON "{publicLive}" {RootPublicEventLiveWaits.CatchUpColumns};
                CREATE TABLE "{publicLiveReceipts}" ({RootPublicEventLiveWaits.ReceiptColumns(sqlite: false)});
                CREATE TABLE "{publicHorizons}" ({RootPublicEventPublications.HorizonColumns(sqlite: false)});
                CREATE UNIQUE INDEX "{publicPublications}_op" ON "{publicPublications}" {RootPublicEventPublications.ReplayColumns};
                CREATE INDEX "{publicPublications}_r" ON "{publicPublications}" {RootPublicEventPublications.RetainedColumns};
                CREATE INDEX "{publicPublications}_c" ON "{publicPublications}" {RootPublicEventPublications.CleanupColumns};
                """, token);

        if (hostAuthority)
            await db.Database.ExecuteSqlRawAsync($"""
                CREATE TABLE "{publicHost}" ({RootPublicEventHost.Columns(sqlite: false)})
                """, token);

        if (dispatch)
            await db.Database.ExecuteSqlRawAsync($"""
                CREATE TABLE "{publicDispatch}" ({RootPublicEventDispatch.Columns(sqlite: false)});
                CREATE TABLE "{staged}" ({StagedTargetColumns(sqlite: false)});
                CREATE INDEX "{staged}_s" ON "{staged}" {StagedSlotColumns};
                CREATE INDEX "{publicTargets}_p" ON "{publicTargets}" {RootPublicEventStarts.PendingTargetColumns};
                """, token);

        var same = await db.Database.SqlQuery<bool>($"""
            WITH pairs(expected, installed) AS (
                VALUES (pg_catalog.to_regclass({slots}), pg_catalog.to_regclass('"FoundationEventStartSlots"')),
                       (pg_catalog.to_regclass({registrations}), pg_catalog.to_regclass('"FoundationEventStartRegistrations"')),
                       (pg_catalog.to_regclass({targets}), pg_catalog.to_regclass('"FoundationEventStartTargets"')),
                       (pg_catalog.to_regclass({publicRegistrations}), pg_catalog.to_regclass('"FoundationPublicEventStarts"')),
                       (pg_catalog.to_regclass({publicTargets}), pg_catalog.to_regclass('"FoundationPublicEventTargets"')),
                       (pg_catalog.to_regclass({publicPublications}), pg_catalog.to_regclass('"FoundationPublicEventPublications"')),
                       (pg_catalog.to_regclass({publicLive}), pg_catalog.to_regclass('"FoundationPublicEventLiveWaits"')),
                       (pg_catalog.to_regclass({publicLiveReceipts}), pg_catalog.to_regclass('"FoundationPublicEventLiveReceipts"')),
                       (pg_catalog.to_regclass({publicHorizons}), pg_catalog.to_regclass('"FoundationPublicEventHorizons"')),
                       (pg_catalog.to_regclass({publicHost}), pg_catalog.to_regclass('"FoundationPublicEventHost"')),
                       (pg_catalog.to_regclass({publicDispatch}), pg_catalog.to_regclass('"FoundationPublicEventDispatch"')),
                       (pg_catalog.to_regclass({staged}), pg_catalog.to_regclass('"FoundationPagedEventStarts"'))
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
            SELECT count(*) = {3 + (publicStarts ? PublicAuthorityTables : 0) + (hostAuthority ? 1 : 0) + (dispatch ? 2 : 0)} AND bool_and(expected.columns = installed.columns AND expected.constraints = installed.constraints
                AND expected.indexes = installed.indexes) AS "Value"
            FROM pairs JOIN shapes expected ON expected.oid = pairs.expected JOIN shapes installed ON installed.oid = pairs.installed
            """).SingleAsync(token);
        await transaction.RollbackToSavepointAsync(savepoint, token);
        if (!same) throw new StoreFormatException("The installed event start definitions differ from the selected profile.");
        if (hostAuthority) await RootPublicEventHost.InstallAsync(db, db.PublicEventHost, createPublicAuthority, token);
        if (publicStarts)
        {
            var format = db.RequiredFormat;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "KernelStoreVersions" ("Id", "StoreSchema", "MessageProtocol", "JournalCodec")
                VALUES ({PublicAuthorityMarkerId}, {format.StoreSchema}, {format.MessageProtocol}, {format.JournalCodec})
                ON CONFLICT ("Id") DO NOTHING
                """, token);
        }
    }
}
