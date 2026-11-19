using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// #4032: the in-memory poison store records under a run's ownership fence when it is built with the in-memory
/// ownership store, and the runtime composition root builds it that way only when that store is the registered one.
/// </summary>
public sealed class InMemoryWorkflowSchedulerFencedPoisonStoreTests
{
    private const string WorkflowExecutionId = "wfexec-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordFencedAsync_WithTheCurrentFence_WritesAndReplacesTheRecord()
    {
        var fixture = new Fixture();
        var lease = await fixture.NodeA.AcquireAsync(WorkflowExecutionId);

        Assert.True(fixture.Store.CanRecordFenced);
        await fixture.Store.RecordFencedAsync(Record(failureCount: 1), lease.ToFence());
        await fixture.Store.RecordFencedAsync(Record(failureCount: 2), lease.ToFence());

        Assert.Equal(2, Assert.Single(await fixture.Store.ListAsync(WorkflowExecutionId)).FailureCount);
    }

    [Fact]
    public async Task RecordFencedAsync_AfterAnotherOwnerAcquiredTheRun_ThrowsStaleTokenAndWritesNothing()
    {
        var fixture = new Fixture();
        var lease = await fixture.NodeA.AcquireAsync(WorkflowExecutionId);
        await fixture.NodeB.AcquireAsync(WorkflowExecutionId);

        var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => fixture.Store.RecordFencedAsync(Record(), lease.ToFence()).AsTask());

