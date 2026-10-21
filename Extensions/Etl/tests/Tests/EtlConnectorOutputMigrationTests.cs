using Elsa.Etl.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Connector_output_migration_preserves_reserved_batch_rows_and_foreign_keys()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var database = new TestDatabase(directory);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();

        var migrator = await SeedHttpHistoryThroughBatchReservationsAsync(context);

        const string batchId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO elsa_etl_connector_batches
                (ScopeKey, RunKey, PartitionOrdinal, BatchId, StartKey, DescriptorJson, DescriptorDigest,
                 RecordCount, InputBytes, MetadataBytes, Settled, SchemaVersion)
            VALUES ({{new string('a', 64)}}, {{"migration-run"}}, 0, {{batchId}}, {{new string('f', 64)}}, '{}',
                    {{new string('d', 64)}}, 1, 12, 17, 0, {{EtlRuntimeEfModule.SchemaVersion}});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE elsa_etl_connector_partitions
            SET OwedBatchId = {{batchId}}, BatchMetadataBytes = 17
            WHERE ScopeKey = {{new string('a', 64)}} AND RunKey = {{"migration-run"}} AND PartitionOrdinal = 0;
            """);

        await migrator.MigrateAsync("20261021130000_ConnectorBatchOutputs");

        Assert.Equal(1, await ForeignKeysEnabledAsync(context));
        await AssertNoForeignKeyViolationsAsync(context);
        Assert.Single(await context.Runs.AsNoTracking().ToListAsync());
        var connectorRun = await context.ConnectorRuns.AsNoTracking().SingleAsync();
        Assert.Equal(0, connectorRun.RetainedOutputBytes);
        var partition = await context.ConnectorPartitions.AsNoTracking()
            .Select(row => new { row.OwedBatchId, row.BatchMetadataBytes, row.RetainedOutputBytes }).SingleAsync();
        Assert.Equal(batchId, partition.OwedBatchId);
        Assert.Equal(17, partition.BatchMetadataBytes);
        Assert.Equal(0, partition.RetainedOutputBytes);
        Assert.Single(await context.ConnectorHttpRequests.AsNoTracking().ToListAsync());
        Assert.Single(await context.ConnectorHttpPages.AsNoTracking().Select(row => row.PageSequence).ToListAsync());
        var batch = await context.ConnectorBatches.AsNoTracking()
            .Select(row => new { row.BatchId, row.Settled, row.MetadataBytes, row.OutputJson, row.OutputDigest, row.OutputBytes }).SingleAsync();
        Assert.Equal(batchId, batch.BatchId);
        Assert.False(batch.Settled);
        Assert.Equal(17, batch.MetadataBytes);
        Assert.Null(batch.OutputJson);
        Assert.Null(batch.OutputDigest);
        Assert.Equal(0, batch.OutputBytes);
    }

    [Fact]
    public async Task Connector_output_migration_enforces_output_plan_and_byte_constraints()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var database = new TestDatabase(directory);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();
        var migrator = await SeedHttpHistoryThroughBatchReservationsAsync(context);
        await migrator.MigrateAsync("20261021130000_ConnectorBatchOutputs");

        const string batchId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        // Insert only columns available at this historical migration target.
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO elsa_etl_connector_batches
                (ScopeKey, RunKey, PartitionOrdinal, BatchId, StartKey, DescriptorJson, DescriptorDigest,
                 RecordCount, InputBytes, MetadataBytes, Settled, SchemaVersion)
            VALUES ({{new string('a', 64)}}, {{"migration-run"}}, 0, {{batchId}}, {{new string('f', 64)}}, '{}',
                    {{new string('d', 64)}}, 0, 0, 0, 0, {{EtlRuntimeEfModule.SchemaVersion}});
            """);

        var missingPlanError = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET OutputDigest = {new string('a', 64)} WHERE BatchId = {batchId}"));
        Assert.Equal(19, missingPlanError.SqliteErrorCode);

        var missingDigestError = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET OutputJson = '{{}}' WHERE BatchId = {batchId}"));
        Assert.Equal(19, missingDigestError.SqliteErrorCode);

        var shortDigestError = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET OutputJson = '{{}}', OutputDigest = 'short' WHERE BatchId = {batchId}"));
        Assert.Equal(19, shortDigestError.SqliteErrorCode);

        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE elsa_etl_connector_batches SET OutputJson = '{}', OutputDigest = {{new string('a', 64)}}, OutputBytes = 0
            WHERE BatchId = {{batchId}};
            """);
        var negativeBatchBytesError = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET OutputBytes = -1 WHERE BatchId = {batchId}"));
        Assert.Equal(19, negativeBatchBytesError.SqliteErrorCode);

        var bytesWithoutPlanError = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE elsa_etl_connector_batches SET OutputJson = NULL, OutputDigest = NULL, OutputBytes = 1
            WHERE BatchId = {{batchId}};
            """));
        Assert.Equal(19, bytesWithoutPlanError.SqliteErrorCode);

        var runOutputBytesError = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_runs SET RetainedOutputBytes = -1"));
        Assert.Equal(19, runOutputBytesError.SqliteErrorCode);
        var partitionOutputBytesError = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_partitions SET RetainedOutputBytes = -1"));
        Assert.Equal(19, partitionOutputBytesError.SqliteErrorCode);
    }

    [Fact]
    public async Task Connector_output_migration_model_matches_the_runtime_snapshot()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static async Task<IMigrator> SeedHttpHistoryThroughBatchReservationsAsync(EtlRuntimeSqliteDbContext context)
    {
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261021100000_ConnectorHttpPages");
        await SeedHttpHistoryAsync(context);
        await migrator.MigrateAsync("20261021120000_ConnectorBatchReservations");
        return migrator;
    }
}
