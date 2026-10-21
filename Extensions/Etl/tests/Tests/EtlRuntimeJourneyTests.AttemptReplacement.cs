using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Tests.Support;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task Real_preparation_retains_capacity_until_its_heartbeat_has_unwound()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var barrier = new AttemptReplacementBarrier();
        var pipeline = EtlTestPipelines.Create();
        var capacity = AttemptCapacity(pipeline.Limits);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => ConfigureAttemptServices(services, barrier, capacity));
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(CancellationToken.None);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var operation = host.Services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        var execution = operation.ServiceProvider.GetRequiredService<EtlExecution>();
        var invocation = files.Invocation(pipeline, "preparation-unwind");
        Assert.Equal(EtlStoreStatus.Applied, (await execution.AdmitAsync(invocation, deadline.Token)).Status);
        var lease = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(files.Scope, "prepare-held", deadline.Token));
        var preparation = execution.PrepareAsync(lease, deadline.Token).AsTask();
        try
        {
            await barrier.PreparationStopping.Task.WaitAsync(deadline.Token);
            await AssertAttemptCapacityHeldAsync(host.Services.GetRequiredService<EtlAttemptBudget>(),
                capacity, deadline.Token);
        }
        finally
        {
            barrier.ReleaseAll();
            await preparation.WaitAsync(TimeSpan.FromSeconds(10));
        }
        var prepared = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        Assert.Equal(EtlRunStatus.Running, prepared.Status);
        Assert.NotNull(prepared.Prepared);
        await using var available = await host.Services.GetRequiredService<EtlAttemptBudget>()
            .AcquireAsync(capacity, deadline.Token);
    }

    [Fact]
    public async Task Real_replaced_attempt_retains_capacity_through_cancellation_and_cleanup_then_successor_owns_its_permit()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var clock = new AttemptClock();
        var barrier = new AttemptReplacementBarrier();
        var pipeline = EtlTestPipelines.Create();
        var capacity = AttemptCapacity(pipeline.Limits);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                ConfigureAttemptServices(services, barrier, capacity);
            });
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(CancellationToken.None);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var control = host.Services.CreateAsyncScope();
        await using var oldScope = host.Services.CreateAsyncScope();
        await using var successorScope = host.Services.CreateAsyncScope();
        var store = control.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        // A v1-only provider remains usable without the optional connector capability.
        Assert.False(store is IEtlConnectorRuntimeStore);
        var execution = control.ServiceProvider.GetRequiredService<EtlExecution>();
        var invocation = files.Invocation(pipeline, "live-replacement");
        Assert.Equal(EtlStoreStatus.Applied, (await execution.AdmitAsync(invocation, deadline.Token)).Status);
        var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(files.Scope, "prepare", deadline.Token));
        await execution.PrepareAsync(preparation, deadline.Token);
        var oldLease = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(files.Scope, invocation.RunId, "old", deadline.Token));
        var budget = host.Services.GetRequiredService<EtlAttemptBudget>();
        Assert.Same(budget, oldScope.ServiceProvider.GetRequiredService<EtlAttemptBudget>());
        Assert.Same(budget, successorScope.ServiceProvider.GetRequiredService<EtlAttemptBudget>());
        Task? oldTask = null;
        Task? successorTask = null;
        try
        {
            oldTask = oldScope.ServiceProvider.GetRequiredService<EtlExecution>().ExecutePartitionAsync(oldLease, deadline.Token).AsTask();
            await barrier.OldAcceptanceEntered.Task.WaitAsync(deadline.Token);
            var before = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
            var owed = Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token)).OwedBatch;
            Assert.NotNull(owed);
            Assert.False(owed.Settled);
            Assert.Equal(1, before.OutstandingRecords);
            Assert.True(before.OutstandingBytes > 0);
            Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 1, deadline.Token));
            var oldAcceptance = Assert.IsType<EtlBatchAcceptance>(barrier.OldAcceptance);
            var physical = Assert.Single(oldAcceptance.Effects);
            Assert.Equal(EtlOutputRoute.Main, physical.Route);
            var physicalPath = Path.Combine(files.OutputRoot, physical.Chunk.Key);
            Assert.Equal("{\"name\":\"Ada\"}\n", await File.ReadAllTextAsync(physicalPath, deadline.Token));

            clock.Advance(TimeSpan.FromMinutes(1));
            var successorLease = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(files.Scope, invocation.RunId, "successor", deadline.Token));
            Assert.Equal(oldLease.PartitionOrdinal, successorLease.PartitionOrdinal);
            Assert.True(successorLease.Fence > oldLease.Fence);
            Assert.Equal(oldLease.NextOrdinal, successorLease.NextOrdinal);
            Assert.Equal(owed, Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token)).OwedBatch);
            await AssertAttemptCapacityHeldAsync(budget, capacity, deadline.Token);

            barrier.ReleaseOldRenewal.TrySetResult();
            await barrier.OldCancelled.Task.WaitAsync(deadline.Token);
            // Cancellation and fence replacement are not proof the paused physical operation stopped.
            await AssertAttemptCapacityHeldAsync(budget, capacity, deadline.Token);
            barrier.ReleaseOldAcceptance.TrySetResult();
            await barrier.OldCleanupEntered.Task.WaitAsync(deadline.Token);
            Assert.Equal(EtlStoreStatus.LeaseLost, barrier.OldAcceptanceStatus);
            // Even asynchronous attempt cleanup must finish before this actual task releases capacity.
            await AssertAttemptCapacityHeldAsync(budget, capacity, deadline.Token);
            barrier.ReleaseOldCleanup.TrySetResult();
            await oldTask.WaitAsync(deadline.Token);

            successorTask = successorScope.ServiceProvider.GetRequiredService<EtlExecution>().ExecutePartitionAsync(successorLease, deadline.Token).AsTask();
            await barrier.SuccessorAcceptanceEntered.Task.WaitAsync(deadline.Token);
            await AssertAttemptCapacityHeldAsync(budget, capacity, deadline.Token);
            var retained = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
            Assert.Equal(before.OutstandingRecords, retained.OutstandingRecords);
            Assert.Equal(before.OutstandingBytes, retained.OutstandingBytes);
            Assert.Equal(owed, Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token)).OwedBatch);
            Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 1, deadline.Token));
            var successorAcceptance = Assert.IsType<EtlBatchAcceptance>(barrier.SuccessorAcceptance);
            Assert.Equal(oldAcceptance, successorAcceptance with { Effects = oldAcceptance.Effects });
            Assert.Equal(oldAcceptance.Effects, successorAcceptance.Effects);
            barrier.ReleaseSuccessorAcceptance.TrySetResult();
            await successorTask.WaitAsync(deadline.Token);

            var terminal = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
            Assert.Equal(EtlRunStatus.Succeeded, terminal.Status);
            Assert.Equal(1, terminal.AcceptedRecords);
            Assert.Equal(0, terminal.OutstandingRecords);
            Assert.Equal(0, terminal.OutstandingBytes);
            var manifest = Assert.Single(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));
            Assert.Equal(owed.Batch.BatchId, manifest.BatchId);
            Assert.Equal(physical, manifest.Effect);
            Assert.Equal("{\"name\":\"Ada\"}\n", await files.ReadRouteAsync([manifest], EtlOutputRoute.Main));
            await using var available = await budget.AcquireAsync(capacity, deadline.Token);
        }
        finally
        {
            barrier.ReleaseAll();
            await Task.WhenAll(new[] { oldTask, successorTask }.OfType<Task>()).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static async Task AssertAttemptCapacityHeldAsync(EtlAttemptBudget budget, EtlAttemptCharge capacity, CancellationToken deadline)
    {
        // One attempt slot remains available. Check each allowance separately so a correct slot
        // count or another held dimension cannot hide an early release of records or any byte budget.
        foreach (var charge in new EtlAttemptCharge[]
        {
            new(capacity.Records, 0, 0, 0), new(0, capacity.InputBytes, 0, 0),
            new(0, 0, capacity.LookupBytes, 0), new(0, 0, 0, capacity.OutputBytes)
        })
            await AssertAttemptChargeHeldAsync(budget, charge, deadline);
    }

    private static async Task AssertAttemptChargeHeldAsync(EtlAttemptBudget budget, EtlAttemptCharge charge, CancellationToken deadline)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline);
        var pending = budget.AcquireAsync(charge, cancellation.Token).AsTask();
        if (pending.IsCompletedSuccessfully)
        {
            await using var unexpected = await pending;
            Assert.Fail("A live attempt released its capacity before the asserted unwind boundary.");
        }
        // AcquireAsync runs synchronously to its capacity wait: no sleep or scheduling-based negative.
        Assert.False(pending.IsCompleted);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private static EtlAttemptCharge AttemptCapacity(EtlLimits limits) => new(limits.MaxBatchRecords,
        limits.MaxBatchBytes, Math.Min(limits.MaxLookupBytes, limits.MaxSnapshotBytes), limits.MaxOutputBatchBytes);

    private static void ConfigureAttemptServices(IServiceCollection services, AttemptReplacementBarrier barrier, EtlAttemptCharge capacity)
    {
        services.Replace(ServiceDescriptor.Singleton(new EtlAttemptBudget(2, capacity)));
        services.Replace(ServiceDescriptor.Scoped<IEtlRuntimeStore>(provider =>
            new PausedAttemptStore(ActivatorUtilities.CreateInstance<EfEtlRuntimeStore>(provider), barrier)));
    }

    private sealed class AttemptClock : TimeProvider
    {
        private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }

    private sealed class AttemptReplacementBarrier
    {
        public readonly TaskCompletionSource OldAcceptanceEntered = Signal();
        public readonly TaskCompletionSource ReleaseOldAcceptance = Signal();
        public readonly TaskCompletionSource ReleaseOldRenewal = Signal();
        public readonly TaskCompletionSource OldCancelled = Signal();
        public readonly TaskCompletionSource OldCleanupEntered = Signal();
        public readonly TaskCompletionSource ReleaseOldCleanup = Signal();
        public readonly TaskCompletionSource SuccessorAcceptanceEntered = Signal();
        public readonly TaskCompletionSource ReleaseSuccessorAcceptance = Signal();
        public readonly TaskCompletionSource PreparationStopping = Signal();
        public readonly TaskCompletionSource ReleasePreparationRenewal = Signal();
        public EtlBatchAcceptance? OldAcceptance;
        public EtlBatchAcceptance? SuccessorAcceptance;
        public EtlStoreStatus? OldAcceptanceStatus;
        public int OldRenewals;
        public void ReleaseAll()
        {
            ReleaseOldAcceptance.TrySetResult();
            ReleaseOldRenewal.TrySetResult();
            ReleaseOldCleanup.TrySetResult();
            ReleaseSuccessorAcceptance.TrySetResult();
            ReleasePreparationRenewal.TrySetResult();
        }
        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Pauses real I/O calls before entering the inner EF transaction, never the database lock.</summary>
    private sealed class PausedAttemptStore(IEtlRuntimeStore inner, AttemptReplacementBarrier barrier) : ForwardingEtlRuntimeStore(inner)
    {
        public override async ValueTask<EtlStoreStatus> RenewPreparationAsync(EtlPreparationLease lease, CancellationToken cancellationToken = default)
        {
            if (lease.Owner == "prepare-held")
            {
                using var registration = cancellationToken.Register(() => barrier.PreparationStopping.TrySetResult());
                await barrier.ReleasePreparationRenewal.Task;
            }
            return await Inner.RenewPreparationAsync(lease, cancellationToken);
        }

        public override async ValueTask<EtlStoreStatus> RenewPartitionAsync(EtlPartitionLease lease, CancellationToken cancellationToken = default)
        {
            // Heartbeat starts synchronously before the execution body. Hold only its first old call;
            // the execution body's explicit pre-effect renewal still reaches the real store.
            if (lease.Owner == "old" && Interlocked.Increment(ref barrier.OldRenewals) == 1)
                await barrier.ReleaseOldRenewal.Task;
            return await Inner.RenewPartitionAsync(lease, cancellationToken);
        }

        public override async ValueTask<EtlAcceptanceResult> AcceptBatchAsync(EtlPartitionLease lease, EtlBatchAcceptance acceptance, CancellationToken cancellationToken = default)
        {
            if (lease.Owner == "old")
            {
                barrier.OldAcceptance = acceptance;
                using var registration = cancellationToken.Register(() => barrier.OldCancelled.TrySetResult());
                barrier.OldAcceptanceEntered.TrySetResult();
                // Model a physical operation that cannot stop immediately on cancellation. Still use
                // the real store fence check afterward, independently of caller cancellation.
                await barrier.ReleaseOldAcceptance.Task;
                var result = await Inner.AcceptBatchAsync(lease, acceptance, CancellationToken.None);
                barrier.OldAcceptanceStatus = result.Status;
                return result;
            }
            barrier.SuccessorAcceptance = acceptance;
            barrier.SuccessorAcceptanceEntered.TrySetResult();
            await barrier.ReleaseSuccessorAcceptance.Task.WaitAsync(cancellationToken);
            return await Inner.AcceptBatchAsync(lease, acceptance, cancellationToken);
        }

        public override async ValueTask<EtlStoreStatus> ReleasePartitionAsync(EtlPartitionLease lease, CancellationToken cancellationToken = default)
        {
            if (lease.Owner == "old")
            {
                barrier.OldCleanupEntered.TrySetResult();
                await barrier.ReleaseOldCleanup.Task.WaitAsync(cancellationToken);
            }
            return await Inner.ReleasePartitionAsync(lease, cancellationToken);
        }

    }
}
