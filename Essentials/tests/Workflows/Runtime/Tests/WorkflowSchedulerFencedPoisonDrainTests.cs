using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// #4032: a drain that lost its run to another node keeps running until a write of its own is refused. A handler
/// that throws in that time must not leave a poison record on the run its new owner runs. The drainer writes the
/// record under its ownership lease when the poison store can check that lease atomically, and unfenced otherwise.
/// </summary>
public sealed class WorkflowSchedulerFencedPoisonDrainTests
{
    private const string WorkflowExecutionId = "wfexec-1";
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DrainAsync_HandlerFaultAfterAnotherOwnerAcquiredTheRun_ThrowsStaleFenceAndRecordsNoPoison()
    {
        var fixture = NewFixture();
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync(WorkflowExecutionId));
        var drainer = fixture.NewDrainer(fixture.FencedPoisonStore, beforeFault: fixture.TakeOverAsync);
        await fixture.Queue.EnqueueAsync(NewWorkItem());

        var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());

        Assert.Equal(RuntimeFencingRejectionReason.StaleToken, refusal.Reason);
        Assert.Empty(await fixture.FencedPoisonStore.ListAsync(WorkflowExecutionId));
        // The acknowledgement comes before the record and is not undone. This is the state a process crash between
        // the two writes leaves: the item is gone and nothing is recorded.
        Assert.Empty(await fixture.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task DrainAsync_HandlerFaultWhileTheDrainOwnsTheRun_RecordsPoisonUnderItsFence()
    {
        var fixture = NewFixture();
        var store = new CountingFencedPoisonStore(fixture.FencedPoisonStore);
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync(WorkflowExecutionId));
        var drainer = fixture.NewDrainer(store);
        await fixture.Queue.EnqueueAsync(NewWorkItem());

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await store.ListAsync(WorkflowExecutionId));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, record.Disposition);
        Assert.Equal(1, record.FailureCount);
        Assert.Equal((1, 0), (store.FencedRecords, store.UnfencedRecords));
        Assert.Empty(await fixture.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task DrainAsync_HandlerFaultWithNoAmbientLease_RecordsPoisonUnfenced()
    {
        // Nothing pushed a lease: a drainer driven outside the drain orchestrator, as before this change. Another
        // node owns the run, so a fenced record would be refused; the record is written anyway.
        var fixture = NewFixture();
        var store = new CountingFencedPoisonStore(fixture.FencedPoisonStore);
        await fixture.NodeB.AcquireAsync(WorkflowExecutionId);
        var drainer = fixture.NewDrainer(store);
        await fixture.Queue.EnqueueAsync(NewWorkItem());

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(result.StoppedOnFault);
        Assert.Single(await store.ListAsync(WorkflowExecutionId));
        Assert.Equal((0, 1), (store.FencedRecords, store.UnfencedRecords));
    }

    [Fact]
    public async Task DrainAsync_HandlerFaultUnderALeaseOfAnotherExecution_RecordsPoisonUnfenced()
    {
        // The checkpoint committer attaches a lease only to commits of the lease's own execution. The poison record
        // follows the same rule, so a lease of another run never fences this one.
        var fixture = NewFixture();
        var store = new CountingFencedPoisonStore(fixture.FencedPoisonStore);
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync("wfexec-other"));
        var drainer = fixture.NewDrainer(store);
        await fixture.Queue.EnqueueAsync(NewWorkItem());

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(result.StoppedOnFault);
        Assert.Single(await store.ListAsync(WorkflowExecutionId));
        Assert.Equal((0, 1), (store.FencedRecords, store.UnfencedRecords));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DrainAsync_HandlerFaultWhenThePoisonStoreCannotCheckTheFence_RecordsPoisonUnfenced(bool hasCapability)
    {
        // A decorator that does not forward the capability, and a store whose ownership state lives in another
        // backend, both leave the record unfenced: it is written although the run was taken over.
        var fixture = NewFixture();
        var inner = new InMemoryWorkflowSchedulerPoisonStore();
        IWorkflowSchedulerPoisonStore store = hasCapability ? new CountingFencedPoisonStore(inner) : new PlainPoisonStore(inner);
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync(WorkflowExecutionId));
        var drainer = fixture.NewDrainer(store, beforeFault: fixture.TakeOverAsync);
        await fixture.Queue.EnqueueAsync(NewWorkItem());

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(result.StoppedOnFault);
        Assert.False(inner.CanRecordFenced);
        Assert.Single(await inner.ListAsync(WorkflowExecutionId));
        if (store is CountingFencedPoisonStore counting)
            Assert.Equal((0, 1), (counting.FencedRecords, counting.UnfencedRecords));
    }

    [Fact]
    public async Task DrainAsync_RetryNowFaultAfterAnotherOwnerAcquiredTheRun_LeavesTheRequeuedItemAndNoRecord()
    {
        // The order of the fault arm is unchanged: acknowledge, re-enqueue for RetryNow, then record. A stale drain
        // is refused at the record, so the re-enqueued item stays for the new owner and no record is written.
        var fixture = NewFixture();
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync(WorkflowExecutionId));
        var drainer = fixture.NewDrainer(
            fixture.FencedPoisonStore,
            new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryNow, null, "retry-now")),
            fixture.TakeOverAsync);
        await fixture.Queue.EnqueueAsync(NewWorkItem());

        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());

        Assert.Empty(await fixture.FencedPoisonStore.ListAsync(WorkflowExecutionId));
        Assert.Single(await fixture.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    private Fixture NewFixture() => new(new FakeTimeProvider(_now));

    private RuntimeSchedulerWorkItem NewWorkItem()
    {
        using var document = JsonDocument.Parse("""{"workItemId":"work-1"}""");
        return new(
            workItemId: "work-1",
            workflowExecutionId: WorkflowExecutionId,
            commandId: "command-1",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            envelopeId: "envelope-1",
            idempotencyKey: $"{WorkflowExecutionId}:command-1:{Guid.NewGuid():N}",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 1,
            payload: document.RootElement.Clone());
    }

    private sealed class Fixture
    {
        private readonly FakeTimeProvider _clock;

        public Fixture(FakeTimeProvider clock)
        {
            _clock = clock;
            var ownership = new InMemoryExecutionLivenessStateStore();
            NodeA = new RuntimeExecutionOwnershipService(ownership, clock, new RuntimeExecutionOwnershipOptions { OwnerId = "node-a" });
            NodeB = new RuntimeExecutionOwnershipService(ownership, clock, new RuntimeExecutionOwnershipOptions { OwnerId = "node-b" });
            FencedPoisonStore = new InMemoryWorkflowSchedulerPoisonStore(ownership, clock);
        }

        public InMemoryWorkflowSchedulerWorkQueue Queue { get; } = new();
        public AsyncLocalRuntimeExecutionOwnershipContextAccessor Accessor { get; } = new();
        public RuntimeExecutionOwnershipService NodeA { get; }
        public RuntimeExecutionOwnershipService NodeB { get; }
        public InMemoryWorkflowSchedulerPoisonStore FencedPoisonStore { get; }

        public async ValueTask TakeOverAsync() => await NodeB.AcquireAsync(WorkflowExecutionId);

        public WorkflowSchedulerDrainer NewDrainer(
            IWorkflowSchedulerPoisonStore poisonStore,
            IRuntimeDomainRetryPolicy? retryPolicy = null,
            Func<ValueTask>? beforeFault = null) =>
            TestSchedulerDrainer.Create(
                Queue,
                [new FaultingSchedulerWorkHandler(beforeFault), new NoopWorkflowSchedulerWorkHandler()],
                _clock,
                poisonStore: poisonStore,
                retryPolicy: retryPolicy ?? new NoopRuntimeDomainRetryPolicy(),
                ownershipContextAccessor: Accessor);
    }

    private sealed class FaultingSchedulerWorkHandler(Func<ValueTask>? beforeFault) : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(FaultingSchedulerWorkHandler);
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            if (beforeFault is not null)
                await beforeFault();
            throw new InvalidOperationException($"Handler crashed for {workItem.WorkItemId}.");
        }
    }

    private sealed class StubRetryPolicy(RuntimeDomainRetryDecision decision) : IRuntimeDomainRetryPolicy
    {
        public RuntimeDomainRetryDecision Decide(RuntimeDomainRetryRequest request) => decision;
    }

    // A decorator that does not implement the capability, as one written before it existed.
    private sealed class PlainPoisonStore(IWorkflowSchedulerPoisonStore inner) : IWorkflowSchedulerPoisonStore
    {
        public ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(RuntimeSchedulerPoisonRecord record, CancellationToken cancellationToken = default) =>
            inner.RecordAsync(record, cancellationToken);
        public ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, workItemId, cancellationToken);
        public ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.ListAsync(workflowExecutionId, cancellationToken);
    }

    // A decorator that forwards the capability and counts which of the two record calls the drainer made.
    private sealed class CountingFencedPoisonStore(InMemoryWorkflowSchedulerPoisonStore inner) : IWorkflowSchedulerPoisonStore, IWorkflowSchedulerFencedPoisonStore
    {
        public int FencedRecords { get; private set; }
        public int UnfencedRecords { get; private set; }
        public bool CanRecordFenced => inner.CanRecordFenced;

        public ValueTask<RuntimeSchedulerPoisonRecord> RecordFencedAsync(RuntimeSchedulerPoisonRecord record, RuntimeExecutionFence fence, CancellationToken cancellationToken = default)
        {
            FencedRecords++;
            return inner.RecordFencedAsync(record, fence, cancellationToken);
        }

        public ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(RuntimeSchedulerPoisonRecord record, CancellationToken cancellationToken = default)
        {
            UnfencedRecords++;
            return inner.RecordAsync(record, cancellationToken);
        }

        public ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, workItemId, cancellationToken);
        public ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.ListAsync(workflowExecutionId, cancellationToken);
    }
}
