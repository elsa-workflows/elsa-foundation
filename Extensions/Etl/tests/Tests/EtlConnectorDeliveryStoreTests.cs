using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    private const long HttpInFlightReceiptReservationBytes = 512;
    private const long HttpWaitingReceiptReservationBytes = 256;

    [Fact]
    public async Task Http_delivery_begin_persists_a_permit_before_transport_and_replays_a_lost_reply()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("http-delivery-permit", new("tenant-a", "http-delivery-permit"), httpShardIds: ["shard"]);
        var (lease, batch, output) = await PrepareHttpOutputBatchAsync(store, admission, "worker");
        var metadataBefore = await RetainedMetadataBytesAsync(context);

        var begun = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(1));
        var retained = await context.ConnectorBatches.AsNoTracking().SingleAsync();
        var main = Assert.IsType<EtlConnectorHttpDelivery>(Assert.IsType<EtlConnectorHttpDeliveries>(
            EtlConnectorRecordCodec.Read<EtlConnectorHttpDeliveries>(retained.HttpDeliveryJson!)).Main);
        var replay = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(1));
        var afterReplay = await context.ConnectorBatches.AsNoTracking().SingleAsync();

        Assert.Equal(EtlStoreStatus.Applied, begun.Status);
        var permit = Assert.IsType<EtlHttpDeliveryAttempt>(begun.Permit);
        Assert.Equal(1, permit.Number);
        Assert.Equal(lease.Fence, permit.Fence);
        Assert.Equal(output.Main!.EffectId, permit.EffectId);
        Assert.Equal(EtlHttpDeliveryPhase.InFlight, main.Phase);
        Assert.Equal(permit, Assert.Single(main.Attempts).Attempt);
        var metadataAfterBegin = await RetainedMetadataBytesAsync(context);
        Assert.True(metadataAfterBegin - metadataBefore >= Encoding.UTF8.GetByteCount(retained.HttpDeliveryJson!) + HttpInFlightReceiptReservationBytes);
        Assert.Equal(retained.HttpDeliveryJson, afterReplay.HttpDeliveryJson);
        Assert.Equal(retained.HttpDeliveryDigest, afterReplay.HttpDeliveryDigest);
        Assert.Equal(EtlStoreStatus.Replay, replay.Status);
        Assert.Equal(permit, replay.Permit);
        Assert.Single(main.Attempts);
        Assert.Equal(output, EtlConnectorRecordCodec.Read<EtlConnectorBatchOutput>(retained.OutputJson!));
    }

    [Fact]
    public async Task Expired_http_attempt_is_recorded_as_unknown_before_retrying_with_the_same_effect()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock, partitionLease: TimeSpan.FromSeconds(30));
        var admission = ConnectorAdmission("http-delivery-unknown", new("tenant-a", "http-delivery-unknown"), httpShardIds: ["shard"]);
        var (lease, batch, output) = await PrepareHttpOutputBatchAsync(store, admission, "worker");
        var first = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(1));
        var firstPermit = Assert.IsType<EtlHttpDeliveryAttempt>(first.Permit);
        Assert.Equal(EtlStoreStatus.Applied, first.Status);

        clock.Advance(TimeSpan.FromSeconds(31));
        var replacement = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "replacement"));
        var unknown = await store.BeginConnectorHttpDeliveryAsync(replacement, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(2));
        var waiting = Assert.IsType<EtlConnectorHttpDelivery>(unknown.Delivery);

        Assert.Equal(EtlStoreStatus.Applied, unknown.Status);
        Assert.Null(unknown.Permit);
        Assert.Equal(EtlHttpDeliveryPhase.Waiting, waiting.Phase);
        var unknownAttempt = Assert.Single(waiting.Attempts);
        Assert.Null(unknownAttempt.Response);
        Assert.NotNull(unknownAttempt.CompletedAt);
        Assert.Equal(firstPermit, unknownAttempt.Attempt);
        Assert.True(waiting.NotBefore!.Value > clock.GetUtcNow());

        clock.Advance(waiting.NotBefore!.Value - clock.GetUtcNow());
        var second = await store.BeginConnectorHttpDeliveryAsync(replacement, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(3));

        Assert.Equal(EtlStoreStatus.Applied, second.Status);
        Assert.NotNull(second.Permit);
        Assert.Equal(2, second.Permit!.Number);
        Assert.Equal(output.Main!.EffectId, second.Permit.EffectId);
        Assert.Equal(firstPermit.Fence, Assert.Single(waiting.Attempts).Attempt.Fence);
        Assert.True(second.Permit.Fence > firstPermit.Fence);
    }

    [Fact]
    public async Task Http_delivery_begin_rolls_back_if_lease_expires_after_save()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var expire = new ConnectorExpireAfterSave(clock);
        await using var context = database.CreateContext(expire);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var admission = ConnectorAdmission("http-delivery-expiry", new("tenant-a", "http-delivery-expiry"), httpShardIds: ["shard"]);
        var (lease, batch, _) = await PrepareHttpOutputBatchAsync(store, admission, "worker");
        var before = await DeliveryAccountingAsync(context, lease.PartitionOrdinal);

        expire.Armed = true;
        var result = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(4));
        context.ChangeTracker.Clear();
        var after = await DeliveryAccountingAsync(context, lease.PartitionOrdinal);
        var row = await context.ConnectorBatches.AsNoTracking().SingleAsync();

        Assert.Equal(EtlStoreStatus.LeaseLost, result.Status);
        Assert.Null(result.Permit);
        Assert.Null(row.HttpDeliveryJson);
        Assert.Null(row.HttpDeliveryDigest);
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("cancelled")]
    public async Task Http_delivery_begin_refuses_stale_or_cancelled_ownership_without_retaining_an_attempt(string state)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context);
        var store = (IEtlConnectorRuntimeStore)runtime;
        var admission = ConnectorAdmission($"http-delivery-{state}", new("tenant-a", $"http-delivery-{state}"), httpShardIds: ["shard"]);
        var (lease, batch, _) = await PrepareHttpOutputBatchAsync(store, admission, "worker");
        var before = await DeliveryAccountingAsync(context, lease.PartitionOrdinal);
        EtlStoreStatus expected;

        if (state == "stale")
        {
            Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));
            var replacement = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
                lease.Scope, lease.RunId, "successor"));
            Assert.True(replacement.Fence > lease.Fence);
            expected = EtlStoreStatus.LeaseLost;
        }
        else
        {
            Assert.Equal(EtlStoreStatus.Applied, (await runtime.CancelAsync(admission.Invocation)).Status);
            expected = EtlStoreStatus.Terminal;
        }

        var result = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(5));
        var after = await DeliveryAccountingAsync(context, lease.PartitionOrdinal);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Permit);
        Assert.Null(result.Delivery);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Http_delivery_recovers_partial_route_ack_and_honors_retry_after_while_other_partition_progresses()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var admission = ConnectorAdmission("http-delivery-partial-recovery", new("tenant-a", "http-delivery-partial-recovery"), httpShardIds: ["shard-a", "shard-b"]);
        var initialLeases = await StartHttpRunAsync(store, admission, "worker");
        var firstLease = initialLeases.Single(item => item.PartitionOrdinal == 0);
        var secondLease = initialLeases.Single(item => item.PartitionOrdinal == 1);
        var (lease, batch, output) = await PrepareHttpOutputBatchAsync(store, admission, firstLease, "first-output");

        var mainPermit = Assert.IsType<EtlHttpDeliveryAttempt>((await store.BeginConnectorHttpDeliveryAsync(
            lease, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(10))).Permit);
        var mainComplete = await store.CompleteConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main,
            mainPermit, new(204, null));
        var errorPermit = Assert.IsType<EtlHttpDeliveryAttempt>((await store.BeginConnectorHttpDeliveryAsync(
            lease, batch.BatchId, EtlOutputRoute.Error, DeliveryRequestId(11))).Permit);
        var retryAt = clock.GetUtcNow().AddSeconds(10);
        var errorComplete = await store.CompleteConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Error,
            errorPermit, new(429, retryAt));
        var earlyRetry = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Error, DeliveryRequestId(12));

        Assert.Equal(EtlStoreStatus.Applied, mainComplete.Status);
        Assert.Equal(EtlHttpDeliveryPhase.Acknowledged, mainComplete.Delivery!.Phase);
        Assert.Equal(EtlStoreStatus.Applied, errorComplete.Status);
        Assert.Equal(EtlHttpDeliveryPhase.Waiting, errorComplete.Delivery!.Phase);
        Assert.Equal(retryAt, errorComplete.Delivery.NotBefore);
        Assert.Equal(EtlStoreStatus.Conflict, earlyRetry.Status);
        Assert.Null(earlyRetry.Permit);

        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(secondLease));
        var other = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "other-partition-worker"));
        Assert.Equal(1, other.PartitionOrdinal);
        var emptyPage = await store.AttachTestHttpPageAsync(other, RetainedPage(other, "other-empty-page", 5, 0));
        var exhausted = await store.AdvanceEmptyHttpPageAsync(Assert.IsType<EtlConnectorPartitionLease>(emptyPage.Lease));
        Assert.True(Assert.IsType<EtlConnectorHttpPosition>(exhausted.Lease!.Position).Exhausted);
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(exhausted.Lease));
        Assert.Null(await store.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "too-early"));

        clock.Advance(retryAt - clock.GetUtcNow());
        var recovered = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "delivery-recovery"));
        Assert.Equal(0, recovered.PartitionOrdinal);
        var recoveredBatch = Assert.IsType<EtlConnectorBatchReservation>(recovered.OwedBatch);
        Assert.Equal(batch, recoveredBatch.Batch);
        Assert.Equal(output, recoveredBatch.Output);
        Assert.Equal(EtlHttpDeliveryPhase.Acknowledged, recoveredBatch.HttpDeliveries!.Main!.Phase);
        Assert.Equal(EtlHttpDeliveryPhase.Waiting, recoveredBatch.HttpDeliveries.Errors!.Phase);
        var mainReplay = await store.BeginConnectorHttpDeliveryAsync(recovered, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(14));

        var retry = await store.BeginConnectorHttpDeliveryAsync(recovered, batch.BatchId, EtlOutputRoute.Error, DeliveryRequestId(13));
        var retryPermit = Assert.IsType<EtlHttpDeliveryAttempt>(retry.Permit);
        var retryComplete = await store.CompleteConnectorHttpDeliveryAsync(recovered, batch.BatchId, EtlOutputRoute.Error,
            retryPermit, new(201, null));
        var viewAfterRetry = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(
            admission.Invocation.Scope, admission.Invocation.RunId));

        Assert.Equal(EtlStoreStatus.Applied, retry.Status);
        Assert.Equal(2, retryPermit.Number);
        Assert.Equal(output.Errors!.EffectId, retryPermit.EffectId);
        Assert.Equal(EtlStoreStatus.Replay, mainReplay.Status);
        Assert.Null(mainReplay.Permit);
        Assert.Equal(EtlStoreStatus.Applied, retryComplete.Status);
        Assert.Equal(EtlHttpDeliveryPhase.Acknowledged, retryComplete.Delivery!.Phase);
        Assert.Equal(2, viewAfterRetry.Run.OutstandingRecords);
        Assert.Equal(5, viewAfterRetry.Run.OutstandingBytes);
        Assert.Equal(0, Assert.IsType<EtlConnectorHttpPosition>(recovered.Position).NextItem);
        Assert.False(await context.ConnectorBatches.AsNoTracking().Select(row => row.Settled).SingleAsync());
    }

    [Fact]
    public async Task Http_delivery_rejects_a_receipt_for_another_effect_without_changing_retained_work()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("http-delivery-wrong-receipt", new("tenant-a", "http-delivery-wrong-receipt"), httpShardIds: ["shard"]);
        var (lease, batch, output) = await PrepareHttpOutputBatchAsync(store, admission, "worker");
        var permit = Assert.IsType<EtlHttpDeliveryAttempt>((await store.BeginConnectorHttpDeliveryAsync(
            lease, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(19))).Permit);
        var before = await DeliveryAccountingAsync(context, lease.PartitionOrdinal);

        var result = await store.CompleteConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main,
            permit with { EffectId = output.Errors!.EffectId }, new(200, null));

        Assert.Equal(EtlStoreStatus.Conflict, result.Status);
        Assert.Equal(before, await DeliveryAccountingAsync(context, lease.PartitionOrdinal));
        Assert.Equal(EtlStoreStatus.Applied, (await store.CompleteConnectorHttpDeliveryAsync(lease,
            batch.BatchId, EtlOutputRoute.Main, permit, new(200, null))).Status);
    }

    [Fact]
    public async Task Http_delivery_metadata_budget_refusal_is_atomic()
    {
        var baseline = await MeasureHttpDeliveryMetadataBaselineAsync();
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with { Http = defaults.Http with { MaxRetainedMetadataBytes = baseline } };
        var admission = ConnectorAdmission("http-delivery-metadata-limit", new("tenant-a", "http-delivery-metadata-limit"), pipeline, ["shard"]);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var (lease, batch, _) = await PrepareLegacyHttpOutputBatchAsync(context, store, admission, initialLease,
            admission.Invocation.RunId);
        var before = await DeliveryAccountingAsync(context, lease.PartitionOrdinal);
        Assert.Equal(baseline, before.RunMetadataBytes);

        var result = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(20));
        context.ChangeTracker.Clear();
        var after = await DeliveryAccountingAsync(context, lease.PartitionOrdinal);
        var retained = await context.ConnectorBatches.AsNoTracking().SingleAsync();

        Assert.Equal(EtlStoreStatus.BudgetExceeded, result.Status);
        Assert.Null(result.Permit);
        Assert.Null(retained.HttpDeliveryJson);
        Assert.Null(retained.HttpDeliveryDigest);
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_delivery_receipt_fits_reserved_metadata_at_the_run_budget(bool retryableResponse)
    {
        const string runId = "http-delivery-receipt-budget";
        var requestId = DeliveryRequestId(40);
        var measuredTotal = await MeasureHttpDeliveryBeginMetadataAsync(runId, requestId);
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        await using var context = database.CreateContext();
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with { Http = defaults.Http with { MaxRetainedMetadataBytes = measuredTotal } };
        var admission = ConnectorAdmission(runId, new("tenant-a", runId), pipeline, ["shard"]);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var (lease, batch, _) = await PrepareLegacyHttpOutputBatchAsync(context, store, admission, initialLease, runId);
        var before = await DeliveryAccountingAsync(context, lease.PartitionOrdinal);
        var originalPositionJson = await context.ConnectorPartitions.AsNoTracking()
            .Where(row => row.PartitionOrdinal == lease.PartitionOrdinal).Select(row => row.PositionJson).SingleAsync();
        var acceptedBefore = await context.Runs.AsNoTracking().Select(row => row.AcceptedRecords).SingleAsync();

        var begin = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main, requestId);
        var permit = Assert.IsType<EtlHttpDeliveryAttempt>(begin.Permit);
        var inFlight = await context.ConnectorBatches.AsNoTracking().SingleAsync();
        var afterBegin = await DeliveryAccountingAsync(context, lease.PartitionOrdinal);
        var inFlightJsonBytes = Encoding.UTF8.GetByteCount(inFlight.HttpDeliveryJson!);

        Assert.Equal(EtlStoreStatus.Applied, begin.Status);
        Assert.Equal(measuredTotal, afterBegin.RunMetadataBytes);
        Assert.True(afterBegin.RunMetadataBytes - before.RunMetadataBytes >= inFlightJsonBytes + HttpInFlightReceiptReservationBytes);

        var response = retryableResponse
            ? new EtlHttpDeliveryResponse(429, clock.GetUtcNow().AddSeconds(10))
            : new EtlHttpDeliveryResponse(204, null);
        var complete = await store.CompleteConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main, permit, response);
        var finalBatch = await context.ConnectorBatches.AsNoTracking().SingleAsync();
        var afterComplete = await DeliveryAccountingAsync(context, lease.PartitionOrdinal);
        var finalJsonBytes = Encoding.UTF8.GetByteCount(finalBatch.HttpDeliveryJson!);
        var reservedForWaiting = retryableResponse ? HttpWaitingReceiptReservationBytes : 0;
        var currentPositionJson = await context.ConnectorPartitions.AsNoTracking()
            .Where(row => row.PartitionOrdinal == lease.PartitionOrdinal).Select(row => row.PositionJson).SingleAsync();
        var acceptedAfter = await context.Runs.AsNoTracking().Select(row => row.AcceptedRecords).SingleAsync();

        Assert.Equal(EtlStoreStatus.Applied, complete.Status);
        Assert.Equal(before.RunMetadataBytes + finalJsonBytes + reservedForWaiting, afterComplete.RunMetadataBytes);
        Assert.True(afterComplete.RunMetadataBytes <= measuredTotal);
        Assert.Equal(before.OutstandingRecords, afterComplete.OutstandingRecords);
        Assert.Equal(before.OutstandingBytes, afterComplete.OutstandingBytes);
        Assert.Equal(acceptedBefore, acceptedAfter);
        Assert.Equal(originalPositionJson, currentPositionJson);
        Assert.False(finalBatch.Settled);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("digest")]
    public async Task Corrupt_http_delivery_metadata_cannot_be_recovered(string defect)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
            var admission = ConnectorAdmission($"http-delivery-corrupt-{defect}", new("tenant-a", $"http-delivery-corrupt-{defect}"), httpShardIds: ["shard"]);
            var (lease, batch, _) = await PrepareHttpOutputBatchAsync(store, admission, "worker");
            Assert.Equal(EtlStoreStatus.Applied, (await store.BeginConnectorHttpDeliveryAsync(
                lease, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(30))).Status);
            Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));

            var retained = await context.ConnectorBatches.SingleAsync();
            if (defect == "json") retained.HttpDeliveryJson = "{";
            else
            {
                var digest = retained.HttpDeliveryDigest!;
                retained.HttpDeliveryDigest = (digest[0] == 'a' ? "b" : "a") + digest[1..];
            }
            await context.SaveChangesAsync();
        }

        await using var recoveryContext = database.CreateContext();
        var recoveryStore = (IEtlConnectorRuntimeStore)database.CreateStore(recoveryContext);
        await Assert.ThrowsAsync<InvalidDataException>(() => recoveryStore.ClaimConnectorPartitionAsync(
            new("tenant-a", $"http-delivery-corrupt-{defect}"), $"http-delivery-corrupt-{defect}", "recovery").AsTask());
        var partition = await recoveryContext.ConnectorPartitions.AsNoTracking().SingleAsync();
        Assert.Null(partition.Owner);
        Assert.Null(partition.ExpiresAtUtcTicks);
    }

    private static string DeliveryRequestId(int value) => value.ToString("x32");

    private static async Task<(EtlConnectorPartitionLease Lease, EtlConnectorBatchDescriptor Batch, EtlConnectorBatchOutput Output)>
        PrepareHttpOutputBatchAsync(IEtlConnectorRuntimeStore store, EtlConnectorRunAdmission admission, string workerId)
    {
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, workerId));
        return await PrepareHttpOutputBatchAsync(store, admission, initialLease, admission.Invocation.RunId);
    }

    private static async Task<(EtlConnectorPartitionLease Lease, EtlConnectorBatchDescriptor Batch, EtlConnectorBatchOutput Output)>
        PrepareHttpOutputBatchAsync(IEtlConnectorRuntimeStore store, EtlConnectorRunAdmission admission,
            EtlConnectorPartitionLease initialLease, string? suffix = null)
    {
        var attached = await store.AttachTestHttpPageAsync(initialLease,
            RetainedPage(initialLease, $"{admission.Invocation.RunId}-delivery-page", byteLength: 9, recordCount: 2));
        var lease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        return await PrepareHttpOutputBatchAsync(store, admission, lease, run.Prepared!, suffix);
    }

    private static async Task<(EtlConnectorPartitionLease Lease, EtlConnectorBatchDescriptor Batch, EtlConnectorBatchOutput Output)>
        PrepareLegacyHttpOutputBatchAsync(EtlRuntimeDbContext context, IEtlConnectorRuntimeStore store,
            EtlConnectorRunAdmission admission, EtlConnectorPartitionLease initialLease, string suffix)
    {
        var (lease, prepared) = await AttachLegacyAcceptanceBudgetPageAsync(context, store, admission, initialLease, suffix);
        return await PrepareHttpOutputBatchAsync(store, admission, lease, prepared, suffix);
    }

    private static async Task<(EtlConnectorPartitionLease Lease, EtlConnectorBatchDescriptor Batch, EtlConnectorBatchOutput Output)>
        PrepareHttpOutputBatchAsync(IEtlConnectorRuntimeStore store, EtlConnectorRunAdmission admission,
            EtlConnectorPartitionLease lease, EtlConnectorPreparedRun prepared, string? suffix)
    {
        var batch = HttpBatch(admission, prepared, lease, endItemExclusive: 2, inputBytes: 5,
            marker: $"{admission.Invocation.RunId}-delivery-input");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = CreateOutput(admission, batch, accepted: 1, rejected: 1, filtered: 0,
            suffix: suffix ?? admission.Invocation.RunId, errorKind: EtlConnectorKind.Http);
        Assert.Equal(EtlConnectorKind.Http, output.Main!.Kind);
        Assert.Equal(EtlConnectorKind.Http, output.Errors!.Kind);
        Assert.Equal(EtlStoreStatus.Applied, (await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output)).Status);
        return (lease, batch, output);
    }

    private static async Task<long> MeasureHttpDeliveryMetadataBaselineAsync()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("http-delivery-metadata-limit", new("tenant-a", "http-delivery-metadata-limit"), httpShardIds: ["shard"]);
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        _ = await PrepareLegacyHttpOutputBatchAsync(context, store, admission, initialLease, admission.Invocation.RunId);
        return await RetainedMetadataBytesAsync(context);
    }

    private static async Task<long> MeasureHttpDeliveryBeginMetadataAsync(string runId, string requestId)
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var admission = ConnectorAdmission(runId, new("tenant-a", runId), httpShardIds: ["shard"]);
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var (lease, batch, _) = await PrepareLegacyHttpOutputBatchAsync(context, store, admission, initialLease, runId);
        var begin = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main, requestId);
        Assert.Equal(EtlStoreStatus.Applied, begin.Status);
        return await RetainedMetadataBytesAsync(context);
    }

    private static async Task<(long RunMetadataBytes, long OutstandingRecords, long OutstandingBytes,
        long PartitionMetadataBytes, long BatchMetadataBytes, long RetainedOutputBytes,
        string? DeliveryJson, string? DeliveryDigest)> DeliveryAccountingAsync(
        EtlRuntimeDbContext context, int partitionOrdinal)
    {
        var run = await context.Runs.AsNoTracking().SingleAsync();
        var connector = await context.ConnectorRuns.AsNoTracking().SingleAsync();
        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync(row => row.PartitionOrdinal == partitionOrdinal);
        var batch = await context.ConnectorBatches.AsNoTracking().SingleAsync(row => row.PartitionOrdinal == partitionOrdinal);
        return (connector.RetainedMetadataBytes, run.OutstandingRecords, run.OutstandingBytes,
            partition.MetadataBytes, partition.BatchMetadataBytes, partition.RetainedOutputBytes,
            batch.HttpDeliveryJson, batch.HttpDeliveryDigest);
    }
}
