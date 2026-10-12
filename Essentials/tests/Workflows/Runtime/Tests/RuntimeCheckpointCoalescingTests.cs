using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workflows.Runtime.Tests;

// End-to-end coverage for the checkpoint-folding machinery every persistence mode drains through. A workflow is driven
// to completion through the in-process agent over an in-memory durable substrate, and the drain's session is observed to
// run in the mode the run resolved to.
public sealed class RuntimeCheckpointCoalescingTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddPersistenceModes_SelectsCoalescingPolicyAndDecoratesStores()
    {
        var services = CreateCoalescingServices();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<CoalescingRuntimeCheckpointPersistencePolicy>(provider.GetRequiredService<IRuntimeCheckpointPersistencePolicy>());
        Assert.IsType<CoalescingRuntimeCheckpointCommitStore>(provider.GetRequiredService<IRuntimeCheckpointCommitStore>());
        Assert.IsType<CoalescingWorkflowSchedulerWorkQueue>(provider.GetRequiredService<IWorkflowSchedulerWorkQueue>());
        Assert.IsType<CoalescingRuntimePostCommitOutboxStore>(provider.GetRequiredService<IRuntimePostCommitOutboxStore>());
        Assert.Same(
            provider.GetRequiredService<IRuntimePostCommitOutboxStore>(),
            provider.GetRequiredService<IPostCommitOutboxLookupStore>());
        Assert.IsType<CoalescingWorkflowExecutionStateStore>(provider.GetRequiredService<IWorkflowExecutionStateStore>());
        Assert.IsType<CoalescingActivityExecutionStateStore>(provider.GetRequiredService<IActivityExecutionStateStore>());
        Assert.IsType<CoalescingDurableValueStateStore>(provider.GetRequiredService<IDurableValueStateStore>());
        Assert.IsType<CoalescingSchedulerStateStore>(provider.GetRequiredService<ISchedulerStateStore>());
        Assert.IsType<CoalescingActivityExecutionInspectionStore>(provider.GetRequiredService<IActivityExecutionInspectionStore>());
        Assert.NotNull(provider.GetRequiredService<IRuntimeCoalescingSessionAccessor>());
        Assert.NotNull(provider.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>());
    }

    // Kept beside the composition test: idempotence, repeated registration keeps the explicit lifetime.
    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    public async Task RepeatedRegistrationPreservesTheExplicitDrainFactoryLifetime(ServiceLifetime lifetime)
    {
        var services = CreateCoalescingServices(collection =>
            collection.Add(new ServiceDescriptor(
                typeof(IRuntimeCoalescingDrainScopeFactory),
                typeof(ExplicitCoalescingDrainScopeFactory),
                lifetime)));
        var afterFirstRegistration = services.ToArray();

        services.AddPersistenceModes();

        Assert.Equal(afterFirstRegistration, services);
        Assert.Equal(lifetime, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRuntimeCoalescingDrainScopeFactory)).Lifetime);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>();
        var firstAgain = firstScope.ServiceProvider.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>();
        var second = secondScope.ServiceProvider.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>();

        Assert.IsType<ExplicitCoalescingDrainScopeFactory>(first);
        Assert.Same(first, firstAgain);
        if (lifetime == ServiceLifetime.Singleton)
            Assert.Same(first, second);
        else
            Assert.NotSame(first, second);
    }

    [Fact]
    public async Task AdvanceInnerQueue_RemovesConsumedSeededItemsByIdentity_WhenListOrderDiffersFromDequeueOrder()
    {
        const string workflowExecutionId = "wfexec-list-dequeue-order";
        var inner = new InMemoryWorkflowSchedulerWorkQueue();

        // ListAsync is key-ordered, while the in-memory queue's FIFO head is insertion-ordered.
        foreach (var sequence in new long[] { 30, 5, 20 })
            await inner.EnqueueAsync(NewReconciliationWorkItem(workflowExecutionId, sequence));

        Assert.Equal(
            ["work-5", "work-20", "work-30"],
            (await inner.ListAllAsync(new RuntimeSchedulerWorkQuery(workflowExecutionId))).Select(item => item.WorkItemId));
        Assert.Equal(
            "work-30",
            (await inner.ListNextWorkItemsAsync([workflowExecutionId])).Single().Value.WorkItemId);

        var session = new RuntimeCoalescingSession(
            workflowExecutionId,
            inner,
            new PersistenceModeOptions());
        await session.EnsureQueueSeededAsync(CancellationToken.None);

        Assert.Equal("work-5", (await session.DequeueOverlayAsync(CancellationToken.None))!.WorkItemId);
        Assert.Equal("work-20", (await session.DequeueOverlayAsync(CancellationToken.None))!.WorkItemId);

        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: false, CancellationToken.None);

        Assert.Equal(
            ["work-30"],
            (await inner.ListAllAsync(new RuntimeSchedulerWorkQuery(workflowExecutionId))).Select(item => item.WorkItemId));
        Assert.Equal(
            ["work-30"],
            (await session.ListOverlayAsync(new RuntimeSchedulerWorkQuery(workflowExecutionId), CancellationToken.None))
                .Items.Select(item => item.WorkItemId));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AdvanceInnerQueue_PreservesUnobservedConcurrentWork_AndRemovesOnlyKnownConsumedDelivery(
        bool knownConsumedDelivery, bool addsNewContinuation)
    {
        const string workflowExecutionId = "wfexec-concurrent-delivery";
        var inner = new InMemoryWorkflowSchedulerWorkQueue();
        await inner.EnqueueAsync(NewReconciliationWorkItem(workflowExecutionId, 10));
        var session = new RuntimeCoalescingSession(workflowExecutionId, inner, new PersistenceModeOptions());
        await session.EnsureQueueSeededAsync(CancellationToken.None);
        Assert.Equal("work-10", (await session.DequeueOverlayAsync(CancellationToken.None))!.WorkItemId);
        var delivered = NewReconciliationWorkItem(workflowExecutionId, 5);
        if (knownConsumedDelivery)
        {
            await session.EnqueueOverlayAsync(delivered, CancellationToken.None);
            Assert.Equal("work-5", (await session.DequeueOverlayAsync(CancellationToken.None))!.WorkItemId);
        }

        // This represents a separate deliverer; it changes only the durable queue, outside the active overlay.
        await inner.EnqueueAsync(delivered);
        session.MarkOutboxDurablyPersisted(NewContinuationIntentCommit(workflowExecutionId, 1).StateChanges.PostCommitOutbox);
        if (addsNewContinuation)
        {
            await session.EnqueueOverlayAsync(NewReconciliationWorkItem(workflowExecutionId, 20), CancellationToken.None);
            await session.AdvanceInnerQueueAsync(consumeInFlightClaims: false, CancellationToken.None);
            Assert.Equal("work-20", (await session.DequeueOverlayAsync(CancellationToken.None))!.WorkItemId);
        }
        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: false, CancellationToken.None);

        var durable = await inner.ListAllAsync(workflowExecutionId);
        if (knownConsumedDelivery)
            Assert.Empty(durable);
        else
            Assert.Equal("work-5", Assert.Single(durable).WorkItemId);
    }

    [Fact]
    public async Task LegacyQueueWithoutTargetedDeletion_DisablesBufferingBeforeTheFirstCheckpoint()
    {
        var services = CreateCoalescingServices(collection =>
            collection.AddSingleton<IWorkflowSchedulerWorkQueue>(new LegacySchedulerWorkQueue()));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>();
        await using (var scope = factory.Begin("wfexec-legacy", new ResolvedPersistenceMode(PersistenceMode.Balanced, RunHasRecord: true)))
        {
            Assert.False(scope.Session.IsActive);
            await provider.GetRequiredService<IRuntimeCheckpointCommitStore>().CommitAsync(
                NewEmptyCommit("wfexec-legacy", 1, RuntimeCheckpointNames.ActivityScheduled),
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred));
            Assert.False(scope.Session.HasBufferedChanges);
            var recorded = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
            Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, recorded.Decision.Mode);
        }

        await SeedAsync(provider);
        await EnqueueStartAsync(provider);
        Assert.Equal(WorkflowExecutionStatus.Completed,
            (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1"))!.Status);
        Assert.Empty(await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync("wfexec-1"));
    }

    private static RuntimeSchedulerWorkItem NewReconciliationWorkItem(string workflowExecutionId, long sequence) =>
        new($"work-{sequence}", workflowExecutionId, $"command-{sequence}", WorkflowExecutionCommandKind.ScheduleActivity,
            $"envelope-{sequence}", $"idempotency-{sequence}", Now, Now, sequence);

    private sealed class LegacySchedulerWorkQueue : IWorkflowSchedulerWorkQueue
    {
        private readonly InMemoryWorkflowSchedulerWorkQueue _inner = new();

        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem item, CancellationToken cancellationToken = default) =>
            _inner.EnqueueAsync(item, cancellationToken);
        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) =>
            _inner.ListAsync(query, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            _inner.DequeueAsync(workflowExecutionId, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) =>
            _inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);
    }

    [Fact]
    public async Task CoalescingOutboxLookup_ConsultsActiveOverlayBeforeDurableInner()
    {
        var inner = new InMemoryRuntimeCheckpointCommitStore();
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var store = new CoalescingRuntimePostCommitOutboxStore(
            new CoalescingInner<IRuntimePostCommitOutboxStore>(inner),
            accessor,
            new InMemoryWorkflowExecutionStateStore());
        var session = new RuntimeCoalescingSession(
            "parent-dispatch",
            new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions());
        var commit = NewDispatchBoundaryCommit();
        session.BufferDeferred(commit);
        var expected = Assert.Single(commit.StateChanges.PostCommitOutbox).State;

        using (accessor.Push(session))
        {
            var found = await store.FindAsync(expected.OutboxItemId);
            Assert.Same(expected, found);
        }

        Assert.Null(await store.FindAsync(expected.OutboxItemId));
    }

    /// <summary>
    /// #2225: a drain asks who else holds its execution's continuations. Only the drain that owns a session ever claims an
    /// overlay item, so the decorator answers from the durable store, with or without an active session.
    /// </summary>
    [Fact]
    public async Task CoalescingOutboxListClaimed_AnswersFromTheDurableStoreWithOrWithoutASession()
    {
        const string workflowExecutionId = "wfexec-held-elsewhere";
        var inner = new InMemoryRuntimeCheckpointCommitStore();
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var store = new CoalescingRuntimePostCommitOutboxStore(
            new CoalescingInner<IRuntimePostCommitOutboxStore>(inner),
            accessor,
            new InMemoryWorkflowExecutionStateStore());
        await inner.AddPendingForTestingAsync(new RuntimePostCommitOutboxItem(
            "outbox-held",
            new RuntimePostCommitIntent("intent-held", workflowExecutionId, RuntimePostCommitIntentKinds.EnqueueSchedulerWork, Now, null, null, null),
            RuntimePostCommitOutboxStatus.Pending,
            Now,
            Now));
        var claim = Assert.Single(await inner.ClaimAsync(new RuntimePostCommitOutboxClaimRequest("sweep", Now, TimeSpan.FromMinutes(1), limit: 1)));
        var session = new RuntimeCoalescingSession(
            workflowExecutionId,
            new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions());
        var query = new RuntimePostCommitOutboxClaimedQuery(workflowExecutionId, RuntimePostCommitIntentKinds.EnqueueSchedulerWork, limit: 10);

        Assert.Equal([claim.OutboxItemId], (await store.ListClaimedAsync(query)).Select(item => item.OutboxItemId));
        using (accessor.Push(session))
            Assert.Equal([claim.OutboxItemId], (await store.ListClaimedAsync(query)).Select(item => item.OutboxItemId));
    }

    [Fact]
    public async Task ClaimsAcquiredAfterBoundaryFlush_AreCompletedAgainstDurableQueue()
    {
        const string workflowExecutionId = "wfexec-claim-boundary";
        var inner = new InMemoryWorkflowSchedulerWorkQueue();
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var queue = new CoalescingWorkflowSchedulerWorkQueue(
            new CoalescingInner<IWorkflowSchedulerWorkQueue>(inner),
            accessor);
        var session = new RuntimeCoalescingSession(
            workflowExecutionId,
            inner,
            new PersistenceModeOptions());
        var workItem = new RuntimeSchedulerWorkItem(
            "work-1",
            workflowExecutionId,
            "command-1",
            WorkflowExecutionCommandKind.Start,
            "envelope-1",
            "idempotency-1",
            Now,
            Now);
        await inner.EnqueueAsync(workItem);

        using (accessor.Push(session))
        {
            var overlayClaim = await queue.ClaimAsync(NewClaimRequest(workflowExecutionId));
            Assert.NotNull(overlayClaim);

            session.Deactivate();
            Assert.True((await queue.CompleteClaimAsync(overlayClaim!)).Succeeded);

            var durableClaim = await queue.ClaimAsync(NewClaimRequest(workflowExecutionId));
            Assert.NotNull(durableClaim);
            Assert.True((await queue.CompleteClaimAsync(durableClaim!)).Succeeded);
        }

        Assert.Empty(await inner.ListAllAsync(new RuntimeSchedulerWorkQuery(workflowExecutionId)));
    }

    /// <summary>
    /// #2188: the decorator must forward claimable backlog discovery. Not forwarding the capability would quietly put a
    /// coalescing host back on the unfiltered first-page listing that starved newer executions.
    /// </summary>
    [Fact]
    public async Task BacklogDiscovery_ForwardsClaimableDiscoveryToTheDurableQueue()
    {
        var inner = new InMemoryWorkflowSchedulerWorkQueue();
        var queue = new CoalescingWorkflowSchedulerWorkQueue(
            new CoalescingInner<IWorkflowSchedulerWorkQueue>(inner),
            new AsyncLocalRuntimeCoalescingSessionAccessor());
        foreach (var workflowExecutionId in new[] { "wfexec-claimed", "wfexec-visible" })
        {
            await inner.EnqueueAsync(new RuntimeSchedulerWorkItem(
                "work-1",
                workflowExecutionId,
                "command-1",
                WorkflowExecutionCommandKind.Start,
                "envelope-1",
                "idempotency-1",
                Now,
                Now));
        }
        Assert.NotNull(await inner.ClaimAsync(NewClaimRequest("wfexec-claimed")));

        Assert.True(queue.SupportsClaimableBacklogDiscovery);
        Assert.Equal(["wfexec-visible"], await queue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(Now)));
    }

    [Fact]
    public async Task ActivityAttemptBoundary_FlushesBeforeActivation_AndStartsFreshSegment()
    {
        const string workflowExecutionId = "wfexec-1";
        var innerStore = new InMemoryRuntimeCheckpointCommitStore();
        var session = new RuntimeCoalescingSession(
            workflowExecutionId,
            new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions());
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            new FixedCoalescingSessionAccessor(session));
        var startedState = NewRunningActivityState();
        var claimedState = startedState with
        {
            Metadata = new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.ActivityAttemptActivationClaim] = "attempt-1"
            }
        };

        await store.CommitAsync(
            NewEmptyCommit(workflowExecutionId, 1, RuntimeCheckpointNames.ActivityStarted) with
            {
                StateChanges = ActivityUpsert(startedState)
            },
            new(RuntimeCheckpointPersistenceMode.Deferred));
        await store.CommitAsync(
            NewEmptyCommit(workflowExecutionId, 2, RuntimeCheckpointNames.ActivityAttemptClaimed) with
            {
                StateChanges = ActivityUpsert(claimedState)
            },
            new(RuntimeCheckpointPersistenceMode.Immediate));

        var firstBoundary = Assert.Single(innerStore.ListCommits()).Commit;
        Assert.Equal(RuntimeCheckpointNames.ActivityAttemptClaimed, firstBoundary.Checkpoint.Name);
        Assert.True(session.IsActive);
        Assert.Equal(0, session.ActivityCount);
        Assert.True(session.TryGetActivity("actexec-1", out var overlayState, out var tombstoned));
        Assert.False(tombstoned);
        Assert.Equal("attempt-1", overlayState!.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);

        await store.CommitAsync(
            NewEmptyCommit(workflowExecutionId, 3, RuntimeCheckpointNames.ActivityCompleted),
            new(RuntimeCheckpointPersistenceMode.Deferred));
        Assert.True(session.HasBufferedChanges);
        Assert.Single(innerStore.ListCommits());

        await store.CommitAsync(
            NewEmptyCommit(workflowExecutionId, 4, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.False(session.IsActive);
        Assert.Equal(2, innerStore.ListCommits().Count);
    }

    // #2601: a boundary's folded commit lands durably before the decorator reconciles the durable queue. When that
    // reconciliation then fails (here: tracked work removed outside the drain), the drain records the failure and carries
    // on to its quiescence flush. The buffered segment is already in the durable commit, so the session must not keep it:
    // a quiescence flush would re-apply the older buffered state over the newer durable one.
    [Fact]
    public async Task BoundaryReconciliationFailure_AfterTheDurableCommit_LeavesNothingForTheQuiescenceFlush()
    {
        const string workflowExecutionId = "wfexec-1";
        var innerQueue = new InMemoryWorkflowSchedulerWorkQueue();
        await innerQueue.EnqueueAsync(NewReconciliationWorkItem(workflowExecutionId, 10));
        await innerQueue.EnqueueAsync(NewReconciliationWorkItem(workflowExecutionId, 20));
        var innerStore = new InMemoryRuntimeCheckpointCommitStore();
        var session = new RuntimeCoalescingSession(
            workflowExecutionId,
            innerQueue,
            new PersistenceModeOptions());
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            new FixedCoalescingSessionAccessor(session));
        await session.EnsureQueueSeededAsync(CancellationToken.None);
        Assert.Equal("work-10", (await session.DequeueOverlayAsync(CancellationToken.None))!.WorkItemId);

        await store.CommitAsync(
            NewEmptyCommit(workflowExecutionId, 1, RuntimeCheckpointNames.ActivityStarted) with
            {
                StateChanges = ActivityUpsert(NewRunningActivityState())
            },
            new(RuntimeCheckpointPersistenceMode.Deferred));
        Assert.True(session.HasBufferedChanges);

        // A continuation the boundary must persist makes it re-read the durable queue, which no longer holds work-20.
        await session.EnqueueOverlayAsync(NewReconciliationWorkItem(workflowExecutionId, 30), CancellationToken.None);
        Assert.True(await innerQueue.DeleteAsync(workflowExecutionId, "work-20"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.CommitAsync(
            NewEmptyCommit(workflowExecutionId, 2, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate)));

        Assert.Equal(RuntimeCheckpointNames.WorkflowCompleted, Assert.Single(innerStore.ListCommits()).Commit.Checkpoint.Name);
        Assert.False(session.HasBufferedChanges);
        Assert.False(session.IsActive);
    }

    // The committer validated each commit on its own, but a boundary flush folds the buffered segment into a new commit that
    // can combine changes no single commit carried. The decorator validates that fold before the durable store sees it.
    [Fact]
    public async Task BoundaryFold_ThatCombinesValidCommitsIntoAnInvalidCommit_IsRejectedBeforeTheDurableStore()
    {
        const string workflowExecutionId = "wfexec-1";
        var innerStore = new InMemoryRuntimeCheckpointCommitStore();
        var session = new RuntimeCoalescingSession(
            workflowExecutionId,
            new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions());
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            new FixedCoalescingSessionAccessor(session));
        var cleanup = NewEmptyCommit(workflowExecutionId, 1, RuntimeCheckpointNames.ActivityCancelled) with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                null, null, [], [], [], [], [],
                activityScopeCleanups: [new ActivityScopeCleanupRequest(workflowExecutionId, "scope-1", ["scope-1"], ["bookmark-1"], [], [])])
        };
        var bookmark = NewEmptyCommit(workflowExecutionId, 2, RuntimeCheckpointNames.BookmarkCreated) with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                null, null, [],
                [
                    new RuntimeStateChange<BookmarkState>(
                        "bookmark-1",
                        RuntimeStateChangeOperation.Upsert,
                        new BookmarkState("bookmark-1", workflowExecutionId, "actexec-1", "node-wait", "node-wait", "stimulus", "stimulus-hash", null, new Dictionary<string, string>(), Now, null),
                        new Dictionary<string, string>())
                ],
                [], [], [])
        };
        RuntimeCheckpointCommitValidator.Validate(cleanup);
        RuntimeCheckpointCommitValidator.Validate(bookmark);

        await store.CommitAsync(cleanup, new(RuntimeCheckpointPersistenceMode.Deferred));
        var exception = await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() =>
            store.CommitAsync(bookmark, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());

        Assert.Equal("Bookmark 'bookmark-1' cannot be both changed and deleted by activity-scope cleanup in one checkpoint commit.", exception.Message);
        Assert.Empty(innerStore.ListCommits());
    }

    // A ReplaySafe attempt-claim arrives as a Deferred decision (the coalescing policy
    // decided so from the checkpoint's profile metadata). Unlike the External/Immediate case above, it must NOT
    // flush before activation — it buffers into the overlay working set and folds forward into the next flushed
    // commit, so a hot loop of ReplaySafe activities collapses to one coalesced commit.
    [Fact]
    public async Task ReplaySafeAttemptClaim_IsDeferred_BuffersInsteadOfFlushing_AndFoldsForward()
    {
        const string workflowExecutionId = "wfexec-1";
        var innerStore = new InMemoryRuntimeCheckpointCommitStore();
        var session = new RuntimeCoalescingSession(
            workflowExecutionId,
            new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions());
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            new FixedCoalescingSessionAccessor(session));
        var claimedState = NewRunningActivityState() with
        {
            Metadata = new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.ActivityAttemptActivationClaim] = "attempt-1"
            }
        };

        // The ReplaySafe claim: policy decided Deferred, so the store buffers it — nothing durable yet.
        await store.CommitAsync(
            NewEmptyCommit(workflowExecutionId, 1, RuntimeCheckpointNames.ActivityAttemptClaimed) with
            {
                StateChanges = ActivityUpsert(claimedState)
            },
            new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Empty(innerStore.ListCommits());
        Assert.True(session.IsActive);
        Assert.Equal(1, session.ActivityCount);
        Assert.True(session.TryGetActivity("actexec-1", out var overlayState, out var tombstoned));
        Assert.False(tombstoned);
        Assert.Equal("attempt-1", overlayState!.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);

        // The terminal boundary folds the buffered claim forward into one durable commit.
        await store.CommitAsync(
            NewEmptyCommit(workflowExecutionId, 2, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.False(session.IsActive);
        var folded = Assert.Single(innerStore.ListCommits()).Commit;
        Assert.Equal(RuntimeCheckpointNames.WorkflowCompleted, folded.Checkpoint.Name);
        Assert.Equal("attempt-1", Assert.Single(folded.StateChanges.ActivityExecutions).State.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);
    }

    [Fact]
    public async Task ActivityAttemptBoundary_WithPendingSegmentOutbox_HandsOwnershipToDurableStore()
    {
        const string workflowExecutionId = "parent-dispatch";
        var innerStore = new InMemoryRuntimeCheckpointCommitStore();
        var session = new RuntimeCoalescingSession(
            workflowExecutionId,
            new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions());
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            new FixedCoalescingSessionAccessor(session));
        var pendingOutbox = Assert.Single(NewDispatchBoundaryCommit().StateChanges.PostCommitOutbox);
        var deferred = NewEmptyCommit(workflowExecutionId, 1, RuntimeCheckpointNames.ActivityCompleted);

        await store.CommitAsync(
            deferred with
            {
                StateChanges = deferred.StateChanges.WithPostCommitOutbox([pendingOutbox])
            },
            new(RuntimeCheckpointPersistenceMode.Deferred));
        await store.CommitAsync(
            NewEmptyCommit(workflowExecutionId, 2, RuntimeCheckpointNames.ActivityAttemptClaimed),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.False(session.IsActive);
        var persisted = Assert.Single(innerStore.ListCommits()).Commit;
        Assert.Equal(RuntimeCheckpointNames.ActivityAttemptClaimed, persisted.Checkpoint.Name);
        Assert.Single(persisted.StateChanges.PostCommitOutbox);
    }

    [Fact]
    public async Task Coalescing_workflow_store_preserves_bounded_history_queries()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddPersistenceModes();

        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IWorkflowExecutionStateStore>();
        await store.SaveAsync(new WorkflowExecutionState(
            "wfexec-history",
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            WorkflowExecutionStatus.Completed,
            null,
            Now.AddMinutes(-2),
            Now.AddMinutes(-1),
            Now,
            Now,
            null,
            null,
            "tenant-1",
            new Dictionary<string, string>()));

        var page = await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 10));

        Assert.Equal("wfexec-history", Assert.Single(page.Items).WorkflowExecutionId);
    }

    [Fact]
    public async Task Coalesced_activity_pages_merge_overlay_without_traversing_the_inner_collection()
    {
        var (inner, session, store) = CreateCountingActivityExecutionStateStore();
        await inner.SaveAsync(Activity("act-a"));
        await inner.SaveAsync(Activity("act-c"));
        session.BufferDeferred(NewEmptyCommit("wfexec-1", 99, "overlay") with
        {
            StateChanges = ActivityUpsert(Activity("act-b"))
        });

        var first = await store.ListPageAsync(new ActivityExecutionStatePageQuery("wfexec-1", limit: 1));
        var second = await store.ListPageAsync(new ActivityExecutionStatePageQuery("wfexec-1", limit: 1, first.NextContinuationToken));
        var third = await store.ListPageAsync(new ActivityExecutionStatePageQuery("wfexec-1", limit: 1, second.NextContinuationToken));

        Assert.Equal("act-a", Assert.Single(first.Items).Execution.ActivityExecutionId);
        Assert.Equal("act-b", Assert.Single(second.Items).Execution.ActivityExecutionId);
        Assert.Equal("act-c", Assert.Single(third.Items).Execution.ActivityExecutionId);
        Assert.NotNull(first.NextContinuationToken);
        Assert.NotNull(second.NextContinuationToken);
        Assert.Null(third.NextContinuationToken);
        // The final durable row is replayed across the output-page boundary once, but look-ahead reuses the buffered
        // candidate within the second request instead of issuing the old duplicate provider call.
        Assert.Equal(3, inner.PageReadCount);
    }

    [Fact]
    public async Task Coalesced_page_merger_empty_terminal_page_is_read_once_for_large_overlay()
    {
        const string workflowExecutionId = "wfexec-1";
        var (inner, session, store) = CreateCountingActivityExecutionStateStore();
        var overlayRows = Enumerable.Range(0, 128)
            .Select(index => Activity($"overlay-{index:D3}"))
            .ToArray();
        session.BufferDeferred(NewEmptyCommit(workflowExecutionId, 100, "overlay") with
        {
            StateChanges = ActivityUpsert(overlayRows)
        });

        var page = await store.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 7));

        Assert.Equal(
            Enumerable.Range(0, 7).Select(index => $"overlay-{index:D3}"),
            page.Items.Select(state => state.Execution.ActivityExecutionId));
        Assert.NotNull(page.NextContinuationToken);
        Assert.Equal(1, inner.PageReadCount);
        Assert.All(inner.PageRequests, request => Assert.InRange(request.Limit, 1, 7));
    }

    [Fact]
    public async Task Coalesced_page_merger_retains_terminal_candidate_while_overlay_rows_are_emitted()
    {
        const string workflowExecutionId = "wfexec-1";
        var (inner, session, store) = CreateCountingActivityExecutionStateStore();
        await inner.SaveAsync(Activity("d"));
        session.BufferDeferred(NewEmptyCommit(workflowExecutionId, 101, "overlay") with
        {
            StateChanges = ActivityUpsert(Activity("a"), Activity("b"), Activity("c"))
        });

        var page = await store.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 4));

        Assert.Equal(["a", "b", "c", "d"], page.Items.Select(state => state.Execution.ActivityExecutionId));
        Assert.Null(page.NextContinuationToken);
        Assert.Equal(1, inner.PageReadCount);
    }

    [Fact]
    public async Task Coalesced_page_merger_preserves_interleaved_replacements_and_tombstones_across_pages()
    {
        const string workflowExecutionId = "wfexec-1";
        var (inner, session, store) = CreateCountingActivityExecutionStateStore();
        await inner.SaveAsync(Activity("a"));
        await inner.SaveAsync(Activity("c"));
        await inner.SaveAsync(Activity("e"));
        var replacement = Activity("c");
        session.BufferDeferred(NewEmptyCommit(workflowExecutionId, 102, "overlay") with
        {
            StateChanges = ActivityChanges(
                ActivityChange("b"),
                ActivityChange(replacement),
                ActivityChange("f"),
                ActivityDelete("e"))
        });

        var whole = await store.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 7));
        Assert.Equal(["a", "b", "c", "f"], whole.Items.Select(state => state.Execution.ActivityExecutionId));
        Assert.Same(replacement, Assert.Single(whole.Items, state => state.Execution.ActivityExecutionId == "c"));
        Assert.Null(whole.NextContinuationToken);
        Assert.Single(inner.PageRequests);
        Assert.Equal(7, Assert.Single(inner.PageRequests).Limit);

        inner.PageRequests.Clear();
        var first = await store.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 2));
        var firstReadPositions = inner.PageRequests.Select(request => request.ContinuationToken).ToArray();
        Assert.Equal(firstReadPositions.Length, firstReadPositions.Distinct().Count());
        Assert.InRange(firstReadPositions.Length, 1, 2);

        inner.PageRequests.Clear();
        var second = await store.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 2, first.NextContinuationToken));
        var secondReadPositions = inner.PageRequests.Select(request => request.ContinuationToken).ToArray();
        Assert.Equal(secondReadPositions.Length, secondReadPositions.Distinct().Count());
        Assert.InRange(secondReadPositions.Length, 0, 2);

        Assert.Equal(["a", "b"], first.Items.Select(state => state.Execution.ActivityExecutionId));
        Assert.Equal(["c", "f"], second.Items.Select(state => state.Execution.ActivityExecutionId));
        Assert.Same(replacement, Assert.Single(second.Items, state => state.Execution.ActivityExecutionId == "c"));
        Assert.Null(second.NextContinuationToken);
    }

    [Fact]
    public async Task Coalesced_page_merger_replays_a_bounded_page_at_an_output_boundary()
    {
        const string workflowExecutionId = "wfexec-1";
        var (inner, session, store) = CreateCountingActivityExecutionStateStore();
        var durable = Activity("c");
        await inner.SaveAsync(durable);
        session.BufferDeferred(NewEmptyCommit(workflowExecutionId, 103, "overlay") with
        {
            StateChanges = ActivityUpsert(Activity("a"), Activity("b"))
        });

        var first = await store.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 2));
        Assert.Equal(["a", "b"], first.Items.Select(state => state.Execution.ActivityExecutionId));
        Assert.NotNull(first.NextContinuationToken);
        Assert.Single(inner.PageRequests);
        Assert.DoesNotContain("\"c\"", DecodeCoalescingContinuationPayload(first.NextContinuationToken));

        inner.PageRequests.Clear();
        var second = await store.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 2, first.NextContinuationToken));
        Assert.Same(durable, Assert.Single(second.Items));
        Assert.Null(second.NextContinuationToken);
        Assert.Single(inner.PageRequests);
    }

    [Fact]
    public async Task Coalesced_page_merger_keeps_legacy_continuations_and_rejects_invalid_bindings()
    {
        const string workflowExecutionId = "wfexec-1";
        const string binding = "coalesced-activity-state:workflow:wfexec-1";
        var (inner, session, store) = CreateCountingActivityExecutionStateStore();
        await inner.SaveAsync(Activity("a"));
        await inner.SaveAsync(Activity("b"));
        session.BufferDeferred(NewEmptyCommit(workflowExecutionId, 104, "overlay"));

        var preChangeProviderPage = await inner.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 1));
        var legacyToken = EncodeLegacyCoalescingContinuation(
            binding,
            lastIdentity: "a",
            preChangeProviderPage.NextContinuationToken,
            exhausted: false);
        var resumed = await store.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 1, legacyToken));

        Assert.Equal("b", Assert.Single(resumed.Items).Execution.ActivityExecutionId);
        Assert.Null(resumed.NextContinuationToken);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 1, "malformed")));
        var checksumMismatch = EncodeLegacyCoalescingContinuation(binding, "a", null, exhausted: true).Split('.');
        checksumMismatch[2] = $"{(checksumMismatch[2][0] == 'A' ? 'B' : 'A')}{checksumMismatch[2][1..]}";
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 1, string.Join('.', checksumMismatch))));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.ListPageAsync(new ActivityExecutionStatePageQuery(
                workflowExecutionId,
                limit: 1,
                EncodeLegacyCoalescingContinuation("coalesced-activity-state:workflow:other", "a", null, exhausted: true))));
    }

    [Fact]
    public async Task Coalesced_page_merger_memoizes_empty_terminal_source_and_observes_cancellation()
    {
        const string workflowExecutionId = "wfexec-1";
        var (inner, session, store) = CreateCountingActivityExecutionStateStore();
        session.BufferDeferred(NewEmptyCommit(workflowExecutionId, 105, "overlay"));

        var empty = await store.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 2));
        Assert.Empty(empty.Items);
        Assert.Null(empty.NextContinuationToken);
        Assert.Equal(1, inner.PageReadCount);
        session.BufferDeferred(NewEmptyCommit(workflowExecutionId, 106, "overlay") with
        {
            StateChanges = ActivityUpsert(Activity("overlay"))
        });

        using var fetchCancellation = new CancellationTokenSource();
        inner.PageReaderOverride = async (query, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new RuntimeStorePage<ActivityExecutionState>(query, []);
        };
        var fetch = store.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 2), fetchCancellation.Token).AsTask();
        fetchCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await fetch);
        Assert.Equal(2, inner.PageReadCount);

        var allOverlayContinuation = EncodeLegacyCoalescingContinuation(binding:
            "coalesced-activity-state:workflow:wfexec-1", lastIdentity: "before-overlay", innerContinuation: null, exhausted: true);
        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 1, allOverlayContinuation), preCancelled.Token));
        Assert.Equal(2, inner.PageReadCount);
    }

    [Fact]
    public async Task Coalesced_page_merger_scans_filtered_tail_once_per_provider_position()
    {
        const string workflowExecutionId = "wfexec-1";
        var (inner, session, store) = CreateCountingActivityExecutionStateStore();
        foreach (var activityExecutionId in Enumerable.Range('a', 12).Select(value => ((char)value).ToString()))
            await inner.SaveAsync(Activity(activityExecutionId));

        session.BufferDeferred(NewEmptyCommit(workflowExecutionId, 107, "overlay") with
        {
            StateChanges = ActivityChanges(
                Enumerable.Range('c', 10)
                    .Select(value => ActivityDelete(((char)value).ToString()))
                    .ToArray())
        });

        var page = await store.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId, limit: 2));

        Assert.Equal(["a", "b"], page.Items.Select(state => state.Execution.ActivityExecutionId));
        Assert.Null(page.NextContinuationToken);
        Assert.Equal(6, inner.PageReadCount);
        Assert.Equal(inner.PageRequests.Count, inner.PageRequests.Select(request => request.ContinuationToken).Distinct().Count());
        Assert.All(inner.PageRequests, request => Assert.Equal(2, request.Limit));
    }

    // W8's Delay is the first real suspending activity: it writes a durable timer (via IDurableTimerStore) and
    // creates a bookmark, then W8's background timer pump resumes off the DURABLE timer + bookmark stores at due
    // time. Coalescing decorates only the seven core checkpoint stores, so neither the durable-timer store nor the
    // bookmark store is ever wrapped by the buffer — even with both features composed. A Delay suspension's timer
    // and bookmark are therefore durable the instant they are written (before quiescence ends), so the pump can
    // never race an in-memory-only bookmark/timer. Proven here against W8's landed IDurableTimerStore surface.
    [Fact]
    public void Coalescing_DoesNotDecorateDurableTimerOrBookmarkStores_SoDelaySuspensionStaysDurable()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddSingleton<IDurableTimerStore, InMemoryDurableTimerStore>();
        services.AddPersistenceModes();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryDurableTimerStore>(provider.GetRequiredService<IDurableTimerStore>());
        Assert.IsType<InMemoryBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
    }

    [Fact]
    public void WithoutPersistenceModes_TheStoresAreUndecoratedAndThereIsNoDrainScope()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<ImmediateRuntimeCheckpointPersistencePolicy>(provider.GetRequiredService<IRuntimeCheckpointPersistencePolicy>());
        Assert.IsType<InMemoryWorkflowSchedulerWorkQueue>(provider.GetRequiredService<IWorkflowSchedulerWorkQueue>());
        Assert.Null(provider.GetService<IRuntimeCoalescingSessionAccessor>());
        Assert.Null(provider.GetService<IRuntimeCoalescingDrainScopeFactory>());
    }

    [Fact]
    public async Task Durable_and_Balanced_reach_the_same_terminal_state()
    {
        var durable = await DriveAsync(hostMode: PersistenceMode.Durable);
        var balanced = await DriveAsync(hostMode: PersistenceMode.Balanced);

        // Behavior parity: identical terminal activity-execution snapshot, and the run genuinely completed — a
        // dispatch fault would previously park silently in the poison store and leave both runs equally "identical"
        // while stuck in Running.
        Assert.Equal(durable.Snapshot, balanced.Snapshot);
        Assert.NotEmpty(balanced.Snapshot);
        Assert.Equal(WorkflowExecutionStatus.Completed, durable.State?.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, balanced.State?.Status);
    }

    [Fact]
    public async Task ChosenDurable_UnderBalancedHost_RunsDurable_AndRecordsTheMode()
    {
        // Precedence: a workflow that chose Durable runs Durable even though the host default is Balanced. Its drain
        // runs a Durable session, which commits once per activity whatever the host's cap is.
        var durableHost = await DriveAsync(hostMode: PersistenceMode.Durable);
        var chosenDurable = await DriveAsync(chosenMode: PersistenceMode.Durable);

        Assert.Equal(durableHost.Snapshot, chosenDurable.Snapshot);
        Assert.Equal(WorkflowExecutionStatus.Completed, chosenDurable.State?.Status);
        Assert.Equal(durableHost.CommitCount, chosenDurable.CommitCount);
        Assert.All(chosenDurable.Sessions, session => Assert.Equal((PersistenceMode.Durable, (int?)1), session));
        Assert.NotEmpty(chosenDurable.Sessions);

        // The run records the mode it executed under.
        Assert.Equal(nameof(PersistenceMode.Durable), chosenDurable.State!.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
    }

    [Fact]
    public async Task NoChoice_UnderBalancedHost_UsesHostDefault_AndRecordsBalanced()
    {
        var run = await DriveAsync();

        Assert.Equal(WorkflowExecutionStatus.Completed, run.State?.Status);
        Assert.Equal(1, run.CommitCount);
        Assert.All(run.Sessions, session => Assert.Equal(PersistenceMode.Balanced, session.Mode));
        Assert.Equal(nameof(PersistenceMode.Balanced), run.State!.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
    }

    [Fact]
    public async Task ChosenBalanced_UnderDurableHost_FoldsAtTheHostCap_AndRecordsBalanced()
    {
        // The workflow's choice wins in both directions. A Durable host default must not turn a workflow that chose
        // Balanced into one commit per activity: that would look like a working run while ignoring the choice.
        var durableHost = await DriveAsync(hostMode: PersistenceMode.Durable);
        var chosenBalanced = await DriveAsync(chosenMode: PersistenceMode.Balanced, hostMode: PersistenceMode.Durable);

        Assert.Equal(nameof(PersistenceMode.Durable), durableHost.State!.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
        Assert.All(durableHost.Sessions, session => Assert.Equal((PersistenceMode.Durable, (int?)1), session));
        Assert.Equal(durableHost.Snapshot, chosenBalanced.Snapshot);
        Assert.Equal(WorkflowExecutionStatus.Completed, chosenBalanced.State?.Status);
        Assert.Equal(1, chosenBalanced.CommitCount);
        Assert.NotEmpty(chosenBalanced.Sessions);
        Assert.All(chosenBalanced.Sessions, session => Assert.Equal(
            (PersistenceMode.Balanced, (int?)new PersistenceModeOptions().MaxActivitiesPerCommit),
            session));
        Assert.Equal(nameof(PersistenceMode.Balanced), chosenBalanced.State!.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task A_host_without_persistence_modes_refuses_every_run_before_anything_runs(PersistenceMode? chosenMode)
    {
        // Every run drains in a persistence-mode session, Durable included, so a direct composition that never added
        // the persistence-mode services cannot run any of them. It refuses before anything runs, naming the fix.
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        await SeedAsync(provider, chosenMode);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(async () => await EnqueueStartAsync(provider));

        Assert.Contains("AddPersistenceModes()", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        Assert.Null(await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1"));
        Assert.Empty(await SnapshotAsync(provider));
    }

    [Fact]
    public async Task ChosenBalanced_MandatoryBookmarkBoundary_StillFlushesImmediately()
    {
        // Precedence guardrail: the boundary checkpoints are never relaxable by any chosen mode. A BookmarkCreated
        // suspend boundary must land durably within the segment, exactly as on a host-default Balanced run.
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddPersistenceModes();

        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(NewExecutableWithResumeTarget(PersistenceMode.Balanced));
        await provider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(NewRunningActivityState());

        var agentProvider = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        var agent = await agentProvider.GetAgentAsync(NewSchedulerWorkActivationRequest("wfexec-1"));
        await agent.EnqueueAsync(NewCreateBookmarkEnvelope());

        var bookmark = await provider.GetRequiredService<IBookmarkStateStore>().FindAsync("wfexec-1", "bookmark-1");
        Assert.NotNull(bookmark);

        var commit = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        Assert.Equal(RuntimeCheckpointNames.BookmarkCreated, commit.Commit.Checkpoint.Name);
    }

    // ---- Ephemeral and the completion record ----------------------------------------------------------------------

    [Fact]
    public async Task Ephemeral_WithoutCompletionRecords_DropsTheCompletionOfARunThatWasNeverCommitted()
    {
        var outcome = await CompleteEphemeralRunAsync(runHasRecord: false, recordCompletion: false);

        Assert.Equal(0, outcome.DurableCommits);
        Assert.False(outcome.Session.IsActive);
        Assert.False(outcome.Session.HasDurableRecord);
    }

    [Fact]
    public async Task Ephemeral_WithCompletionRecords_CommitsTheCompletion()
    {
        var outcome = await CompleteEphemeralRunAsync(runHasRecord: false, recordCompletion: true);

        Assert.Equal(1, outcome.DurableCommits);
        Assert.True(outcome.Session.HasDurableRecord);
    }

    [Fact]
    public async Task Ephemeral_WithoutCompletionRecords_CommitsTheCompletionOfARunThatAlreadyHasARecord()
    {
        // The record came from an earlier drain (the run suspended or faulted before). Dropping its completion would
        // leave the stored run looking unfinished forever.
        var outcome = await CompleteEphemeralRunAsync(runHasRecord: true, recordCompletion: false);

        Assert.Equal(1, outcome.DurableCommits);
    }

    [Fact]
    public async Task Ephemeral_WithoutCompletionRecords_CommitsTheCompletionOnceThisDrainCommittedAnything()
    {
        // A boundary earlier in the same drain (here an activity-attempt boundary, as before an activity that is not
        // safe to repeat) gave the run a record, so its completion must be committed too.
        var attemptBoundary = NewEmptyCommit(EphemeralRunId, 1, RuntimeCheckpointNames.ActivityAttemptClaimed);

        var outcome = await CompleteEphemeralRunAsync(runHasRecord: false, recordCompletion: false, before: [(attemptBoundary, RuntimeCheckpointPersistenceMode.Immediate)]);

        Assert.Equal(2, outcome.DurableCommits);
        Assert.Equal(RuntimeCheckpointNames.WorkflowCompleted, outcome.LastCommitName);
    }

    [Fact]
    public async Task Balanced_IgnoresRecordCompletion_AndCommitsTheCompletion()
    {
        var outcome = await CompleteEphemeralRunAsync(runHasRecord: false, recordCompletion: false, mode: PersistenceMode.Balanced);

        Assert.Equal(1, outcome.DurableCommits);
    }

    [Fact]
    public async Task Ephemeral_WithoutCompletionRecords_CommitsTheCompletionWhileBufferedWorkStillAwaitsDelivery()
    {
        // A continuation the segment staged but never delivered outlives the run's last checkpoint; dropping the record
        // would drop that work with it.
        var outcome = await CompleteEphemeralRunAsync(
            runHasRecord: false,
            recordCompletion: false,
            before: [(NewContinuationIntentCommit(EphemeralRunId, 1), RuntimeCheckpointPersistenceMode.Deferred)]);

        Assert.Equal(1, outcome.DurableCommits);
        Assert.NotEmpty(outcome.LastCommit!.StateChanges.PostCommitOutbox);
    }

    public static TheoryData<string> CompletionContentsThatOutliveTheRun() =>
        new() { "pending outbox item", "workflow dispatch", "bookmark", "incident" };

    [Theory]
    [MemberData(nameof(CompletionContentsThatOutliveTheRun))]
    public async Task Ephemeral_WithoutCompletionRecords_CommitsACompletionThatCarriesSomethingThatOutlivesTheRun(string contents)
    {
        var outcome = await CompleteEphemeralRunAsync(
            runHasRecord: false,
            recordCompletion: false,
            decorateCompletion: completion => WithContents(completion, contents));

        Assert.Equal(1, outcome.DurableCommits);
    }

    [Fact]
    public async Task Ephemeral_EndOfBurstWithoutCompletion_StillCommitsTheBufferedWork()
    {
        // A burst that ends without completing means the run stopped to wait. That is a suspension: its progress is
        // committed whatever RecordCompletion says, or the queued work would replay it from the start.
        var services = CreateCoalescingServices();
        services.AddPersistenceModes(options => options.RecordCompletion = false);
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>();
        var store = provider.GetRequiredService<IRuntimeCheckpointCommitStore>();

        await using (var scope = factory.Begin(EphemeralRunId, new ResolvedPersistenceMode(PersistenceMode.Ephemeral, RunHasRecord: false)))
        {
            Assert.Null(scope.Session.MaxActivitiesPerCommit);
            for (var checkpoint = 1; checkpoint <= 3; checkpoint++)
                await store.CommitAsync(EphemeralDeferred(checkpoint), new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred));
            Assert.Empty(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());

            await scope.FlushAtQuiescenceAsync();
            Assert.True(scope.Session.HasDurableRecord);
        }

        var flush = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        Assert.Equal(RuntimeCoalescingMetadataKeys.FlushCheckpointName, flush.Commit.Checkpoint.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheDrainTellsTheSessionWhetherTheRunAlreadyHasARecord(bool runHasRecord)
    {
        // The drain reads the run's stored row once to resolve its mode; the same read tells the session whether the run
        // already has a record, which is what keeps the completion of a stored run from being dropped.
        var begun = new List<ResolvedPersistenceMode>();
        var services = CreateCoalescingServices();
        services.Replace(ServiceDescriptor.Scoped<IRuntimeCoalescingDrainScopeFactory>(serviceProvider =>
            new RecordingDrainScopeFactory(ActivatorUtilities.CreateInstance<RuntimeCoalescingDrainScopeFactory>(serviceProvider), begun)));
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(NewExecutableWithResumeTarget());
        await provider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(NewRunningActivityState());
        if (runHasRecord)
            await provider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(NewWorkflowState("wfexec-1", WorkflowExecutionStatus.Running));

        var agent = await provider.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(NewSchedulerWorkActivationRequest("wfexec-1"));
        await agent.EnqueueAsync(NewCreateBookmarkEnvelope());

        Assert.Equal(new ResolvedPersistenceMode(PersistenceMode.Balanced, runHasRecord), Assert.Single(begun));
    }

    private sealed class RecordingDrainScopeFactory(IRuntimeCoalescingDrainScopeFactory inner, List<ResolvedPersistenceMode> begun)
        : IRuntimeCoalescingDrainScopeFactory
    {
        public IRuntimeCoalescingDrainScope Begin(string workflowExecutionId, ResolvedPersistenceMode mode)
        {
            begun.Add(mode);
            return inner.Begin(workflowExecutionId, mode);
        }
    }

    [Fact]
    public async Task Ephemeral_HasNoIntermediateCommits()
    {
        var services = CreateCoalescingServices();
        services.AddPersistenceModes(options => options.MaxActivitiesPerCommit = 1);
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IRuntimeCheckpointCommitStore>();

        await using (var scope = provider.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>()
                         .Begin(EphemeralRunId, new ResolvedPersistenceMode(PersistenceMode.Ephemeral, RunHasRecord: false)))
        {
            for (var checkpoint = 1; checkpoint <= 10; checkpoint++)
                await store.CommitAsync(EphemeralDeferred(checkpoint), new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred));

            Assert.Equal(10, scope.Session.ActivityCount);
            Assert.Empty(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        }
    }

    private const string EphemeralRunId = "wfexec-ephemeral";

    private sealed record EphemeralCompletionOutcome(
        int DurableCommits,
        RuntimeCoalescingSession Session,
        RuntimeCheckpointCommit? LastCommit)
    {
        public string? LastCommitName => LastCommit?.Checkpoint.Name;
    }

    // Drives one folding session to a WorkflowCompleted boundary through the real decorated store and reports what
    // reached the durable store.
    private static async Task<EphemeralCompletionOutcome> CompleteEphemeralRunAsync(
        bool runHasRecord,
        bool recordCompletion,
        PersistenceMode mode = PersistenceMode.Ephemeral,
        IReadOnlyList<(RuntimeCheckpointCommit Commit, RuntimeCheckpointPersistenceMode Decision)>? before = null,
        Func<RuntimeCheckpointCommit, RuntimeCheckpointCommit>? decorateCompletion = null)
    {
        var services = CreateCoalescingServices();
        services.AddPersistenceModes(options => options.RecordCompletion = recordCompletion);
        await using var provider = services.BuildServiceProvider();
        await SeedAsync(provider);
        var store = provider.GetRequiredService<IRuntimeCheckpointCommitStore>();
        var inner = provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();

        await using var scope = provider.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>()
            .Begin(EphemeralRunId, new ResolvedPersistenceMode(mode, runHasRecord));
        foreach (var (commit, decision) in before ?? [])
            await store.CommitAsync(commit, new RuntimeCheckpointPersistenceDecision(decision));

        var completion = (decorateCompletion ?? (commit => commit))(NewCompletionCommit(EphemeralRunId, checkpoint: 99));
        await store.CommitAsync(completion, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        var commits = inner.ListCommits();
        return new EphemeralCompletionOutcome(commits.Count, scope.Session, commits.LastOrDefault()?.Commit);
    }

    private static RuntimeCheckpointCommit EphemeralDeferred(int checkpoint)
    {
        var commit = NewEmptyCommit(EphemeralRunId, checkpoint, RuntimeCheckpointNames.ActivityCompleted);
        return commit with { Checkpoint = commit.Checkpoint with { ActivityExecutionIds = [$"actexec-ephemeral-{checkpoint}"] } };
    }

    private static WorkflowExecutionState NewWorkflowState(string workflowExecutionId, WorkflowExecutionStatus status) =>
        new(
            WorkflowExecutionId: workflowExecutionId,
            PinnedExecutable: NewExecutable().Identity,
            Status: status,
            SubStatus: null,
            CreatedAt: Now,
            StartedAt: Now,
            UpdatedAt: Now,
            CompletedAt: status == WorkflowExecutionStatus.Completed ? Now : null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());

    private static RuntimeCheckpointCommit NewCompletionCommit(string workflowExecutionId, int checkpoint)
    {
        var commit = NewEmptyCommit(workflowExecutionId, checkpoint, RuntimeCheckpointNames.WorkflowCompleted);
        var completed = NewWorkflowState(workflowExecutionId, WorkflowExecutionStatus.Completed);
        return commit with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(workflowExecutionId, RuntimeStateChangeOperation.Upsert, completed, new Dictionary<string, string>()),
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [])
        };
    }

    private static RuntimeCheckpointCommit WithContents(RuntimeCheckpointCommit completion, string contents)
    {
        var changes = completion.StateChanges;
        return contents switch
        {
            "pending outbox item" => completion with
            {
                StateChanges = changes.WithPostCommitOutbox(NewContinuationIntentCommit(completion.WorkflowExecutionId, 98).StateChanges.PostCommitOutbox)
            },
            "workflow dispatch" => completion with
            {
                StateChanges = changes.WithWorkflowDispatches(NewDispatchBoundaryCommit().StateChanges.WorkflowDispatches)
            },
            "bookmark" => completion with
            {
                StateChanges = new RuntimeCheckpointStateChangeSet(
                    changes.WorkflowExecution,
                    changes.Scheduler,
                    changes.ActivityExecutions,
                    [
                        new RuntimeStateChange<BookmarkState>(
                            "bookmark-left",
                            RuntimeStateChangeOperation.Delete,
                            new BookmarkState("bookmark-left", completion.WorkflowExecutionId, "actexec-1", "node-wait", "node-wait", "stimulus", "stimulus-hash", null, new Dictionary<string, string>(), Now, null),
                            new Dictionary<string, string>())
                    ],
                    changes.DurableValues,
                    changes.Incidents,
                    changes.Operational)
            },
            "incident" => completion with
            {
                StateChanges = new RuntimeCheckpointStateChangeSet(
                    changes.WorkflowExecution,
                    changes.Scheduler,
                    changes.ActivityExecutions,
                    changes.Bookmarks,
                    changes.DurableValues,
                    [
                        new RuntimeStateChange<IncidentState>(
                            "incident-1",
                            RuntimeStateChangeOperation.Upsert,
                            new IncidentState(
                                incidentId: "incident-1",
                                workflowExecutionId: completion.WorkflowExecutionId,
                                activityExecutionId: "actexec-1",
                                executableNodeId: "node-1",
                                severity: IncidentSeverity.Warning,
                                status: IncidentStatus.Open,
                                resolutionOutcome: null,
                                failureType: "TestFailure",
                                message: "Recorded at completion.",
                                createdAt: Now,
                                resolvedAt: null),
                            new Dictionary<string, string>())
                    ],
                    changes.Operational)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(contents), contents, null)
        };
    }

    [Fact]
    public async Task CrashMidSegment_DurableQueueStillHoldsSegmentEntry_AndNoPartialCheckpointPersisted()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddPersistenceModes();
        // Crash injection: the post-commit outbox processor throws inside the drain loop, after the first hops have been
        // buffered into the in-memory working set but before the quiescence flush lands. This models a process crash
        // mid-segment: nothing durable has been written and the segment-entry command is still in the durable queue.
        services.AddSingleton<IRuntimePostCommitOutboxProcessor>(new ThrowingOutboxProcessor());

        using var provider = services.BuildServiceProvider();
        await SeedAsync(provider);

        await Assert.ThrowsAnyAsync<Exception>(() => EnqueueStartAsync(provider).AsTask());

        // Condition B: the durable scheduler queue never advanced past the last flushed state — the segment-entry
        // command is still present because the coalescing buffer is only dequeued as part of the (never-reached) flush.
        var innerQueue = provider.GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>().Value;
        var pending = await innerQueue.ListPendingWorkflowExecutionIdsAsync(10);
        Assert.Contains("wfexec-1", pending);

        // Nothing partial persisted: the durable checkpoint store recorded no commit for the crashed segment.
        var innerStore = provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        Assert.Empty(innerStore.ListCommits());
    }

    [Fact]
    public async Task Coalescing_BookmarkSuspend_FlushesDurableBookmarkImmediately()
    {
        // A bookmark-suspend is a mandatory flush boundary: under coalescing the BookmarkCreated checkpoint must still
        // land in the DURABLE bookmark store within the segment, so a durable timer/stimulus pump (which reads the
        // durable bookmark store) can never race an in-memory-only bookmark. Delay-style suspensions rely on this.
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddPersistenceModes();

        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(NewExecutableWithResumeTarget());
        await provider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(NewRunningActivityState());

        var agentProvider = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        var agent = await agentProvider.GetAgentAsync(NewSchedulerWorkActivationRequest("wfexec-1"));
        await agent.EnqueueAsync(NewCreateBookmarkEnvelope());

        // The bookmark is durable (flushed at the suspend boundary, not left buffered in-memory).
        var bookmark = await provider.GetRequiredService<IBookmarkStateStore>().FindAsync("wfexec-1", "bookmark-1");
        Assert.NotNull(bookmark);
        Assert.Equal("delivery-status", bookmark!.StimulusType);
        Assert.Equal("sha256:delivery-status:order-123", bookmark.StimulusHash);

        // The activity durably transitioned to Suspended.
        var state = await provider.GetRequiredService<IActivityExecutionStateStore>().FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Suspended, state!.Status);

        // Exactly one durable commit landed and it is the BookmarkCreated boundary — coalescing did not defer it.
        var commit = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        Assert.Equal(RuntimeCheckpointNames.BookmarkCreated, commit.Commit.Checkpoint.Name);

        // The bookmark boundary deactivates the session mid-dispatch; the in-flight work item must be consumed by
        // that final flush, not copied into the durable inner queue where the still-live drain loop would redeliver
        // it as a duplicate dispatch and park it in the poison store (replay-conflict tripwire).
        Assert.Empty(await provider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync("wfexec-1"));
    }

    // Activity cap: a cap-hit folds-and-flushes the segment and STARTS A FRESH SEGMENT (like a
    // mandatory attempt boundary), instead of deactivating the session and degrading the drain remainder to
    // per-checkpoint persistence. Each probe is the first checkpoint of a new activity.
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(50)]
    public async Task Coalescing_CapHit_FoldsAndFlushesThenStartsFreshSegment(int cap)
    {
        var innerQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var innerStore = new InMemoryRuntimeCheckpointCommitStore();
        var session = new RuntimeCoalescingSession(
            "wfexec-cap",
            innerQueue,
            new PersistenceModeOptions { MaxActivitiesPerCommit = cap });
        var accessor = new FixedCoalescingSessionAccessor(session);
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            accessor);

        Assert.Same(session, accessor.Current);
        Assert.True(session.AppliesTo("wfexec-cap"));

        for (var checkpoint = 1; checkpoint <= cap; checkpoint++)
            await store.CommitAsync(NewEmptyDeferredCommit(checkpoint), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(cap, session.ActivityCount);
        Assert.True(session.IsActive);
        Assert.Empty(innerStore.ListCommits());

        // The cap-tripping checkpoint folds the whole segment (cap buffered activities + itself) into one durable commit
        // and the session keeps coalescing with an empty fresh segment.
        await store.CommitAsync(NewEmptyDeferredCommit(cap + 1), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(0, session.ActivityCount);
        Assert.True(session.IsActive);
        Assert.Single(innerStore.ListCommits());

        // The fresh segment buffers again instead of falling back to per-checkpoint Immediate persistence.
        for (var checkpoint = cap + 2; checkpoint <= 2 * cap + 1; checkpoint++)
            await store.CommitAsync(NewEmptyDeferredCommit(checkpoint), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(cap, session.ActivityCount);
        Assert.Single(innerStore.ListCommits());

        await store.CommitAsync(NewEmptyDeferredCommit(2 * cap + 2), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(0, session.ActivityCount);
        Assert.True(session.IsActive);
        Assert.Equal(2, innerStore.ListCommits().Count);

        // A terminal boundary folds the tail and deactivates as before.
        await store.CommitAsync(
            NewEmptyCommit("wfexec-cap", 2 * cap + 3, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.False(session.IsActive);
        Assert.Equal(3, innerStore.ListCommits().Count);
    }

    // The guard the segment-cap rework exists for: a ReplaySafe hot loop longer than the cap must fold into
    // one durable commit per (cap + 1)-activity window plus the terminal fold, not a per-checkpoint
    // tail after the first cap hit.
    [Fact]
    public async Task ReplaySafeHotLoop_LongerThanCap_FoldsPerCapWindow_NoImmediateTail()
    {
        const int cap = 10;
        const int checkpoints = 35;
        var innerStore = new InMemoryRuntimeCheckpointCommitStore();
        var session = new RuntimeCoalescingSession(
            "wfexec-cap",
            new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions { MaxActivitiesPerCommit = cap });
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            new FixedCoalescingSessionAccessor(session));

        for (var checkpoint = 1; checkpoint <= checkpoints; checkpoint++)
            await store.CommitAsync(NewEmptyDeferredCommit(checkpoint), new(RuntimeCheckpointPersistenceMode.Deferred));

        await store.CommitAsync(
            NewEmptyCommit("wfexec-cap", checkpoints + 1, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        // Each intermediate fold covers cap buffered activities + the cap-tripping one; the terminal boundary folds
        // the remainder. 35 checkpoints @ cap 10 -> 3 intermediate folds (33 checkpoints) + 1 terminal fold (2 + terminal).
        var expectedCommits = checkpoints / (cap + 1) + 1;
        Assert.Equal(expectedCommits, innerStore.ListCommits().Count);
        Assert.False(session.IsActive);
        // Explicitly not the old degraded shape: a per-checkpoint Immediate tail would exceed the checkpoint budget.
        Assert.True(expectedCommits < checkpoints - cap,
            "The commit count must stay a per-window fold, not a per-checkpoint tail.");
    }

    // A cap flush that carries a pending continuation intent persists it durably (crash-redrive guarantee) while the
    // still-active session keeps delivering it from the overlay. The next flush writes the overlay outcome back to
    // the durable outbox store, so no durable Pending residue survives the drain to be redelivered by a later sweep.
    [Fact]
    public async Task CapFlush_PersistsPendingOutboxDurably_AndReconcilesOverlayDeliveryAtNextFlush()
    {
        const string workflowExecutionId = "wfexec-cap";
        var innerStore = new InMemoryRuntimeCheckpointCommitStore();
        var session = new RuntimeCoalescingSession(
            workflowExecutionId,
            new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions { MaxActivitiesPerCommit = 1 },
            innerOutboxStore: innerStore);
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            new FixedCoalescingSessionAccessor(session));

        await store.CommitAsync(NewEmptyDeferredCommit(1), new(RuntimeCheckpointPersistenceMode.Deferred));

        var capCommit = NewContinuationIntentCommit(workflowExecutionId, 2);
        var outboxItemId = Assert.Single(capCommit.StateChanges.PostCommitOutbox).StateId;
        await store.CommitAsync(capCommit, new(RuntimeCheckpointPersistenceMode.Deferred));

        // The fold landed durably with the continuation intent still Pending, and the session stayed active with the
        // item owned by (and deliverable from) the overlay.
        Assert.True(session.IsActive);
        var folded = Assert.Single(innerStore.ListCommits()).Commit;
        Assert.Equal(outboxItemId, Assert.Single(folded.StateChanges.PostCommitOutbox).StateId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await innerStore.FindAsync(outboxItemId))!.Status);
        Assert.True(session.OwnsOutboxItem(outboxItemId));

        // The drain delivers the continuation from the overlay during the next segment.
        session.RecordOutboxDelivery(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId,
            RuntimePostCommitOutboxStatus.Delivered,
            Now.AddTicks(3)));

        await store.CommitAsync(
            NewEmptyCommit(workflowExecutionId, 4, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        // The terminal flush reconciled the overlay outcome into the durable store: no Pending residue survives.
        Assert.False(session.IsActive);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await innerStore.FindAsync(outboxItemId))!.Status);
        Assert.Empty(await innerStore.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            now: Now.AddMinutes(5),
            limit: 10,
            workflowExecutionId: workflowExecutionId)));
    }

    // #2225: EnqueueSchedulerWork retries, so an overlay delivery that fails leaves the item FailedRetryable in the working
    // set. The next fold must still persist it: a checkpoint's outbox carries only Pending items, so the fold writes it as
    // the Pending crash backstop it is, keeping its attempt count and retry time for the sweep that delivers it.
    [Fact]
    public async Task Fold_AfterAnOverlayContinuationFailedRetryably_PersistsItPendingWithItsRetryState()
    {
        const string workflowExecutionId = "wfexec-cap";
        var retryDelay = TimeSpan.FromSeconds(1);
        var innerStore = new InMemoryRuntimeCheckpointCommitStore();
        var session = new RuntimeCoalescingSession(
            workflowExecutionId,
            new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions(),
            innerOutboxStore: innerStore);
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            new FixedCoalescingSessionAccessor(session));
        var continuation = NewContinuationIntentCommit(workflowExecutionId, 1, new RuntimePostCommitRetryPolicy(4, retryDelay));
        var outboxItemId = Assert.Single(continuation.StateChanges.PostCommitOutbox).StateId;
        await store.CommitAsync(continuation, new(RuntimeCheckpointPersistenceMode.Deferred));
        var failedAt = Now.AddSeconds(5);
        session.RecordOutboxDelivery(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId,
            RuntimePostCommitOutboxStatus.FailedRetryable,
            failedAt,
            "Transient enqueue failure."));

        await store.CommitAsync(
            NewEmptyCommit(workflowExecutionId, 2, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        var persisted = await innerStore.FindAsync(outboxItemId);
        Assert.NotNull(persisted);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, persisted.Status);
        Assert.Equal(1, persisted.DeliveryAttemptCount);
        Assert.Equal(failedAt + retryDelay, persisted.AvailableAt);
    }

    // The same fold after a cap flush already persisted the item: its durable Pending row is the backstop, and saving the
    // changed overlay state over it would be a conflicting duplicate, so the fold leaves it as it is.
    [Fact]
    public async Task CapFold_AfterAnOverlayContinuationFailedRetryably_LeavesItsDurablePendingRowAlone()
    {
        const string workflowExecutionId = "wfexec-cap";
        var innerStore = new InMemoryRuntimeCheckpointCommitStore();
        var session = new RuntimeCoalescingSession(
            workflowExecutionId,
            new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions { MaxActivitiesPerCommit = 1 },
            innerOutboxStore: innerStore);
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            new FixedCoalescingSessionAccessor(session));
        await store.CommitAsync(NewEmptyDeferredCommit(1), new(RuntimeCheckpointPersistenceMode.Deferred));
        var continuation = NewContinuationIntentCommit(workflowExecutionId, 2, new RuntimePostCommitRetryPolicy(4, TimeSpan.FromSeconds(1)));
        var outboxItemId = Assert.Single(continuation.StateChanges.PostCommitOutbox).StateId;
        await store.CommitAsync(continuation, new(RuntimeCheckpointPersistenceMode.Deferred));
        session.RecordOutboxDelivery(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId,
            RuntimePostCommitOutboxStatus.FailedRetryable,
            Now.AddSeconds(5),
            "Transient enqueue failure."));

        // The next segment buffers one checkpoint, and the one after it trips the cap again.
        await store.CommitAsync(NewEmptyDeferredCommit(3), new(RuntimeCheckpointPersistenceMode.Deferred));
        await store.CommitAsync(NewEmptyDeferredCommit(4), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(2, innerStore.ListCommits().Count);
        Assert.True(session.IsActive);
        var persisted = await innerStore.FindAsync(outboxItemId);
        Assert.NotNull(persisted);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, persisted.Status);
        Assert.Equal(0, persisted.DeliveryAttemptCount);
    }

    [Fact]
    public async Task Coalescing_FlushesDispatchRecordAndChildStartOutboxAtomicallyAfterBufferedWork()
    {
        var checkpointState = new InMemoryRuntimeCheckpointStoreState();
        var innerStore = new InMemoryRuntimeCheckpointCommitStore(
            workflowDispatchStore: new InMemoryWorkflowDispatchStore(checkpointState),
            state: checkpointState);
        var session = new RuntimeCoalescingSession(
            "parent-dispatch",
            new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions());
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            new FixedCoalescingSessionAccessor(session));

        await store.CommitAsync(NewEmptyDeferredCommit(1) with
        {
            Checkpoint = new RuntimeCheckpoint(
                "checkpoint-buffered",
                "BufferedWork",
                "parent-dispatch",
                Now,
                [],
                new Dictionary<string, string>())
        }, new(RuntimeCheckpointPersistenceMode.Deferred));

        await store.CommitAsync(NewDispatchBoundaryCommit(), new(RuntimeCheckpointPersistenceMode.Deferred));

        var persisted = Assert.Single(innerStore.ListCommits()).Commit;
        var dispatch = Assert.Single(persisted.StateChanges.WorkflowDispatches);
        var outbox = Assert.Single(persisted.StateChanges.PostCommitOutbox);
        Assert.Equal(dispatch.State.ParentWorkflowExecutionId, outbox.State.Intent.WorkflowExecutionId);
        Assert.Equal("elsa.dispatch-workflow.start-child.v1", outbox.State.Intent.Kind);
        Assert.False(session.IsActive);
    }

    private static async Task<(IReadOnlyList<(string NodeId, ActivityExecutionStatus Status)> Snapshot, int CommitCount, WorkflowExecutionState? State, IReadOnlyList<(PersistenceMode Mode, int? MaxActivitiesPerCommit)> Sessions)> DriveAsync(
        PersistenceMode? chosenMode = null,
        PersistenceMode hostMode = PersistenceMode.Balanced)
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddPersistenceModes(options => options.Mode = hostMode);
        var sessions = new List<(PersistenceMode Mode, int? MaxActivitiesPerCommit)>();
        services.AddScoped<RuntimeCoalescingDrainScopeFactory>();
        services.Replace(ServiceDescriptor.Scoped<IRuntimeCoalescingDrainScopeFactory>(serviceProvider =>
            new SessionRecordingDrainScopeFactory(serviceProvider.GetRequiredService<RuntimeCoalescingDrainScopeFactory>(), sessions)));

        using var provider = services.BuildServiceProvider();
        await SeedAsync(provider, chosenMode);
        await EnqueueStartAsync(provider);

        var snapshot = await SnapshotAsync(provider);
        var commitCount = provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits().Count;
        var state = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
        return (snapshot, commitCount, state, sessions);
    }

    // Records the mode and activity cap of every session a drain runs in.
    private sealed class SessionRecordingDrainScopeFactory(
        IRuntimeCoalescingDrainScopeFactory inner,
        List<(PersistenceMode Mode, int? MaxActivitiesPerCommit)> sessions) : IRuntimeCoalescingDrainScopeFactory
    {
        public IRuntimeCoalescingDrainScope Begin(string workflowExecutionId, ResolvedPersistenceMode mode)
        {
            var scope = inner.Begin(workflowExecutionId, mode);
            sessions.Add((scope.Session.Mode, scope.Session.MaxActivitiesPerCommit));
            return scope;
        }
    }

    private static async Task SeedAsync(ServiceProvider provider, PersistenceMode? chosenMode = null)
    {
        var store = provider.GetRequiredService<IWorkflowExecutableStore>();
        await store.SaveAsync(NewExecutable(chosenMode));
    }

    private static async ValueTask EnqueueStartAsync(ServiceProvider provider)
    {
        var executable = NewExecutable();
        var agentProvider = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        var agent = await agentProvider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));
    }

    private static async Task<IReadOnlyList<(string NodeId, ActivityExecutionStatus Status)>> SnapshotAsync(ServiceProvider provider)
    {
        var stateStore = provider.GetRequiredService<IActivityExecutionStateStore>();
        var states = await stateStore.ListAllAsync("wfexec-1");
        return states
            .Select(state => (state.Execution.ExecutableNodeId, state.Status))
            .OrderBy(entry => entry.ExecutableNodeId, StringComparer.Ordinal)
            .ToList();
    }

    private static WorkflowExecutionActorActivationRequest NewActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: Now,
            requestedBy: "runtime-test",
            requiredCapabilities: WorkflowExecutionActorCapabilities.InProcessMailbox);

    private static RuntimeSchedulerWorkClaimRequest NewClaimRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId,
            ownerId: "worker-1",
            Now,
            visibilityTimeout: TimeSpan.FromMinutes(1));

    private static WorkflowExecutionCommandEnvelope NewStartEnvelope(WorkflowExecutableIdentity pinnedExecutable)
    {
        var payload = new WorkflowExecutionStartCommandPayload(pinnedExecutable, pinnedExecutable.ArtifactId);
        var command = new WorkflowExecutionCommand(
            CommandId: "command-start",
            WorkflowExecutionId: "wfexec-1",
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: Now,
            Payload: JsonSerializer.SerializeToElement(payload),
            Metadata: new Dictionary<string, string>
            {
                ["source"] = "test",
                // Mirrors WorkflowStartDispatcher.CreateDispatchMetadata: the artifact-id breadcrumb the per-execution
                // cadence resolver reads on the first drain, before any WorkflowExecutionState row exists.
                ["runtime.artifactId"] = pinnedExecutable.ArtifactId
            });

        return new(
            envelopeId: "envelope-start",
            workflowExecutionId: "wfexec-1",
            command: command,
            idempotencyKey: "wfexec-1:start:artifact-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now,
            sequence: 1,
            metadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    // Each cap probe is the first checkpoint of a distinct activity, so the probes count against the activity cap.
    private static RuntimeCheckpointCommit NewEmptyDeferredCommit(int checkpoint)
    {
        var commit = NewEmptyCommit("wfexec-cap", checkpoint, "CapProbe");
        return commit with { Checkpoint = commit.Checkpoint with { ActivityExecutionIds = [$"actexec-cap-{checkpoint}"] } };
    }

    private static RuntimeCheckpointCommit NewEmptyCommit(
        string workflowExecutionId,
        int checkpoint,
        string checkpointName) =>
        new(
            CommitId: $"commit-cap-{checkpoint}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint-cap-{checkpoint}",
                Name: checkpointName,
                WorkflowExecutionId: workflowExecutionId,
                OccurredAt: Now.AddTicks(checkpoint),
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
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

    private static RuntimeCheckpointStateChangeSet ActivityUpsert(params ActivityExecutionState[] states) =>
        ActivityChanges(states.Select(ActivityChange).ToArray());

    private static RuntimeCheckpointStateChangeSet ActivityChanges(params RuntimeStateChange<ActivityExecutionState>[] changes) =>
        new(
            workflowExecution: null,
            scheduler: null,
            activityExecutions: changes,
            bookmarks: [],
            durableValues: [],
            incidents: [],
            operational: []);

    private static RuntimeStateChange<ActivityExecutionState> ActivityChange(ActivityExecutionState state) =>
        new(state.Execution.ActivityExecutionId, RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>());

    private static RuntimeStateChange<ActivityExecutionState> ActivityChange(string activityExecutionId) =>
        ActivityChange(Activity(activityExecutionId));

    private static RuntimeStateChange<ActivityExecutionState> ActivityDelete(string activityExecutionId) =>
        new(activityExecutionId, RuntimeStateChangeOperation.Delete, Activity(activityExecutionId), new Dictionary<string, string>());

    private static string EncodeLegacyCoalescingContinuation(string binding, string lastIdentity, string? innerContinuation, bool exhausted)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Binding = binding,
            Cursor = new { LastIdentity = lastIdentity, InnerContinuation = innerContinuation, InnerExhausted = exhausted }
        });
        return $"crsp1.{EncodeBase64Url(payload)}.{EncodeBase64Url(SHA256.HashData(payload))}";
    }

    private static string DecodeCoalescingContinuationPayload(string token)
    {
        var encoded = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        encoded += (encoded.Length % 4) switch { 0 => "", 2 => "==", 3 => "=", _ => throw new FormatException() };
        return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
    }

    private static string EncodeBase64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // A deferrable (non-boundary) checkpoint commit carrying a pending EnqueueSchedulerWork continuation intent,
    // like a hot-loop ActivityCompleted hop that schedules its successor.
    private static RuntimeCheckpointCommit NewContinuationIntentCommit(
        string workflowExecutionId,
        int checkpoint,
        RuntimePostCommitRetryPolicy? retryPolicy = null)
    {
        // Like a hot-loop activity completing and scheduling its successor: it belongs to its own activity, so it
        // counts against the activity cap.
        var empty = NewEmptyCommit(workflowExecutionId, checkpoint, RuntimeCheckpointNames.ActivityCompleted);
        var commit = empty with
        {
            Checkpoint = empty.Checkpoint with { ActivityExecutionIds = [$"actexec-cap-{checkpoint}"] },
            PostCommitIntents =
            [
                new RuntimePostCommitIntent(
                    $"intent-{checkpoint}",
                    workflowExecutionId,
                    RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
                    Now.AddTicks(checkpoint),
                    activityExecutionId: null,
                    idempotencyKey: $"{workflowExecutionId}:continue:{checkpoint}",
                    JsonSerializer.SerializeToElement(new { next = checkpoint + 1 }))
            ]
        };

        return commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(
                commit,
                [
                    new RuntimePostCommitIntentHandlerContribution(
                        RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
                        typeof(RuntimeSchedulerPostCommitIntentDispatcher),
                        retryPolicy ?? RuntimePostCommitRetryPolicy.None)
                ])),
            PostCommitIntents = []
        };
    }

    private static RuntimeCheckpointCommit NewDispatchBoundaryCommit()
    {
        var identity = new WorkflowDispatchIdentity("parent-dispatch", "activity-dispatch");
        var source = new WorkflowExecutableSourceProvenance(
            "source-child",
            "WorkflowDefinitionVersion",
            "version-child",
            "1.0.0",
            "definition-child",
            "version-child",
            "1.0.0",
            "publication-child",
            "slot-child");
        var record = new WorkflowDispatchRecord(
            identity.DispatchId,
            "parent-dispatch",
            "activity-dispatch",
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-child", "definition-child", "version-child", "1.0.0", "sha256:child"),
            source,
            WorkflowDispatchMode.FireAndForget,
            WorkflowDispatchStatus.Pending,
            null,
            null,
            new WorkflowExecutionPartition("partition-1"),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot("parent-dispatch", "initiator-1"),
            [],
            Now,
            Now);
        var intent = new RuntimePostCommitIntent(
            identity.StartIntentId,
            "parent-dispatch",
            "elsa.dispatch-workflow.start-child.v1",
            Now,
            "activity-dispatch",
            identity.StartIdempotencyKey,
            JsonSerializer.SerializeToElement(new { dispatchId = identity.DispatchId }));
        var commit = new RuntimeCheckpointCommit(
            "commit-dispatch",
            new RuntimeCheckpoint(
                "checkpoint-dispatch",
                RuntimeCheckpointNames.ActivityCompleted,
                "parent-dispatch",
                Now,
                ["activity-dispatch"],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                null,
                null,
                [],
                [],
                [],
                [],
                [],
                workflowDispatches:
                [
                    new RuntimeStateChange<WorkflowDispatchRecord>(
                        record.DispatchId,
                        RuntimeStateChangeOperation.Upsert,
                        record,
                        new Dictionary<string, string>())
                ]),
            [intent],
            new Dictionary<string, string>());

        return commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(commit))
        };
    }

    private static WorkflowExecutionActorActivationRequest NewSchedulerWorkActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.SchedulerWork,
            requestedAt: Now,
            requestedBy: "runtime-test",
            requiredCapabilities: WorkflowExecutionActorCapabilities.InProcessMailbox);

    private static WorkflowExecutionCommandEnvelope NewCreateBookmarkEnvelope()
    {
        var payload = JsonSerializer.SerializeToElement(new RuntimeCreateBookmarkCommandPayload(
            pinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            bookmarkId: "bookmark-1",
            activityExecutionId: "actexec-1",
            executableNodeId: "node-wait",
            resumeTargetId: "resume-target:delivery",
            stimulusType: "delivery-status",
            stimulusHash: "sha256:delivery-status:order-123",
            payload: JsonSerializer.SerializeToElement(new { orderId = "order-123" }),
            expiresAt: Now.AddMinutes(30),
            reason: RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason,
            metadata: new Dictionary<string, string> { ["customer"] = "northwind" },
            valueSnapshots: []));

        var command = new WorkflowExecutionCommand(
            CommandId: "command-create-bookmark",
            WorkflowExecutionId: "wfexec-1",
            Kind: WorkflowExecutionCommandKind.CreateBookmark,
            EnqueuedAt: Now,
            Payload: payload,
            Metadata: new Dictionary<string, string> { ["source"] = "test" });

        return new(
            envelopeId: "envelope-create-bookmark",
            workflowExecutionId: "wfexec-1",
            command: command,
            idempotencyKey: "wfexec-1:create-bookmark:bookmark-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now,
            sequence: 1,
            metadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static ActivityExecutionState NewRunningActivityState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-1",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-wait",
                AuthoredActivityId: "authored-node-wait",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: Now.AddMinutes(-3),
            StartedAt: Now.AddMinutes(-2),
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static ActivityExecutionState Activity(string activityExecutionId) =>
        NewRunningActivityState() with
        {
            Execution = new ActivityExecution(
                activityExecutionId,
                "wfexec-1",
                $"node-{activityExecutionId}",
                $"authored-{activityExecutionId}",
                "test/activity",
                "1.0.0")
        };

    private static WorkflowExecutable NewExecutableWithResumeTarget(PersistenceMode? persistenceMode = null)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var node = new ExecutableNode(
            executableNodeId: "node-wait",
            authoredActivityId: "authored-node-wait",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                ["resume-target:delivery"] = new(
                    ResumeTargetId: "resume-target:delivery",
                    ExecutableNodeId: "node-wait",
                    HandlerKey: "test-handler",
                    Metadata: new Dictionary<string, string>())
            },
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference,
            persistenceMode: persistenceMode);
    }

    // The driven workflow must genuinely run to completion: a CLR-activity node without a pinned activity contract
    // faults dispatch with VF-ACT-001 and gets parked in the poison store, which (since poison surfacing landed)
    // faults the workflow instead of hanging it silently. A Finish intrinsic root needs no CLR contract and
    // completes the run, so the burst-folding comparison measures a real start-to-completed burst.
    private static WorkflowExecutable NewExecutable(PersistenceMode? persistenceMode = null)
    {
        var outcomeType = new ValueTypeDescriptor("String");
        var node = new ExecutableNode(
            executableNodeId: "node-start",
            authoredActivityId: "authored-node-start",
            activityType: "elsa.intrinsic.finish",
            activityTypeVersion: "1.0.0",
            descriptorType: "intrinsic",
            descriptorPayload: JsonSerializer.SerializeToElement(new { kind = nameof(WorkflowIntrinsicKind.Finish), schemaVersion = "1.0.0" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Outcome] = new(
                    WorkflowIntrinsicInputKeys.Outcome,
                    outcomeType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(outcomeType, JsonSerializer.SerializeToElement("Done"), ValueProtectionPolicy.InstanceInline))
            },
            metadata: new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Finish);

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference,
            persistenceMode: persistenceMode);
    }

    private sealed class ThrowingOutboxProcessor : IRuntimePostCommitOutboxProcessor
    {
        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected crash before quiescence flush.");
    }

    private sealed class FixedCoalescingSessionAccessor(RuntimeCoalescingSession session) : IRuntimeCoalescingSessionAccessor
    {
        public RuntimeCoalescingSession? Current => session;

        public IDisposable Push(RuntimeCoalescingSession? pushedSession) =>
            throw new NotSupportedException("The cap test provides a fixed ambient session.");
    }

    private static (CountingActivityExecutionStateStore Inner, RuntimeCoalescingSession Session, CoalescingActivityExecutionStateStore Store)
        CreateCountingActivityExecutionStateStore()
    {
        var inner = new CountingActivityExecutionStateStore();
        var session = new RuntimeCoalescingSession(
            "wfexec-1",
            new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions());
        var store = new CoalescingActivityExecutionStateStore(
            new CoalescingInner<IActivityExecutionStateStore>(inner),
            new FixedCoalescingSessionAccessor(session));
        return (inner, session, store);
    }

    private sealed class CountingActivityExecutionStateStore : IActivityExecutionStateStore
    {
        private readonly InMemoryActivityExecutionStateStore inner = new();

        public int PageReadCount { get; private set; }
        public List<(int Limit, string? ContinuationToken)> PageRequests { get; } = [];
        public Func<ActivityExecutionStatePageQuery, CancellationToken, ValueTask<RuntimeStorePage<ActivityExecutionState>>>? PageReaderOverride { get; set; }
        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default) => inner.SaveAsync(state, cancellationToken);
        public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) => inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        public ValueTask<long> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => inner.CountAsync(workflowExecutionId, cancellationToken);
        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query, CancellationToken cancellationToken = default) => inner.ListByParentPageAsync(query, cancellationToken);

        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query, CancellationToken cancellationToken = default)
        {
            PageReadCount++;
            PageRequests.Add((query.Limit, query.ContinuationToken));
            return PageReaderOverride is { } pageReader
                ? pageReader(query, cancellationToken)
                : inner.ListPageAsync(query, cancellationToken);
        }
    }

    private static ServiceCollection CreateCoalescingServices(Action<IServiceCollection>? configureBeforeCoalescing = null)
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        configureBeforeCoalescing?.Invoke(services);
        services.AddPersistenceModes();
        return services;
    }

    private sealed class ExplicitCoalescingDrainScopeFactory : IRuntimeCoalescingDrainScopeFactory
    {
        public IRuntimeCoalescingDrainScope Begin(string workflowExecutionId, ResolvedPersistenceMode mode) =>
            throw new NotSupportedException("The registration test does not begin a runtime drain.");
    }
}
