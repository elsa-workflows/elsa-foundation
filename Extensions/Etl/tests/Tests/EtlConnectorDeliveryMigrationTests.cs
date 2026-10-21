using Elsa.Etl.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Connector_http_delivery_migration_preserves_retained_batch_output_and_foreign_keys()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var database = new TestDatabase(directory);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();

        var migrator = await SeedConnectorBatchThroughOutputsAsync(context);
        await migrator.MigrateAsync("20261021140000_ConnectorHttpDeliveryAttempts");

        Assert.Equal(1, await ForeignKeysEnabledAsync(context));
        await AssertNoForeignKeyViolationsAsync(context);
        var indexes = await context.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index'")
            .ToListAsync();
        Assert.Contains("IX_elsa_etl_connector_partitions_ScopeKey_RunKey_Completed_NotBeforeUtcTicks_ExpiresAtUtcTicks", indexes);

        var batch = await context.ConnectorBatches.AsNoTracking().SingleAsync();
        Assert.Equal("{\"rows\":[]}", batch.OutputJson);
        Assert.Equal(new string('c', 64), batch.OutputDigest);
        Assert.Equal(11L, batch.OutputBytes);
        Assert.Null(batch.HttpDeliveryJson);
        Assert.Null(batch.HttpDeliveryDigest);

        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync();
        Assert.Equal(new string('e', 64), partition.OwedBatchId);
        Assert.Equal(17L, partition.BatchMetadataBytes);
        Assert.Null(partition.NotBeforeUtcTicks);
    }

    [Fact]
    public async Task Connector_http_delivery_migration_enforces_delivery_pair_output_dependency_and_retry_time()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var database = new TestDatabase(directory);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();

        var migrator = await SeedConnectorBatchThroughOutputsAsync(context);
        await migrator.MigrateAsync("20261021140000_ConnectorHttpDeliveryAttempts");

        const string batchId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        var missingDigestError = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET HttpDeliveryJson = '{{}}' WHERE BatchId = {batchId}"));
        Assert.Equal(19, missingDigestError.SqliteErrorCode);

        var missingJsonError = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET HttpDeliveryDigest = {new string('d', 64)} WHERE BatchId = {batchId}"));
        Assert.Equal(19, missingJsonError.SqliteErrorCode);

        var shortDigestError = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET HttpDeliveryJson = '{{}}', HttpDeliveryDigest = 'short' WHERE BatchId = {batchId}"));
        Assert.Equal(19, shortDigestError.SqliteErrorCode);

        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET HttpDeliveryJson = '{{}}', HttpDeliveryDigest = {new string('d', 64)} WHERE BatchId = {batchId}");
        var outputDependencyError = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET OutputJson = NULL, OutputDigest = NULL, OutputBytes = 0 WHERE BatchId = {batchId}"));
        Assert.Equal(19, outputDependencyError.SqliteErrorCode);

        var missingDeliveryJsonError = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET HttpDeliveryJson = NULL WHERE BatchId = {batchId}"));
        Assert.Equal(19, missingDeliveryJsonError.SqliteErrorCode);

        var nonPositiveRetryTimeError = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_partitions SET NotBeforeUtcTicks = 0"));
        Assert.Equal(19, nonPositiveRetryTimeError.SqliteErrorCode);
        var negativeRetryTimeError = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_partitions SET NotBeforeUtcTicks = -1"));
        Assert.Equal(19, negativeRetryTimeError.SqliteErrorCode);

        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_partitions SET NotBeforeUtcTicks = 639000000000000000");
        Assert.Equal(639000000000000000L, await context.ConnectorPartitions.Select(row => row.NotBeforeUtcTicks ?? 0L).SingleAsync());
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_partitions SET NotBeforeUtcTicks = NULL");
        Assert.Null(await context.ConnectorPartitions.Select(row => row.NotBeforeUtcTicks).SingleAsync());
        Assert.Equal("{}", await context.ConnectorBatches.Select(row => row.HttpDeliveryJson).SingleAsync());
    }

    [Fact]
    public async Task Connector_http_delivery_migration_model_matches_the_runtime_snapshot()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static async Task<IMigrator> SeedConnectorBatchThroughOutputsAsync(EtlRuntimeSqliteDbContext context)
    {
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261021100000_ConnectorHttpPages");
        await SeedHttpHistoryAsync(context);
        await migrator.MigrateAsync("20261021120000_ConnectorBatchReservations");

        const string batchId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO elsa_etl_connector_batches
                (ScopeKey, RunKey, PartitionOrdinal, BatchId, StartKey, DescriptorJson, DescriptorDigest,
                 RecordCount, InputBytes, MetadataBytes, Settled, SchemaVersion)
            VALUES ({{new string('a', 64)}}, {{"migration-run"}}, 0, {{batchId}}, {{new string('f', 64)}}, '{}',
                    {{new string('b', 64)}}, 1, 12, 17, 0, {{EtlRuntimeEfModule.SchemaVersion}});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE elsa_etl_connector_partitions
            SET OwedBatchId = {{batchId}}, BatchMetadataBytes = 17
            WHERE ScopeKey = {{new string('a', 64)}} AND RunKey = {{"migration-run"}} AND PartitionOrdinal = 0;
            """);

        await migrator.MigrateAsync("20261021130000_ConnectorBatchOutputs");
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE elsa_etl_connector_batches
            SET OutputJson = '{"rows":[]}', OutputDigest = {{new string('c', 64)}}, OutputBytes = 11
            WHERE BatchId = {{batchId}};
            """);

        return migrator;
    }
}
