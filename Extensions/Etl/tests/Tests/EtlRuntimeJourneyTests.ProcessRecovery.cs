using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task Two_real_worker_processes_recover_a_published_unaccepted_effect_after_predecessor_exit()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var seed = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        await seed.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(CancellationToken.None);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var operation = seed.Services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        var execution = operation.ServiceProvider.GetRequiredService<EtlExecution>();
        var invocation = files.Invocation(EtlTestPipelines.Create(), "process-recovery");
        Assert.Equal(EtlStoreStatus.Applied, (await execution.AdmitAsync(invocation, deadline.Token)).Status);
        var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(files.Scope, "fixture-prepare", deadline.Token));
        await execution.PrepareAsync(preparation, deadline.Token);

        var predecessor = await files.StartWorkerAsync("predecessor", deadline.Token);
        var old = await predecessor.WaitForAcceptanceAsync(deadline.Token);
        Assert.Equal(predecessor.ProcessId, old.ProcessId);
        Assert.NotEqual(Environment.ProcessId, old.ProcessId);
        var physical = Assert.Single(old.Acceptance.Effects);
        Assert.Equal("{\"name\":\"Ada\"}\n", await File.ReadAllTextAsync(Path.Combine(files.OutputRoot, physical.Chunk.Key), deadline.Token));
        var owed = Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token)).OwedBatch;
        Assert.NotNull(owed);
        Assert.Equal(old.Acceptance.Batch, owed.Batch);
        Assert.False(owed.Settled);
        var before = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        Assert.Equal(1, before.OutstandingRecords);
        Assert.True(before.OutstandingBytes > 0);
        Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));

        var survivor = await files.StartWorkerAsync("survivor", deadline.Token);
        Assert.NotEqual(predecessor.ProcessId, survivor.ProcessId);
        Assert.False(predecessor.HasExited);
        Assert.False(survivor.HasExited);
        var held = Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token));
        Assert.Equal(old.Lease.Owner, held.Owner);
        Assert.Equal(old.Lease.Fence, held.Fence);
        await predecessor.CrashAsync();
        Assert.True(predecessor.HasExited);
        // The dead worker can no longer renew. Use the persisted expiry sampled while it still
        // owned the partition; after exit the survivor may already have claimed, so do not assert
        // the old fence remains visible during an unsynchronized inspection window.
        Assert.NotNull(held.ExpiresAt);

        var recovered = await survivor.WaitForAcceptanceAsync(deadline.Token);
        Assert.Equal(survivor.ProcessId, recovered.ProcessId);
        Assert.True(recovered.Lease.Fence > old.Lease.Fence);
        Assert.NotEqual(old.Lease.Owner, recovered.Lease.Owner);
        Assert.Equal(old.Lease.NextOrdinal, recovered.Lease.NextOrdinal);
        Assert.True(DateTimeOffset.UtcNow >= held.ExpiresAt.Value);
        Assert.Equal(old.Acceptance, recovered.Acceptance with { Effects = old.Acceptance.Effects });
        Assert.Equal(old.Acceptance.Effects, recovered.Acceptance.Effects);
        var retry = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        Assert.Equal(before.OutstandingRecords, retry.OutstandingRecords);
        Assert.Equal(before.OutstandingBytes, retry.OutstandingBytes);
        Assert.Equal(owed, Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token)).OwedBatch);
        Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));
        await survivor.ReleaseAsync(deadline.Token);

        var terminal = await files.WaitForTerminalAsync(seed, invocation.RunId);
        Assert.Equal(EtlRunStatus.Succeeded, terminal.Status);
        Assert.Equal(1, terminal.AcceptedRecords);
        Assert.Equal(0, terminal.OutstandingRecords);
        Assert.Equal(0, terminal.OutstandingBytes);
        var manifest = Assert.Single(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));
        Assert.Equal(owed.Batch.BatchId, manifest.BatchId);
        Assert.Equal(physical, manifest.Effect);
        Assert.Equal("{\"name\":\"Ada\"}\n", await files.ReadRouteAsync([manifest], EtlOutputRoute.Main));
        var delivery = Assert.IsType<EtlDeliveryLease>(await store.ClaimDeliveryAsync(files.Scope, "inspection-only", deadline.Token));
        Assert.Equal(terminal.TerminalResult, delivery.Result);
        Assert.Equal(invocation.ResultAddress, delivery.Address);
        // No native parent exists here; inspection does not pretend this owed delivery was consumed.
    }
}