        Assert.Equal(RuntimeFencingRejectionReason.StaleToken, refusal.Reason);
        Assert.Empty(await fixture.Store.ListAsync(WorkflowExecutionId));
    }

    [Fact]
    public async Task RecordFencedAsync_AfterTheLeaseExpired_ThrowsExpiredLeaseAndWritesNothing()
    {
        var fixture = new Fixture();
        var lease = await fixture.NodeA.AcquireAsync(WorkflowExecutionId);
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));

        var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => fixture.Store.RecordFencedAsync(Record(), lease.ToFence()).AsTask());

        Assert.Equal(RuntimeFencingRejectionReason.ExpiredLease, refusal.Reason);
        Assert.Empty(await fixture.Store.ListAsync(WorkflowExecutionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordFencedAsync_WhenTheRunHasNoLease_ThrowsNoActiveLeaseAndWritesNothing(bool released)
    {
        // Either the run was never owned, or its owner released it.
        var fixture = new Fixture();
        var fence = new RuntimeExecutionFence("lease-1", "node-a", 1);
        if (released)
        {
            var lease = await fixture.NodeA.AcquireAsync(WorkflowExecutionId);
            await fixture.NodeA.ReleaseAsync(lease);
            fence = lease.ToFence();
        }

        var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => fixture.Store.RecordFencedAsync(Record(), fence).AsTask());

        Assert.Equal(RuntimeFencingRejectionReason.NoActiveLease, refusal.Reason);
        Assert.Empty(await fixture.Store.ListAsync(WorkflowExecutionId));
    }

    [Fact]
    public async Task RecordFencedAsync_WaitsForAnOwnershipChangeInProgress_SoTheCheckAndTheWriteAreOneStep()
    {
        // The ownership store makes every lease change under one gate. The fenced record takes the same gate, so a
        // takeover cannot land between its lease check and its write. Held from another flow here, the gate keeps the
        // record waiting.
        var fixture = new Fixture();
        var lease = await fixture.NodeA.AcquireAsync(WorkflowExecutionId);
        var gateHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = Task.Run(async () => await fixture.Ownership.ExecuteOwnershipAtomicAsync(async _ =>
        {
            gateHeld.SetResult();
            await releaseGate.Task;
            return true;
        }));
        await gateHeld.Task;

        var record = fixture.Store.RecordFencedAsync(Record(), lease.ToFence()).AsTask();

        Assert.False(record.IsCompleted);
        Assert.Empty(await fixture.Store.ListAsync(WorkflowExecutionId));
        releaseGate.SetResult();
        await holder;
        await record;
        Assert.Single(await fixture.Store.ListAsync(WorkflowExecutionId));
    }

    [Fact]
    public async Task RecordFencedAsync_OnAStoreBuiltWithoutAnOwnershipStore_IsNotSupportedAndWritesNothing()
    {
        var store = new InMemoryWorkflowSchedulerPoisonStore();

        Assert.False(store.CanRecordFenced);
        await Assert.ThrowsAsync<NotSupportedException>(
            () => store.RecordFencedAsync(Record(), new RuntimeExecutionFence("lease-1", "node-a", 1)).AsTask());
        Assert.Empty(await store.ListAsync(WorkflowExecutionId));
    }

    [Fact]
    public void CanRecordFenced_AfterALookupOfTheOwnershipStoreThatFailed_LooksAgain()
    {
        // The composition root's lookup resolves the registered ownership store, which can fail for a while (a
        // provider that is not ready yet, say). A failure must not be remembered as the answer.
        var ownership = new InMemoryExecutionLivenessStateStore();
        var lookups = 0;
        var store = new InMemoryWorkflowSchedulerPoisonStore(
            () => ++lookups == 1 ? throw new InvalidOperationException("The ownership store cannot be resolved yet.") : ownership,
            timeProvider: null);

        Assert.Throws<InvalidOperationException>(() => store.CanRecordFenced);
        Assert.True(store.CanRecordFenced);
        Assert.True(store.CanRecordFenced);

        // Once found, the store is kept.
        Assert.Equal(2, lookups);
    }

    [Fact]
    public async Task AddWorkflowRuntime_ChecksOwnershipBeforeItAcknowledgesAFaultedStep()
    {
        // Through the container: the composition root has to give the drainer the ownership accessor and the
        // ownership service. Every drainer test passes both by hand, so dropping either from the registration would
        // let a drain that lost its run acknowledge and poison the step on a real host while those tests stayed green.
        var handler = new FaultingInvokeActivityHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowSchedulerWorkHandler>(handler);
        services.AddWorkflowRuntime();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var poisonStore = provider.GetRequiredService<IWorkflowSchedulerPoisonStore>();
        var queue = provider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        await queue.EnqueueAsync(NewInvokeActivityWorkItem());
        using var scope = provider.CreateScope();
        var ownership = scope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
        using var owned = provider.GetRequiredService<IRuntimeExecutionOwnershipContextAccessor>()
            .Push(await ownership.AcquireAsync(WorkflowExecutionId));
        // A second acquisition issues a new lease, as another node's takeover does.
        handler.BeforeFault = async () => await ownership.AcquireAsync(WorkflowExecutionId);

        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => scope.ServiceProvider
            .GetRequiredService<IWorkflowSchedulerDrainer>()
            .DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());

        Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items);
        Assert.Empty(await poisonStore.ListAsync(WorkflowExecutionId));
    }

    [Fact]
    public async Task AddWorkflowRuntime_WithInMemoryOwnership_FencesThePoisonRecordOfARunLostAfterTheCheck()
    {
        // Through the container: the composition root has to give the poison store the registered in-memory ownership
        // store, or the record of a run lost between the drainer's check and the record would be written unfenced.
        // The retry policy is consulted after that check and before the acknowledgement, so it stands in for the
        // other node here.
        var retryPolicy = new TakeOverRetryPolicy();
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowSchedulerWorkHandler>(new FaultingInvokeActivityHandler());
        services.AddSingleton<IRuntimeDomainRetryPolicy>(retryPolicy);
        services.AddWorkflowRuntime();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var poisonStore = provider.GetRequiredService<IWorkflowSchedulerPoisonStore>();
        Assert.True(Assert.IsAssignableFrom<ISchedulerPoisonFenceCapability>(poisonStore).CanRecordFenced);
        var queue = provider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        await queue.EnqueueAsync(NewInvokeActivityWorkItem());
        using var scope = provider.CreateScope();
        var ownership = scope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
        using var owned = provider.GetRequiredService<IRuntimeExecutionOwnershipContextAccessor>()
            .Push(await ownership.AcquireAsync(WorkflowExecutionId));
        retryPolicy.OnDecide = () => ownership.AcquireAsync(WorkflowExecutionId).AsTask().GetAwaiter().GetResult();

        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => scope.ServiceProvider
            .GetRequiredService<IWorkflowSchedulerDrainer>()
            .DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());

        // The step was acknowledged before the run was found lost; the record was refused.
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items);
        Assert.Empty(await poisonStore.ListAsync(WorkflowExecutionId));
    }

    [Fact]
    public async Task AddWorkflowRuntime_WithOwnershipInAnotherStore_LeavesThePoisonStoreUnfenced()
    {
        // A provider's ownership store is scoped. The singleton poison store must resolve under scope validation, must
        // not take that store, and must report that it cannot check the fence, so the drainer records unfenced.
        var handler = new FaultingInvokeActivityHandler();
        var services = new ServiceCollection();
        services.AddScoped<IExecutionLivenessStateStore, ForeignOwnershipStore>();
        // The default execution-state store is a singleton that resolves the ownership store from the root provider,
        // which scope validation refuses for a scoped one. That is not what this test is about, so it brings its own.
        services.AddSingleton<IWorkflowExecutionStateStore>(new InMemoryWorkflowExecutionStateStore());
        services.AddSingleton<IWorkflowSchedulerWorkHandler>(handler);
        services.AddWorkflowRuntime();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var poisonStore = provider.GetRequiredService<IWorkflowSchedulerPoisonStore>();
        Assert.False(Assert.IsAssignableFrom<ISchedulerPoisonFenceCapability>(poisonStore).CanRecordFenced);
        await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().EnqueueAsync(NewInvokeActivityWorkItem());
        using var scope = provider.CreateScope();
        using var owned = provider.GetRequiredService<IRuntimeExecutionOwnershipContextAccessor>().Push(new RuntimeExecutionLease(
            "lease-1", WorkflowExecutionId, "node-a", Now, Now.AddMinutes(1), fencingToken: 1));

        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerDrainer>()
            .DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(result.StoppedOnFault);
        Assert.Single(await poisonStore.ListAsync(WorkflowExecutionId));
    }

    [Fact]
    public async Task AddWorkflowRuntime_WithAScopedInMemoryOwnershipStore_RecordsUnfencedSoAnOwningDrainKeepsItsRecord()
    {
        // A host that registers the in-memory ownership store scoped gets one per scope. The poison store is a
        // singleton and cannot hold the drain scope's one; a copy of its own would hold no lease and refuse the record
        // of a drain that owns its run, after the step was acknowledged. So it reports that it cannot record fenced.
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowSchedulerWorkHandler>(new FaultingInvokeActivityHandler());
        // As in the foreign-store test: the default execution-state store resolves the ownership store from the root.
        services.AddSingleton<IWorkflowExecutionStateStore>(new InMemoryWorkflowExecutionStateStore());
        services.AddWorkflowRuntime();
        services.AddLogging();
        services.RemoveAll<IExecutionLivenessStateStore>();
        services.AddScoped<IExecutionLivenessStateStore, InMemoryExecutionLivenessStateStore>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var poisonStore = provider.GetRequiredService<IWorkflowSchedulerPoisonStore>();
        var queue = provider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        await queue.EnqueueAsync(NewInvokeActivityWorkItem());
        using var scope = provider.CreateScope();
        using var owned = provider.GetRequiredService<IRuntimeExecutionOwnershipContextAccessor>()
            .Push(await scope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>().AcquireAsync(WorkflowExecutionId));

        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerDrainer>()
            .DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(result.StoppedOnFault);
        Assert.Single(await poisonStore.ListAsync(WorkflowExecutionId));
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items);
        Assert.False(Assert.IsAssignableFrom<ISchedulerPoisonFenceCapability>(poisonStore).CanRecordFenced);
    }

    [Fact]
    public async Task AddWorkflowRuntime_DoesNotLookForTheOwnershipStoreUntilAFencedRecordIsAskedFor()
    {
        // Reading and unfenced recording must keep working where the registered ownership store cannot be built, as
        // before the capability existed.
        var lookups = 0;
        var services = new ServiceCollection();
        services.AddScoped<IExecutionLivenessStateStore>(_ =>
        {
            Interlocked.Increment(ref lookups);
            throw new InvalidOperationException("The ownership store cannot be built here.");
        });
        services.AddWorkflowRuntime();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var poisonStore = provider.GetRequiredService<IWorkflowSchedulerPoisonStore>();
        await poisonStore.RecordAsync(Record());
        Assert.NotNull(await poisonStore.FindAsync(WorkflowExecutionId, "work-1"));
        Assert.Single(await poisonStore.ListAsync(WorkflowExecutionId));

        Assert.Equal(0, lookups);
    }

    private static RuntimeSchedulerWorkItem NewInvokeActivityWorkItem() => new(
        "work-1", WorkflowExecutionId, "command-1", WorkflowExecutionCommandKind.InvokeActivity, "envelope-1", "idempotency-1", Now, Now);

    private static RuntimeSchedulerPoisonRecord Record(int failureCount = 1) => new(
        WorkflowExecutionId,
        "work-1",
        WorkflowExecutionCommandKind.RunSchedulerWork,
        "handler",
        new RuntimeFaultInfo("System.InvalidOperationException", "boom"),
        failureCount,
        RuntimeSchedulerPoisonDisposition.Poisoned,
        Now,
        Now);

    private sealed class Fixture
    {
        public Fixture()
        {
            NodeA = new RuntimeExecutionOwnershipService(Ownership, Clock, new RuntimeExecutionOwnershipOptions { OwnerId = "node-a" });
            NodeB = new RuntimeExecutionOwnershipService(Ownership, Clock, new RuntimeExecutionOwnershipOptions { OwnerId = "node-b" });
            Store = new InMemoryWorkflowSchedulerPoisonStore(Ownership, Clock);
        }

        public FakeTimeProvider Clock { get; } = new(Now);
        public InMemoryExecutionLivenessStateStore Ownership { get; } = new();
        public RuntimeExecutionOwnershipService NodeA { get; }
        public RuntimeExecutionOwnershipService NodeB { get; }
        public InMemoryWorkflowSchedulerPoisonStore Store { get; }
    }

    private sealed class FaultingInvokeActivityHandler : IWorkflowSchedulerWorkHandler
    {
        public Func<ValueTask>? BeforeFault { get; set; }
        public string Name => nameof(FaultingInvokeActivityHandler);
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => workItem.CommandKind == WorkflowExecutionCommandKind.InvokeActivity;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            if (BeforeFault is not null)
                await BeforeFault();
            throw new InvalidOperationException($"Handler crashed for {workItem.WorkItemId}.");
        }
    }

    private sealed class TakeOverRetryPolicy : IRuntimeDomainRetryPolicy
    {
        public Action? OnDecide { get; set; }

        public RuntimeDomainRetryDecision Decide(RuntimeDomainRetryRequest request)
        {
            OnDecide?.Invoke();
            return new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.DoNotRetry, null, "do-not-retry");
        }
    }

    // Stands for a provider's ownership store: scoped, and not the in-memory one. It holds no ownership record, so the
    // drainer's ownership check finds nothing to refuse.
    private sealed class ForeignOwnershipStore : IExecutionLivenessStateStore
    {
        public ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(ExecutionLivenessState state, long expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<ExecutionLivenessState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<VersionedExecutionLivenessState?> FindVersionedAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<VersionedExecutionLivenessState?>(null);
    }
}
