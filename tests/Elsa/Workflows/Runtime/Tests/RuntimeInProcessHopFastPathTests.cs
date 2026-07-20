using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// WU-3 (spec 109, ADR 0031 follow-up item (a)): the in-process-hop payload short-circuit. While a live drain owns an
/// execution's delivery, the checkpoint committer publishes the already-materialized continuation work item onto the
/// drain-scoped carrier and the scheduler post-commit intent dispatcher takes it instead of deserializing the durable
/// intent payload. These are the seam-level tests for the dispatcher + carrier: the durable serialized payload stays
/// authoritative, so every absence (fast path off, no live-drain scope, different execution, post-crash empty scope,
/// second delivery) falls back to the deserialize path and produces the same enqueue. The end-to-end byte-identical
/// ENABLED-vs-DISABLED guardrail (follow-up item (c)) lives in Elsa.Activities.Runtime.Tests
/// (RuntimeInProcessHopFastPathGuardrailTests) where a real multi-hop drain can be driven.
/// </summary>
public sealed class RuntimeInProcessHopFastPathTests
{
    private const string Wfid = "wfexec-1";
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    // Fast path engages: with the live-drain scope owning the execution and the cached continuation published, the
    // dispatcher enqueues the CACHED work item and never reads the durable payload — proven by making the durable
    // payload serialize a DIFFERENT work item id. A byte-mismatch here would surface as the wrong id being enqueued.
    [Fact]
    public async Task FastPath_WithCachedContinuation_EnqueuesCachedItem_WithoutDeserializingPayload()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var dispatcher = new RuntimeSchedulerPostCommitIntentDispatcher(queue, liveDrain);

        var scope = new RuntimeLiveDrainDeliveryScope(Wfid);
        scope.PublishHopWorkItem("intent-1", NewWorkItem("work-CACHED"));
        var intent = NewContinuationIntent("intent-1", payloadWorkItemId: "work-DURABLE");

        using (liveDrain.Push(scope))
            await dispatcher.DispatchAsync(intent);

