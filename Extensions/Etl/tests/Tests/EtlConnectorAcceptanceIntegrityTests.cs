using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Published_file_route_allows_remaining_http_retry_to_defer_its_partition()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var clock = new MutableTimeProvider();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var admission = ConnectorAdmission("publication-retry", new("tenant", "publication-retry"), CsvMixedRoutePipeline());
        var lease = (await StartOutputCsvRunAsync(store, admission))[0];
        var run = (await store.FindConnectorRunAsync(lease.Scope, lease.RunId))!;
        var batch = OutputTestBatch(admission, run.Prepared!, lease);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = CreateOutput(admission, batch, 1, 1, 0, "publication-retry");
        Assert.Equal(EtlStoreStatus.Applied, (await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output)).Status);
        var started = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(1));
        var delayed = await store.CompleteConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main, started.Permit!,
            new(429, clock.GetUtcNow().AddSeconds(5)));
        Assert.Equal(EtlStoreStatus.Applied, delayed.Status);
        Assert.Null(await context.ConnectorPartitions.Where(row => row.PartitionOrdinal == lease.PartitionOrdinal)
            .Select(row => row.NotBeforeUtcTicks).SingleAsync());

        var publication = output.Errors!.Body with { Key = EtlRuntimeIdentity.ConnectorFileOutputKey(output.Errors.EffectId) };
        Assert.Equal(EtlStoreStatus.Applied, await store.RecordConnectorFilePublicationAsync(lease, batch.BatchId, EtlOutputRoute.Error, publication));
        Assert.Equal(delayed.Delivery!.NotBefore!.Value.UtcTicks,
            await context.ConnectorPartitions.Where(row => row.PartitionOrdinal == lease.PartitionOrdinal)
                .Select(row => row.NotBeforeUtcTicks).SingleAsync());
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));
        Assert.Null(await store.ClaimConnectorPartitionAsync(lease.Scope, lease.RunId, "early"));
        clock.Advance(TimeSpan.FromSeconds(5));
        var replacement = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(lease.Scope, lease.RunId, "due"));
        Assert.True(replacement.OwedBatch!.FilePublications.Errors);
        Assert.Equal(EtlHttpDeliveryPhase.Waiting, replacement.OwedBatch.HttpDeliveries!.Main!.Phase);
    }

    [Theory]
    [InlineData("ordinal")]
    [InlineData("page-sequence")]
    [InlineData("counts")]
    [InlineData("receipt")]
    [InlineData("page-count")]
    public async Task Accepted_connector_history_corruption_cannot_be_claimed(string defect)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("accepted-history", new("tenant", "accepted-history"), httpShardIds: ["shard"]);
        var initial = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var attached = await store.AttachTestHttpPageAsync(initial, RetainedPage(initial, "history-page", 20, 2, "next"));
        var lease = attached.Lease!;
        var run = (await store.FindConnectorRunAsync(lease.Scope, lease.RunId))!;
        var batch = HttpBatch(admission, run.Prepared!, lease, 1, 5);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var output = CreateOutput(admission, batch, 1, 0, 0, "accepted");
        Assert.Equal(EtlStoreStatus.Applied, (await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, output)).Status);
        var attempt = await store.BeginConnectorHttpDeliveryAsync(lease, batch.BatchId, EtlOutputRoute.Main, DeliveryRequestId(1));
        Assert.Equal(EtlStoreStatus.Applied, (await store.CompleteConnectorHttpDeliveryAsync(lease, batch.BatchId,
            EtlOutputRoute.Main, attempt.Permit!, new(200, null))).Status);
        var accepted = await store.AcceptConnectorBatchAsync(lease, batch.BatchId);
        Assert.Equal(EtlStoreStatus.Applied, accepted.Status);
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(accepted.Lease!));
        var retained = await context.ConnectorBatches.SingleAsync();
        var partition = await context.ConnectorPartitions.SingleAsync();
        switch (defect)
        {
            case "ordinal": retained.BatchOrdinal = 1; break;
            case "page-sequence": retained.HttpPageSequence = 1; break;
            case "counts": partition.AcceptedRecords++; break;
            case "page-count": (await context.ConnectorHttpPages.SingleAsync()).AcceptedItemCount = 0; break;
            case "receipt":
                var delta = -System.Text.Encoding.UTF8.GetByteCount(retained.HttpDeliveryJson!);
                retained.HttpDeliveryJson = null;
                retained.HttpDeliveryDigest = null;
                retained.MetadataBytes += delta;
                partition.BatchMetadataBytes += delta;
                partition.MetadataBytes += delta;
                (await context.ConnectorRuns.SingleAsync()).RetainedMetadataBytes += delta;
                break;
        }
        await context.SaveChangesAsync();
        await using var recovery = database.CreateContext();
        var replacement = (IEtlConnectorRuntimeStore)database.CreateStore(recovery);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await replacement.ClaimConnectorPartitionAsync(lease.Scope, lease.RunId, "replacement"));
        Assert.Empty(await recovery.Terminals.ToListAsync());
    }
}
