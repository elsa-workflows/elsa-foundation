using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public void Connector_batch_identity_binds_source_range_scope_and_destination_revision()
    {
        var admission = ConnectorAdmission("batch-identity", new("tenant-a", "partition-a"), httpShardIds: ["shard"]);
        var prepared = new EtlConnectorPreparedRun(null, null);
        var range = new EtlConnectorHttpBatchRange(0, new string('a', 64), new string('b', 64), 0, 2);
        var hash = new string('c', 64);
        string Identity(EtlConnectorRunAdmission value, EtlConnectorBatchRange input) =>
            EtlRuntimeIdentity.ConnectorBatchId(value, prepared, 0, input, 20, hash);
        var identity = Identity(admission, range);
        Assert.Equal(identity, Identity(admission, range with { }));
        Assert.NotEqual(identity, Identity(admission, range with { PageContentHash = new string('d', 64) }));
        Assert.NotEqual(identity, Identity(admission, range with { StartItem = 1 }));
        Assert.NotEqual(identity, Identity(admission, new EtlConnectorFileBatchRange(0, 2)));
        Assert.NotEqual(identity, Identity(admission with
        {
            Invocation = admission.Invocation with { Scope = new("tenant-b", "partition-a") }
        }, range));
        Assert.NotEqual(identity, Identity(admission with
        {
            Bindings = admission.Bindings with { Main = admission.Bindings.Main with
            {
                Revision = admission.Bindings.Main.Revision with { Revision = "replacement" }
            } }
        }, range));
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("ar-EG");
            Assert.Equal(identity, Identity(admission, range));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }

    [Theory]
    [InlineData("descriptor")]
    [InlineData("count")]
    [InlineData("metadata")]
    [InlineData("unlinked")]
    [InlineData("settled")]
    [InlineData("position")]
    public async Task Connector_batch_recovery_refuses_corrupt_or_unlinked_responsibility(string defect)
    {
        await using var database = await TestDatabase.CreateAsync();
        var admission = ConnectorAdmission("batch-corrupt", new("tenant-a", "batch-corrupt"), httpShardIds: ["shard"]);
        await using (var context = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
            var initial = Assert.Single(await StartHttpRunAsync(store, admission, "original"));
            var lease = Assert.IsType<EtlConnectorPartitionLease>((await store.AttachHttpPageAsync(initial,
                RetainedPage(initial, "page", 12, 2))).Lease);
            var view = (await store.FindConnectorRunAsync(lease.Scope, lease.RunId))!;
            var batch = HttpBatch(admission, view.Prepared!, lease, 1, 5);
            Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
            Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));
            var retained = await context.ConnectorBatches.SingleAsync();
            var partition = await context.ConnectorPartitions.SingleAsync();
            switch (defect)
            {
                case "descriptor":
                    retained.DescriptorJson = EtlConnectorRecordCodec.Write(batch with { InputBytes = 6 });
                    break;
                case "count": retained.RecordCount++; break;
                case "metadata": retained.MetadataBytes++; break;
                case "settled": retained.Settled = true; break;
                case "unlinked":
                    partition.OwedBatchId = null;
                    var run = await context.Runs.SingleAsync();
                    run.OutstandingRecords = 0;
                    run.OutstandingBytes = 0;
                    break;
                case "position":
                    partition.PositionJson = EtlConnectorRecordCodec.Write<EtlConnectorPosition>(
                        Assert.IsType<EtlConnectorHttpPosition>(lease.Position) with { NextItem = 1 });
                    break;
            }
            await context.SaveChangesAsync();
        }
        await using var replacement = database.CreateContext();
        var replacementStore = (IEtlConnectorRuntimeStore)database.CreateStore(replacement);
        await Assert.ThrowsAsync<InvalidDataException>(() => replacementStore.ClaimConnectorPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "replacement").AsTask());
        var partitionAfter = await replacement.ConnectorPartitions.AsNoTracking().SingleAsync();
        Assert.Null(partitionAfter.Owner);
        Assert.Null(partitionAfter.ExpiresAtUtcTicks);
    }

    [Fact]
    public async Task Connector_batch_replay_requires_unexpired_authority_without_refunding_owed_work()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock, partitionLease: TimeSpan.FromSeconds(30));
        var admission = ConnectorAdmission("batch-expired-replay", new("tenant-a", "batch-expired-replay"), httpShardIds: ["shard"]);
        var initial = Assert.Single(await StartHttpRunAsync(store, admission, "original"));
        var lease = Assert.IsType<EtlConnectorPartitionLease>((await store.AttachHttpPageAsync(initial,
            RetainedPage(initial, "page", 10, 1))).Lease);
        var run = (await store.FindConnectorRunAsync(lease.Scope, lease.RunId))!;
        var batch = HttpBatch(admission, run.Prepared!, lease, 1, 5);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
        var metadata = await RetainedMetadataBytesAsync(context);
        clock.Advance(TimeSpan.FromSeconds(31));

        var replay = await store.ReserveConnectorBatchAsync(lease, batch);
        Assert.Equal(EtlStoreStatus.LeaseLost, replay.Status);
        Assert.Null(replay.Reservation);
        var after = (await store.FindConnectorRunAsync(lease.Scope, lease.RunId))!;
        Assert.Equal(1, after.Run.OutstandingRecords);
        Assert.Equal(5, after.Run.OutstandingBytes);
        Assert.Equal(metadata, await RetainedMetadataBytesAsync(context));
        var recovered = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(lease.Scope, lease.RunId, "replacement"));
        Assert.Equal(batch, recovered.OwedBatch!.Batch);
        Assert.Equal(EtlStoreStatus.Replay, (await store.ReserveConnectorBatchAsync(recovered, batch)).Status);
    }

    [Fact]
    public async Task Connector_claim_prioritizes_recovering_owed_work_over_new_partitions()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("owed-first", new("tenant-a", "owed-first"), httpShardIds: ["first", "second"]);
        var leases = await StartHttpRunAsync(store, admission, "original");
        var second = leases.Single(lease => lease.PartitionOrdinal == 1);
        second = Assert.IsType<EtlConnectorPartitionLease>((await store.AttachHttpPageAsync(second,
            RetainedPage(second, "page", 10, 1))).Lease);
        var run = (await store.FindConnectorRunAsync(second.Scope, second.RunId))!;
        var batch = HttpBatch(admission, run.Prepared!, second, 1, 5);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(second, batch)).Status);
        foreach (var lease in leases) Assert.Equal(EtlStoreStatus.Applied,
            await store.ReleaseConnectorPartitionAsync(lease.PartitionOrdinal == 1 ? second : lease));

        var recovered = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(second.Scope, null, "replacement"));
        Assert.Equal(1, recovered.PartitionOrdinal);
        Assert.Equal(batch, recovered.OwedBatch!.Batch);
        Assert.Equal(EtlStoreStatus.Replay, (await store.ReserveConnectorBatchAsync(recovered, batch)).Status);
    }
}
