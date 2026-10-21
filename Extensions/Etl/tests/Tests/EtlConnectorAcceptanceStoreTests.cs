using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Mixed_route_acceptance_requires_exact_file_publication_and_http_ack_and_settles_once()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var pipeline = CsvMixedRoutePipeline() with
        {
            Routes = new EtlRoutes(new EtlJsonlDestination("output"), new EtlHttpDestination("errors"))
        };
        var admission = ConnectorAdmission("accept-mixed-routes", new("tenant-a", "accept-mixed-routes"), pipeline);
        var lease = (await StartOutputCsvRunAsync(store, admission))[0];
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = OutputTestBatch(admission, run.Prepared!, lease);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);

        var main = OutputRoute(admission, batch, EtlOutputRoute.Main, EtlConnectorKind.Jsonl,
            "main-untrusted-label", EtlRuntimeRecordCodec.HashText("main-jsonl"), 17, 1);
        var errors = OutputRoute(admission, batch, EtlOutputRoute.Error, EtlConnectorKind.Http,
            "errors-body", EtlRuntimeRecordCodec.HashText("error-http"), 19, 1);
        var output = new EtlConnectorBatchOutput(1, 1, 0, main, errors);
        Assert.Equal(EtlStoreStatus.Applied, (await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output)).Status);

        var before = await ReadAcceptanceStateAsync(context, lease.PartitionOrdinal);
        var expectedPublication = FilePublication(main);
        foreach (var invalidPublication in new[]
        {
            expectedPublication with { Key = "wrong-publication-key" },
            expectedPublication with { ContentHash = CorruptHash(expectedPublication.ContentHash) },
            expectedPublication with { ByteLength = expectedPublication.ByteLength + 1 }
        })
        {
            Assert.Equal(EtlStoreStatus.Conflict, await store.RecordConnectorFilePublicationAsync(
                lease, batch.BatchId, EtlOutputRoute.Main, invalidPublication));
        }
        Assert.Equal(EtlStoreStatus.Conflict, (await store.AcceptConnectorBatchAsync(lease, batch.BatchId)).Status);
        var afterRefusals = await ReadAcceptanceStateAsync(context, lease.PartitionOrdinal);
        Assert.Equal(before, afterRefusals);
        Assert.False((await context.ConnectorBatches.AsNoTracking().SingleAsync()).MainFilePublished);

        Assert.Equal(EtlStoreStatus.Applied, await store.RecordConnectorFilePublicationAsync(
            lease, batch.BatchId, EtlOutputRoute.Main, expectedPublication));
        var afterPublication = await ReadAcceptanceStateAsync(context, lease.PartitionOrdinal);
        Assert.Equal(EtlStoreStatus.Replay, await store.RecordConnectorFilePublicationAsync(
            lease, batch.BatchId, EtlOutputRoute.Main, expectedPublication));
        Assert.Equal(afterPublication, await ReadAcceptanceStateAsync(context, lease.PartitionOrdinal));
        Assert.Equal(EtlStoreStatus.Conflict, (await store.AcceptConnectorBatchAsync(lease, batch.BatchId)).Status);
        await AcknowledgeHttpRouteAsync(store, lease, batch.BatchId, EtlOutputRoute.Error, 821);

        var accepted = await store.AcceptConnectorBatchAsync(lease, batch.BatchId);
        var runAfterAcceptance = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(
            admission.Invocation.Scope, admission.Invocation.RunId));
        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync(row => row.PartitionOrdinal == lease.PartitionOrdinal);
        var batchRow = await context.ConnectorBatches.AsNoTracking().SingleAsync();
        var fileRange = Assert.IsType<EtlConnectorFilePosition>(lease.Position);

        Assert.Equal(EtlStoreStatus.Applied, accepted.Status);
        Assert.Null(accepted.Lease!.OwedBatch);
        Assert.Equal(fileRange with { NextOrdinal = fileRange.EndOrdinalExclusive }, accepted.Lease.Position);
        Assert.True(partition.Completed);
        Assert.Null(partition.OwedBatchId);
        Assert.Equal(1, partition.AcceptedBatchCount);
        Assert.Equal(1, partition.AcceptedRecords);
        Assert.Equal(1, partition.RejectedRecords);
        Assert.Equal(0, partition.FilteredRecords);
        Assert.Equal(1, runAfterAcceptance.Run.AcceptedRecords);
        Assert.Equal(1, runAfterAcceptance.Run.RejectedRecords);
        Assert.Equal(0, runAfterAcceptance.Run.FilteredRecords);
        Assert.Equal(0, runAfterAcceptance.Run.OutstandingRecords);
        Assert.Equal(0, runAfterAcceptance.Run.OutstandingBytes);
        Assert.True(batchRow.Settled);
        Assert.Equal(0, batchRow.BatchOrdinal);
        Assert.Null(batchRow.HttpPageSequence);

        var afterAcceptance = await ReadAcceptanceStateAsync(context, lease.PartitionOrdinal);
        Assert.Equal(EtlStoreStatus.Replay, (await store.AcceptConnectorBatchAsync(lease, batch.BatchId)).Status);
        Assert.Equal(afterAcceptance, await ReadAcceptanceStateAsync(context, lease.PartitionOrdinal));
    }

    [Fact]
    public async Task Http_partial_and_full_page_batches_recover_and_final_fully_filtered_acceptance_terminalizes()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("accept-http-pages", new("tenant-a", "accept-http-pages"), httpShardIds: ["shard-a"]);
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, "worker-a"));
        var attached = await store.AttachHttpPageAsync(initialLease,
            RetainedPage(initialLease, "page-zero", byteLength: 23, recordCount: 3, nextContinuation: "cursor-one"));
        var lease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));

        var firstBatch = HttpAcceptanceBatch(admission, run.Prepared!, lease, startItem: 0, endItemExclusive: 1, marker: "http-first");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, firstBatch)).Status);
        var firstOutput = HttpOutput(admission, firstBatch, accepted: 1, rejected: 0, filtered: 0, suffix: "http-first");
        Assert.Equal(EtlStoreStatus.Applied, (await store.RetainConnectorBatchOutputAsync(lease, firstBatch.BatchId, firstOutput)).Status);
        await AcknowledgeHttpRouteAsync(store, lease, firstBatch.BatchId, EtlOutputRoute.Main, 822);

        var firstAccepted = await store.AcceptConnectorBatchAsync(lease, firstBatch.BatchId);
        var afterFirst = Assert.IsType<EtlConnectorHttpPosition>(firstAccepted.Lease!.Position);
        var pageAfterFirst = await context.ConnectorHttpPages.AsNoTracking().SingleAsync(row => row.PageSequence == 0);
        var partitionAfterFirst = await context.ConnectorPartitions.AsNoTracking().SingleAsync();
        var runAfterFirst = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));

        Assert.Equal(EtlStoreStatus.Applied, firstAccepted.Status);
        Assert.Equal(0, afterFirst.PageSequence);
        Assert.Equal(1, afterFirst.NextItem);
        Assert.Equal(1, pageAfterFirst.AcceptedItemCount);
        Assert.Null(partitionAfterFirst.OwedBatchId);
        Assert.Equal(1, partitionAfterFirst.AcceptedBatchCount);
        Assert.Equal(1, partitionAfterFirst.AcceptedRecords);
        Assert.Equal(1, runAfterFirst.Run.AcceptedRecords);
        Assert.Equal(0, runAfterFirst.Run.OutstandingRecords);

        var afterFirstState = await ReadAcceptanceStateAsync(context, lease.PartitionOrdinal);
        Assert.Equal(EtlStoreStatus.Replay, (await store.AcceptConnectorBatchAsync(lease, firstBatch.BatchId)).Status);
        Assert.Equal(afterFirstState, await ReadAcceptanceStateAsync(context, lease.PartitionOrdinal));

        var secondBatch = HttpAcceptanceBatch(admission, run.Prepared!, firstAccepted.Lease, startItem: 1,
            endItemExclusive: 3, marker: "http-second");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(firstAccepted.Lease, secondBatch)).Status);
        var secondOutput = HttpOutput(admission, secondBatch, accepted: 2, rejected: 0, filtered: 0, suffix: "http-second");
        Assert.Equal(EtlStoreStatus.Applied, (await store.RetainConnectorBatchOutputAsync(
            firstAccepted.Lease, secondBatch.BatchId, secondOutput)).Status);
        await AcknowledgeHttpRouteAsync(store, firstAccepted.Lease, secondBatch.BatchId, EtlOutputRoute.Main, 823);

        var priorBatchReplay = await store.AcceptConnectorBatchAsync(lease, firstBatch.BatchId);
        var pageReplay = await store.AttachHttpPageAsync(lease,
            RetainedPage(lease, "page-zero", byteLength: 23, recordCount: 3, nextContinuation: "cursor-one"));
        Assert.Equal(EtlStoreStatus.Replay, priorBatchReplay.Status);
        Assert.Equal(secondBatch, priorBatchReplay.Lease!.OwedBatch!.Batch);
        Assert.Equal(secondOutput, priorBatchReplay.Lease.OwedBatch.Output);
        Assert.Equal(EtlStoreStatus.Replay, pageReplay.Status);
        Assert.Equal(secondBatch, pageReplay.Lease!.OwedBatch!.Batch);
        Assert.Equal(secondOutput, pageReplay.Lease.OwedBatch.Output);
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(firstAccepted.Lease));

        await using var recoveryContext = database.CreateContext();
        var recoveryStore = (IEtlConnectorRuntimeStore)database.CreateStore(recoveryContext);
        var recovered = Assert.IsType<EtlConnectorPartitionLease>(await recoveryStore.ClaimConnectorPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "worker-b"));
        var recoveredPosition = Assert.IsType<EtlConnectorHttpPosition>(recovered.Position);
        var recoveredRun = Assert.IsType<EtlConnectorRunView>(await recoveryStore.FindConnectorRunAsync(
            admission.Invocation.Scope, admission.Invocation.RunId));
        var recoveredPartition = await recoveryContext.ConnectorPartitions.AsNoTracking().SingleAsync();

        Assert.True(recovered.Fence > firstAccepted.Lease.Fence);
        Assert.Equal(0, recoveredPosition.PageSequence);
        Assert.Equal(1, recoveredPosition.NextItem);
        Assert.Equal(secondBatch, recovered.OwedBatch!.Batch);
        Assert.Equal(secondOutput, recovered.OwedBatch.Output);
        Assert.Equal(EtlHttpDeliveryPhase.Acknowledged, recovered.OwedBatch.HttpDeliveries!.Main!.Phase);
        Assert.Equal(1, recoveredPartition.AcceptedBatchCount);
        Assert.Equal(1, recoveredPartition.AcceptedRecords);
        Assert.Equal(1, recoveredRun.Run.AcceptedRecords);
        Assert.Equal(2, recoveredRun.Run.OutstandingRecords);
        var ordinalRows = await recoveryContext.ConnectorBatches.AsNoTracking().OrderBy(row => row.BatchOrdinal).ToListAsync();
        Assert.Equal(new long[] { 0, 1 }, ordinalRows.Select(row => row.BatchOrdinal));
        Assert.All(ordinalRows, row => Assert.Equal((long?)0, row.HttpPageSequence));

        var secondAccepted = await recoveryStore.AcceptConnectorBatchAsync(recovered, secondBatch.BatchId);
        var nextPosition = Assert.IsType<EtlConnectorHttpPosition>(secondAccepted.Lease!.Position);
        var pageZero = await recoveryContext.ConnectorHttpPages.AsNoTracking().SingleAsync(row => row.PageSequence == 0);
        var nextRequest = await recoveryContext.ConnectorHttpRequests.AsNoTracking().SingleAsync(row => row.PageSequence == 1);
        var expectedRequest = new EtlHttpRequest("shard-a", "cursor-one");
        Assert.Equal(EtlStoreStatus.Applied, secondAccepted.Status);
        Assert.Equal(1, nextPosition.PageSequence);
        Assert.Equal(0, nextPosition.NextItem);
        Assert.Equal(expectedRequest, nextPosition.Request);
        Assert.Null(nextPosition.Page);
        Assert.Equal(3, pageZero.AcceptedItemCount);
        Assert.Equal(expectedRequest, EtlConnectorRecordCodec.Read<EtlHttpRequest>(nextRequest.RequestJson));
        Assert.Equal(EtlRuntimeIdentity.HttpRequestHash(expectedRequest), nextRequest.RequestHash);
        Assert.Equal(nextRequest.RequestHash, pageZero.NextRequestHash);
        Assert.Equal((long?)0, nextRequest.PreviousPageSequence);
        Assert.Equal(true, nextRequest.PreviousHasContinuation);

        var finalPage = RetainedPage(secondAccepted.Lease, "page-one", byteLength: 15, recordCount: 2);
        var attachedFinal = await recoveryStore.AttachHttpPageAsync(secondAccepted.Lease, finalPage);
        var finalLease = Assert.IsType<EtlConnectorPartitionLease>(attachedFinal.Lease);
        var finalBatch = HttpAcceptanceBatch(admission, recoveredRun.Prepared!, finalLease, 0, 2, "http-filtered");
        Assert.Equal(EtlStoreStatus.Applied, (await recoveryStore.ReserveConnectorBatchAsync(finalLease, finalBatch)).Status);
        var filtered = new EtlConnectorBatchOutput(0, 0, 2, null, null);
        Assert.Equal(EtlStoreStatus.Applied, (await recoveryStore.RetainConnectorBatchOutputAsync(finalLease, finalBatch.BatchId, filtered)).Status);

        var finalAccepted = await recoveryStore.AcceptConnectorBatchAsync(finalLease, finalBatch.BatchId);
        var finalRun = Assert.IsType<EtlConnectorRunView>(await recoveryStore.FindConnectorRunAsync(
            admission.Invocation.Scope, admission.Invocation.RunId));
        var finalPartition = await recoveryContext.ConnectorPartitions.AsNoTracking().SingleAsync();
        var finalPageRecord = await recoveryContext.ConnectorHttpPages.AsNoTracking().SingleAsync(row => row.PageSequence == 1);
        var finalBatchRows = await recoveryContext.ConnectorBatches.AsNoTracking().OrderBy(row => row.BatchOrdinal).ToListAsync();

        Assert.Equal(EtlStoreStatus.Applied, finalAccepted.Status);
        Assert.Null(finalAccepted.Lease!.OwedBatch);
        Assert.True(Assert.IsType<EtlConnectorHttpPosition>(finalAccepted.Lease.Position).Exhausted);
        Assert.Equal(EtlRunStatus.Succeeded, finalRun.Run.Status);
        Assert.Equal(EtlTerminalOutcome.Succeeded, finalRun.Run.TerminalResult!.Outcome);
        Assert.Equal(3, finalPartition.AcceptedBatchCount);
        Assert.Equal(3, finalPartition.AcceptedRecords);
        Assert.Equal(2, finalPartition.FilteredRecords);
        Assert.True(finalPartition.Completed);
        Assert.Null(finalPartition.OwedBatchId);
        Assert.Equal(2, finalPageRecord.AcceptedItemCount);
        Assert.Equal(new long[] { 0, 1, 2 }, finalBatchRows.Select(row => row.BatchOrdinal));
        Assert.Equal(3, finalBatchRows.Select(row => row.BatchOrdinal).Distinct().Count());
        Assert.Single(await recoveryContext.Terminals.AsNoTracking().ToListAsync());
        Assert.Single(await recoveryContext.Outbox.AsNoTracking().ToListAsync());

        var terminalState = await ReadAcceptanceStateAsync(recoveryContext, finalLease.PartitionOrdinal);
        Assert.Equal(EtlStoreStatus.Terminal,
            (await recoveryStore.AcceptConnectorBatchAsync(finalLease, finalBatch.BatchId)).Status);
        Assert.Equal(terminalState, await ReadAcceptanceStateAsync(recoveryContext, finalLease.PartitionOrdinal));
        Assert.Single(await recoveryContext.Terminals.AsNoTracking().ToListAsync());
        Assert.Single(await recoveryContext.Outbox.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Expiry_after_acceptance_save_rolls_back_progress_and_automatic_terminal()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var expire = new ConnectorExpireAfterSave(clock);
        await using var context = database.CreateContext(expire);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock, partitionLease: TimeSpan.FromSeconds(30));
        var admission = ConnectorAdmission("accept-expiry", new("tenant-a", "accept-expiry"), httpShardIds: ["shard-a"]);
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var attached = await store.AttachHttpPageAsync(initialLease,
            RetainedPage(initialLease, "only-page", byteLength: 12, recordCount: 1));
        var lease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = HttpAcceptanceBatch(admission, run.Prepared!, lease, 0, 1, "expiry-batch");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = HttpOutput(admission, batch, accepted: 1, rejected: 0, filtered: 0, suffix: "expiry-output");
        Assert.Equal(EtlStoreStatus.Applied, (await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output)).Status);
        await AcknowledgeHttpRouteAsync(store, lease, batch.BatchId, EtlOutputRoute.Main, 824);
        var before = await ReadAcceptanceStateAsync(context, lease.PartitionOrdinal);
        var batchBefore = await context.ConnectorBatches.AsNoTracking().SingleAsync();
        var pageBefore = await context.ConnectorHttpPages.AsNoTracking().SingleAsync();

        expire.Armed = true;
        var expiredAcceptance = await store.AcceptConnectorBatchAsync(lease, batch.BatchId);
        context.ChangeTracker.Clear();
        var afterExpiry = await ReadAcceptanceStateAsync(context, lease.PartitionOrdinal);
        var batchAfter = await context.ConnectorBatches.AsNoTracking().SingleAsync();
        var pageAfter = await context.ConnectorHttpPages.AsNoTracking().SingleAsync();

        await using var successorContext = database.CreateContext();
        var successorStore = (IEtlConnectorRuntimeStore)database.CreateStore(successorContext, clock);
        var successor = Assert.IsType<EtlConnectorPartitionLease>(await successorStore.ClaimConnectorPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "successor-worker"));
        var beforeOldFence = await ReadAcceptanceStateAsync(context, lease.PartitionOrdinal);
        var oldFenceAcceptance = await store.AcceptConnectorBatchAsync(lease, batch.BatchId);
        var afterOldFence = await ReadAcceptanceStateAsync(context, lease.PartitionOrdinal);

        Assert.Equal(EtlStoreStatus.LeaseLost, expiredAcceptance.Status);
        Assert.Equal(EtlStoreStatus.LeaseLost, oldFenceAcceptance.Status);
        Assert.True(successor.Fence > lease.Fence);
        Assert.Equal(batch, successor.OwedBatch!.Batch);
        Assert.Equal(before, afterExpiry);
        Assert.Equal(beforeOldFence, afterOldFence);
        Assert.Equal(batchBefore.Settled, batchAfter.Settled);
        Assert.False(batchAfter.Settled);
        Assert.Equal(pageBefore.AcceptedItemCount, pageAfter.AcceptedItemCount);
        Assert.Equal(0, pageAfter.AcceptedItemCount);
        Assert.Equal(batch.BatchId, afterExpiry.PartitionOwedBatchId);
        Assert.Equal((int)EtlRunStatus.Running, afterExpiry.RunStatus);
        Assert.Empty(await context.Terminals.AsNoTracking().ToListAsync());
        Assert.Empty(await context.Outbox.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Final_rejected_http_page_acceptance_publishes_business_error_terminal()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var pipeline = EtlV2TestPipelines.Create() with
        {
            Routes = new EtlRoutes(new EtlHttpDestination("output"), new EtlJsonlDestination("errors"))
        };
        var admission = ConnectorAdmission("accept-business-error", new("tenant-a", "accept-business-error"),
            pipeline, ["shard-a"]);
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var attached = await store.AttachHttpPageAsync(initialLease,
            RetainedPage(initialLease, "rejected-page", byteLength: 9, recordCount: 1));
        var lease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = HttpAcceptanceBatch(admission, run.Prepared!, lease, 0, 1, "rejected-final");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var error = OutputRoute(admission, batch, EtlOutputRoute.Error, EtlConnectorKind.Jsonl,
            "error-body", EtlRuntimeRecordCodec.HashText("rejected-row"), 21, 1);
        var output = new EtlConnectorBatchOutput(0, 1, 0, null, error);
        Assert.Equal(EtlStoreStatus.Applied, (await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output)).Status);
        Assert.Equal(EtlStoreStatus.Applied, await store.RecordConnectorFilePublicationAsync(
            lease, batch.BatchId, EtlOutputRoute.Error, FilePublication(error)));

        var accepted = await store.AcceptConnectorBatchAsync(lease, batch.BatchId);
        var terminal = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(
            admission.Invocation.Scope, admission.Invocation.RunId));
        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync();

        Assert.Equal(EtlStoreStatus.Applied, accepted.Status);
        Assert.Equal(EtlRunStatus.BusinessError, terminal.Run.Status);
        Assert.Equal(EtlTerminalOutcome.BusinessError, terminal.Run.TerminalResult!.Outcome);
        Assert.Equal("etl.rows-rejected", terminal.Run.TerminalResult.Diagnostic!.Code);
        Assert.Equal(1, terminal.Run.RejectedRecords);
        Assert.Equal(1, terminal.Run.TerminalResult.RejectedRecords);
        Assert.Equal(1, partition.RejectedRecords);
        Assert.Equal(1, partition.AcceptedBatchCount);
        Assert.True(partition.Completed);
        Assert.Single(await context.Terminals.AsNoTracking().ToListAsync());
        Assert.Single(await context.Outbox.AsNoTracking().ToListAsync());
    }

    private static async Task AcknowledgeHttpRouteAsync(IEtlConnectorRuntimeStore store,
        EtlConnectorPartitionLease lease, string batchId, EtlOutputRoute route, int requestNumber)
    {
        var begun = await store.BeginConnectorHttpDeliveryAsync(lease, batchId, route, DeliveryRequestId(requestNumber));
        Assert.Equal(EtlStoreStatus.Applied, begun.Status);
        var permit = Assert.IsType<EtlHttpDeliveryAttempt>(begun.Permit);
        var completed = await store.CompleteConnectorHttpDeliveryAsync(lease, batchId, route, permit, new(204, null));
        Assert.Equal(EtlStoreStatus.Applied, completed.Status);
        Assert.Equal(EtlHttpDeliveryPhase.Acknowledged, completed.Delivery!.Phase);
    }

    private static EtlConnectorBatchDescriptor HttpAcceptanceBatch(EtlConnectorRunAdmission admission,
        EtlConnectorPreparedRun prepared, EtlConnectorPartitionLease lease, int startItem,
        int endItemExclusive, string marker)
    {
        var position = Assert.IsType<EtlConnectorHttpPosition>(lease.Position);
        var page = Assert.IsType<EtlRetainedHttpPage>(position.Page);
        var range = new EtlConnectorHttpBatchRange(position.PageSequence, page.RequestHash,
            page.Body.ContentHash, startItem, endItemExclusive);
        return ConnectorBatch(admission, prepared, lease, range, inputBytes: 7,
            inputHash: EtlRuntimeRecordCodec.HashText(marker));
    }

    private static EtlConnectorBatchOutput HttpOutput(EtlConnectorRunAdmission admission,
        EtlConnectorBatchDescriptor batch, long accepted, long rejected, long filtered, string suffix)
    {
        var main = accepted == 0 ? null : OutputRoute(admission, batch, EtlOutputRoute.Main,
            EtlConnectorKind.Http, $"{suffix}-main", EtlRuntimeRecordCodec.HashText($"{suffix}-main"), 17, accepted);
        var errors = rejected == 0 ? null : OutputRoute(admission, batch, EtlOutputRoute.Error,
            EtlConnectorKind.Jsonl, $"{suffix}-errors", EtlRuntimeRecordCodec.HashText($"{suffix}-errors"), 19, rejected);
        return new(accepted, rejected, filtered, main, errors);
    }

    private static EtlObjectReference FilePublication(EtlConnectorRouteOutput route) =>
        route.Body with { Key = EtlRuntimeIdentity.ConnectorFileOutputKey(route.EffectId) };

    private static async Task<AcceptanceState> ReadAcceptanceStateAsync(EtlRuntimeDbContext context, int partitionOrdinal)
    {
        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync(row => row.PartitionOrdinal == partitionOrdinal);
        var run = await context.Runs.AsNoTracking().SingleAsync();
        var connector = await context.ConnectorRuns.AsNoTracking().SingleAsync();
        return new(partition.PositionJson, partition.OwedBatchId, partition.Completed, partition.AcceptedBatchCount,
            partition.AcceptedRecords, partition.RejectedRecords, partition.FilteredRecords,
            run.Status, run.AcceptedRecords, run.RejectedRecords, run.FilteredRecords,
            run.OutstandingRecords, run.OutstandingBytes, run.Revision, partition.Revision,
            connector.RetainedMetadataBytes, connector.RetainedOutputBytes);
    }

    private sealed record AcceptanceState(string PositionJson, string? PartitionOwedBatchId, bool Completed,
        long AcceptedBatchCount, long PartitionAcceptedRecords, long PartitionRejectedRecords, long PartitionFilteredRecords,
        int RunStatus, long RunAcceptedRecords, long RunRejectedRecords, long RunFilteredRecords,
        long OutstandingRecords, long OutstandingBytes, long RunRevision, long PartitionRevision,
        long RetainedMetadataBytes, long RetainedOutputBytes);
}
