using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// WU-2 (spec 105-runtime-live-drain-delivery): a live Immediate-mode drain delivers EnqueueSchedulerWork intents
/// in-memory — idempotent enqueue + a direct Delivered mark, skipping the durable claim round-trip — while the durable
/// outbox item remains the crash backstop the resumption sweep re-drives idempotently.
/// </summary>
public sealed class RuntimeLiveDrainDeliveryTests
{
    private const string Wfid = "wfexec-1";
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    // (a) A 2-hop drain delivers the continuation intent in-memory: the durable outbox item ends Delivered without a
    // claim (fencing token still 0), the next work item is enqueued, and the loop counts the delivery then quiesces.
    [Fact]
    public async Task LiveDrain_TwoHopDrain_DeliversContinuationInMemory_AndQuiescesWithoutClaimRoundTrip()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var processor = NewProcessor(store, queue, liveDrain);
        // Hop 0's checkpoint commit appends one continuation intent, which the live drain then delivers in-memory.
        var drainer = new SeedingSchedulerDrainer(store, continuationsToSeed: 1);
        var orchestrator = NewOrchestrator(drainer, processor, liveDrain);

        var result = await orchestrator.DrainAsync(NewEnvelope(), new RuntimeSchedulerDrainRequest(Wfid));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(1, result.OutboxDeliveredCount);
        Assert.Equal(2, drainer.DrainCount); // loop continued past the delivered hop, then quiesced.

        var delivered = await store.FindAsync("outbox-hop-0");
        Assert.NotNull(delivered);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
        Assert.Equal(0, delivered.DeliveryFencingToken); // no claim taken.

