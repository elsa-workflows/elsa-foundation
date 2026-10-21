using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Connector_acceptance_migration_backfills_http_sequence_and_preserves_prior_state()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var database = new TestDatabase(directory);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();

        var migrator = await SeedConnectorBatchThroughOutputsAsync(context);
        await migrator.MigrateAsync("20261021140000_ConnectorHttpDeliveryAttempts");

        const string batchId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        const string outputJson = "{\"rows\":[]}";
        const string deliveryJson = "{\"main\":{\"attempts\":[{\"number\":1}]}}";
        var httpDescriptor = EtlConnectorRecordCodec.Write(new EtlConnectorBatchDescriptor(
            batchId,
            new EtlConnectorHttpBatchRange(0, new string('b', 64), new string('c', 64), 0, 1),
            12,
            new string('d', 64)));
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE elsa_etl_connector_batches
            SET DescriptorJson = {httpDescriptor}, HttpDeliveryJson = {deliveryJson}, HttpDeliveryDigest = {new string('a', 64)}
            WHERE BatchId = {batchId};
            """);

        var scopeKey = new string('a', 64);
        const string runKey = "migration-run";
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO elsa_etl_connector_partitions
                (ScopeKey, RunKey, PartitionOrdinal, PositionJson, Completed, Owner, Fence, ExpiresAtUtcTicks,
                 PageCount, PageBytes, MetadataBytes, CurrentRequestSequence, CurrentRequestHash, Revision, SchemaVersion,
                 OwedBatchId, BatchMetadataBytes, RetainedOutputBytes, NotBeforeUtcTicks)
            VALUES ({{scopeKey}}, {{runKey}}, 1, '{}', 0, NULL, 1, NULL, 0, 0, 0, NULL, NULL, 1,
                    {{EtlRuntimeEfModule.SchemaVersion}}, NULL, 0, 0, NULL);
            """);

        const string fileBatchId = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
        var fileDescriptor = EtlConnectorRecordCodec.Write(new EtlConnectorBatchDescriptor(
            fileBatchId,
            new EtlConnectorFileBatchRange(1, 2),
            12,
            new string('e', 64)));
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO elsa_etl_connector_batches
                (ScopeKey, RunKey, PartitionOrdinal, BatchId, StartKey, DescriptorJson, DescriptorDigest,
                 OutputBytes, RecordCount, InputBytes, MetadataBytes, Settled, SchemaVersion)
            VALUES ({scopeKey}, {runKey}, 1, {fileBatchId}, {new string('f', 64)}, {fileDescriptor}, {new string('b', 64)},
                    0, 1, 12, 0, 0, {EtlRuntimeEfModule.SchemaVersion});
            """);

        await migrator.MigrateAsync("20261021150000_ConnectorBatchAcceptance");

        Assert.Equal(1, await ForeignKeysEnabledAsync(context));
        await AssertNoForeignKeyViolationsAsync(context);
        Assert.Single(await context.Runs.AsNoTracking().ToListAsync());
        // Query only columns that exist at this historical target; the current model includes
        // source-fetch request columns added by the following migration.
        Assert.Single(await context.ConnectorHttpRequests.AsNoTracking()
            .Select(row => new { row.RequestHash, row.PageSequence }).ToListAsync());
        Assert.Single(await context.ConnectorHttpPages.AsNoTracking()
            .Select(row => new { row.RequestHash, row.AcceptedItemCount }).ToListAsync());

        var httpBatch = await context.ConnectorBatches.AsNoTracking().SingleAsync(row => row.BatchId == batchId);
        Assert.Equal(0L, httpBatch.BatchOrdinal);
        Assert.Equal((long?)0, httpBatch.HttpPageSequence);
        Assert.Equal(outputJson, httpBatch.OutputJson);
        Assert.Equal(deliveryJson, httpBatch.HttpDeliveryJson);
        Assert.Equal(new string('a', 64), httpBatch.HttpDeliveryDigest);
        Assert.False(httpBatch.MainFilePublished);
        Assert.False(httpBatch.ErrorFilePublished);
        Assert.Equal(0L, httpBatch.AcceptanceMetadataBytes);

        var fileBatch = await context.ConnectorBatches.AsNoTracking().SingleAsync(row => row.BatchId == fileBatchId);
        Assert.Equal(0L, fileBatch.BatchOrdinal);
        Assert.Null(fileBatch.HttpPageSequence);
        Assert.False(fileBatch.MainFilePublished);
        Assert.False(fileBatch.ErrorFilePublished);
        Assert.Equal(0L, fileBatch.AcceptanceMetadataBytes);

        var partitions = await context.ConnectorPartitions.AsNoTracking().OrderBy(row => row.PartitionOrdinal)
            .Select(row => new { row.AcceptedBatchCount, row.AcceptedRecords, row.RejectedRecords, row.FilteredRecords })
            .ToListAsync();
        Assert.All(partitions, partition =>
        {
            Assert.Equal(0L, partition.AcceptedBatchCount);
            Assert.Equal(0L, partition.AcceptedRecords);
            Assert.Equal(0L, partition.RejectedRecords);
            Assert.Equal(0L, partition.FilteredRecords);
        });
        Assert.Equal(0, (await context.ConnectorHttpPages.AsNoTracking()
            .Select(row => new { row.AcceptedItemCount }).SingleAsync()).AcceptedItemCount);
    }

    [Fact]
    public async Task Connector_acceptance_migration_refuses_unclassifiable_legacy_descriptors()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var database = new TestDatabase(directory);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();

        var migrator = await SeedConnectorBatchThroughOutputsAsync(context);
        await migrator.MigrateAsync("20261021140000_ConnectorHttpDeliveryAttempts");

        var error = await Assert.ThrowsAsync<SqliteException>(() => migrator.MigrateAsync("20261021150000_ConnectorBatchAcceptance"));

        Assert.Equal(19, error.SqliteErrorCode);
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS Value FROM pragma_table_info('elsa_etl_connector_batches') WHERE name = 'BatchOrdinal'
            """).SingleAsync());
        Assert.Equal("{\"rows\":[]}", await context.Database.SqlQueryRaw<string>("""
            SELECT OutputJson AS Value FROM elsa_etl_connector_batches WHERE BatchId = 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee'
            """).SingleAsync());
    }

    [Fact]
    public async Task Connector_acceptance_migration_enforces_nonnegative_counts_output_dependencies_and_indexes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var database = new TestDatabase(directory);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();

        var migrator = await SeedConnectorBatchThroughOutputsAsync(context);
        await migrator.MigrateAsync("20261021140000_ConnectorHttpDeliveryAttempts");

        const string batchId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        var descriptor = EtlConnectorRecordCodec.Write(new EtlConnectorBatchDescriptor(
            batchId,
            new EtlConnectorHttpBatchRange(0, new string('b', 64), new string('c', 64), 0, 1),
            12,
            new string('d', 64)));
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET DescriptorJson = {descriptor} WHERE BatchId = {batchId}");
        await migrator.MigrateAsync("20261021150000_ConnectorBatchAcceptance");

        const string fileBatchId = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
        var fileDescriptor = EtlConnectorRecordCodec.Write(new EtlConnectorBatchDescriptor(
            fileBatchId,
            new EtlConnectorFileBatchRange(1, 2),
            12,
            new string('e', 64)));
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO elsa_etl_connector_batches
                (ScopeKey, RunKey, PartitionOrdinal, BatchId, StartKey, DescriptorJson, DescriptorDigest,
                 OutputBytes, RecordCount, InputBytes, MetadataBytes, Settled, SchemaVersion, BatchOrdinal)
            VALUES ({new string('a', 64)}, {"migration-run"}, 0, {fileBatchId}, {new string('f', 63) + "e"},
                    {fileDescriptor}, {new string('b', 64)}, 0, 1, 12, 0, 0, {EtlRuntimeEfModule.SchemaVersion}, 1);
            """);

        foreach (var statement in new[]
        {
            "UPDATE elsa_etl_connector_partitions SET AcceptedBatchCount = -1",
            "UPDATE elsa_etl_connector_partitions SET AcceptedRecords = -1",
            "UPDATE elsa_etl_connector_partitions SET RejectedRecords = -1",
            "UPDATE elsa_etl_connector_partitions SET FilteredRecords = -1"
        })
        {
            var error = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(statement));
            Assert.Equal(19, error.SqliteErrorCode);
        }

        var negativeOrdinal = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET BatchOrdinal = -1 WHERE BatchId = {fileBatchId}"));
        Assert.Equal(19, negativeOrdinal.SqliteErrorCode);
        var negativeSequence = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET HttpPageSequence = -1 WHERE BatchId = {batchId}"));
        Assert.Equal(19, negativeSequence.SqliteErrorCode);
        var negativeAcceptanceMetadata = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET AcceptanceMetadataBytes = -1 WHERE BatchId = {batchId}"));
        Assert.Equal(19, negativeAcceptanceMetadata.SqliteErrorCode);
        var negativePageItems = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlRawAsync("UPDATE elsa_etl_connector_http_pages SET AcceptedItemCount = -1"));
        Assert.Equal(19, negativePageItems.SqliteErrorCode);

        var missingMainOutput = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET MainFilePublished = 1 WHERE BatchId = {fileBatchId}"));
        Assert.Equal(19, missingMainOutput.SqliteErrorCode);
        var missingErrorOutput = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET ErrorFilePublished = 1 WHERE BatchId = {fileBatchId}"));
        Assert.Equal(19, missingErrorOutput.SqliteErrorCode);

        var duplicateOrdinal = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO elsa_etl_connector_batches
                (ScopeKey, RunKey, PartitionOrdinal, BatchId, StartKey, DescriptorJson, DescriptorDigest,
                 OutputBytes, RecordCount, InputBytes, MetadataBytes, Settled, SchemaVersion)
            VALUES ({new string('a', 64)}, {"migration-run"}, 0, {new string('1', 64)}, {new string('1', 64)},
                    {fileDescriptor}, {new string('2', 64)}, 0, 1, 12, 0, 0, {EtlRuntimeEfModule.SchemaVersion});
            """));
        Assert.Equal(19, duplicateOrdinal.SqliteErrorCode);

        var indexes = await context.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index'").ToListAsync();
        Assert.Contains("IX_elsa_etl_connector_batches_ScopeKey_RunKey_PartitionOrdinal_BatchOrdinal", indexes);
        Assert.Contains("IX_elsa_etl_connector_batches_ScopeKey_RunKey_PartitionOrdinal_HttpPageSequence", indexes);
    }

    [Fact]
    public async Task Connector_acceptance_migration_model_matches_the_runtime_snapshot()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
