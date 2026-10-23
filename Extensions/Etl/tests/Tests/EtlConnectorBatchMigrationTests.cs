using Elsa.Etl.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Connector_batch_migration_preserves_http_chain_and_enforces_owed_batch_pointer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var database = new TestDatabase(directory);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();

        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261021100000_ConnectorHttpPages");
        await SeedHttpHistoryAsync(context);
        await migrator.MigrateAsync("20261021120000_ConnectorBatchReservations");

        Assert.Equal(1, await ForeignKeysEnabledAsync(context));
        await AssertNoForeignKeyViolationsAsync(context);
        Assert.Single(await context.Runs.AsNoTracking().ToListAsync());
        Assert.Equal(1, await context.ConnectorRuns.CountAsync());
        // Query only the historical columns: the current EF entity may include later migrations.
        var partition = await context.ConnectorPartitions.AsNoTracking().Select(row => new
        {
            row.ScopeKey, row.RunKey, row.PartitionOrdinal, row.CurrentRequestHash, row.OwedBatchId, row.BatchMetadataBytes
        }).SingleAsync();
        Assert.Equal(new string('b', 64), partition.CurrentRequestHash);
        Assert.Null(partition.OwedBatchId);
        Assert.Equal(0, partition.BatchMetadataBytes);
        // This migration target predates the source-fetch columns present in the current model.
        var request = await context.ConnectorHttpRequests.AsNoTracking()
            .Select(row => new { row.RequestHash, row.PageSequence }).SingleAsync();
        Assert.Equal(new string('b', 64), request.RequestHash);
        Assert.Equal(0, request.PageSequence);
        var page = await context.ConnectorHttpPages.AsNoTracking().Select(row => new { row.RequestHash, row.HasContinuation, row.NextRequestHash }).SingleAsync();
        Assert.Equal(request.RequestHash, page.RequestHash);
        Assert.Equal(new string('c', 64), page.NextRequestHash);
        Assert.True(page.HasContinuation);

        var batchId = new string('e', 64);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO elsa_etl_connector_batches
                (ScopeKey, RunKey, PartitionOrdinal, BatchId, StartKey, DescriptorJson, DescriptorDigest,
                 RecordCount, InputBytes, MetadataBytes, Settled, SchemaVersion)
            VALUES ({{partition.ScopeKey}}, {{partition.RunKey}}, {{partition.PartitionOrdinal}}, {{batchId}},
                    {{new string('f', 64)}}, '{}', {{new string('d', 64)}}, 1, 12, 2, 0, {{EtlRuntimeEfModule.SchemaVersion}});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE elsa_etl_connector_partitions SET OwedBatchId = {{batchId}}, BatchMetadataBytes = 2
            WHERE ScopeKey = {{partition.ScopeKey}} AND RunKey = {{partition.RunKey}} AND PartitionOrdinal = {{partition.PartitionOrdinal}};
            """);
        Assert.Equal(batchId, await context.ConnectorPartitions.Select(row => row.OwedBatchId).SingleAsync());

        var missingBatchError = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_partitions SET OwedBatchId = {new string('g', 64)} WHERE ScopeKey = {partition.ScopeKey} AND RunKey = {partition.RunKey} AND PartitionOrdinal = {partition.PartitionOrdinal}"));
        Assert.Equal(19, missingBatchError.SqliteErrorCode);
        Assert.Equal(batchId, await context.ConnectorPartitions.Select(row => row.OwedBatchId).SingleAsync());
        await AssertNoForeignKeyViolationsAsync(context);
    }

    private static async Task SeedHttpHistoryAsync(EtlRuntimeSqliteDbContext context)
    {
        var scopeKey = new string('a', 64);
        const string runKey = "migration-run";
        var requestHash = new string('b', 64);
        var nextRequestHash = new string('c', 64);
        var pageDigest = new string('d', 64);

        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO elsa_etl_runtime_runs
                (ScopeKey, RunKey, TenantId, PersistencePartition, RunId, InvocationFingerprint, InvocationJson, BindingsJson, Status,
                 PreparationOwner, PreparationFence, PreparationExpiresAtUtcTicks, OutstandingRecords, OutstandingBytes,
                 AcceptedRecords, RejectedRecords, FilteredRecords, Revision, CreatedAtUtcTicks, UpdatedAtUtcTicks, SchemaVersion)
            VALUES ({{scopeKey}}, {{runKey}}, 'tenant-a', 'partition-a', 'migration-run', {{new string('a', 64)}}, '{}', NULL, 1,
                    NULL, 0, NULL, 0, 0, 0, 0, 0, 1, 1, 1, '1.0.0');
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO elsa_etl_connector_runs
                (ScopeKey, RunKey, BindingsJson, PreparedJson, RetainedInputBytes, RetainedMetadataBytes, Revision, SchemaVersion)
            VALUES ({{scopeKey}}, {{runKey}}, '{}', NULL, 0, 0, 1, '1.0.0');
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO elsa_etl_connector_partitions
                (ScopeKey, RunKey, PartitionOrdinal, PositionJson, Completed, Owner, Fence, ExpiresAtUtcTicks,
                 PageCount, PageBytes, MetadataBytes, CurrentRequestSequence, CurrentRequestHash, Revision, SchemaVersion)
            VALUES ({{scopeKey}}, {{runKey}}, 0, '{}', 0, NULL, 1, NULL, 1, 2, 2, NULL, NULL, 1, '1.0.0');
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO elsa_etl_connector_http_requests
                (ScopeKey, RunKey, PartitionOrdinal, PageSequence, RequestHash, RequestJson, MetadataBytes,
                 PreviousPageSequence, PreviousHasContinuation, SchemaVersion)
            VALUES ({{scopeKey}}, {{runKey}}, 0, 0, {{requestHash}}, '{}', 2, NULL, NULL, '1.0.0');
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO elsa_etl_connector_http_pages
                (ScopeKey, RunKey, PartitionOrdinal, PageSequence, RequestHash, NextRequestHash, HasContinuation,
                 PageJson, ByteLength, PageDigest, MetadataBytes, SchemaVersion)
            VALUES ({{scopeKey}}, {{runKey}}, 0, 0, {{requestHash}}, {{nextRequestHash}}, 1, '[]', 2, {{pageDigest}}, 2, '1.0.0');
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE elsa_etl_connector_partitions
            SET CurrentRequestSequence = 0, CurrentRequestHash = {{requestHash}}
            WHERE ScopeKey = {{scopeKey}} AND RunKey = {{runKey}} AND PartitionOrdinal = 0;
            """);
    }

    private static async Task<int> ForeignKeysEnabledAsync(EtlRuntimeSqliteDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task AssertNoForeignKeyViolationsAsync(EtlRuntimeSqliteDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.False(await reader.ReadAsync());
    }
}
