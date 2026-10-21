using System.Text;
using Elsa.Etl.Core;
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
    public async Task Connector_source_fetch_migration_preserves_legacy_history_and_claims_its_continuation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var database = new TestDatabase(directory);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261021150000_ConnectorBatchAcceptance");

        var clock = new MutableTimeProvider();
        var fixture = await SeedLegacyHttpContinuationAsync(context, clock.GetUtcNow());

        await migrator.MigrateAsync("20261021160000_ConnectorSourceFetch");
        context.ChangeTracker.Clear();

        var requests = await context.ConnectorHttpRequests.AsNoTracking().OrderBy(row => row.PageSequence).ToArrayAsync();
        Assert.Equal(2, requests.Length);
        Assert.All(requests, request =>
        {
            Assert.Null(request.FetchJson);
            Assert.Null(request.FetchDigest);
            Assert.Equal(0, request.FetchMetadataBytes);
            Assert.Equal(0, request.SourceMetadataReserveBytes);
            Assert.Equal(0, request.InputReservationBytes);
        });
        var retainedPage = await context.ConnectorHttpPages.AsNoTracking().SingleAsync();
        Assert.Equal(fixture.PageJson, retainedPage.PageJson);
        Assert.Equal(1, retainedPage.AcceptedItemCount);
        Assert.Equal(17, retainedPage.ByteLength);
        // Inspect only fields available at this historical migration target.
        var batch = await context.ConnectorBatches.AsNoTracking()
            .Select(row => new { row.Settled, row.BatchId, row.OutputJson }).SingleAsync();
        Assert.True(batch.Settled);
        Assert.Equal(fixture.BatchId, batch.BatchId);
        Assert.Equal(fixture.OutputJson, batch.OutputJson);
        Assert.Equal(0, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).SourceInputReservedBytes);
        Assert.Equal(0, (await context.ConnectorPartitions.AsNoTracking().SingleAsync()).SourceInputReservedBytes);

        // Current runtime continuation uses the full current schema after verifying the historical step.
        await migrator.MigrateAsync();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var lease = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
            fixture.Admission.Invocation.Scope, fixture.Admission.Invocation.RunId, "migration-recovery"));
        var position = Assert.IsType<EtlConnectorHttpPosition>(lease.Position);
        Assert.Equal(1, position.PageSequence);
        Assert.Equal("shard-a", position.Request.ShardId);
        Assert.Equal("cursor-1", position.Request.Continuation);
        Assert.Null(position.Page);
    }

    [Fact]
    public async Task Connector_source_fetch_migration_rejects_inconsistent_pairs_and_negative_reservations()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var clock = new MutableTimeProvider();
        var fixture = await SeedLegacyHttpContinuationAsync(context, clock.GetUtcNow());
        var request = await context.ConnectorHttpRequests.AsNoTracking().FirstAsync();
        var emptyJson = "{}";
        var digest = new string('a', 64);

        var missingDigest = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE elsa_etl_connector_http_requests SET FetchJson = {emptyJson}
            WHERE ScopeKey = {request.ScopeKey} AND RunKey = {request.RunKey} AND PartitionOrdinal = {request.PartitionOrdinal} AND PageSequence = {request.PageSequence};
            """));
        Assert.Equal(19, missingDigest.SqliteErrorCode);
        var missingJson = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE elsa_etl_connector_http_requests SET FetchDigest = {digest}
            WHERE ScopeKey = {request.ScopeKey} AND RunKey = {request.RunKey} AND PartitionOrdinal = {request.PartitionOrdinal} AND PageSequence = {request.PageSequence};
            """));
        Assert.Equal(19, missingJson.SqliteErrorCode);

        foreach (var column in new[] { "FetchMetadataBytes", "SourceMetadataReserveBytes", "InputReservationBytes" })
        {
            var statement = "UPDATE elsa_etl_connector_http_requests SET " + column + " = -1 " +
                "WHERE ScopeKey = {0} AND RunKey = {1} AND PartitionOrdinal = {2} AND PageSequence = {3};";
            var error = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(statement,
                request.ScopeKey, request.RunKey, request.PartitionOrdinal, request.PageSequence));
            Assert.Equal(19, error.SqliteErrorCode);
        }

        var negativeRunReserve = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE elsa_etl_connector_runs SET SourceInputReservedBytes = -1 WHERE ScopeKey = {request.ScopeKey} AND RunKey = {request.RunKey};
            """));
        Assert.Equal(19, negativeRunReserve.SqliteErrorCode);
        var negativePartitionReserve = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE elsa_etl_connector_partitions SET SourceInputReservedBytes = -1
            WHERE ScopeKey = {request.ScopeKey} AND RunKey = {request.RunKey} AND PartitionOrdinal = {request.PartitionOrdinal};
            """));
        Assert.Equal(19, negativePartitionReserve.SqliteErrorCode);
        var unchanged = await context.ConnectorHttpRequests.AsNoTracking().FirstAsync();
        Assert.Equal(fixture.Admission.Invocation.RunId, EtlRuntimeRecordCodec.DecodeIdentity(unchanged.RunKey, "run id"));
        Assert.Null(unchanged.FetchJson);
    }

    private static async Task<LegacyHttpMigrationFixture> SeedLegacyHttpContinuationAsync(
        EtlRuntimeSqliteDbContext context, DateTimeOffset now)
    {
        var admission = ConnectorAdmission("legacy-source-run", new("tenant-a", "legacy-source-migration"), httpShardIds: ["shard-a"]);
        var pipeline = EtlConnectorRecordCodec.Pipeline(admission.Invocation);
        var prepared = new EtlConnectorPreparedRun(null, null);
        var preparedJson = EtlConnectorRecordCodec.ValidatePrepared(pipeline, admission.Bindings, prepared);
        var bindingJson = EtlConnectorRecordCodec.ValidateBindings(pipeline, admission.Bindings, 128);
        var scope = EtlRuntimeRecordCodec.Scope(admission.Invocation.Scope);
        var run = EtlRuntimeRecordCodec.RunRecord(admission.Invocation,
            EtlRuntimeIdentity.InvocationFingerprint(admission.Invocation), null, now.UtcTicks);

        var initialRequest = new EtlHttpRequest("shard-a", null);
        var nextRequest = new EtlHttpRequest("shard-a", "cursor-1");
        var initialRequestHash = EtlRuntimeIdentity.HttpRequestHash(initialRequest);
        var nextRequestHash = EtlRuntimeIdentity.HttpRequestHash(nextRequest);
        var body = new EtlObjectReference("legacy-page-content", EtlRuntimeFixtures.Hash, 17);
        var page = new EtlRetainedHttpPage(0, initialRequestHash, body, 1, "cursor-1");
        var position = new EtlConnectorHttpPosition(1, 0, nextRequest, null, false);
        var range = new EtlConnectorHttpBatchRange(0, initialRequestHash, body.ContentHash, 0, 1);
        var descriptor = new EtlConnectorBatchDescriptor(
            EtlRuntimeIdentity.ConnectorBatchId(admission, prepared, 0, range, body.ByteLength, body.ContentHash),
            range, body.ByteLength, body.ContentHash);
        var output = new EtlConnectorBatchOutput(0, 0, 1, null, null);

        var positionJson = EtlConnectorRecordCodec.Write<EtlConnectorPosition>(position);
        var initialRequestJson = EtlConnectorRecordCodec.Write(initialRequest);
        var nextRequestJson = EtlConnectorRecordCodec.Write(nextRequest);
        var pageJson = EtlConnectorRecordCodec.Write(page);
        var descriptorJson = EtlConnectorRecordCodec.Write(descriptor);
        var outputJson = EtlConnectorRecordCodec.Write(output);
        var initialRequestBytes = Encoding.UTF8.GetByteCount(initialRequestJson);
        var nextRequestBytes = Encoding.UTF8.GetByteCount(nextRequestJson);
        var pageBytes = Encoding.UTF8.GetByteCount(pageJson);
        var positionBytes = Encoding.UTF8.GetByteCount(positionJson);
        var batchMetadataBytes = Encoding.UTF8.GetByteCount(descriptorJson) + Encoding.UTF8.GetByteCount(outputJson);
        var partitionMetadataBytes = checked(positionBytes + initialRequestBytes + nextRequestBytes + pageBytes + batchMetadataBytes);
        var startRangeJson = EtlConnectorRecordCodec.Write<EtlConnectorBatchRange>(range with { EndItemExclusive = 0 });
        var startKey = EtlRuntimeRecordCodec.HashText(startRangeJson);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO elsa_etl_runtime_runs
                (ScopeKey, RunKey, TenantId, PersistencePartition, RunId, InvocationFingerprint, InvocationJson, BindingsJson,
                 Status, PreparationFence, OutstandingRecords, OutstandingBytes, AcceptedRecords, RejectedRecords, FilteredRecords,
                 Revision, CreatedAtUtcTicks, UpdatedAtUtcTicks, SchemaVersion)
            VALUES ({scope.Key}, {run.RunKey}, {scope.TenantId}, {scope.PersistencePartition}, {run.RunId},
                    {run.InvocationFingerprint}, {run.InvocationJson}, NULL, {(int)EtlRunStatus.Running}, 0, 0, 0, 0, 0, 1,
                    1, {now.UtcTicks}, {now.UtcTicks}, {EtlRuntimeEfModule.SchemaVersion});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO elsa_etl_connector_runs
                (ScopeKey, RunKey, BindingsJson, PreparedJson, RetainedInputBytes, RetainedMetadataBytes,
                 RetainedOutputBytes, Revision, SchemaVersion)
            VALUES ({scope.Key}, {run.RunKey}, {bindingJson}, {preparedJson}, {body.ByteLength}, {partitionMetadataBytes}, 0,
                    1, {EtlRuntimeEfModule.SchemaVersion});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO elsa_etl_connector_partitions
                (ScopeKey, RunKey, PartitionOrdinal, PositionJson, Completed, Owner, Fence, ExpiresAtUtcTicks, PageCount,
                 PageBytes, MetadataBytes, CurrentRequestSequence, CurrentRequestHash, Revision, SchemaVersion, OwedBatchId,
                 BatchMetadataBytes, RetainedOutputBytes, NotBeforeUtcTicks, AcceptedBatchCount, AcceptedRecords,
                 RejectedRecords, FilteredRecords)
            VALUES ({scope.Key}, {run.RunKey}, 0, {positionJson}, 0, NULL, 1, NULL, 1, {body.ByteLength},
                    {partitionMetadataBytes}, NULL, NULL, 1, {EtlRuntimeEfModule.SchemaVersion}, NULL,
                    {batchMetadataBytes}, 0, NULL, 1, 0, 0, 1);
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO elsa_etl_connector_http_requests
                (ScopeKey, RunKey, PartitionOrdinal, PageSequence, RequestHash, RequestJson, MetadataBytes,
                 PreviousPageSequence, PreviousHasContinuation, SchemaVersion)
            VALUES ({scope.Key}, {run.RunKey}, 0, 0, {initialRequestHash}, {initialRequestJson}, {initialRequestBytes},
                    NULL, NULL, {EtlRuntimeEfModule.SchemaVersion});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO elsa_etl_connector_http_pages
                (ScopeKey, RunKey, PartitionOrdinal, PageSequence, RequestHash, NextRequestHash, HasContinuation,
                 PageJson, ByteLength, PageDigest, MetadataBytes, SchemaVersion, AcceptedItemCount)
            VALUES ({scope.Key}, {run.RunKey}, 0, 0, {initialRequestHash}, {nextRequestHash}, 1, {pageJson},
                    {body.ByteLength}, {EtlRuntimeRecordCodec.HashText(pageJson)}, {pageBytes}, {EtlRuntimeEfModule.SchemaVersion}, 1);
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO elsa_etl_connector_http_requests
                (ScopeKey, RunKey, PartitionOrdinal, PageSequence, RequestHash, RequestJson, MetadataBytes,
                 PreviousPageSequence, PreviousHasContinuation, SchemaVersion)
            VALUES ({scope.Key}, {run.RunKey}, 0, 1, {nextRequestHash}, {nextRequestJson}, {nextRequestBytes},
                    0, 1, {EtlRuntimeEfModule.SchemaVersion});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO elsa_etl_connector_batches
                (ScopeKey, RunKey, PartitionOrdinal, BatchId, StartKey, DescriptorJson, DescriptorDigest, RecordCount,
                 InputBytes, MetadataBytes, Settled, SchemaVersion, OutputJson, OutputDigest, OutputBytes, BatchOrdinal,
                 HttpPageSequence, MainFilePublished, ErrorFilePublished, AcceptanceMetadataBytes)
            VALUES ({scope.Key}, {run.RunKey}, 0, {descriptor.BatchId}, {startKey}, {descriptorJson},
                    {EtlRuntimeRecordCodec.HashText(descriptorJson)}, 1, {body.ByteLength}, {batchMetadataBytes}, 1,
                    {EtlRuntimeEfModule.SchemaVersion}, {outputJson}, {EtlRuntimeRecordCodec.HashText(outputJson)}, 0,
                    0, 0, 0, 0, 0);
            """);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE elsa_etl_connector_partitions SET CurrentRequestSequence = 1, CurrentRequestHash = {nextRequestHash}
            WHERE ScopeKey = {scope.Key} AND RunKey = {run.RunKey} AND PartitionOrdinal = 0;
            """);

        return new(admission, pageJson, outputJson, descriptor.BatchId);
    }

    private sealed record LegacyHttpMigrationFixture(EtlConnectorRunAdmission Admission, string PageJson,
        string OutputJson, string BatchId);
}
