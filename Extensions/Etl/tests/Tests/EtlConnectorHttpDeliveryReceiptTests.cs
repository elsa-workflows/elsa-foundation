using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Timely_http_success_receipt_reconciles_after_deadline_and_survives_store_reopen()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with { Http = defaults.Http with { DeadlineSeconds = 10, RequestTimeoutSeconds = 10, MaxDelaySeconds = 10 } };
        var admission = ConnectorAdmission("http-late-success-receipt", new("tenant-a", "http-late-success-receipt"),
            pipeline, ["shard"]);
        EtlConnectorPartitionLease lease;
        EtlConnectorBatchDescriptor batch;
        EtlConnectorBatchOutput output;
        EtlHttpDeliveryAttempt permit;
        EtlHttpDeliveryResponse response;
        (long OutstandingRecords, long OutstandingBytes, long RetainedOutputBytes, string PositionJson, bool Settled) before;

        await using (var context = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock,
                partitionLease: TimeSpan.FromMinutes(2));
            (lease, batch, output) = await PrepareHttpOutputBatchAsync(store, admission, "receipt-worker");
            var begun = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main,
                DeliveryRequestId(81));
            Assert.Equal(EtlStoreStatus.Applied, begun.Status);
            permit = Assert.IsType<EtlHttpDeliveryAttempt>(begun.Permit);
            response = new EtlHttpDeliveryResponse(204, null)
            {
                ResponseReceivedAtUtc = permit.ExpiresAt.AddTicks(-1)
            };
            var accounting = await DeliveryAccountingAsync(context, lease.PartitionOrdinal);
            before = (accounting.OutstandingRecords, accounting.OutstandingBytes, accounting.RetainedOutputBytes,
                accounting.PositionJson, accounting.Settled);
        }

        var reconciledAt = permit.ExpiresAt.AddSeconds(1);
        clock.Advance(reconciledAt - clock.GetUtcNow());
        await using (var reopenedContext = database.CreateContext())
        {
            var reopenedStore = (IEtlConnectorRuntimeStore)database.CreateStore(reopenedContext, clock,
                partitionLease: TimeSpan.FromMinutes(2));
            var accepted = await reopenedStore.CompleteConnectorHttpDeliveryAsync(lease, batch.BatchId,
                EtlOutputRoute.Main, permit, response);

            Assert.Equal(EtlStoreStatus.Applied, accepted.Status);
            Assert.Equal(EtlHttpDeliveryPhase.Acknowledged, accepted.Delivery!.Phase);
            var attempt = Assert.Single(accepted.Delivery.Attempts);
            Assert.Equal(response, attempt.Response);
            Assert.Equal(reconciledAt, attempt.CompletedAt);
            Assert.True(attempt.CompletedAt!.Value > accepted.Delivery.Deadline);

            var after = await DeliveryAccountingAsync(reopenedContext, lease.PartitionOrdinal);
            Assert.Equal(before.OutstandingRecords, after.OutstandingRecords);
            Assert.Equal(before.OutstandingBytes, after.OutstandingBytes);
            Assert.Equal(before.RetainedOutputBytes, after.RetainedOutputBytes);
            Assert.Equal(before.PositionJson, after.PositionJson);
            Assert.False(after.Settled);
            var row = await reopenedContext.ConnectorBatches.AsNoTracking().SingleAsync();
            Assert.Equal(EtlConnectorRecordCodec.Write(output), row.OutputJson);
        }

        await using var verificationContext = database.CreateContext();
        var verificationStore = (IEtlConnectorRuntimeStore)database.CreateStore(verificationContext, clock,
            partitionLease: TimeSpan.FromMinutes(2));
        var persistedBatch = await verificationContext.ConnectorBatches.AsNoTracking().SingleAsync();
        var persistedDeliveries = EtlConnectorRecordCodec.Read<EtlConnectorHttpDeliveries>(persistedBatch.HttpDeliveryJson!);
        var persistedAttempt = Assert.Single(persistedDeliveries.Main!.Attempts);
        Assert.Equal(response, persistedAttempt.Response);
        Assert.Equal(reconciledAt, persistedAttempt.CompletedAt);

        var replay = await verificationStore.CompleteConnectorHttpDeliveryAsync(lease, batch.BatchId,
            EtlOutputRoute.Main, permit, response);
        var conflictingReceipt = response with
        {
            ResponseReceivedAtUtc = response.ResponseReceivedAtUtc!.Value.AddTicks(-1)
        };
        var conflict = await verificationStore.CompleteConnectorHttpDeliveryAsync(lease, batch.BatchId,
            EtlOutputRoute.Main, permit, conflictingReceipt);

        Assert.Equal(EtlStoreStatus.Replay, replay.Status);
        Assert.Equal(EtlStoreStatus.Conflict, conflict.Status);
    }

    [Fact]
    public async Task Cancellation_that_commits_before_a_timely_http_receipt_is_reconciled_preserves_terminal_state()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var admission = ConnectorAdmission("http-cancel-before-receipt-reconcile",
            new("tenant-a", "http-cancel-before-receipt-reconcile"), httpShardIds: ["shard"]);
        EtlConnectorPartitionLease lease;
        EtlConnectorBatchDescriptor batch;
        EtlHttpDeliveryAttempt permit;
        string beforeCancellation;

        await using (var setupContext = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(setupContext, clock,
                partitionLease: TimeSpan.FromMinutes(2));
            (lease, batch, _) = await PrepareHttpOutputBatchAsync(store, admission, "cancel-receipt-worker");
            var begun = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main,
                DeliveryRequestId(82));
            permit = Assert.IsType<EtlHttpDeliveryAttempt>(begun.Permit);
            Assert.Equal(EtlStoreStatus.Applied, begun.Status);
            beforeCancellation = await SnapshotCancellationAccountingAsync(setupContext);
        }

        var receivedAt = permit.StartedAt.AddSeconds(1);
        clock.Advance(TimeSpan.FromSeconds(2));
        string cancelledSnapshot;
        await using (var cancelContext = database.CreateContext())
        {
            var runtime = database.CreateStore(cancelContext, clock);
            var cancelled = await runtime.CancelAsync(admission.Invocation);
            Assert.Equal(EtlStoreStatus.Applied, cancelled.Status);
            Assert.Equal(EtlTerminalOutcome.Cancelled, cancelled.Result!.Outcome);
            Assert.Equal(beforeCancellation, await SnapshotCancellationAccountingAsync(cancelContext));
            Assert.Single(await cancelContext.Terminals.AsNoTracking().ToListAsync());
            Assert.Single(await cancelContext.Outbox.AsNoTracking().ToListAsync());
            cancelledSnapshot = await ReadHttpTerminalRaceSnapshotAsync(cancelContext);
        }

        await using var lateContext = database.CreateContext();
        var lateStore = (IEtlConnectorRuntimeStore)database.CreateStore(lateContext, clock,
            partitionLease: TimeSpan.FromMinutes(2));
        var response = new EtlHttpDeliveryResponse(204, null) { ResponseReceivedAtUtc = receivedAt };
        var lateReceipt = await lateStore.CompleteConnectorHttpDeliveryAsync(lease, batch.BatchId,
            EtlOutputRoute.Main, permit, response);

        Assert.Equal(EtlStoreStatus.Terminal, lateReceipt.Status);
        Assert.Null(lateReceipt.Permit);
        Assert.Equal(cancelledSnapshot, await ReadHttpTerminalRaceSnapshotAsync(lateContext));
        Assert.Single(await lateContext.Terminals.AsNoTracking().ToListAsync());
        Assert.Single(await lateContext.Outbox.AsNoTracking().ToListAsync());
    }
}
