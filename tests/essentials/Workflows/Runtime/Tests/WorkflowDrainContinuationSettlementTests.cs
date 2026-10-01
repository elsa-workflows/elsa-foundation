using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// #2225 at the drain orchestrator, over the in-memory stores and the real outbox processor: what a drain reports when
/// another deliverer holds one of its execution's continuations and the wait does not end with that work drained. The
/// end-to-end interleavings, with the real resumption sweep on every store, are the Runtime EF Core tests'
/// <c>LiveDrainSweepContentionContract</c>.
/// </summary>
public sealed class WorkflowDrainContinuationSettlementTests
{
    private const string Wfid = "wfexec-settlement";
    private const string ContinuationId = "outbox-continuation";
    private const string OtherDeliverer = "other-deliverer";

    private readonly InMemoryRuntimeCheckpointCommitStore _store = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _queue = new();
    private readonly InMemoryExecutionLivenessStateStore _liveness = new();
    private readonly AsyncLocalRuntimeLiveDrainDeliveryAccessor _liveDrain = new();
    private readonly ObservedClaimStore _observed;
    private readonly HopDrainer _drainer;

    public WorkflowDrainContinuationSettlementTests()
    {
        _observed = new ObservedClaimStore(_store);
        _drainer = new HopDrainer(_store, _queue);
    }

    [Fact]
    public async Task Cancelling_a_drain_that_waits_for_another_deliverers_claim_ends_it_without_quiescence()
    {
        using var cancellation = new CancellationTokenSource();
        _observed.OnClaimedListed = () =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DrainAsync(cancellation.Token));

