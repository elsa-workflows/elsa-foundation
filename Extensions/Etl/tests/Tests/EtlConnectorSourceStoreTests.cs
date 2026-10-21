using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Captured_source_progress_capacity_transfers_to_acceptance_without_a_second_charge()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, new MutableTimeProvider());
        var admission = ConnectorAdmission("source-transfer", new("tenant", "source-transfer"), httpShardIds: ["a"]);
        var initial = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var page = RetainedPage(initial, "source-page", 5000, 2, new string('c', 4000));
        var attached = await store.AttachTestHttpPageAsync(initial, page);
        var lease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
        var source = await context.ConnectorHttpRequests.AsNoTracking().SingleAsync();
        Assert.True(source.SourceMetadataReserveBytes > 0);
        var before = (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes;
        var prepared = (await store.FindConnectorRunAsync(lease.Scope, lease.RunId))!.Prepared!;
        var batch = HttpAcceptanceBatch(admission, prepared, lease, 0, 2, "source-transfer");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var reserved = await context.ConnectorBatches.AsNoTracking().SingleAsync();
        Assert.Equal(source.SourceMetadataReserveBytes, reserved.AcceptanceMetadataBytes);
        Assert.Equal(0, (await context.ConnectorHttpRequests.AsNoTracking().SingleAsync()).SourceMetadataReserveBytes);
        Assert.Equal(before + System.Text.Encoding.UTF8.GetByteCount(EtlConnectorRecordCodec.Write(batch)),
            (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes);
        Assert.Equal(EtlStoreStatus.Applied,
            (await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, new(0, 0, 2, null, null))).Status);
        var accepted = await store.AcceptConnectorBatchAsync(lease, batch.BatchId);
        Assert.Equal(EtlStoreStatus.Applied, accepted.Status);
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(accepted.Lease!));
        var recovered = await store.ClaimConnectorPartitionAsync(lease.Scope, lease.RunId, "replacement");
        Assert.Equal(1, Assert.IsType<EtlConnectorHttpPosition>(recovered!.Position).PageSequence);
    }

    [Theory]
    [InlineData("ascii")]
    [InlineData("escaped-base64")]
    [InlineData("surrogate-pairs")]
    public async Task Source_reservation_covers_large_opaque_continuations(string shape)
    {
        var continuation = shape switch
        {
            "ascii" => new string('~', 65_536),
            "escaped-base64" => string.Concat(Enumerable.Repeat("\uEFFB\uFBBE\uBEEF", 7_281)),
            _ => string.Concat(Enumerable.Repeat("\U0001F680", 16_384))
        };
        var baseline = EtlV2TestPipelines.Create();
        var pipeline = baseline with { Http = baseline.Http with { MaxContinuationBytes = 65_536 } };
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, new MutableTimeProvider());
        var admission = ConnectorAdmission("source-opaque", new("tenant", "source-opaque"), pipeline, ["a"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var begin = await store.BeginConnectorHttpSourceAsync(lease, Guid.NewGuid().ToString("N"));
        Assert.Equal(EtlStoreStatus.Applied, begin.Status);
        var reserved = (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes;
        var page = RetainedPage(lease, new string('z', 128), 300_000, 0, continuation);
        var captured = await store.CaptureConnectorHttpSourceAsync(lease, begin.Permit!, page, begin.Permit!.StartedAt);
        Assert.Equal(EtlStoreStatus.Applied, captured.Status);
        Assert.True((await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes <= reserved);
        var attached = await store.AttachHttpPageAsync(lease, page);
        Assert.Equal(EtlStoreStatus.Applied, attached.Status);
        var advanced = await store.AdvanceEmptyHttpPageAsync(attached.Lease!);
        Assert.Equal(EtlStoreStatus.Applied, advanced.Status);
        Assert.Equal(continuation, Assert.IsType<EtlConnectorHttpPosition>(advanced.Lease!.Position).Request.Continuation);
        Assert.True((await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes <= reserved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_dispatch_budget_is_checked_before_returning_a_permit(bool exactFit)
    {
        const string requestId = "00000000000000000000000000000001";
        var baseline = EtlV2TestPipelines.Create();
        var pipeline = baseline with { Http = baseline.Http with { MaxContinuationBytes = 64, MaxPageBytes = 128 } };
        long limit;
        await using (var measurement = await TestDatabase.CreateAsync())
        {
            await using var db = measurement.CreateContext();
            var measuringStore = (IEtlConnectorRuntimeStore)measurement.CreateStore(db, new MutableTimeProvider());
            var measuringAdmission = ConnectorAdmission("source-budget", new("tenant", "source-budget"), pipeline, ["a"]);
            var measuringLease = Assert.Single(await StartHttpRunAsync(measuringStore, measuringAdmission, "worker"));
            Assert.Equal(EtlStoreStatus.Applied, (await measuringStore.BeginConnectorHttpSourceAsync(measuringLease, requestId)).Status);
            limit = (await db.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes;
        }
        pipeline = pipeline with { Http = pipeline.Http with { MaxRetainedMetadataBytes = limit - (exactFit ? 0 : 1) } };
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var clock = new MutableTimeProvider();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var admission = ConnectorAdmission("source-budget", new("tenant", "source-budget"), pipeline, ["a"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var before = (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes;
        var begin = await store.BeginConnectorHttpSourceAsync(lease, requestId);
        if (!exactFit)
        {
            Assert.Equal(EtlStoreStatus.BudgetExceeded, begin.Status);
            Assert.Null(begin.Permit);
            Assert.Null((await context.ConnectorHttpRequests.AsNoTracking().SingleAsync()).FetchJson);
            var unchanged = await context.ConnectorRuns.AsNoTracking().SingleAsync();
            Assert.Equal(before, unchanged.RetainedMetadataBytes);
            Assert.Equal(0, unchanged.SourceInputReservedBytes);
            return;
        }
        Assert.Equal(EtlStoreStatus.Applied, begin.Status);
        Assert.NotNull(begin.Permit);
        Assert.Equal(limit, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes);
        var outcome = await store.CompleteConnectorHttpSourceAsync(lease, begin.Permit, new(429, clock.GetUtcNow().AddSeconds(20), false));
        Assert.Equal(EtlStoreStatus.Applied, outcome.Status);
        Assert.Equal(EtlConnectorHttpSourceFetchPhase.Waiting, outcome.Fetch!.Phase);
        Assert.True((await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes <= limit);
    }

    [Fact]
    public async Task Source_permit_reserves_capacity_before_dispatch_and_capture_is_required_for_attachment()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var clock = new MutableTimeProvider();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var admission = ConnectorAdmission("source-capture", new("tenant-a", "source-capture"), httpShardIds: ["shard-a"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var page = RetainedPage(lease, "page", 31, 0, null);
        Assert.Equal(EtlStoreStatus.Conflict, (await store.AttachHttpPageAsync(lease, page)).Status);
        var begin = await store.BeginConnectorHttpSourceAsync(lease, Guid.NewGuid().ToString("N"));
        Assert.Equal(EtlStoreStatus.Applied, begin.Status);
        Assert.NotNull(begin.Permit);
        var allocated = await context.ConnectorHttpRequests.AsNoTracking().SingleAsync();
        Assert.Equal(EtlV2TestPipelines.Create().Http.MaxPageBytes, allocated.InputReservationBytes);
        Assert.True(allocated.SourceMetadataReserveBytes > 0);
        var capture = await store.CaptureConnectorHttpSourceAsync(lease, begin.Permit, page, begin.Permit.StartedAt);
        Assert.Equal(EtlStoreStatus.Applied, capture.Status);
        var attached = await store.AttachHttpPageAsync(lease, page);
        Assert.Equal(EtlStoreStatus.Applied, attached.Status);
        Assert.Equal(EtlStoreStatus.Replay, (await store.AttachHttpPageAsync(lease, page)).Status);
        var retained = await context.ConnectorRuns.AsNoTracking().SingleAsync();
        Assert.Equal(31, retained.RetainedInputBytes);
        Assert.Equal(0, retained.SourceInputReservedBytes);
        var complete = await store.AdvanceEmptyHttpPageAsync(attached.Lease!);
        Assert.Equal(EtlStoreStatus.Applied, complete.Status);
    }

    [Fact]
    public async Task Source_waiting_is_durable_and_does_not_block_another_partition()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var clock = new MutableTimeProvider();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var admission = ConnectorAdmission("source-wait", new("tenant-a", "source-wait"), httpShardIds: ["a", "b"]);
        var leases = await StartHttpRunAsync(store, admission, "worker");
        var first = leases[0];
        var begin = await store.BeginConnectorHttpSourceAsync(first, Guid.NewGuid().ToString("N"));
        var wait = await store.CompleteConnectorHttpSourceAsync(first, begin.Permit!, new(429, clock.GetUtcNow().AddSeconds(20), false));
        Assert.Equal(EtlConnectorHttpSourceFetchPhase.Waiting, wait.Fetch!.Phase);
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(first));
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(leases[1]));
        var other = await store.ClaimConnectorPartitionAsync(first.Scope, first.RunId, "replacement");
        Assert.Equal(leases[1].PartitionOrdinal, other!.PartitionOrdinal);
        clock.Advance(TimeSpan.FromSeconds(21));
        var recovered = await store.ClaimConnectorPartitionAsync(first.Scope, first.RunId, "replacement");
        Assert.Equal(first.PartitionOrdinal, recovered!.PartitionOrdinal);
        var retry = await store.BeginConnectorHttpSourceAsync(recovered, Guid.NewGuid().ToString("N"));
        Assert.Equal(2, retry.Permit!.Number);
        Assert.Equal(begin.Fetch!.Deadline, retry.Fetch!.Deadline);
    }
}
