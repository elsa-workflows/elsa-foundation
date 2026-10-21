using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    private const int AcceptanceBudgetContinuationLength = 4096;

    [Fact]
    public async Task Http_acceptance_uses_reserved_metadata_when_dispatch_fills_the_run_budget()
    {
        const string runId = "acceptance-reserve-full-budget";
        var requestId = DeliveryRequestId(91);
        var metadataLimit = await MeasureAcceptanceBudgetAfterHttpBeginAsync(runId, requestId);

        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var admission = AcceptanceBudgetAdmission(runId, metadataLimit);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, new MutableTimeProvider());
        var fixture = await PrepareAcceptanceBudgetOutputAsync(store, admission, "worker");
        var reserved = await context.ConnectorBatches.AsNoTracking().SingleAsync();

        Assert.True(reserved.AcceptanceMetadataBytes > 0);
        var begin = await store.BeginConnectorHttpDeliveryAsync(fixture.Lease, fixture.Batch.BatchId,
            EtlOutputRoute.Main, requestId);
        Assert.Equal(EtlStoreStatus.Applied, begin.Status);
        var permit = Assert.IsType<EtlHttpDeliveryAttempt>(begin.Permit);
        var metadataAtDispatch = await RetainedMetadataBytesAsync(context);
        Assert.Equal(metadataLimit, metadataAtDispatch);
        var completed = await store.CompleteConnectorHttpDeliveryAsync(fixture.Lease, fixture.Batch.BatchId,
            EtlOutputRoute.Main, permit, new(204, null));
        Assert.Equal(EtlStoreStatus.Applied, completed.Status);
        Assert.Equal(EtlHttpDeliveryPhase.Acknowledged, completed.Delivery!.Phase);

        var beforeAcceptance = await RetainedMetadataBytesAsync(context);
        var oldPositionJson = await context.ConnectorPartitions.AsNoTracking()
            .Where(row => row.PartitionOrdinal == fixture.Lease.PartitionOrdinal)
            .Select(row => row.PositionJson).SingleAsync();
        var accepted = await store.AcceptConnectorBatchAsync(fixture.Lease, fixture.Batch.BatchId);
        var acceptedPosition = Assert.IsType<EtlConnectorHttpPosition>(accepted.Lease!.Position);
        var settledBatch = await context.ConnectorBatches.AsNoTracking().SingleAsync();
        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync();
        var connector = await context.ConnectorRuns.AsNoTracking().SingleAsync();
        var page = await context.ConnectorHttpPages.AsNoTracking().SingleAsync();
        var nextRequest = await context.ConnectorHttpRequests.AsNoTracking().SingleAsync(row => row.PageSequence == 1);
        var expectedRequest = new EtlHttpRequest("shard-a", AcceptanceBudgetContinuation());

        Assert.Equal(EtlStoreStatus.Applied, accepted.Status);
        Assert.Equal(expectedRequest, acceptedPosition.Request);
        Assert.Null(acceptedPosition.Page);
        Assert.Equal(1, acceptedPosition.PageSequence);
        Assert.Equal(0, acceptedPosition.NextItem);
        Assert.Equal(expectedRequest, EtlConnectorRecordCodec.Read<EtlHttpRequest>(nextRequest.RequestJson));
        Assert.Equal(EtlRuntimeIdentity.HttpRequestHash(expectedRequest), nextRequest.RequestHash);
        Assert.Equal(nextRequest.RequestHash, page.NextRequestHash);
        Assert.Equal(2, page.AcceptedItemCount);
        Assert.True(settledBatch.Settled);
        Assert.Equal(0, settledBatch.AcceptanceMetadataBytes);
        Assert.Null(partition.OwedBatchId);
        var nextPositionJson = EtlConnectorRecordCodec.Write<EtlConnectorPosition>(acceptedPosition);
        Assert.Equal(beforeAcceptance + Encoding.UTF8.GetByteCount(nextPositionJson) - Encoding.UTF8.GetByteCount(oldPositionJson) +
            nextRequest.MetadataBytes - reserved.AcceptanceMetadataBytes, connector.RetainedMetadataBytes);
        Assert.True(connector.RetainedMetadataBytes <= metadataLimit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_batch_reservation_respects_independent_acceptance_metadata_budget(bool exactFit)
    {
        const string runId = "acceptance-reserve-insufficient";
        var measured = await MeasureAcceptanceBatchReservationAsync(runId);
        Assert.True(measured.AcceptanceReserve > 0);
        var metadataLimit = checked(measured.BeforeReservation + measured.DescriptorBytes + measured.AcceptanceReserve - (exactFit ? 0 : 1));
        Assert.True(measured.BeforeReservation + measured.DescriptorBytes <= metadataLimit);

        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var admission = AcceptanceBudgetAdmission(runId, metadataLimit);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, new MutableTimeProvider());
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var (lease, prepared) = await AttachAcceptanceBudgetPageAsync(store, admission, initialLease, "only-page");
        var batch = AcceptanceBudgetBatch(admission, prepared, lease, "only-batch");
        var before = await ReadAcceptanceStateAsync(context, lease.PartitionOrdinal);
        var metadataBefore = await RetainedMetadataBytesAsync(context);

        var result = await store.ReserveConnectorBatchAsync(lease, batch);
        var after = await ReadAcceptanceStateAsync(context, lease.PartitionOrdinal);

        if (exactFit)
        {
            Assert.Equal(EtlStoreStatus.Applied, result.Status);
            Assert.NotNull(result.Reservation);
            var retained = await context.ConnectorBatches.AsNoTracking().SingleAsync();
            Assert.Equal(measured.AcceptanceReserve, retained.AcceptanceMetadataBytes);
            Assert.Equal(measured.DescriptorBytes + measured.AcceptanceReserve, retained.MetadataBytes);
            Assert.Equal(metadataLimit, await RetainedMetadataBytesAsync(context));
            return;
        }
        Assert.Equal(EtlStoreStatus.BudgetExceeded, result.Status);
        Assert.Null(result.Reservation);
        Assert.Equal(before, after);
        Assert.Equal(metadataBefore, await RetainedMetadataBytesAsync(context));
        Assert.Empty(await context.ConnectorBatches.AsNoTracking().ToListAsync());
        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync();
        var run = await context.Runs.AsNoTracking().SingleAsync();
        Assert.Null(partition.OwedBatchId);
        Assert.Equal(0, partition.BatchMetadataBytes);
        Assert.Equal(0, run.OutstandingRecords);
        Assert.Equal(0, run.OutstandingBytes);
    }

    [Fact]
    public async Task Legacy_zero_acceptance_reserve_is_topped_up_on_output_replay()
    {
        const string runId = "acceptance-reserve-legacy-replay";
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var admission = AcceptanceBudgetAdmission(runId);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, new MutableTimeProvider());
        var fixture = await PrepareAcceptanceBudgetOutputAsync(store, admission, "worker");
        var before = await ReadAcceptanceBudgetRowsAsync(context, fixture.Lease.PartitionOrdinal);
        Assert.True(before.AcceptanceMetadataBytes > 0);

        _ = await RemoveLegacyAcceptanceReserveAsync(context, fixture.Lease.PartitionOrdinal);
        var legacy = await ReadAcceptanceBudgetRowsAsync(context, fixture.Lease.PartitionOrdinal);
        Assert.Equal(0, legacy.AcceptanceMetadataBytes);
        Assert.Equal(before.BatchMetadataBytes - before.AcceptanceMetadataBytes, legacy.BatchMetadataBytes);
        Assert.Equal(before.PartitionBatchMetadataBytes - before.AcceptanceMetadataBytes,
            legacy.PartitionBatchMetadataBytes);
        Assert.Equal(before.PartitionMetadataBytes - before.AcceptanceMetadataBytes, legacy.PartitionMetadataBytes);
        Assert.Equal(before.ConnectorMetadataBytes - before.AcceptanceMetadataBytes, legacy.ConnectorMetadataBytes);

        var replay = await store.RetainConnectorBatchOutputAsync(fixture.Lease, fixture.Batch.BatchId, fixture.Output);
        var toppedUp = await ReadAcceptanceBudgetRowsAsync(context, fixture.Lease.PartitionOrdinal);

        Assert.Equal(EtlStoreStatus.Replay, replay.Status);
        Assert.Equal(fixture.Output, replay.Output);
        Assert.Equal(before.AcceptanceMetadataBytes, toppedUp.AcceptanceMetadataBytes);
        Assert.Equal(before.BatchMetadataBytes, toppedUp.BatchMetadataBytes);
        Assert.Equal(before.PartitionBatchMetadataBytes, toppedUp.PartitionBatchMetadataBytes);
        Assert.Equal(before.PartitionMetadataBytes, toppedUp.PartitionMetadataBytes);
        Assert.Equal(before.ConnectorMetadataBytes, toppedUp.ConnectorMetadataBytes);
        Assert.Equal(before.PositionJson, toppedUp.PositionJson);
        Assert.Equal(before.OwedBatchId, toppedUp.OwedBatchId);
        Assert.Equal(before.OutputJson, toppedUp.OutputJson);
    }

    [Fact]
    public async Task Legacy_output_replay_refuses_acceptance_reserve_top_up_without_headroom_atomically()
    {
        const string runId = "acceptance-reserve-legacy-replay-full";
        var metadataLimit = await MeasureLegacyAcceptanceReplayLimitAsync(runId);

        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var admission = AcceptanceBudgetAdmission(runId, metadataLimit, ["shard-a", "shard-b"]);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, new MutableTimeProvider());
        var leases = await StartHttpRunAsync(store, admission, "worker");
        var first = await PrepareAcceptanceBudgetOutputAsync(store, admission, leases[0], "first");
        await RemoveLegacyAcceptanceReserveAsync(context, first.Lease.PartitionOrdinal);
        _ = await PrepareAcceptanceBudgetOutputAsync(store, admission, leases[1], "second");

        Assert.Equal(metadataLimit, await RetainedMetadataBytesAsync(context));
        var before = await ReadAcceptanceStateAsync(context, first.Lease.PartitionOrdinal);
        var beforeRows = await ReadAcceptanceBudgetRowsAsync(context, first.Lease.PartitionOrdinal);

        var replay = await store.RetainConnectorBatchOutputAsync(first.Lease, first.Batch.BatchId, first.Output);
        var after = await ReadAcceptanceStateAsync(context, first.Lease.PartitionOrdinal);
        var afterRows = await ReadAcceptanceBudgetRowsAsync(context, first.Lease.PartitionOrdinal);

        Assert.Equal(EtlStoreStatus.BudgetExceeded, replay.Status);
        Assert.Null(replay.Output);
        Assert.Equal(before, after);
        Assert.Equal(beforeRows, afterRows);
        Assert.Equal(metadataLimit, afterRows.ConnectorMetadataBytes);
    }

    [Fact]
    public async Task Legacy_http_begin_refuses_when_acceptance_reserve_and_attempt_metadata_do_not_fit()
    {
        const string runId = "acceptance-reserve-legacy-http-begin";
        var requestId = DeliveryRequestId(92);
        var measured = await MeasureAcceptanceHttpBeginDeltaAsync(runId, requestId);
        Assert.True(measured.BeginDelta > 0);
        var metadataLimit = checked(measured.BeforeBegin + measured.BeginDelta - 1);

        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var admission = AcceptanceBudgetAdmission(runId, metadataLimit);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, new MutableTimeProvider());
        var fixture = await PrepareAcceptanceBudgetOutputAsync(store, admission, "worker");
        Assert.Equal(measured.BeforeBegin, await RetainedMetadataBytesAsync(context));
        var removedReserve = await RemoveLegacyAcceptanceReserveAsync(context, fixture.Lease.PartitionOrdinal);
        var before = await ReadAcceptanceStateAsync(context, fixture.Lease.PartitionOrdinal);
        var beforeRows = await ReadAcceptanceBudgetRowsAsync(context, fixture.Lease.PartitionOrdinal);

        var result = await store.BeginConnectorHttpDeliveryAsync(fixture.Lease, fixture.Batch.BatchId,
            EtlOutputRoute.Main, requestId);
        var after = await ReadAcceptanceStateAsync(context, fixture.Lease.PartitionOrdinal);
        var afterRows = await ReadAcceptanceBudgetRowsAsync(context, fixture.Lease.PartitionOrdinal);

        Assert.Equal(EtlStoreStatus.BudgetExceeded, result.Status);
        Assert.Null(result.Permit);
        Assert.Equal(before, after);
        Assert.Equal(beforeRows, afterRows);
        Assert.Equal(0, afterRows.AcceptanceMetadataBytes);
        Assert.Null(afterRows.HttpDeliveryJson);
        Assert.Equal(measured.BeforeBegin - removedReserve, afterRows.ConnectorMetadataBytes);
    }

    private static EtlConnectorRunAdmission AcceptanceBudgetAdmission(string runId, long? metadataLimit = null,
        IReadOnlyList<string>? shardIds = null)
    {
        var pipeline = EtlV2TestPipelines.Create();
        if (metadataLimit is { } limit)
            pipeline = pipeline with { Http = pipeline.Http with { MaxRetainedMetadataBytes = limit } };
        return ConnectorAdmission(runId, new("tenant-a", runId), pipeline, shardIds ?? ["shard-a"]);
    }

    private static string AcceptanceBudgetContinuation() => new('c', AcceptanceBudgetContinuationLength);

    private static async Task<(EtlConnectorPartitionLease Lease, EtlConnectorPreparedRun Prepared)>
        AttachAcceptanceBudgetPageAsync(IEtlConnectorRuntimeStore store, EtlConnectorRunAdmission admission,
            EtlConnectorPartitionLease initialLease, string suffix)
    {
        var attached = await store.AttachHttpPageAsync(initialLease,
            RetainedPage(initialLease, $"acceptance-budget-{suffix}-page", byteLength: 23, recordCount: 2,
                nextContinuation: AcceptanceBudgetContinuation()));
        var lease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(
            admission.Invocation.Scope, admission.Invocation.RunId));
        return (lease, run.Prepared!);
    }

    private static EtlConnectorBatchDescriptor AcceptanceBudgetBatch(EtlConnectorRunAdmission admission,
        EtlConnectorPreparedRun prepared, EtlConnectorPartitionLease lease, string marker) =>
        HttpAcceptanceBatch(admission, prepared, lease, 0, 2, $"acceptance-budget-{marker}");

    private static async Task<AcceptanceReserveBudgetFixture> PrepareAcceptanceBudgetOutputAsync(
        IEtlConnectorRuntimeStore store, EtlConnectorRunAdmission admission, string workerId) =>
        await PrepareAcceptanceBudgetOutputAsync(store, admission,
            Assert.Single(await StartHttpRunAsync(store, admission, workerId)), "only");

    private static async Task<AcceptanceReserveBudgetFixture> PrepareAcceptanceBudgetOutputAsync(
        IEtlConnectorRuntimeStore store, EtlConnectorRunAdmission admission,
        EtlConnectorPartitionLease initialLease, string suffix)
    {
        var (lease, prepared) = await AttachAcceptanceBudgetPageAsync(store, admission, initialLease, suffix);
        var batch = AcceptanceBudgetBatch(admission, prepared, lease, suffix);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = HttpOutput(admission, batch, accepted: 2, rejected: 0, filtered: 0,
            suffix: $"acceptance-budget-{suffix}");
        Assert.Equal(EtlStoreStatus.Applied, (await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output)).Status);
        return new(lease, batch, output);
    }

    private static async Task<long> MeasureAcceptanceBudgetAfterHttpBeginAsync(string runId, string requestId)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var admission = AcceptanceBudgetAdmission(runId);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, new MutableTimeProvider());
        var fixture = await PrepareAcceptanceBudgetOutputAsync(store, admission, "worker");
        var begin = await store.BeginConnectorHttpDeliveryAsync(fixture.Lease, fixture.Batch.BatchId,
            EtlOutputRoute.Main, requestId);
        Assert.Equal(EtlStoreStatus.Applied, begin.Status);
        return await RetainedMetadataBytesAsync(context);
    }

    private static async Task<(long BeforeReservation, long DescriptorBytes, long AcceptanceReserve)> MeasureAcceptanceBatchReservationAsync(
        string runId)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var admission = AcceptanceBudgetAdmission(runId);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, new MutableTimeProvider());
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var (lease, prepared) = await AttachAcceptanceBudgetPageAsync(store, admission, initialLease, "only-page");
        var before = await RetainedMetadataBytesAsync(context);
        var batch = AcceptanceBudgetBatch(admission, prepared, lease, "only-batch");
        // Derive the required bytes from the expected next durable state, without reserving a batch.
        // Measuring the reservation itself would let an omitted reserve shrink the test's quota too.
        var nextRequest = new EtlHttpRequest("shard-a", AcceptanceBudgetContinuation());
        var nextPosition = new EtlConnectorHttpPosition(1, 0, nextRequest, null, false);
        var currentPositionJson = await context.ConnectorPartitions.AsNoTracking().Select(row => row.PositionJson).SingleAsync();
        var acceptanceReserve = Encoding.UTF8.GetByteCount(EtlConnectorRecordCodec.Write<EtlConnectorPosition>(nextPosition)) -
            Encoding.UTF8.GetByteCount(currentPositionJson) + Encoding.UTF8.GetByteCount(EtlConnectorRecordCodec.Write(nextRequest));
        return (before, Encoding.UTF8.GetByteCount(EtlConnectorRecordCodec.Write(batch)), acceptanceReserve);
    }

    private static async Task<long> MeasureLegacyAcceptanceReplayLimitAsync(string runId)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var admission = AcceptanceBudgetAdmission(runId, shardIds: ["shard-a", "shard-b"]);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, new MutableTimeProvider());
        var leases = await StartHttpRunAsync(store, admission, "worker");
        var first = await PrepareAcceptanceBudgetOutputAsync(store, admission, leases[0], "first");
        await RemoveLegacyAcceptanceReserveAsync(context, first.Lease.PartitionOrdinal);
        _ = await PrepareAcceptanceBudgetOutputAsync(store, admission, leases[1], "second");
        return await RetainedMetadataBytesAsync(context);
    }

    private static async Task<(long BeforeBegin, long BeginDelta)> MeasureAcceptanceHttpBeginDeltaAsync(
        string runId, string requestId)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var admission = AcceptanceBudgetAdmission(runId);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, new MutableTimeProvider());
        var fixture = await PrepareAcceptanceBudgetOutputAsync(store, admission, "worker");
        var before = await RetainedMetadataBytesAsync(context);
        var begin = await store.BeginConnectorHttpDeliveryAsync(fixture.Lease, fixture.Batch.BatchId,
            EtlOutputRoute.Main, requestId);
        Assert.Equal(EtlStoreStatus.Applied, begin.Status);
        return (before, await RetainedMetadataBytesAsync(context) - before);
    }

    private static async Task<long> RemoveLegacyAcceptanceReserveAsync(EtlRuntimeDbContext context, int partitionOrdinal)
    {
        context.ChangeTracker.Clear();
        var batch = await context.ConnectorBatches.SingleAsync(row => row.PartitionOrdinal == partitionOrdinal);
        var partition = await context.ConnectorPartitions.SingleAsync(row => row.PartitionOrdinal == partitionOrdinal);
        var connector = await context.ConnectorRuns.SingleAsync();
        var reserve = batch.AcceptanceMetadataBytes;
        Assert.True(reserve > 0);
        batch.AcceptanceMetadataBytes = 0;
        batch.MetadataBytes = checked(batch.MetadataBytes - reserve);
        partition.BatchMetadataBytes = checked(partition.BatchMetadataBytes - reserve);
        partition.MetadataBytes = checked(partition.MetadataBytes - reserve);
        connector.RetainedMetadataBytes = checked(connector.RetainedMetadataBytes - reserve);
        await context.SaveChangesAsync();
        return reserve;
    }

    private static async Task<AcceptanceReserveBudgetRows> ReadAcceptanceBudgetRowsAsync(
        EtlRuntimeDbContext context, int partitionOrdinal)
    {
        context.ChangeTracker.Clear();
        var batch = await context.ConnectorBatches.AsNoTracking().SingleAsync(row => row.PartitionOrdinal == partitionOrdinal);
        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync(row => row.PartitionOrdinal == partitionOrdinal);
        var connector = await context.ConnectorRuns.AsNoTracking().SingleAsync();
        return new(batch.AcceptanceMetadataBytes, batch.MetadataBytes, batch.OutputJson, batch.HttpDeliveryJson,
            partition.PositionJson, partition.OwedBatchId, partition.BatchMetadataBytes, partition.MetadataBytes,
            connector.RetainedMetadataBytes);
    }

    private sealed record AcceptanceReserveBudgetFixture(EtlConnectorPartitionLease Lease,
        EtlConnectorBatchDescriptor Batch, EtlConnectorBatchOutput Output);

    private sealed record AcceptanceReserveBudgetRows(long AcceptanceMetadataBytes, long BatchMetadataBytes,
        string? OutputJson, string? HttpDeliveryJson, string PositionJson, string? OwedBatchId,
        long PartitionBatchMetadataBytes, long PartitionMetadataBytes, long ConnectorMetadataBytes);
}