        Assert.Equal(1, _drainer.DrainCount);
        var continuation = Assert.IsType<RuntimePostCommitOutboxItem>(await _store.FindAsync(ContinuationId));
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, continuation.Status);
        Assert.Equal(OtherDeliverer, continuation.DeliveringOwnerId);
        Assert.Null((await _liveness.FindAsync(Wfid, RuntimeExecutionOwnershipStateId.For(Wfid)))?.ExecutionLease);
    }

    [Fact]
    public async Task A_continuation_its_other_deliverer_failed_ends_the_drain_as_a_failed_delivery()
    {
        _observed.OnClaimedListed = async () => await _store.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            _drainer.OtherClaim!,
            new RuntimePostCommitOutboxDeliveryResult(
                ContinuationId,
                RuntimePostCommitOutboxStatus.FailedRetryable,
                DateTimeOffset.UtcNow,
                "The other deliverer could not enqueue the work.")));

        var result = await DrainAsync();

        Assert.Equal(RuntimeSchedulerDrainStopReason.OutboxDeliveryFailed, result.StopReason);
        Assert.Equal(1, _drainer.DrainCount);
    }

    [Fact]
    public async Task Queued_work_a_drain_could_not_claim_does_not_keep_it_draining()
    {
        // A scheduler drain that ran nothing could not take the queued head either, so finding it queued afterwards is no
        // sign that another deliverer just delivered it: the drain quiesces instead of cycling to its cap.
        await _queue.EnqueueAsync(WorkItem("work-held-elsewhere"));
        var idle = new HopDrainer(_store, _queue, commitsContinuation: false, drainsQueue: false);

        var result = await DrainAsync(drainer: idle);

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(1, idle.DrainCount);
    }

    private Task<RuntimeSchedulerDrainResult> DrainAsync(CancellationToken cancellationToken = default, HopDrainer? drainer = null)
    {
        var orchestrator = new WorkflowDrainOrchestrator(
            drainer ?? _drainer,
            new RuntimePostCommitOutboxProcessor(
                _store,
                new RuntimeSchedulerPostCommitIntentDispatcher(_queue),
                TimeProvider.System,
                DefaultRuntimeFaultCapturePolicy.CreateDefault(),
                workflowDispatchStore: null,
                logger: null,
                _liveDrain,
                coalescingSessionAccessor: null),
            schedulerDrainObservers: [],
            checkpointRuleViolationFaulter: TestCheckpointRuleViolationFaulter.Create(),
            ownershipService: new RuntimeExecutionOwnershipService(_liveness),
            ownershipContextAccessor: new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            liveDrainDeliveryAccessor: _liveDrain,
            outboxStore: _observed,
            schedulerWorkQueue: _queue);
        return orchestrator.DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(Wfid), cancellationToken).AsTask();
    }

    private static RuntimeSchedulerWorkItem WorkItem(string workItemId) =>
        new(
            workItemId: workItemId,
            workflowExecutionId: Wfid,
            commandId: $"command-{workItemId}",
            commandKind: WorkflowExecutionCommandKind.CreateBookmark,
            envelopeId: $"envelope-{workItemId}",
            idempotencyKey: $"{Wfid}:{workItemId}",
            enqueuedAt: DateTimeOffset.UtcNow,
            recordedAt: DateTimeOffset.UtcNow);

    private static WorkflowExecutionCommandEnvelope Envelope()
    {
        using var document = JsonDocument.Parse("""{"kind":"run"}""");
        return new(
            envelopeId: "envelope-drain",
            workflowExecutionId: Wfid,
            command: new WorkflowExecutionCommand(
                CommandId: "command-drain",
                WorkflowExecutionId: Wfid,
                Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
                EnqueuedAt: DateTimeOffset.UtcNow,
                Payload: document.RootElement.Clone(),
                Metadata: new Dictionary<string, string>()),
            idempotencyKey: $"{Wfid}:command-drain",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: DateTimeOffset.UtcNow,
            sequence: 1,
            metadata: new Dictionary<string, string>());
    }

    /// <summary>
    /// Stands in for the scheduler drainer. Its first drain runs one item whose commit records a continuation, which
    /// another deliverer then claims before the drain's delivery step, as the resumption sweep can. Later drains run what
    /// is queued.
    /// </summary>
    private sealed class HopDrainer(
        InMemoryRuntimeCheckpointCommitStore store,
        InMemoryWorkflowSchedulerWorkQueue queue,
        bool commitsContinuation = true,
        bool drainsQueue = true) : IWorkflowSchedulerDrainer
    {
        public int DrainCount { get; private set; }
        public RuntimePostCommitOutboxClaim? OtherClaim { get; private set; }

        public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            List<string> ran = [];
            if (DrainCount++ == 0 && commitsContinuation)
            {
                ran.Add("work-start");
                var work = WorkItem("work-bookmark");
                await store.AddPendingForTestingAsync(new RuntimePostCommitOutboxItem(
                    ContinuationId,
                    new RuntimePostCommitIntent(
                        $"intent-{work.WorkItemId}",
                        Wfid,
                        RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
                        now,
                        activityExecutionId: null,
                        idempotencyKey: $"checkpoint:{work.WorkItemId}",
                        payload: JsonSerializer.SerializeToElement(work)),
                    RuntimePostCommitOutboxStatus.Pending,
                    now,
                    now), cancellationToken);
                OtherClaim = Assert.Single(await store.ClaimAsync(
                    new RuntimePostCommitOutboxClaimRequest(OtherDeliverer, now, TimeSpan.FromMinutes(1), limit: 1),
                    cancellationToken));
            }
            else if (drainsQueue)
            {
                while (await queue.DequeueAsync(Wfid, cancellationToken) is { } work)
                    ran.Add(work.WorkItemId);
            }

            return new RuntimeSchedulerDrainResult(
                Wfid,
                now,
                DateTimeOffset.UtcNow,
                ran.Select(workItemId => new RuntimeSchedulerWorkItemResult(
                    workItemId,
                    Wfid,
                    WorkflowExecutionCommandKind.CreateBookmark,
                    RuntimeSchedulerWorkItemResultStatus.Completed,
                    nameof(HopDrainer),
                    now,
                    now)).ToArray());
        }
    }

    /// <summary>The in-memory store as the drain sees it, with a hook on the drain's read of claimed continuations.</summary>
    private sealed class ObservedClaimStore(InMemoryRuntimeCheckpointCommitStore inner) :
        IRuntimePostCommitOutboxStore,
        IRuntimePostCommitOutboxClaimStore,
        IPostCommitOutboxLookupStore
    {
        private int _observed;

        /// <summary>Runs once, the first time the drain finds a continuation claimed.</summary>
        public Func<Task>? OnClaimedListed { get; set; }

        public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ListClaimedAsync(
            RuntimePostCommitOutboxClaimedQuery query,
            CancellationToken cancellationToken = default)
        {
            var claimed = await inner.ListClaimedAsync(query, cancellationToken);
            if (claimed.Count > 0 && OnClaimedListed is { } observe && Interlocked.Exchange(ref _observed, 1) == 0)
                await observe();
            return claimed;
        }

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default) =>
            inner.GetDeliverableAsync(query, cancellationToken);

        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) =>
            inner.RecordDeliveryResultAsync(result, cancellationToken);

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(RuntimePostCommitOutboxClaimRequest request, CancellationToken cancellationToken = default) =>
            inner.ClaimAsync(request, cancellationToken);

        public ValueTask<RuntimePostCommitOutboxClaim?> RenewClaimAsync(RuntimePostCommitOutboxClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default) =>
            inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);

        public ValueTask RecordDeliveryResultAsync(RuntimePostCommitOutboxClaim claim, RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) =>
            inner.RecordDeliveryResultAsync(claim, result, cancellationToken);

        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(outboxItemId, cancellationToken);
    }
}