        var enqueued = await queue.ListAsync(new RuntimeSchedulerWorkQuery(Wfid));
        Assert.Equal(["work-hop-0"], enqueued.Items.Select(item => item.WorkItemId));
    }

    // (b) After in-memory delivery the item is Delivered; a system-wide recovery sweep (claim path, no marker) finds
    // nothing deliverable and does not re-enqueue — no duplicate work item, no re-dispatch.
    [Fact]
    public async Task LiveDrain_SweepAfterInMemoryDelivery_IsNoOp()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var processor = NewProcessor(store, queue, liveDrain);
        var drainer = new SeedingSchedulerDrainer(store, continuationsToSeed: 1);
        var orchestrator = NewOrchestrator(drainer, processor, liveDrain);
        await orchestrator.DrainAsync(NewEnvelope(), new RuntimeSchedulerDrainRequest(Wfid));

        // A later sweep pass runs the same processor with no live-drain marker, system-wide (claim path).
        var sweepResult = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        Assert.Equal(0, sweepResult.AttemptedCount);
        var enqueued = await queue.ListAsync(new RuntimeSchedulerWorkQuery(Wfid));
        Assert.Equal(["work-hop-0"], enqueued.Items.Select(item => item.WorkItemId)); // still exactly one, no duplicate.
    }

    // (d) Crash window: the continuation was enqueued (idempotent) but the Delivered mark was lost, so the outbox item
    // is still Pending. The resumption sweep (claim path) re-drives it: the idempotent enqueue dedupes to a no-op and
    // the item converges to Delivered — no duplicate work item.
    [Fact]
    public async Task LiveDrain_CrashWindow_UnmarkedIntent_RedrivesIdempotentlyToNoOp()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var dispatcher = new RuntimeSchedulerPostCommitIntentDispatcher(queue);
        var processor = new RuntimePostCommitOutboxProcessor(store, dispatcher, new FixedTimeProvider(Now));

        // Pre-crash: the continuation work item was durably enqueued, but the outbox item stayed Pending (mark lost).
        var workItem = NewWorkItem("work-hop-0");
        await queue.EnqueueAsync(workItem);
        await store.AddPendingForTestingAsync(NewContinuationOutboxItem("outbox-hop-0", workItem));

        // The sweep re-drives the still-Pending intent through the durable claim path.
        var sweepResult = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        Assert.Equal(1, sweepResult.DeliveredCount);
        var delivered = await store.FindAsync("outbox-hop-0");
        Assert.NotNull(delivered);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
        var enqueued = await queue.ListAsync(new RuntimeSchedulerWorkQuery(Wfid));
        Assert.Equal(["work-hop-0"], enqueued.Items.Select(item => item.WorkItemId)); // idempotent enqueue: no duplicate.
    }

    private static RuntimePostCommitOutboxProcessor NewProcessor(
        InMemoryRuntimeCheckpointCommitStore store,
        InMemoryWorkflowSchedulerWorkQueue queue,
        IRuntimeLiveDrainDeliveryAccessor liveDrain) =>
        new(
            store,
            new RuntimeSchedulerPostCommitIntentDispatcher(queue),
            new FixedTimeProvider(Now),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            workflowDispatchStore: null,
            logger: null,
            liveDrain,
            coalescingSessionAccessor: null);

    private static WorkflowDrainOrchestrator NewOrchestrator(
        IWorkflowSchedulerDrainer drainer,
        IRuntimePostCommitOutboxProcessor processor,
        IRuntimeLiveDrainDeliveryAccessor liveDrain) =>
        new(
            drainer,
            processor,
            schedulerDrainObservers: [],
            options: new WorkflowDrainOrchestratorOptions(),
            ownershipService: null,
            ownershipContextAccessor: null,
            liveDrainDeliveryAccessor: liveDrain,
            timeProvider: new FixedTimeProvider(Now));

    private static RuntimeSchedulerWorkItem NewWorkItem(string workItemId) =>
        new(
            workItemId: workItemId,
            workflowExecutionId: Wfid,
            commandId: $"command-{workItemId}",
            commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
            envelopeId: $"envelope-{workItemId}",
            idempotencyKey: $"{Wfid}:{workItemId}",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: 2);

    private static RuntimePostCommitOutboxItem NewContinuationOutboxItem(string outboxItemId, RuntimeSchedulerWorkItem workItem) =>
        new(
            outboxItemId: outboxItemId,
            intent: new RuntimePostCommitIntent(
                intentId: $"intent-{workItem.WorkItemId}",
                workflowExecutionId: Wfid,
                kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
                recordedAt: Now,
                activityExecutionId: null,
                idempotencyKey: $"checkpoint:{workItem.WorkItemId}",
                payload: JsonSerializer.SerializeToElement(workItem)),
            status: RuntimePostCommitOutboxStatus.Pending,
            recordedAt: Now,
            availableAt: Now);

    private static WorkflowExecutionCommandEnvelope NewEnvelope()
    {
        using var document = JsonDocument.Parse("""{"kind":"run"}""");
        var command = new WorkflowExecutionCommand(
            CommandId: "command-drain",
            WorkflowExecutionId: Wfid,
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: Now,
            Payload: document.RootElement.Clone(),
            Metadata: new Dictionary<string, string> { ["source"] = "test" });

        return new(
            envelopeId: "envelope-drain",
            workflowExecutionId: Wfid,
            command: command,
            idempotencyKey: $"{Wfid}:command-drain",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now,
            sequence: 1,
            metadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    // Simulates the scheduler drainer half of the loop: each DrainAsync represents draining one hop's work. The first
    // `continuationsToSeed` cycles append one EnqueueSchedulerWork continuation intent to the durable outbox (as a real
    // checkpoint commit would), so the orchestrator's outbox step has an intent to deliver in-memory that cycle.
    private sealed class SeedingSchedulerDrainer(
        InMemoryRuntimeCheckpointCommitStore store,
        int continuationsToSeed) : IWorkflowSchedulerDrainer
    {
        public int DrainCount { get; private set; }

        public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
        {
            var hop = DrainCount;
            DrainCount++;
            if (hop < continuationsToSeed)
            {
                var workItem = NewWorkItem($"work-hop-{hop}");
                await store.AddPendingForTestingAsync(NewContinuationOutboxItem($"outbox-hop-{hop}", workItem), cancellationToken);
            }

            return new RuntimeSchedulerDrainResult(
                workflowExecutionId: request.WorkflowExecutionId,
                startedAt: Now,
                completedAt: Now,
                items: []);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
