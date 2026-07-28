using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workflows.Runtime.Tests;

// End-to-end coverage for the opt-in burst-coalescing checkpoint persistence policy (W9, findings E3-6/RT-10).
// A straight-line workflow is driven to completion through the in-process agent under the default (Immediate) policy
// and again under the coalescing policy over the same in-memory durable substrate; the two runs must reach identical
// terminal state while coalescing performs strictly fewer durable checkpoint commits (Elsa-3-style burst folding).
public sealed class RuntimeCheckpointCoalescingTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddCoalescingRuntimeCheckpointPersistence_SelectsCoalescingPolicyAndDecoratesStores()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddCoalescingRuntimeCheckpointPersistence();

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

    [Fact]
    public async Task CoalescingOutboxLookup_ConsultsActiveOverlayBeforeDurableInner()
    {
        var inner = new InMemoryRuntimeCheckpointCommitStore();
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var store = new CoalescingRuntimePostCommitOutboxStore(
            new CoalescingInner<IRuntimePostCommitOutboxStore>(inner),
            accessor);
        var session = new RuntimeCoalescingSession(
            "parent-dispatch",
            new InMemoryWorkflowSchedulerWorkQueue(),
            new CoalescingRuntimeCheckpointPersistenceOptions());
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
            new CoalescingRuntimeCheckpointPersistenceOptions());
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

    [Fact]
    public async Task ActivityAttemptBoundary_FlushesBeforeActivation_AndStartsFreshSegment()
    {
        const string workflowExecutionId = "wfexec-1";
        var innerStore = new InMemoryRuntimeCheckpointCommitStore();
        var session = new RuntimeCoalescingSession(
            workflowExecutionId,
            new InMemoryWorkflowSchedulerWorkQueue(),
            new CoalescingRuntimeCheckpointPersistenceOptions());
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
        Assert.Equal(0, session.HopCount);
        Assert.True(session.TryGetActivity("actexec-1", out var overlayState, out var tombstoned));
        Assert.False(tombstoned);
        Assert.Equal("attempt-1", overlayState!.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);

        await store.CommitAsync(
            NewEmptyCommit(workflowExecutionId, 3, RuntimeCheckpointNames.ActivityCompleted),
            new(RuntimeCheckpointPersistenceMode.Deferred));
        Assert.Equal(1, session.HopCount);
        Assert.Single(innerStore.ListCommits());

        await store.CommitAsync(
            NewEmptyCommit(workflowExecutionId, 4, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.False(session.IsActive);
        Assert.Equal(2, innerStore.ListCommits().Count);
    }

    // ADR 0032 R2 / spec 107: a ReplaySafe attempt-claim arrives as a Deferred decision (the coalescing policy
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
            new CoalescingRuntimeCheckpointPersistenceOptions());
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
        Assert.Equal(1, session.HopCount);
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
            new CoalescingRuntimeCheckpointPersistenceOptions());
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
        services.AddCoalescingRuntimeCheckpointPersistence();

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
        var inner = new CountingActivityExecutionStateStore();
        var session = new RuntimeCoalescingSession(
            "wfexec-1",
            new InMemoryWorkflowSchedulerWorkQueue(),
            new CoalescingRuntimeCheckpointPersistenceOptions());
        var store = new CoalescingActivityExecutionStateStore(
            new CoalescingInner<IActivityExecutionStateStore>(inner),
            new FixedCoalescingSessionAccessor(session));
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
        Assert.Equal(4, inner.PageReadCount);
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
        services.AddCoalescingRuntimeCheckpointPersistence();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryDurableTimerStore>(provider.GetRequiredService<IDurableTimerStore>());
        Assert.IsType<InMemoryBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
    }

    [Fact]
    public void WithoutOptIn_KeepsImmediatePolicyAndUndecoratedStores()
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
    public async Task Coalescing_ReachesSameTerminalStateWithFewerCommitsThanImmediate()
    {
        var immediate = await DriveAsync(coalescing: false);
        var coalescing = await DriveAsync(coalescing: true);

        output.WriteLine($"Immediate durable checkpoint commits: {immediate.CommitCount}");
        output.WriteLine($"Coalescing durable checkpoint commits: {coalescing.CommitCount}");

        // Behavior parity: identical terminal activity-execution snapshot, and the run genuinely completed — a
        // dispatch fault would previously park silently in the poison store and leave both runs equally "identical"
        // while stuck in Running.
        Assert.Equal(immediate.Snapshot, coalescing.Snapshot);
        Assert.NotEmpty(coalescing.Snapshot);
        Assert.Equal(WorkflowExecutionStatus.Completed, immediate.State?.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, coalescing.State?.Status);

        // Burst folding: coalescing performs strictly fewer durable commits, converging toward Elsa 3's one-per-burst.
        Assert.True(coalescing.CommitCount < immediate.CommitCount,
            $"Expected coalescing ({coalescing.CommitCount}) < immediate ({immediate.CommitCount}).");
        Assert.Equal(1, coalescing.CommitCount);
    }

    // End-to-end liveness across a cap fold in the real drain loop: with a cap of 1, intermediate fold-and-flushes
    // land mid-drain, the session keeps coalescing (fresh segments), overlay continuation delivery keeps the drain
    // moving, and the run still reaches the same completed terminal state as an uncapped coalescing run.
    [Fact]
    public async Task Coalescing_TinyCap_CapFoldsMidDrain_AndRunStillCompletes()
    {
        var uncapped = await DriveAsync(coalescing: true);
        var capped = await DriveAsync(coalescing: true, maxSegmentCheckpoints: 1);

        output.WriteLine($"Uncapped coalescing commits: {uncapped.CommitCount}");
        output.WriteLine($"Cap=1 coalescing commits: {capped.CommitCount}");

        Assert.Equal(WorkflowExecutionStatus.Completed, capped.State?.Status);
        Assert.Equal(uncapped.Snapshot, capped.Snapshot);
        Assert.NotEmpty(capped.Snapshot);
        // The tiny cap genuinely forced intermediate folds (more commits than the single quiescence fold), while the
        // fresh-segment restart still kept it a fold-per-window shape.
        Assert.True(capped.CommitCount > uncapped.CommitCount,
            $"Expected cap=1 ({capped.CommitCount}) to force intermediate folds beyond the uncapped run ({uncapped.CommitCount}).");
    }

    [Fact]
    public async Task AuthoredImmediateCadence_UnderCoalescedHost_RunsImmediate_AndStampsTheRun()
    {
        // ADR 0032 R5 precedence: a workflow that authored Immediate must run Immediate even though the host default is
        // Coalesced — the drain skips the coalescing session for this execution, so its durable commit count matches an
        // Immediate host, not the folded single-commit burst.
        var immediateHost = await DriveAsync(coalescing: false);
        var authoredImmediate = await DriveAsync(
            coalescing: true,
            authoredCadence: new WorkflowExecutableCheckpointCadence(WorkflowExecutableCheckpointCadence.ImmediateMode));

        Assert.Equal(immediateHost.Snapshot, authoredImmediate.Snapshot);
        Assert.Equal(WorkflowExecutionStatus.Completed, authoredImmediate.State?.Status);
        Assert.Equal(immediateHost.CommitCount, authoredImmediate.CommitCount);
        Assert.True(authoredImmediate.CommitCount > 1,
            $"Expected the authored-Immediate run to perform per-checkpoint commits, but saw {authoredImmediate.CommitCount}.");

        // The per-run stamp records the effective cadence the run executed under (upgrades #850's host-only projection).
        Assert.Equal(
            WorkflowExecutableCheckpointCadence.ImmediateMode,
            authoredImmediate.State!.SystemMetadata[RuntimeMetadataKeys.CheckpointCadence]);
        Assert.False(authoredImmediate.State.SystemMetadata.ContainsKey(RuntimeMetadataKeys.CheckpointMaxSegmentCheckpoints));
    }

    [Fact]
    public async Task AuthoredNoCadence_UnderCoalescedHost_UsesHostDefault_AndStampsCoalescedWithHostCap()
    {
        var run = await DriveAsync(coalescing: true);

        Assert.Equal(WorkflowExecutionStatus.Completed, run.State?.Status);
        Assert.Equal(1, run.CommitCount);
        Assert.Equal(
            WorkflowExecutableCheckpointCadence.CoalescedMode,
            run.State!.SystemMetadata[RuntimeMetadataKeys.CheckpointCadence]);
        Assert.Equal("50", run.State.SystemMetadata[RuntimeMetadataKeys.CheckpointMaxSegmentCheckpoints]);
    }

    [Fact]
    public async Task AuthoredCoalescedCadence_StampsTheAuthoredCap_AndStillCoalesces()
    {
        var run = await DriveAsync(
            coalescing: true,
            authoredCadence: new WorkflowExecutableCheckpointCadence(WorkflowExecutableCheckpointCadence.CoalescedMode, 8));

        Assert.Equal(WorkflowExecutionStatus.Completed, run.State?.Status);
        Assert.Equal(1, run.CommitCount);
        Assert.Equal(
            WorkflowExecutableCheckpointCadence.CoalescedMode,
            run.State!.SystemMetadata[RuntimeMetadataKeys.CheckpointCadence]);
        Assert.Equal("8", run.State.SystemMetadata[RuntimeMetadataKeys.CheckpointMaxSegmentCheckpoints]);
    }

    [Fact]
    public async Task AuthoredCoalescedCadence_MandatoryBookmarkBoundary_StillFlushesImmediately()
    {
        // Precedence guardrail (ADR 0032 R5): the mandatory-boundary set is never relaxable by any authored cadence.
        // Even with the most relaxed authored cadence, a BookmarkCreated suspend boundary must land durably within the
        // segment, exactly as on a host-default coalesced run.
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddCoalescingRuntimeCheckpointPersistence();

        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(NewExecutableWithResumeTarget(
            new WorkflowExecutableCheckpointCadence(WorkflowExecutableCheckpointCadence.CoalescedMode, 500)));
        await provider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(NewRunningActivityState());

        var agentProvider = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        var agent = await agentProvider.GetAgentAsync(NewSchedulerWorkActivationRequest("wfexec-1"));
        await agent.EnqueueAsync(NewCreateBookmarkEnvelope());

        var bookmark = await provider.GetRequiredService<IBookmarkStateStore>().FindAsync("wfexec-1", "bookmark-1");
        Assert.NotNull(bookmark);

        var commit = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        Assert.Equal(RuntimeCheckpointNames.BookmarkCreated, commit.Commit.Checkpoint.Name);
    }

    [Fact]
    public async Task CrashMidSegment_DurableQueueStillHoldsSegmentEntry_AndNoPartialCheckpointPersisted()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddCoalescingRuntimeCheckpointPersistence();
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
        services.AddCoalescingRuntimeCheckpointPersistence();

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

    // ADR 0032 segment-cap follow-up: a cap-hit folds-and-flushes the segment and STARTS A FRESH SEGMENT (like a
    // mandatory attempt boundary), instead of deactivating the session and degrading the drain remainder to
    // per-checkpoint Immediate persistence.
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
            new CoalescingRuntimeCheckpointPersistenceOptions { MaxSegmentCheckpoints = cap });
        var accessor = new FixedCoalescingSessionAccessor(session);
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            accessor);

        Assert.Same(session, accessor.Current);
        Assert.True(session.AppliesTo("wfexec-cap"));

        for (var checkpoint = 1; checkpoint <= cap; checkpoint++)
            await store.CommitAsync(NewEmptyDeferredCommit(checkpoint), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(cap, session.HopCount);
        Assert.True(session.IsActive);
        Assert.Empty(innerStore.ListCommits());

        // The cap-tripping checkpoint folds the whole segment (cap buffered hops + itself) into one durable commit
        // and the session keeps coalescing with an empty fresh segment.
        await store.CommitAsync(NewEmptyDeferredCommit(cap + 1), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(0, session.HopCount);
        Assert.True(session.IsActive);
        Assert.Single(innerStore.ListCommits());

        // The fresh segment buffers again instead of falling back to per-checkpoint Immediate persistence.
        for (var checkpoint = cap + 2; checkpoint <= 2 * cap + 1; checkpoint++)
            await store.CommitAsync(NewEmptyDeferredCommit(checkpoint), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(cap, session.HopCount);
        Assert.Single(innerStore.ListCommits());

        await store.CommitAsync(NewEmptyDeferredCommit(2 * cap + 2), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(0, session.HopCount);
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
    // one durable commit per (cap + 1)-checkpoint window plus the terminal fold — not a per-checkpoint Immediate
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
            new CoalescingRuntimeCheckpointPersistenceOptions { MaxSegmentCheckpoints = cap });
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            new FixedCoalescingSessionAccessor(session));

        for (var checkpoint = 1; checkpoint <= checkpoints; checkpoint++)
            await store.CommitAsync(NewEmptyDeferredCommit(checkpoint), new(RuntimeCheckpointPersistenceMode.Deferred));

        await store.CommitAsync(
            NewEmptyCommit("wfexec-cap", checkpoints + 1, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        // Each intermediate fold covers cap buffered hops + the cap-tripping checkpoint; the terminal boundary folds
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
            new CoalescingRuntimeCheckpointPersistenceOptions { MaxSegmentCheckpoints = 1 },
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
            new CoalescingRuntimeCheckpointPersistenceOptions());
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

    private static async Task<(IReadOnlyList<(string NodeId, ActivityExecutionStatus Status)> Snapshot, int CommitCount, WorkflowExecutionState? State)> DriveAsync(
        bool coalescing,
        WorkflowExecutableCheckpointCadence? authoredCadence = null,
        int? maxSegmentCheckpoints = null)
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        if (coalescing)
        {
            services.AddCoalescingRuntimeCheckpointPersistence(options =>
            {
                if (maxSegmentCheckpoints is { } cap)
                    options.MaxSegmentCheckpoints = cap;
            });
        }

        using var provider = services.BuildServiceProvider();
        await SeedAsync(provider, authoredCadence);
        await EnqueueStartAsync(provider);

        var snapshot = await SnapshotAsync(provider);
        var commitCount = provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits().Count;
        var state = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
        return (snapshot, commitCount, state);
    }

    private static async Task SeedAsync(ServiceProvider provider, WorkflowExecutableCheckpointCadence? authoredCadence = null)
    {
        var store = provider.GetRequiredService<IWorkflowExecutableStore>();
        await store.SaveAsync(NewExecutable(authoredCadence));
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
                // cadence resolver reads on the first drain, before any WorkflowExecutionState row exists (ADR 0032 R5).
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

    private static RuntimeCheckpointCommit NewEmptyDeferredCommit(int checkpoint) =>
        NewEmptyCommit("wfexec-cap", checkpoint, "CapProbe");

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

    private static RuntimeCheckpointStateChangeSet ActivityUpsert(ActivityExecutionState state) =>
        new(
            workflowExecution: null,
            scheduler: null,
            activityExecutions:
            [
                new RuntimeStateChange<ActivityExecutionState>(
                    state.Execution.ActivityExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    state,
                    new Dictionary<string, string>())
            ],
            bookmarks: [],
            durableValues: [],
            incidents: [],
            operational: []);

    // A deferrable (non-boundary) checkpoint commit carrying a pending EnqueueSchedulerWork continuation intent,
    // like a hot-loop ActivityCompleted hop that schedules its successor.
    private static RuntimeCheckpointCommit NewContinuationIntentCommit(string workflowExecutionId, int checkpoint)
    {
        var commit = NewEmptyCommit(workflowExecutionId, checkpoint, RuntimeCheckpointNames.ActivityCompleted) with
        {
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
            StateChanges = commit.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(commit)),
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

    private static WorkflowExecutable NewExecutableWithResumeTarget(WorkflowExecutableCheckpointCadence? checkpointCadence = null)
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
            checkpointCadence: checkpointCadence);
    }

    // The driven workflow must genuinely run to completion: a CLR-activity node without a pinned activity contract
    // faults dispatch with VF-ACT-001 and gets parked in the poison store, which (since poison surfacing landed)
    // faults the workflow instead of hanging it silently. A Finish intrinsic root needs no CLR contract and
    // completes the run, so the burst-folding comparison measures a real start-to-completed burst.
    private static WorkflowExecutable NewExecutable(WorkflowExecutableCheckpointCadence? checkpointCadence = null)
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
            checkpointCadence: checkpointCadence);
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

    private sealed class CountingActivityExecutionStateStore : IActivityExecutionStateStore
    {
        private readonly InMemoryActivityExecutionStateStore inner = new();

        public int PageReadCount { get; private set; }
        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default) => inner.SaveAsync(state, cancellationToken);
        public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) => inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        public ValueTask<long> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => inner.CountAsync(workflowExecutionId, cancellationToken);
        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query, CancellationToken cancellationToken = default) => inner.ListByParentPageAsync(query, cancellationToken);

        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query, CancellationToken cancellationToken = default)
        {
            PageReadCount++;
            return inner.ListPageAsync(query, cancellationToken);
        }
    }
}
