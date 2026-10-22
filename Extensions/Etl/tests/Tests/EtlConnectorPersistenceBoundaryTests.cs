using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Connector_and_file_capabilities_share_the_same_scoped_provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEtlRuntimeEntityFrameworkCore(new EtlRuntimeEntityFrameworkCoreOptions { ConnectionString = "Data Source=:memory:" });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        Assert.Same(scope.ServiceProvider.GetRequiredService<IEtlRuntimeStore>(), scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>());
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("claim")]
    [InlineData("renew")]
    [InlineData("release")]
    public async Task Connector_transition_expiring_during_save_rolls_back(string transition)
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var expire = new ConnectorExpireAfterSave(clock);
        await using var context = database.CreateContext(expire);
        var store = database.CreateStore(context, clock);
        var admission = ConnectorAdmission("boundary", new EtlScope("tenant-a", "boundary"), httpShardIds: ["shard-a"]);
        var scope = admission.Invocation.Scope;
        var runId = admission.Invocation.RunId;
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitConnectorAsync(admission)).Status);
        var prep = Assert.IsType<EtlPreparationLease>(await store.ClaimConnectorPreparationAsync(scope, "worker"));
        if (transition == "prepare")
        {
            expire.Armed = true;
            Assert.Equal(EtlStoreStatus.LeaseLost, await store.CommitConnectorPreparationAsync(prep, new(null, null)));
            var retained = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(scope, runId));
            Assert.Equal(EtlRunStatus.Preparing, retained.Run.Status);
            Assert.Null(retained.Prepared);
            Assert.Empty(await context.ConnectorPartitions.AsNoTracking().ToListAsync());
            return;
        }
        Assert.Equal(EtlStoreStatus.Applied, await store.CommitConnectorPreparationAsync(prep, new(null, null)));
        if (transition == "claim")
        {
            expire.Armed = true;
            Assert.Null(await store.ClaimConnectorPartitionAsync(scope, runId, "worker"));
            context.ChangeTracker.Clear();
            Assert.All(await context.ConnectorPartitions.ToListAsync(), row => { Assert.Null(row.Owner); Assert.Equal(0, row.Fence); });
            return;
        }
        var lease = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(scope, runId, "worker"));
        expire.Armed = true;
        var status = transition == "renew" ? await store.RenewConnectorPartitionAsync(lease) : await store.ReleaseConnectorPartitionAsync(lease);
        Assert.Equal(EtlStoreStatus.LeaseLost, status);
        context.ChangeTracker.Clear();
        var row = await context.ConnectorPartitions.SingleAsync(x => x.PartitionOrdinal == lease.PartitionOrdinal);
        Assert.Equal(lease.Fence, row.Fence);
        Assert.Equal(lease.ExpiresAt.UtcTicks, row.ExpiresAtUtcTicks);
        Assert.NotNull(row.Owner);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("completed")]
    [InlineData("missing")]
    [InlineData("null-request")]
    public async Task Connector_corrupt_partition_cannot_be_claimed_or_settle_success(string corruption)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = ConnectorAdmission("boundary", new EtlScope("tenant-a", "boundary"), httpShardIds: ["shard-a"]);
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitConnectorAsync(admission)).Status);
        var prep = Assert.IsType<EtlPreparationLease>(await store.ClaimConnectorPreparationAsync(admission.Invocation.Scope, "worker"));
        Assert.Equal(EtlStoreStatus.Applied, await store.CommitConnectorPreparationAsync(prep, new(null, null)));
        context.ChangeTracker.Clear();
        var row = await context.ConnectorPartitions.FirstAsync();
        if (corruption == "unknown") row.PositionJson = "{\"kind\":\"unsupported\"}";
        else if (corruption == "completed") row.Completed = true;
        else if (corruption == "null-request") row.PositionJson = EtlConnectorRecordCodec.Write<EtlConnectorPosition>(new EtlConnectorHttpPosition(0, 0, null!, null, false));
        else
        {
            context.ConnectorPartitions.Remove(row);
            // The immutable seed request now prevents deleting its owning partition.
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Empty(await context.Terminals.AsNoTracking().ToListAsync());
            Assert.Empty(await context.Outbox.AsNoTracking().ToListAsync());
            return;
        }
        await context.SaveChangesAsync();
        if (corruption is "unknown" or "null-request")
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker").AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SettleTerminalAsync(new(admission.Invocation.Scope, admission.Invocation.RunId, EtlTerminalOutcome.Succeeded, null)).AsTask());
        Assert.Empty(await context.Terminals.AsNoTracking().ToListAsync());
        Assert.Empty(await context.Outbox.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Connector_retained_input_overcount_is_rejected()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = ConnectorAdmission("retention", new EtlScope("tenant-a", "boundary"), CsvMixedRoutePipeline());
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitConnectorAsync(admission)).Status);
        var lease = Assert.IsType<EtlPreparationLease>(await store.ClaimConnectorPreparationAsync(admission.Invocation.Scope, "worker"));
        Assert.Equal(EtlStoreStatus.Applied, await store.CommitConnectorPreparationAsync(lease, ConnectorPreparedFile()));
        var row = await context.ConnectorRuns.SingleAsync();
        row.RetainedInputBytes++;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId).AsTask());
    }

    [Fact]
    public async Task Connector_cancel_retains_work_and_disallows_new_claims_and_renewals()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = ConnectorAdmission("cancel", new EtlScope("tenant-a", "boundary"));
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitConnectorAsync(admission)).Status);
        var prep = Assert.IsType<EtlPreparationLease>(await store.ClaimConnectorPreparationAsync(admission.Invocation.Scope, "worker"));
        Assert.Equal(EtlStoreStatus.Applied, await store.CommitConnectorPreparationAsync(prep, new(null, null)));
        var lease = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker"));
        Assert.Equal(EtlStoreStatus.Applied, (await store.CancelAsync(admission.Invocation)).Status);
        Assert.Null(await store.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "successor"));
        Assert.Equal(EtlStoreStatus.Terminal, await store.RenewConnectorPartitionAsync(lease));
        Assert.Equal(EtlStoreStatus.Terminal, await store.ReleaseConnectorPartitionAsync(lease));
        Assert.Equal(2, await context.ConnectorPartitions.CountAsync());
        Assert.Single(await context.Terminals.ToListAsync());
        Assert.Single(await context.Outbox.ToListAsync());
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("shards")]
    [InlineData("pages")]
    public async Task Connector_invalid_admission_creates_neither_run_nor_marker(string defect)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var pipeline = EtlV2TestPipelines.Create();
        if (defect == "pages") pipeline = pipeline with { Http = pipeline.Http with { MaxPagesPerPartition = 0 } };
        var admission = ConnectorAdmission("invalid", new EtlScope("tenant-a", "boundary"), pipeline);
        if (defect == "kind") admission = admission with { Bindings = admission.Bindings with { Source = admission.Bindings.Source with { Kind = EtlConnectorKind.Csv } } };
        if (defect == "shards") admission = admission with { Bindings = admission.Bindings with { HttpShardIds = ["duplicate", "duplicate"] } };
        Assert.Equal(EtlStoreStatus.Invalid, (await store.AdmitConnectorAsync(admission)).Status);
        Assert.Empty(await context.Runs.ToListAsync());
        Assert.Empty(await context.ConnectorRuns.ToListAsync());
    }

    private sealed class ConnectorExpireAfterSave(MutableTimeProvider clock) : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Armed) { Armed = false; clock.Advance(TimeSpan.FromSeconds(31)); }
            return ValueTask.FromResult(result);
        }
    }
}
