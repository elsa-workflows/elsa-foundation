using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Http_batch_reservation_is_stable_replayable_and_owned_after_reacquisition()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context);
        var store = (IEtlConnectorRuntimeStore)runtime;
        var admission = ConnectorAdmission("connector-http-reservation", new("tenant-a", "http-reservation"), httpShardIds: ["shard-a"]);
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, "worker-a"));
        var attached = await store.AttachHttpPageAsync(initialLease,
            RetainedPage(initialLease, "page-0", byteLength: 31, recordCount: 2, nextContinuation: "cursor-1"));
        var lease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = HttpBatch(admission, run.Prepared!, lease, endItemExclusive: 2, inputBytes: 17);
        var metadataBefore = await RetainedMetadataBytesAsync(context);
        var expectedBatchMetadata = Encoding.UTF8.GetByteCount(EtlConnectorRecordCodec.Write(batch));

        var reserved = await store.ReserveConnectorBatchAsync(lease, batch);
        var afterReservation = await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId);
        var metadataAfterReservation = await RetainedMetadataBytesAsync(context);
        var replay = await store.ReserveConnectorBatchAsync(lease, batch);
        var conflict = await store.ReserveConnectorBatchAsync(lease,
            HttpBatch(admission, run.Prepared!, lease, endItemExclusive: 2, inputBytes: 18));
        var successWhileOwed = await runtime.SettleTerminalAsync(new(admission.Invocation.Scope,
            admission.Invocation.RunId, EtlTerminalOutcome.Succeeded, null));

        Assert.Equal(EtlStoreStatus.Applied, attached.Status);
        Assert.Equal(EtlStoreStatus.Applied, reserved.Status);
        Assert.Equal(new EtlConnectorBatchReservation(batch, Settled: false), reserved.Reservation);
        Assert.Equal(EtlStoreStatus.Replay, replay.Status);
        Assert.Equal(reserved.Reservation, replay.Reservation);
        Assert.Equal(EtlStoreStatus.Conflict, conflict.Status);
        Assert.Null(conflict.Reservation);
        Assert.Equal(EtlStoreStatus.Conflict, successWhileOwed.Status);
        Assert.Null(successWhileOwed.Result);
        Assert.Equal(2, afterReservation!.Run.OutstandingRecords);
        Assert.Equal(17, afterReservation.Run.OutstandingBytes);
        Assert.Equal(metadataBefore + expectedBatchMetadata, metadataAfterReservation);
        Assert.Equal(metadataAfterReservation, await RetainedMetadataBytesAsync(context));
        Assert.Single(await context.ConnectorBatches.AsNoTracking().ToListAsync());

        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));
        await using var recoveryContext = database.CreateContext();
        var recoveryStore = (IEtlConnectorRuntimeStore)database.CreateStore(recoveryContext);
        var recovered = Assert.IsType<EtlConnectorPartitionLease>(await recoveryStore.ClaimConnectorPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "worker-b"));

        Assert.True(recovered.Fence > lease.Fence);
        Assert.Equal(lease.Position, recovered.Position);
        Assert.Equal(new EtlConnectorBatchReservation(batch, Settled: false), recovered.OwedBatch);
        Assert.Equal(EtlStoreStatus.LeaseLost, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var recoveredRun = Assert.IsType<EtlConnectorRunView>(await recoveryStore.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var recomputed = HttpBatch(admission, recoveredRun.Prepared!, recovered, endItemExclusive: 2, inputBytes: 17);
        Assert.Equal(batch, recomputed);
        Assert.Equal(EtlStoreStatus.Replay, (await recoveryStore.ReserveConnectorBatchAsync(recovered, recomputed)).Status);
        recoveredRun = Assert.IsType<EtlConnectorRunView>(await recoveryStore.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        Assert.Equal(2, recoveredRun.Run.OutstandingRecords);
        Assert.Equal(17, recoveredRun.Run.OutstandingBytes);
        Assert.Equal(metadataAfterReservation, await RetainedMetadataBytesAsync(recoveryContext));
        Assert.Single(await recoveryContext.ConnectorBatches.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("no-page")]
    [InlineData("wrong-type")]
    [InlineData("wrong-sequence")]
    [InlineData("wrong-request")]
    [InlineData("wrong-content")]
    [InlineData("wrong-start")]
    [InlineData("wrong-end")]
    [InlineData("zero-bytes")]
    [InlineData("invalid-hash")]
    [InlineData("wrong-id")]
    [InlineData("stale-position")]
    public async Task Http_batch_reservation_requires_a_valid_range_at_the_current_page(string defect)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission($"connector-http-reservation-invalid-{defect}",
            new("tenant-a", $"http-reservation-invalid-{defect}"), httpShardIds: ["shard-a"]);
        var originalLease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var lease = originalLease;
        if (defect != "no-page")
        {
            var attached = await store.AttachHttpPageAsync(lease,
                RetainedPage(lease, "page-0", byteLength: 20, recordCount: 2, nextContinuation: "cursor-1"));
            lease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
        }

        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        EtlConnectorBatchRange range = CurrentHttpRange(lease, startItem: 0, endItemExclusive: 1);
        long inputBytes = 7;
        var inputHash = EtlRuntimeRecordCodec.HashText("measured-http-input");
        switch (defect)
        {
            case "wrong-type": range = new EtlConnectorFileBatchRange(1, 2); break;
            case "wrong-sequence": range = Assert.IsType<EtlConnectorHttpBatchRange>(range) with { PageSequence = 1 }; break;
            case "wrong-request": range = Assert.IsType<EtlConnectorHttpBatchRange>(range) with { RequestHash = new string('b', 64) }; break;
            case "wrong-content": range = Assert.IsType<EtlConnectorHttpBatchRange>(range) with { PageContentHash = new string('b', 64) }; break;
            case "wrong-start": range = Assert.IsType<EtlConnectorHttpBatchRange>(range) with { StartItem = 1, EndItemExclusive = 2 }; break;
            case "wrong-end": range = Assert.IsType<EtlConnectorHttpBatchRange>(range) with { EndItemExclusive = 3 }; break;
            case "zero-bytes": inputBytes = 0; break;
            case "invalid-hash": inputHash = "not-sha256"; break;
        }
        var batch = ConnectorBatch(admission, run.Prepared!, lease, range, inputBytes, inputHash);
        if (defect == "wrong-id") batch = batch with { BatchId = new string('c', 64) };
        var metadataBefore = await RetainedMetadataBytesAsync(context);
        var targetLease = defect == "stale-position" ? originalLease : lease;

        var result = await store.ReserveConnectorBatchAsync(targetLease, batch);
        var runAfter = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync();

        Assert.Equal(defect == "stale-position" ? EtlStoreStatus.Conflict : EtlStoreStatus.Invalid, result.Status);
        Assert.Null(result.Reservation);
        Assert.Equal(0, runAfter.Run.OutstandingRecords);
        Assert.Equal(0, runAfter.Run.OutstandingBytes);
        Assert.Equal(metadataBefore, await RetainedMetadataBytesAsync(context));
        Assert.Empty(await context.ConnectorBatches.AsNoTracking().ToListAsync());
        Assert.Null(partition.OwedBatchId);
        Assert.Equal(0, partition.BatchMetadataBytes);
    }

    [Theory]
    [InlineData("records")]
    [InlineData("bytes")]
    public async Task Http_batch_reservation_enforces_run_wide_outstanding_quotas(string quota)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with
        {
            Limits = defaults.Limits with
            {
                MaxBatchRecords = quota == "records" ? 1 : 10,
                MaxBatchBytes = quota == "bytes" ? 7 : 100,
                MaxOutstandingRecords = quota == "records" ? 1 : 10,
                MaxOutstandingBytes = quota == "bytes" ? 7 : 100
            }
        };
        var admission = ConnectorAdmission($"connector-http-quota-{quota}", new("tenant-a", $"http-quota-{quota}"), pipeline, ["shard-a", "shard-b"]);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var initialLeases = await StartHttpRunAsync(store, admission, "worker");
        var leases = new List<EtlConnectorPartitionLease>();
        foreach (var initial in initialLeases)
        {
            var attached = await store.AttachHttpPageAsync(initial, RetainedPage(initial, $"{initial.PartitionOrdinal}-page", 12, 1));
            leases.Add(Assert.IsType<EtlConnectorPartitionLease>(attached.Lease));
        }
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var first = HttpBatch(admission, run.Prepared!, leases[0], endItemExclusive: 1, inputBytes: 4);
        var second = HttpBatch(admission, run.Prepared!, leases[1], endItemExclusive: 1, inputBytes: 4);

        var firstResult = await store.ReserveConnectorBatchAsync(leases[0], first);
        var metadataAfterFirst = await RetainedMetadataBytesAsync(context);
        var secondResult = await store.ReserveConnectorBatchAsync(leases[1], second);
        var current = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));

        Assert.Equal(EtlStoreStatus.Applied, firstResult.Status);
        Assert.Equal(EtlStoreStatus.BudgetExceeded, secondResult.Status);
        Assert.Null(secondResult.Reservation);
        Assert.Equal(1, current.Run.OutstandingRecords);
        Assert.Equal(4, current.Run.OutstandingBytes);
        Assert.Equal(metadataAfterFirst, await RetainedMetadataBytesAsync(context));
        Assert.Single(await context.ConnectorBatches.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Http_batch_metadata_budget_refusal_does_not_persist_the_reservation()
    {
        var (metadataLimit, expectedBaseline, expectedDescriptorBytes) = await MeasureHttpBatchMetadataLimitAsync();
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with { Http = defaults.Http with { MaxRetainedMetadataBytes = metadataLimit } };
        var admission = ConnectorAdmission("connector-http-metadata-limit", new("tenant-a", "http-metadata-limit"), pipeline, ["shard-a"]);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        lease = Assert.IsType<EtlConnectorPartitionLease>((await store.AttachHttpPageAsync(lease,
            RetainedPage(lease, "page-0", 9, 1))).Lease);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = HttpBatch(admission, run.Prepared!, lease, endItemExclusive: 1, inputBytes: 5);
        var before = await RetainedMetadataBytesAsync(context);
        var batchMetadataBytes = Encoding.UTF8.GetByteCount(EtlConnectorRecordCodec.Write(batch));

        var result = await store.ReserveConnectorBatchAsync(lease, batch);
        var current = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync();

        Assert.Equal(expectedBaseline, before);
        Assert.Equal(expectedDescriptorBytes, batchMetadataBytes);
        Assert.Equal(metadataLimit, before + batchMetadataBytes - 1);
        Assert.Equal(EtlStoreStatus.BudgetExceeded, result.Status);
        Assert.Null(result.Reservation);
        Assert.Equal(0, current.Run.OutstandingRecords);
        Assert.Equal(0, current.Run.OutstandingBytes);
        Assert.Equal(before, await RetainedMetadataBytesAsync(context));
        Assert.Empty(await context.ConnectorBatches.AsNoTracking().ToListAsync());
        Assert.Null(partition.OwedBatchId);
    }

    [Fact]
    public async Task Http_batch_reservation_expiry_after_save_rolls_back_the_reservation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var expire = new ConnectorExpireAfterSave(clock);
        await using var context = database.CreateContext(expire);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock, partitionLease: TimeSpan.FromSeconds(30));
        var admission = ConnectorAdmission("connector-http-reservation-expiry", new("tenant-a", "http-reservation-expiry"), httpShardIds: ["shard-a"]);
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var lease = Assert.IsType<EtlConnectorPartitionLease>((await store.AttachHttpPageAsync(initialLease,
            RetainedPage(initialLease, "page-0", 9, 1))).Lease);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = HttpBatch(admission, run.Prepared!, lease, endItemExclusive: 1, inputBytes: 5);
        var metadataBefore = await RetainedMetadataBytesAsync(context);

        expire.Armed = true;
        var result = await store.ReserveConnectorBatchAsync(lease, batch);
        context.ChangeTracker.Clear();
        var current = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync();

        Assert.Equal(EtlStoreStatus.LeaseLost, result.Status);
        Assert.Null(result.Reservation);
        Assert.Equal(0, current.Run.OutstandingRecords);
        Assert.Equal(0, current.Run.OutstandingBytes);
        Assert.Equal(metadataBefore, await RetainedMetadataBytesAsync(context));
        Assert.Empty(await context.ConnectorBatches.AsNoTracking().ToListAsync());
        Assert.Null(partition.OwedBatchId);
        Assert.Equal(0, partition.BatchMetadataBytes);
        Assert.Equal(EtlConnectorRecordCodec.Write(lease.Position), partition.PositionJson);
    }

    [Fact]
    public async Task Cancellation_preserves_an_owed_http_batch_and_refuses_further_reservations()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context);
        var store = (IEtlConnectorRuntimeStore)runtime;
        var admission = ConnectorAdmission("connector-http-reservation-cancel", new("tenant-a", "http-reservation-cancel"), httpShardIds: ["shard-a"]);
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var lease = Assert.IsType<EtlConnectorPartitionLease>((await store.AttachHttpPageAsync(initialLease,
            RetainedPage(initialLease, "page-0", 9, 2))).Lease);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var owed = HttpBatch(admission, run.Prepared!, lease, endItemExclusive: 2, inputBytes: 13);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, owed)).Status);
        var metadataBeforeTerminal = await RetainedMetadataBytesAsync(context);
        var success = await runtime.SettleTerminalAsync(new(admission.Invocation.Scope, admission.Invocation.RunId, EtlTerminalOutcome.Succeeded, null));

        var cancelled = await runtime.CancelAsync(admission.Invocation);
        var newBatch = HttpBatch(admission, run.Prepared!, lease, endItemExclusive: 1, inputBytes: 6);
        var refused = await store.ReserveConnectorBatchAsync(lease, newBatch);
        var current = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync();

        Assert.Equal(EtlStoreStatus.Conflict, success.Status);
        Assert.Equal(EtlStoreStatus.Applied, cancelled.Status);
        Assert.Equal(EtlStoreStatus.Terminal, refused.Status);
        Assert.Null(refused.Reservation);
        Assert.Equal(EtlRunStatus.Cancelled, current.Run.Status);
        Assert.Equal(2, current.Run.OutstandingRecords);
        Assert.Equal(13, current.Run.OutstandingBytes);
        Assert.Equal(owed.BatchId, partition.OwedBatchId);
        Assert.Equal(1, await context.ConnectorBatches.AsNoTracking().CountAsync());
        Assert.Equal(metadataBeforeTerminal, await RetainedMetadataBytesAsync(context));
    }

    [Fact]
    public async Task Csv_v2_file_range_can_be_reserved_and_recovered_as_the_same_owed_batch()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var pipeline = CsvMixedRoutePipeline();
        var admission = ConnectorAdmission("connector-csv-reservation", new("tenant-a", "csv-reservation"), pipeline);
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitConnectorAsync(admission)).Status);
        var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimConnectorPreparationAsync(admission.Invocation.Scope, "preparer"));
        Assert.Equal(EtlStoreStatus.Applied, await store.CommitConnectorPreparationAsync(preparation, ConnectorPreparedFile(recordCount: 4)));
        var lease = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "worker-a"));
        var position = Assert.IsType<EtlConnectorFilePosition>(lease.Position);
        var range = new EtlConnectorFileBatchRange(position.NextOrdinal, position.NextOrdinal + 2);
        var inputHash = EtlRuntimeRecordCodec.HashText("canonical-csv-input");
        var prepared = (await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId))!.Prepared!;
        var batch = ConnectorBatch(admission, prepared, lease, range, 11, inputHash);

        var reserved = await store.ReserveConnectorBatchAsync(lease, batch);

        Assert.Equal(EtlStoreStatus.Applied, reserved.Status);
        Assert.Equal(new EtlConnectorBatchReservation(batch, Settled: false), reserved.Reservation);
        var runAfterReservation = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        Assert.Equal(2, runAfterReservation.Run.OutstandingRecords);
        Assert.Equal(11, runAfterReservation.Run.OutstandingBytes);
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));

        await using var recoveryContext = database.CreateContext();
        var recoveryStore = (IEtlConnectorRuntimeStore)database.CreateStore(recoveryContext);
        var recovered = Assert.IsType<EtlConnectorPartitionLease>(await recoveryStore.ClaimConnectorPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "worker-b"));

        Assert.True(recovered.Fence > lease.Fence);
        Assert.Equal(position, recovered.Position);
        Assert.Equal(new EtlConnectorBatchReservation(batch, Settled: false), recovered.OwedBatch);
        var recoveredRun = Assert.IsType<EtlConnectorRunView>(await recoveryStore.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var recomputed = ConnectorBatch(admission, recoveredRun.Prepared!, recovered, range, 11, inputHash);
        Assert.Equal(batch, recomputed);
        Assert.Equal(EtlStoreStatus.Replay, (await recoveryStore.ReserveConnectorBatchAsync(recovered, recomputed)).Status);
        Assert.Single(await recoveryContext.ConnectorBatches.AsNoTracking().ToListAsync());
    }

    private static EtlConnectorBatchDescriptor HttpBatch(EtlConnectorRunAdmission admission, EtlConnectorPreparedRun prepared,
        EtlConnectorPartitionLease lease, int endItemExclusive, long inputBytes, string? marker = null)
    {
        var range = CurrentHttpRange(lease, 0, endItemExclusive);
        return ConnectorBatch(admission, prepared, lease, range, inputBytes,
            EtlRuntimeRecordCodec.HashText(marker ?? "canonical-http-input"));
    }

    private static EtlConnectorHttpBatchRange CurrentHttpRange(EtlConnectorPartitionLease lease,
        int startItem, int endItemExclusive)
    {
        var position = Assert.IsType<EtlConnectorHttpPosition>(lease.Position);
        var page = position.Page;
        return new(position.PageSequence, page?.RequestHash ?? EtlRuntimeIdentity.HttpRequestHash(position.Request),
            page?.Body.ContentHash ?? EtlRuntimeFixtures.Hash, startItem, endItemExclusive);
    }

    private static EtlConnectorBatchDescriptor ConnectorBatch(EtlConnectorRunAdmission admission,
        EtlConnectorPreparedRun prepared, EtlConnectorPartitionLease lease, EtlConnectorBatchRange range,
        long inputBytes, string inputHash)
    {
        var id = EtlRuntimeIdentity.ConnectorBatchId(admission, prepared, lease.PartitionOrdinal, range, inputBytes, inputHash);
        return new(id, range, inputBytes, inputHash);
    }

    private static async Task<long> RetainedMetadataBytesAsync(EtlRuntimeDbContext context) =>
        (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes;

    private static async Task<(long Limit, long Baseline, int DescriptorBytes)> MeasureHttpBatchMetadataLimitAsync()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("connector-http-metadata-limit", new("tenant-a", "http-metadata-limit"), httpShardIds: ["shard-a"]);
        var initialLease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var lease = Assert.IsType<EtlConnectorPartitionLease>((await store.AttachHttpPageAsync(initialLease,
            RetainedPage(initialLease, "page-0", 9, 1))).Lease);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var batch = HttpBatch(admission, run.Prepared!, lease, endItemExclusive: 1, inputBytes: 5);
        var baseline = await RetainedMetadataBytesAsync(context);
        var descriptorBytes = Encoding.UTF8.GetByteCount(EtlConnectorRecordCodec.Write(batch));
        return (checked(baseline + descriptorBytes - 1), baseline, descriptorBytes);
    }
}
