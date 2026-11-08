using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Cancellation_that_commits_first_refuses_late_http_ack_acceptance_and_source_progress()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var admission = ConnectorAdmission("http-cancel-first-race", new("tenant-a", "http-cancel-first-race"),
            httpShardIds: ["shard-a", "shard-b"]);
        EtlConnectorPartitionLease outputLease;
        EtlConnectorPartitionLease sourceLease;
        EtlConnectorBatchDescriptor batch;
        EtlHttpDeliveryAttempt permit;
        string beforeCancellationAccounting;

        await using (var setupContext = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(setupContext, clock);
            var leases = await StartHttpRunAsync(store, admission, "race-worker");
            Assert.Equal(2, leases.Count);
            outputLease = leases.Single(lease => lease.PartitionOrdinal == 0);
            sourceLease = leases.Single(lease => lease.PartitionOrdinal == 1);

            (outputLease, batch, _) = await PrepareHttpOutputBatchAsync(store, admission, outputLease, "cancel-first");
            var begun = await store.BeginConnectorHttpDeliveryAsync(outputLease, batch.BatchId,
                EtlOutputRoute.Main, DeliveryRequestId(70));
            Assert.Equal(EtlStoreStatus.Applied, begun.Status);
            permit = Assert.IsType<EtlHttpDeliveryAttempt>(begun.Permit);
            beforeCancellationAccounting = await SnapshotCancellationAccountingAsync(setupContext);
            var retained = await setupContext.ConnectorBatches.AsNoTracking().SingleAsync();
            var deliveries = EtlConnectorRecordCodec.Read<EtlConnectorHttpDeliveries>(retained.HttpDeliveryJson!);
            Assert.Equal(EtlHttpDeliveryPhase.InFlight, deliveries.Main!.Phase);
            Assert.Equal(permit, Assert.Single(deliveries.Main.Attempts).Attempt);
        }

        string cancelledSnapshot;
        await using (var cancelContext = database.CreateContext())
        {
            var runtime = database.CreateStore(cancelContext, clock);
            var cancelled = await runtime.CancelAsync(admission.Invocation);
            Assert.Equal(EtlStoreStatus.Applied, cancelled.Status);
            Assert.Equal(EtlTerminalOutcome.Cancelled, cancelled.Result!.Outcome);
            Assert.Equal(beforeCancellationAccounting,
                await SnapshotCancellationAccountingAsync(cancelContext));

            var partition = await cancelContext.ConnectorPartitions.AsNoTracking()
                .SingleAsync(row => row.PartitionOrdinal == outputLease.PartitionOrdinal);
            var run = Assert.IsType<EtlConnectorRunView>(await ((IEtlConnectorRuntimeStore)runtime)
                .FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
            var deliveryRow = await cancelContext.ConnectorBatches.AsNoTracking().SingleAsync();
            var deliveries = EtlConnectorRecordCodec.Read<EtlConnectorHttpDeliveries>(deliveryRow.HttpDeliveryJson!);

            Assert.Equal(EtlRunStatus.Cancelled, run.Run.Status);
            Assert.Equal(2, run.Run.OutstandingRecords);
            Assert.Equal(5, run.Run.OutstandingBytes);
            Assert.Equal(batch.BatchId, partition.OwedBatchId);
            Assert.False(partition.Completed);
            Assert.False(deliveryRow.Settled);
            Assert.Equal(EtlHttpDeliveryPhase.InFlight, deliveries.Main!.Phase);
            Assert.Equal(permit, Assert.Single(deliveries.Main.Attempts).Attempt);
            Assert.Single(await cancelContext.Terminals.AsNoTracking().ToListAsync());
            Assert.Single(await cancelContext.Outbox.AsNoTracking().ToListAsync());
            cancelledSnapshot = await ReadHttpTerminalRaceSnapshotAsync(cancelContext);
        }

        await using (var lateAckContext = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(lateAckContext, clock);
            var lateAck = await store.CompleteConnectorHttpDeliveryAsync(outputLease, batch.BatchId,
                EtlOutputRoute.Main, permit, new(204, null));

            Assert.Equal(EtlStoreStatus.Terminal, lateAck.Status);
            Assert.Null(lateAck.Permit);
            Assert.Equal(cancelledSnapshot, await ReadHttpTerminalRaceSnapshotAsync(lateAckContext));
            Assert.Equal(beforeCancellationAccounting,
                await SnapshotCancellationAccountingAsync(lateAckContext));
        }

        await using (var lateAcceptanceContext = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(lateAcceptanceContext, clock);
            var lateAcceptance = await store.AcceptConnectorBatchAsync(outputLease, batch.BatchId);

            Assert.Equal(EtlStoreStatus.Terminal, lateAcceptance.Status);
            Assert.Null(lateAcceptance.Lease);
            Assert.Equal(cancelledSnapshot, await ReadHttpTerminalRaceSnapshotAsync(lateAcceptanceContext));
        }

        await using (var lateDeliveryBeginContext = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(lateDeliveryBeginContext, clock);
            var lateBegin = await store.BeginConnectorHttpDeliveryAsync(outputLease, batch.BatchId,
                EtlOutputRoute.Error, DeliveryRequestId(71));

            Assert.Equal(EtlStoreStatus.Terminal, lateBegin.Status);
            Assert.Null(lateBegin.Permit);
            Assert.Null(lateBegin.Delivery);
            Assert.Equal(cancelledSnapshot, await ReadHttpTerminalRaceSnapshotAsync(lateDeliveryBeginContext));
        }

        await using (var lateSourceBeginContext = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(lateSourceBeginContext, clock);
            var lateBegin = await store.BeginConnectorHttpSourceAsync(sourceLease,
                "00000000000000000000000000000073");

            Assert.Equal(EtlStoreStatus.Terminal, lateBegin.Status);
            Assert.Null(lateBegin.Permit);
            Assert.Null(lateBegin.Fetch);
            Assert.Equal(cancelledSnapshot, await ReadHttpTerminalRaceSnapshotAsync(lateSourceBeginContext));
        }
    }

    [Fact]
    public async Task Final_http_acceptance_that_commits_first_cannot_be_overwritten_by_cancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var admission = ConnectorAdmission("http-success-first-race", new("tenant-a", "http-success-first-race"),
            httpShardIds: ["shard"]);
        string successSnapshot;

        await using (var successContext = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(successContext, clock);
            var (lease, batch) = await PrepareFinalHttpSuccessBatchAsync(store, admission, "success-worker", 72);
            var accepted = await store.AcceptConnectorBatchAsync(lease, batch.BatchId);
            var terminal = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(
                admission.Invocation.Scope, admission.Invocation.RunId));

            Assert.Equal(EtlStoreStatus.Applied, accepted.Status);
            Assert.Null(accepted.Lease!.OwedBatch);
            Assert.Equal(EtlRunStatus.Succeeded, terminal.Run.Status);
            Assert.Equal(EtlTerminalOutcome.Succeeded, terminal.Run.TerminalResult!.Outcome);
            Assert.Equal(2, terminal.Run.AcceptedRecords);
            Assert.Equal(0, terminal.Run.OutstandingRecords);
            Assert.Equal(0, terminal.Run.OutstandingBytes);
            Assert.Single(await successContext.Terminals.AsNoTracking().ToListAsync());
            Assert.Single(await successContext.Outbox.AsNoTracking().ToListAsync());
            successSnapshot = await ReadHttpTerminalRaceSnapshotAsync(successContext);
        }

        await using (var cancellationContext = database.CreateContext())
        {
            var runtime = database.CreateStore(cancellationContext, clock);
            var cancellation = await runtime.CancelAsync(admission.Invocation);

            Assert.Equal(EtlStoreStatus.Conflict, cancellation.Status);
            Assert.Equal(EtlTerminalOutcome.Succeeded, cancellation.Result!.Outcome);
            Assert.Equal(successSnapshot, await ReadHttpTerminalRaceSnapshotAsync(cancellationContext));
            Assert.Single(await cancellationContext.Terminals.AsNoTracking().ToListAsync());
            Assert.Single(await cancellationContext.Outbox.AsNoTracking().ToListAsync());
        }
    }

    [Fact]
    public async Task Cancellation_that_commits_before_final_http_acceptance_preserves_ready_owed_batch_and_journal()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var admission = ConnectorAdmission("http-cancel-ready-race", new("tenant-a", "http-cancel-ready-race"),
            httpShardIds: ["shard"]);
        EtlConnectorPartitionLease lease;
        EtlConnectorBatchDescriptor batch;
        string readyAccounting;

        await using (var setupContext = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(setupContext, clock);
            (lease, batch) = await PrepareFinalHttpSuccessBatchAsync(store, admission, "cancel-ready-worker", 73);

            var partition = await setupContext.ConnectorPartitions.AsNoTracking().SingleAsync();
            var retainedBatch = await setupContext.ConnectorBatches.AsNoTracking().SingleAsync();
            var output = EtlConnectorRecordCodec.Read<EtlConnectorBatchOutput>(retainedBatch.OutputJson!);
            var deliveries = EtlConnectorRecordCodec.Read<EtlConnectorHttpDeliveries>(retainedBatch.HttpDeliveryJson!);
            var request = await setupContext.ConnectorHttpRequests.AsNoTracking().SingleAsync();
            var page = await setupContext.ConnectorHttpPages.AsNoTracking().SingleAsync();

            Assert.Equal(batch.BatchId, partition.OwedBatchId);
            Assert.False(partition.Completed);
            Assert.False(retainedBatch.Settled);
            Assert.Equal(2, retainedBatch.RecordCount);
            Assert.Equal(5, retainedBatch.InputBytes);
            Assert.Equal(2, output.AcceptedRecords);
            Assert.Equal(0, output.RejectedRecords);
            Assert.Null(output.Errors);
            Assert.Equal(EtlHttpDeliveryPhase.Acknowledged, deliveries.Main!.Phase);
            Assert.Equal(new EtlHttpDeliveryResponse(204, null), Assert.Single(deliveries.Main.Attempts).Response);
            Assert.NotNull(request.FetchJson);
            Assert.False(page.HasContinuation);
            Assert.Equal(0, page.AcceptedItemCount);
            readyAccounting = await SnapshotCancellationAccountingAsync(setupContext);
        }

        string cancelledSnapshot;
        await using (var cancelContext = database.CreateContext())
        {
            var runtime = database.CreateStore(cancelContext, clock);
            var cancelled = await runtime.CancelAsync(admission.Invocation);
            Assert.Equal(EtlStoreStatus.Applied, cancelled.Status);
            Assert.Equal(EtlTerminalOutcome.Cancelled, cancelled.Result!.Outcome);
            Assert.Equal(readyAccounting, await SnapshotCancellationAccountingAsync(cancelContext));

            var run = Assert.IsType<EtlConnectorRunView>(await ((IEtlConnectorRuntimeStore)runtime)
                .FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
            var partition = await cancelContext.ConnectorPartitions.AsNoTracking().SingleAsync();
            var retainedBatch = await cancelContext.ConnectorBatches.AsNoTracking().SingleAsync();
            var request = await cancelContext.ConnectorHttpRequests.AsNoTracking().SingleAsync();
            var page = await cancelContext.ConnectorHttpPages.AsNoTracking().SingleAsync();

            Assert.Equal(EtlRunStatus.Cancelled, run.Run.Status);
            Assert.Equal(2, run.Run.OutstandingRecords);
            Assert.Equal(5, run.Run.OutstandingBytes);
            Assert.Equal(batch.BatchId, partition.OwedBatchId);
            Assert.False(partition.Completed);
            Assert.False(retainedBatch.Settled);
            Assert.Equal(0, page.AcceptedItemCount);
            Assert.NotNull(request.FetchJson);
            Assert.Single(await cancelContext.Terminals.AsNoTracking().ToListAsync());
            Assert.Single(await cancelContext.Outbox.AsNoTracking().ToListAsync());
            cancelledSnapshot = await ReadHttpTerminalRaceSnapshotAsync(cancelContext);
        }

        await using (var lateAcceptanceContext = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(lateAcceptanceContext, clock);
            var lateAcceptance = await store.AcceptConnectorBatchAsync(lease, batch.BatchId);

            Assert.Equal(EtlStoreStatus.Terminal, lateAcceptance.Status);
            Assert.Null(lateAcceptance.Lease);
            Assert.Equal(cancelledSnapshot, await ReadHttpTerminalRaceSnapshotAsync(lateAcceptanceContext));
            Assert.Single(await lateAcceptanceContext.Terminals.AsNoTracking().ToListAsync());
            Assert.Single(await lateAcceptanceContext.Outbox.AsNoTracking().ToListAsync());
        }
    }

    private static async Task<(EtlConnectorPartitionLease Lease, EtlConnectorBatchDescriptor Batch)>
        PrepareFinalHttpSuccessBatchAsync(IEtlConnectorRuntimeStore store, EtlConnectorRunAdmission admission,
            string workerId, int deliveryRequestNumber)
    {
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, workerId));
        var attached = await store.AttachTestHttpPageAsync(initialLease,
            RetainedPage(initialLease, $"{admission.Invocation.RunId}-final-page", byteLength: 9, recordCount: 2));
        var lease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(
            admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = HttpBatch(admission, run.Prepared!, lease, endItemExclusive: 2, inputBytes: 5,
            marker: $"{admission.Invocation.RunId}-final-batch");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);

        var output = HttpOutput(admission, batch, accepted: 2, rejected: 0, filtered: 0,
            suffix: $"{admission.Invocation.RunId}-final-output");
        Assert.Equal(EtlStoreStatus.Applied,
            (await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output)).Status);
        await AcknowledgeHttpRouteAsync(store, lease, batch.BatchId, EtlOutputRoute.Main, deliveryRequestNumber);
        return (lease, batch);
    }

    private static async Task<string> SnapshotCancellationAccountingAsync(EtlRuntimeDbContext context)
    {
        context.ChangeTracker.Clear();
        var run = await context.Runs.AsNoTracking().SingleAsync();
        return JsonSerializer.Serialize(new
        {
            run.AcceptedRecords, run.RejectedRecords, run.FilteredRecords, run.OutstandingRecords, run.OutstandingBytes,
            Connectors = await context.ConnectorRuns.AsNoTracking().OrderBy(row => row.RunKey).ToArrayAsync(),
            Partitions = await context.ConnectorPartitions.AsNoTracking().OrderBy(row => row.PartitionOrdinal).ToArrayAsync(),
            Batches = await context.ConnectorBatches.AsNoTracking().OrderBy(row => row.BatchId).ToArrayAsync(),
            Requests = await context.ConnectorHttpRequests.AsNoTracking()
                .OrderBy(row => row.PartitionOrdinal).ThenBy(row => row.PageSequence).ToArrayAsync(),
            Pages = await context.ConnectorHttpPages.AsNoTracking()
                .OrderBy(row => row.PartitionOrdinal).ThenBy(row => row.PageSequence).ToArrayAsync()
        });
    }

    private static async Task<string> ReadHttpTerminalRaceSnapshotAsync(EtlRuntimeDbContext context) =>
        JsonSerializer.Serialize(new
        {
            Accounting = await SnapshotCancellationAccountingAsync(context),
            Runs = await context.Runs.AsNoTracking().OrderBy(row => row.RunKey).ToArrayAsync(),
            Terminals = await context.Terminals.AsNoTracking().OrderBy(row => row.RunKey).ToArrayAsync(),
            Outbox = await context.Outbox.AsNoTracking().OrderBy(row => row.RunKey).ToArrayAsync()
        });
}