        var enqueued = await queue.ListAsync(new RuntimeSchedulerWorkQuery(Wfid));
        Assert.Equal(["work-CACHED"], enqueued.Items.Select(item => item.WorkItemId));
    }

    // The toggle (follow-up item (c) kill switch): with the fast path disabled the cache is ignored even when present
    // and the owning scope is ambient — the durable payload is deserialized, so the DURABLE id is enqueued.
    [Fact]
    public async Task FastPath_Disabled_IgnoresCache_AndDeserializesDurablePayload()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var dispatcher = new RuntimeSchedulerPostCommitIntentDispatcher(
            queue, liveDrain, new RuntimeInProcessHopFastPathOptions { Enabled = false });

        var scope = new RuntimeLiveDrainDeliveryScope(Wfid);
        scope.PublishHopWorkItem("intent-1", NewWorkItem("work-CACHED"));
        var intent = NewContinuationIntent("intent-1", payloadWorkItemId: "work-DURABLE");

        using (liveDrain.Push(scope))
            await dispatcher.DispatchAsync(intent);

        var enqueued = await queue.ListAsync(new RuntimeSchedulerWorkQuery(Wfid));
        Assert.Equal(["work-DURABLE"], enqueued.Items.Select(item => item.WorkItemId));
    }

    // Absence fallback: no cached entry (e.g. a sweep-delivered item) under an owning scope deserializes the durable
    // payload — the fast path is strictly optional.
    [Fact]
    public async Task Absence_NoCachedContinuation_DeserializesDurablePayload()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var dispatcher = new RuntimeSchedulerPostCommitIntentDispatcher(queue, liveDrain);
        var intent = NewContinuationIntent("intent-1", payloadWorkItemId: "work-DURABLE");

        using (liveDrain.Push(new RuntimeLiveDrainDeliveryScope(Wfid)))
            await dispatcher.DispatchAsync(intent);

        var enqueued = await queue.ListAsync(new RuntimeSchedulerWorkQuery(Wfid));
        Assert.Equal(["work-DURABLE"], enqueued.Items.Select(item => item.WorkItemId));
    }

    // A live-drain scope for a DIFFERENT execution must not divert this execution's delivery to the cache (mirrors
    // spec 106 FR-005): the durable payload is deserialized.
    [Fact]
    public async Task DifferentExecutionScope_DoesNotConsumeCache_Deserializes()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var dispatcher = new RuntimeSchedulerPostCommitIntentDispatcher(queue, liveDrain);

        var otherScope = new RuntimeLiveDrainDeliveryScope("wfexec-other");
        otherScope.PublishHopWorkItem("intent-1", NewWorkItem("work-CACHED"));
        var intent = NewContinuationIntent("intent-1", payloadWorkItemId: "work-DURABLE");

        using (liveDrain.Push(otherScope))
            await dispatcher.DispatchAsync(intent);

        var enqueued = await queue.ListAsync(new RuntimeSchedulerWorkQuery(Wfid));
        Assert.Equal(["work-DURABLE"], enqueued.Items.Select(item => item.WorkItemId));
    }

    // No ambient live-drain scope at all (the default sweep/recovery path) deserializes the durable payload.
    [Fact]
    public async Task NoLiveDrainScope_Deserializes()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var dispatcher = new RuntimeSchedulerPostCommitIntentDispatcher(queue, new AsyncLocalRuntimeLiveDrainDeliveryAccessor());
        var intent = NewContinuationIntent("intent-1", payloadWorkItemId: "work-DURABLE");

        await dispatcher.DispatchAsync(intent);

        var enqueued = await queue.ListAsync(new RuntimeSchedulerWorkQuery(Wfid));
        Assert.Equal(["work-DURABLE"], enqueued.Items.Select(item => item.WorkItemId));
    }

    // Crash window: an item was cached in a live drain that then 'crashed' before dispatch. Recovery is a fresh
    // process/scope with an empty carrier reading the still-durable outbox payload — it redrives from the serialized
    // form and enqueues exactly once, with no dependence on the lost in-memory object.
    [Fact]
    public async Task CrashWindow_CachedItemLostWithScope_RedrivesFromDurablePayload()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var dispatcher = new RuntimeSchedulerPostCommitIntentDispatcher(queue, liveDrain);
        var intent = NewContinuationIntent("intent-1", payloadWorkItemId: "work-DURABLE");

        // Pre-crash drain: publish onto its scope, then the drain 'crashes' before delivering (scope disposed, carrier
        // discarded with it). Nothing was enqueued.
        using (liveDrain.Push(new RuntimeLiveDrainDeliveryScope(Wfid)) )
        {
            liveDrain.Current!.PublishHopWorkItem("intent-1", NewWorkItem("work-CACHED"));
        }

        // Recovery: a fresh drain scope (or none) has an empty carrier; the durable outbox payload is re-driven.
        using (liveDrain.Push(new RuntimeLiveDrainDeliveryScope(Wfid)))
            await dispatcher.DispatchAsync(intent);

        var enqueued = await queue.ListAsync(new RuntimeSchedulerWorkQuery(Wfid));
        Assert.Equal(["work-DURABLE"], enqueued.Items.Select(item => item.WorkItemId));
    }

    // Remove-on-take: the cache hands off exactly once. A redelivered intent (idempotent enqueue dedupes it) falls
    // through to the durable payload the second time rather than re-reading a stale cached object.
    [Fact]
    public async Task Take_IsSingleUse_SecondDeliveryDeserializes()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var dispatcher = new RuntimeSchedulerPostCommitIntentDispatcher(queue, liveDrain);

        var scope = new RuntimeLiveDrainDeliveryScope(Wfid);
        // Cached and durable carry the SAME id so the idempotent enqueue dedupes across the two deliveries.
        scope.PublishHopWorkItem("intent-1", NewWorkItem("work-1"));
        var intent = NewContinuationIntent("intent-1", payloadWorkItemId: "work-1");

        using (liveDrain.Push(scope))
        {
            await dispatcher.DispatchAsync(intent); // fast path takes the cached item.
            Assert.False(scope.TryTakeHopWorkItem("intent-1", out _)); // consumed.
            await dispatcher.DispatchAsync(intent); // second delivery: cache empty -> deserialize, dedupe no-op.
        }

        var enqueued = await queue.ListAsync(new RuntimeSchedulerWorkQuery(Wfid));
        Assert.Equal(["work-1"], enqueued.Items.Select(item => item.WorkItemId));
    }

    // The cached path is held to the same contract as the durable path: an execution-id mismatch between the intent and
    // the (cached) work item is rejected exactly as a deserialized one would be.
    [Fact]
    public async Task CachedItem_ExecutionIdMismatch_IsRejected()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var dispatcher = new RuntimeSchedulerPostCommitIntentDispatcher(queue, liveDrain);

        var scope = new RuntimeLiveDrainDeliveryScope(Wfid);
        scope.PublishHopWorkItem("intent-1", NewWorkItemForExecution("work-1", "wfexec-other"));
        var intent = NewContinuationIntent("intent-1", payloadWorkItemId: "work-1");

        using (liveDrain.Push(scope))
            await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(intent).AsTask());
    }

    // Committer side: after a successful commit, each EnqueueSchedulerWork continuation's materialized work item is
    // published onto the owning live drain's carrier keyed by intent id, ready for the dispatcher to take.
    [Fact]
    public async Task Committer_PublishesContinuation_ToOwningLiveDrainCarrier()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var committer = NewCommitter(store, liveDrain, enabled: true, coalescing: null);
        var scope = new RuntimeLiveDrainDeliveryScope(Wfid);
        var (intent, commit) = NewCommitWithContinuation("work-next");

        RuntimeCheckpointCommitResult result;
        using (liveDrain.Push(scope))
            result = await committer.CommitAsync(commit);

        Assert.True(result.Succeeded);
        Assert.True(scope.TryTakeHopWorkItem(intent.IntentId, out var published));
        Assert.Equal("work-next", published.WorkItemId);
    }

    // No publish when the fast path is disabled: the durable outbox item is still committed, but the carrier stays empty
    // so delivery deserializes.
    [Fact]
    public async Task Committer_DoesNotPublish_WhenFastPathDisabled()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var committer = NewCommitter(store, liveDrain, enabled: false, coalescing: null);
        var scope = new RuntimeLiveDrainDeliveryScope(Wfid);
        var (intent, commit) = NewCommitWithContinuation("work-next");

        using (liveDrain.Push(scope))
            await committer.CommitAsync(commit);

        Assert.False(scope.TryTakeHopWorkItem(intent.IntentId, out _));
    }

    // No publish when a coalescing session owns the execution: the coalescing overlay is authoritative on continuation
    // delivery (spec 106 FR-003), so the fast path stands down even though a live-drain scope is (defensively) ambient.
    [Fact]
    public async Task Committer_DoesNotPublish_WhenCoalescingSessionOwnsExecution()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var coalescing = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var committer = NewCommitter(store, liveDrain, enabled: true, coalescing: coalescing);
        var scope = new RuntimeLiveDrainDeliveryScope(Wfid);
        var (intent, commit) = NewCommitWithContinuation("work-next");
        var session = new RuntimeCoalescingSession(Wfid, new InMemoryWorkflowSchedulerWorkQueue(), new CoalescingRuntimeCheckpointPersistenceOptions());

        using (liveDrain.Push(scope))
        using (coalescing.Push(session))
            await committer.CommitAsync(commit);

        Assert.False(scope.TryTakeHopWorkItem(intent.IntentId, out _));
    }

    // No publish when no live drain owns the execution (e.g. a recovery-path commit): the carrier is scope-owned, so
    // there is nowhere to publish and delivery takes the durable path.
    [Fact]
    public async Task Committer_DoesNotPublish_WhenNoLiveDrainScope()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var committer = NewCommitter(store, liveDrain, enabled: true, coalescing: null);
        var scope = new RuntimeLiveDrainDeliveryScope(Wfid);
        var (intent, commit) = NewCommitWithContinuation("work-next");

        // No scope pushed for this execution during the commit.
        await committer.CommitAsync(commit);

        Assert.False(scope.TryTakeHopWorkItem(intent.IntentId, out _));
    }

    private static RuntimeCheckpointCommitter NewCommitter(
        InMemoryRuntimeCheckpointCommitStore store,
        IRuntimeLiveDrainDeliveryAccessor liveDrain,
        bool enabled,
        IRuntimeCoalescingSessionAccessor? coalescing) =>
        new(
            new ImmediateRuntimeCheckpointPersistencePolicy(),
            store,
            ownershipContextAccessor: null,
            tracer: null,
            enrichers: [],
            intentHandlerContributions: [],
            consumedWorkClaimAccessor: null,
            coalescingSessionAccessor: coalescing,
            liveDrainDeliveryAccessor: liveDrain,
            inProcessHopFastPathOptions: new RuntimeInProcessHopFastPathOptions { Enabled = enabled });

    private static (RuntimePostCommitIntent Intent, RuntimeCheckpointCommit Commit) NewCommitWithContinuation(string continuationWorkItemId)
    {
        var source = NewWorkItem("work-source");
        var continuation = NewWorkItem(continuationWorkItemId);
        var intent = SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(source, "actexec-1", continuation, Now);
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit-{continuationWorkItemId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint-{continuationWorkItemId}",
                Name: RuntimeCheckpointNames.ActivityCompleted,
                WorkflowExecutionId: Wfid,
                OccurredAt: Now,
                ActivityExecutionIds: [],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            PostCommitIntents: [intent],
            Metadata: new Dictionary<string, string>());
        return (intent, commit);
    }

    private static RuntimeSchedulerWorkItem NewWorkItem(string workItemId) => NewWorkItemForExecution(workItemId, Wfid);

    private static RuntimeSchedulerWorkItem NewWorkItemForExecution(string workItemId, string workflowExecutionId) =>
        new(
            workItemId: workItemId,
            workflowExecutionId: workflowExecutionId,
            commandId: $"command-{workItemId}",
            commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
            envelopeId: $"envelope-{workItemId}",
            idempotencyKey: $"{workflowExecutionId}:{workItemId}",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: 2);

    private static RuntimePostCommitIntent NewContinuationIntent(string intentId, string payloadWorkItemId) =>
        new(
            intentId: intentId,
            workflowExecutionId: Wfid,
            kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            recordedAt: Now,
            activityExecutionId: null,
            idempotencyKey: $"checkpoint:{intentId}",
            payload: JsonSerializer.SerializeToElement(NewWorkItem(payloadWorkItemId)));
}
