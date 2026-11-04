using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Tests;

public abstract partial class EtlRuntimeStoreContract
{
    private protected const int HttpEffectMechanism = 1;
    private protected const int FileEffectMechanism = 2;

    [SkippableFact]
    public async Task Connector_inspection_is_scoped_and_pages_partitions_in_ascending_bounded_order()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var clock = new MutableTimeProvider();
        var options = new EtlRuntimeStoreOptions(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30), MaximumPartitionsPerRun: 128, MaximumPageSize: 2);
        var store = new EfEtlRuntimeStore(context, clock, options);
        var admission = ConnectorAdmission("inspection-pages", new("tenant-a", "inspection-pages"),
            httpShardIds: ["shard-a", "shard-b", "shard-c"]);
        var leases = await StartHttpRunAsync(store, admission, "worker");
        var sourceAttempt = await store.BeginConnectorHttpSourceAsync(leases[0], DeliveryRequestId(70));
        Assert.Equal(EtlStoreStatus.Applied, sourceAttempt.Status);
        Assert.NotNull(sourceAttempt.Permit);
        var sourceRetry = await store.CompleteConnectorHttpSourceAsync(leases[0], sourceAttempt.Permit!,
            new(503, null, InvalidPage: false));
        Assert.Equal(EtlStoreStatus.Applied, sourceRetry.Status);
        var sourceFetch = Assert.IsType<EtlConnectorHttpSourceFetch>(sourceRetry.Fetch);
        clock.Advance(TimeSpan.FromSeconds(31));

        IEtlConnectorRuntimeInspector inspector = store;
        var run = await inspector.FindConnectorInspectionAsync(admission.Invocation.Scope, admission.Invocation.RunId);
        var first = await inspector.ListConnectorPartitionsAsync(admission.Invocation.Scope, admission.Invocation.RunId, -1, 2);
        var second = await inspector.ListConnectorPartitionsAsync(admission.Invocation.Scope, admission.Invocation.RunId,
            first[^1].PartitionOrdinal, 2);

        Assert.Equal(2, inspector.MaximumPageSize);
        Assert.NotNull(run);
        Assert.Equal(0, run.AcceptedMainEffects);
        Assert.Equal(0, run.AcceptedErrorEffects);
        Assert.Equal(new[] { 0, 1 }, first.Select(partition => partition.PartitionOrdinal));
        Assert.Equal(new[] { 2 }, second.Select(partition => partition.PartitionOrdinal));
        Assert.All(first.Concat(second), partition =>
        {
            Assert.Null(partition.File);
            var http = Assert.IsType<EtlConnectorHttpProgress>(partition.Http);
            Assert.Equal(0, http.PageSequence);
            Assert.Equal(0, http.NextItem);
            Assert.Null(http.PageRecordCount);
            Assert.False(http.Exhausted);
        });
        var firstHttp = Assert.IsType<EtlConnectorHttpProgress>(first[0].Http);
        var fetchRetry = Assert.IsType<EtlConnectorRetryInspection>(firstHttp.Fetch);
        Assert.True(first[0].IsClaimed);
        Assert.Equal<long?>(leases[0].Fence, first[0].ClaimFence);
        Assert.Equal<DateTimeOffset?>(leases[0].ExpiresAt, first[0].ClaimExpiresAt);
        Assert.True(first[0].ClaimExpiresAt!.Value < clock.GetUtcNow());
        Assert.Equal("waiting", fetchRetry.Phase);
        Assert.Equal(1, fetchRetry.AttemptCount);
        Assert.Equal(503, fetchRetry.LastStatusCode);
        Assert.Equal(sourceFetch.Deadline, fetchRetry.Deadline);
        Assert.NotNull(fetchRetry.NotBefore);
        Assert.NotNull(first[0].NotBefore);
        Assert.True(first[1].IsClaimed);
        Assert.True(first[1].ClaimExpiresAt!.Value < clock.GetUtcNow());
        Assert.Null(first[1].Http!.Fetch);
        Assert.True(second[0].IsClaimed);
        Assert.True(second[0].ClaimExpiresAt!.Value < clock.GetUtcNow());
        Assert.Null(second[0].Http!.Fetch);
        Assert.Null(await inspector.FindConnectorInspectionAsync(new("tenant-b", admission.Invocation.Scope.PersistencePartition),
            admission.Invocation.RunId));
        Assert.Empty(await inspector.ListConnectorPartitionsAsync(new("tenant-b", admission.Invocation.Scope.PersistencePartition),
            admission.Invocation.RunId, -1, 2));
        Assert.Empty(await inspector.ListConnectorPartitionsAsync(admission.Invocation.Scope, "missing-inspection-run", -1, 2));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => inspector.ListConnectorPartitionsAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, -1, 3).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => inspector.ListConnectorPartitionsAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, -2, 1).AsTask());
    }

    [SkippableFact]
    public async Task Connector_inspection_tracks_retry_publication_acknowledgement_and_settled_effect_counts_once()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var clock = new MutableTimeProvider();
        var runtime = database.CreateStore(context, clock);
        var store = (IEtlConnectorRuntimeStore)runtime;
        var inspector = (IEtlConnectorRuntimeInspector)runtime;
        var pipeline = CsvMixedRoutePipeline();
        pipeline = pipeline with { Limits = pipeline.Limits with { FilePartitionCount = 1 } };
        var admission = ConnectorAdmission("inspection-effects", new("tenant-a", "inspection-effects"), pipeline);
        var lease = Assert.Single(await StartOutputCsvRunAsync(store, admission, recordCount: 2));
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = OutputTestBatch(admission, run.Prepared!, lease);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = CreateOutput(admission, batch, accepted: 1, rejected: 1, filtered: 0, suffix: "inspection-effects");
        var mainOutput = Assert.IsType<EtlConnectorRouteOutput>(output.Main);
        var errorOutput = Assert.IsType<EtlConnectorRouteOutput>(output.Errors);
        Assert.Equal(EtlConnectorKind.Http, mainOutput.Kind);
        Assert.Equal(EtlConnectorKind.Jsonl, errorOutput.Kind);
        Assert.Equal(EtlStoreStatus.Applied, (await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output)).Status);

        var first = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(71));
        var firstPermit = Assert.IsType<EtlHttpDeliveryAttempt>(first.Permit);
        var delayed = await store.CompleteConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main,
            firstPermit, new(503, null));
        Assert.Equal(EtlStoreStatus.Applied, delayed.Status);
        var delayedDelivery = Assert.IsType<EtlConnectorHttpDelivery>(delayed.Delivery);
        var retrySnapshot = Assert.Single(await inspector.ListConnectorPartitionsAsync(admission.Invocation.Scope,
            admission.Invocation.RunId, -1, 1));
        var owedBeforeDispatch = Assert.IsType<EtlConnectorOwedInspection>(retrySnapshot.OwedBatch);
        var mainBeforeDispatch = Assert.IsType<EtlConnectorEffectInspection>(owedBeforeDispatch.Main);
        var errorsBeforeDispatch = Assert.IsType<EtlConnectorEffectInspection>(owedBeforeDispatch.Errors);
        var retryBeforeDispatch = Assert.IsType<EtlConnectorRetryInspection>(mainBeforeDispatch.Retry);
        Assert.True(owedBeforeDispatch.OutputRetained);
        Assert.Equal("retained", mainBeforeDispatch.State);
        Assert.Equal("waiting", retryBeforeDispatch.Phase);
        Assert.Equal(1, retryBeforeDispatch.AttemptCount);
        Assert.Equal(503, retryBeforeDispatch.LastStatusCode);
        Assert.Equal(delayedDelivery.Deadline, retryBeforeDispatch.Deadline);
        Assert.Equal(delayedDelivery.NotBefore, retryBeforeDispatch.NotBefore);
        Assert.Equal("retained", errorsBeforeDispatch.State);
        Assert.Null(errorsBeforeDispatch.Retry);
        Assert.Equal(0, retrySnapshot.AcceptedMainEffects);
        Assert.Equal(0, retrySnapshot.AcceptedErrorEffects);

        clock.Advance(delayedDelivery.NotBefore!.Value - clock.GetUtcNow());
        var retry = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(72));
        var retryPermit = Assert.IsType<EtlHttpDeliveryAttempt>(retry.Permit);
        Assert.Equal(EtlStoreStatus.Applied, (await store.CompleteConnectorHttpDeliveryAsync(lease, batch.BatchId,
            EtlOutputRoute.Main, retryPermit, new(204, null))).Status);
        var publication = errorOutput.Body with { Key = EtlRuntimeIdentity.ConnectorFileOutputKey(errorOutput.EffectId) };
        Assert.Equal(EtlStoreStatus.Applied, await store.RecordConnectorFilePublicationAsync(
            lease, batch.BatchId, EtlOutputRoute.Error, publication));

        var ready = Assert.Single(await inspector.ListConnectorPartitionsAsync(admission.Invocation.Scope,
            admission.Invocation.RunId, -1, 1));
        var readyOwed = Assert.IsType<EtlConnectorOwedInspection>(ready.OwedBatch);
        var readyMain = Assert.IsType<EtlConnectorEffectInspection>(readyOwed.Main);
        var readyErrors = Assert.IsType<EtlConnectorEffectInspection>(readyOwed.Errors);
        var readyMainRetry = Assert.IsType<EtlConnectorRetryInspection>(readyMain.Retry);
        Assert.Equal("acknowledged", readyMain.State);
        Assert.Equal(2, readyMainRetry.AttemptCount);
        Assert.Equal(204, readyMainRetry.LastStatusCode);
        Assert.Equal("published", readyErrors.State);
        Assert.Null(readyErrors.Retry);
        Assert.Equal(0, ready.AcceptedMainEffects);
        Assert.Equal(0, ready.AcceptedErrorEffects);

        var accepted = await store.AcceptConnectorBatchAsync(lease, batch.BatchId);
        var after = await inspector.FindConnectorInspectionAsync(admission.Invocation.Scope, admission.Invocation.RunId);
        Assert.Equal(EtlStoreStatus.Applied, accepted.Status);
        Assert.Null(accepted.Lease!.OwedBatch);
        Assert.Equal(EtlRunStatus.BusinessError, after!.Run.Run.Status);
        Assert.Equal(1, after.AcceptedMainEffects);
        Assert.Equal(1, after.AcceptedErrorEffects);
        var settled = Assert.Single(await inspector.ListConnectorPartitionsAsync(admission.Invocation.Scope,
            admission.Invocation.RunId, -1, 1));
        Assert.Null(settled.OwedBatch);
        Assert.Equal(1, settled.AcceptedMainEffects);
        Assert.Equal(1, settled.AcceptedErrorEffects);

        Assert.Equal(EtlStoreStatus.Terminal, (await store.AcceptConnectorBatchAsync(lease, batch.BatchId)).Status);
        var replay = await inspector.FindConnectorInspectionAsync(admission.Invocation.Scope, admission.Invocation.RunId);
        Assert.Equal(1, replay!.AcceptedMainEffects);
        Assert.Equal(1, replay.AcceptedErrorEffects);
        Assert.Single(await context.Terminals.AsNoTracking().ToListAsync());
        Assert.Single(await context.Outbox.AsNoTracking().ToListAsync());
    }

    [SkippableFact]
    public async Task Connector_inspection_reads_the_existing_request_for_an_exhausted_final_http_page()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var inspector = (IEtlConnectorRuntimeInspector)database.CreateStore(context);
        var admission = ConnectorAdmission("inspection-exhausted-http", new("tenant-a", "inspection-exhausted-http"),
            httpShardIds: ["shard-a"]);
        var initial = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var attached = await store.AttachTestHttpPageAsync(initial,
            RetainedPage(initial, "inspection-final-page", byteLength: 9, recordCount: 1));
        var lease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = HttpBatch(admission, run.Prepared!, lease, endItemExclusive: 1, inputBytes: 5,
            marker: "inspection-final-http");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = CreateOutput(admission, batch, accepted: 1, rejected: 0, filtered: 0, suffix: "inspection-final-http");
        Assert.Equal(EtlStoreStatus.Applied, (await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output)).Status);
        await AcknowledgeHttpRouteAsync(store, lease, batch.BatchId, EtlOutputRoute.Main, 73);
        Assert.Equal(EtlStoreStatus.Applied, (await store.AcceptConnectorBatchAsync(lease, batch.BatchId)).Status);

        var partition = Assert.Single(await inspector.ListConnectorPartitionsAsync(admission.Invocation.Scope,
            admission.Invocation.RunId, -1, 1));

        var progress = Assert.IsType<EtlConnectorHttpProgress>(partition.Http);
        Assert.Null(partition.File);
        Assert.True(progress.Exhausted);
        Assert.Equal(0, progress.PageSequence);
        Assert.Equal(0, progress.NextItem);
        Assert.Null(progress.PageRecordCount);
        Assert.Equal(1, partition.AcceptedMainEffects);
        Assert.Equal(0, partition.AcceptedErrorEffects);
    }

    [SkippableFact]
    public async Task Connector_inspection_does_not_count_retained_effects_when_cancellation_preserves_owed_work()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context);
        var store = (IEtlConnectorRuntimeStore)runtime;
        var inspector = (IEtlConnectorRuntimeInspector)runtime;
        var admission = ConnectorAdmission("inspection-cancelled", new("tenant-a", "inspection-cancelled"), httpShardIds: ["shard-a"]);
        var (lease, batch, _) = await PrepareHttpOutputBatchAsync(store, admission, "worker");
        var before = await DeliveryAccountingAsync(context, lease.PartitionOrdinal);

        Assert.Equal(EtlStoreStatus.Applied, (await runtime.CancelAsync(admission.Invocation)).Status);

        var snapshot = await inspector.FindConnectorInspectionAsync(admission.Invocation.Scope, admission.Invocation.RunId);
        var partition = Assert.Single(await inspector.ListConnectorPartitionsAsync(admission.Invocation.Scope,
            admission.Invocation.RunId, -1, 1));
        var after = await DeliveryAccountingAsync(context, lease.PartitionOrdinal);
        Assert.Equal(EtlRunStatus.Cancelled, snapshot!.Run.Run.Status);
        Assert.Equal(0, snapshot.AcceptedMainEffects);
        Assert.Equal(0, snapshot.AcceptedErrorEffects);
        var owed = Assert.IsType<EtlConnectorOwedInspection>(partition.OwedBatch);
        Assert.True(owed.OutputRetained);
        Assert.Equal("retained", owed.Main!.State);
        Assert.Equal("retained", owed.Errors!.State);
        Assert.Equal(0, partition.AcceptedMainEffects);
        Assert.Equal(0, partition.AcceptedErrorEffects);
        Assert.Equal(before.OutstandingRecords, after.OutstandingRecords);
        Assert.Equal(before.OutstandingBytes, after.OutstandingBytes);
        Assert.Equal(before.OutstandingRecords, snapshot.Run.Run.OutstandingRecords);
        Assert.Single(await context.Terminals.AsNoTracking().ToListAsync());
        Assert.Single(await context.Outbox.AsNoTracking().ToListAsync());
    }

}
