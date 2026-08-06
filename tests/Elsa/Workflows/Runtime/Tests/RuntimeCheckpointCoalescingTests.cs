using System.Text.Json;
using System.Reflection;
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
        using var scope = provider.CreateScope();
        var orchestrator = Assert.IsType<WorkflowDrainOrchestrator>(
            scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>());
        Assert.NotNull(typeof(WorkflowDrainOrchestrator)
            .GetField("_coalescingScopeFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(orchestrator));
        Assert.NotNull(typeof(WorkflowDrainOrchestrator)
            .GetField("_preparedRecoveryCoordinator", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(orchestrator));
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
        var innerStore = RuntimeCheckpointTestStores.Create();
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

        await CommitPreparedThroughStoreAsync(store,
            NewEmptyCommit(workflowExecutionId, 1, RuntimeCheckpointNames.ActivityStarted) with
            {
                StateChanges = ActivityUpsert(startedState)
            },
            new(RuntimeCheckpointPersistenceMode.Deferred));
        await CommitPreparedThroughStoreAsync(store,
            NewEmptyCommit(workflowExecutionId, 2, RuntimeCheckpointNames.ActivityAttemptClaimed) with
            {
                StateChanges = ActivityUpsert(claimedState)
            },
            new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.Equal(
            [RuntimeCheckpointNames.ActivityStarted, RuntimeCheckpointNames.ActivityAttemptClaimed],
            innerStore.ListCommits().Select(record => record.Commit.Checkpoint.Name));
        Assert.True(session.IsActive);
        Assert.Equal(0, session.HopCount);
        Assert.True(session.TryGetActivity("actexec-1", out var overlayState, out var tombstoned));
        Assert.False(tombstoned);
        Assert.Equal("attempt-1", overlayState!.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);

        await CommitPreparedThroughStoreAsync(store,
            NewEmptyCommit(workflowExecutionId, 3, RuntimeCheckpointNames.ActivityCompleted),
            new(RuntimeCheckpointPersistenceMode.Deferred));
        Assert.Equal(1, session.HopCount);
        Assert.Equal(2, innerStore.ListCommits().Count);

        await CommitPreparedThroughStoreAsync(store,
            NewEmptyCommit(workflowExecutionId, 4, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.False(session.IsActive);
        Assert.Equal(4, innerStore.ListCommits().Count);
    }

    // ADR 0032 R2 / spec 107: a ReplaySafe attempt-claim arrives as a Deferred decision (the coalescing policy
    // decided so from the checkpoint's profile metadata). Unlike the External/Immediate case above, it must NOT
    // flush before activation — it buffers into the overlay working set and folds forward into the next flushed
    // commit, so a hot loop of ReplaySafe activities collapses to one coalesced commit.
    [Fact]
    public async Task ReplaySafeAttemptClaim_IsDeferred_BuffersInsteadOfFlushing_AndFoldsForward()
    {
        const string workflowExecutionId = "wfexec-1";
        var innerStore = RuntimeCheckpointTestStores.Create();
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
        await CommitPreparedThroughStoreAsync(store,
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
        await CommitPreparedThroughStoreAsync(store,
            NewEmptyCommit(workflowExecutionId, 2, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.False(session.IsActive);
        var markers = innerStore.ListCommits();
        Assert.Equal(2, markers.Count);
        Assert.Equal("attempt-1", Assert.Single(markers.Single(record => record.Commit.CommitId == "commit-cap-1").Commit.StateChanges.ActivityExecutions).State.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);
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

        await CommitPreparedThroughStoreAsync(store,
            deferred with
            {
                StateChanges = deferred.StateChanges.WithPostCommitOutbox([pendingOutbox])
            },
            new(RuntimeCheckpointPersistenceMode.Deferred));
        await CommitPreparedThroughStoreAsync(store,
            NewEmptyCommit(workflowExecutionId, 2, RuntimeCheckpointNames.ActivityAttemptClaimed),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.False(session.IsActive);
        var markers = innerStore.ListCommits();
        Assert.Equal(2, markers.Count);
        Assert.Single(markers, marker =>
            StringComparer.Ordinal.Equals(marker.Commit.Checkpoint.Name, RuntimeCheckpointNames.ActivityAttemptClaimed));
        Assert.Single(markers.Single(marker => marker.Commit.CommitId == "commit-cap-1").Commit.StateChanges.PostCommitOutbox);
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
    public async Task Coalescing_WithContinuationWork_UsesImmediateOverride_AndReachesSameTerminalState()
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

        // Every enriched checkpoint carries continuation outbox work, so the approved contract forces Immediate.
        // Logical markers therefore match the Immediate host and no physical prepared fold occurs.
        Assert.Equal(immediate.CommitCount, coalescing.CommitCount);
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
        Assert.Equal(uncapped.CommitCount, capped.CommitCount);
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
        Assert.Equal(3, run.CommitCount);
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
        Assert.Equal(3, run.CommitCount);
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
    public async Task CrashAfterImmediateContinuationCommit_LeavesDurableOutboxAuthority()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddCoalescingRuntimeCheckpointPersistence();
        // Enriched continuation work forces Immediate under the prepared-ledger contract. Crash after that commit but
        // before delivery must leave the durable outbox, not the consumed source queue, as recovery authority.
        services.AddSingleton<IRuntimePostCommitOutboxProcessor>(new ThrowingOutboxProcessor());

        using var provider = services.BuildServiceProvider();
        await SeedAsync(provider);

        await Assert.ThrowsAnyAsync<Exception>(() => EnqueueStartAsync(provider).AsTask());

        var innerQueue = provider.GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>().Value;
        var pending = await innerQueue.ListPendingWorkflowExecutionIdsAsync(10);
        Assert.DoesNotContain("wfexec-1", pending);

        var innerStore = provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        Assert.NotEmpty(innerStore.ListCommits());
        var pendingOutbox = Assert.Single(await innerStore.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            DateTimeOffset.UtcNow.AddMinutes(5),
            10,
            workflowExecutionId: "wfexec-1")));
        Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, pendingOutbox.Intent.Kind);
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
            await CommitPreparedThroughStoreAsync(store, NewEmptyDeferredCommit(checkpoint), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(cap, session.HopCount);
        Assert.True(session.IsActive);
        Assert.Empty(innerStore.ListCommits());

        // The cap-tripping checkpoint folds the whole segment (cap buffered hops + itself) into one durable commit
        // and the session keeps coalescing with an empty fresh segment.
        await CommitPreparedThroughStoreAsync(store, NewEmptyDeferredCommit(cap + 1), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(0, session.HopCount);
        Assert.True(session.IsActive);
        Assert.Equal(cap + 1, innerStore.ListCommits().Count);

        // The fresh segment buffers again instead of falling back to per-checkpoint Immediate persistence.
        for (var checkpoint = cap + 2; checkpoint <= 2 * cap + 1; checkpoint++)
            await CommitPreparedThroughStoreAsync(store, NewEmptyDeferredCommit(checkpoint), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(cap, session.HopCount);
        Assert.Equal(cap + 1, innerStore.ListCommits().Count);

        await CommitPreparedThroughStoreAsync(store, NewEmptyDeferredCommit(2 * cap + 2), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(0, session.HopCount);
        Assert.True(session.IsActive);
        Assert.Equal(2 * cap + 2, innerStore.ListCommits().Count);

        // The fresh segment is empty after the second exact cap fold, so the terminal boundary finalizes directly
        // and deactivates without inventing a third physical batch fold.
        await CommitPreparedThroughStoreAsync(store,
            NewEmptyCommit("wfexec-cap", 2 * cap + 3, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.False(session.IsActive);
        Assert.Equal(2 * cap + 3, innerStore.ListCommits().Count);
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
            await CommitPreparedThroughStoreAsync(store, NewEmptyDeferredCommit(checkpoint), new(RuntimeCheckpointPersistenceMode.Deferred));

        await CommitPreparedThroughStoreAsync(store,
            NewEmptyCommit("wfexec-cap", checkpoints + 1, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        // Each intermediate fold covers cap buffered hops + the cap-tripping checkpoint; the terminal boundary folds
        // the remainder. 35 checkpoints @ cap 10 -> 3 intermediate folds (33 checkpoints) + 1 terminal fold (2 + terminal).
        Assert.Equal(checkpoints + 1, innerStore.ListCommits().Count);
        Assert.False(session.IsActive);
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

        await CommitPreparedThroughStoreAsync(store, NewEmptyDeferredCommit(1), new(RuntimeCheckpointPersistenceMode.Deferred));

        var capCommit = NewContinuationIntentCommit(workflowExecutionId, 2);
        var outboxItemId = Assert.Single(capCommit.StateChanges.PostCommitOutbox).StateId;
        await CommitPreparedThroughStoreAsync(store, capCommit, new(RuntimeCheckpointPersistenceMode.Deferred));

        // The fold landed durably with the continuation intent still Pending, and the session stayed active with the
        // item owned by (and deliverable from) the overlay.
        Assert.True(session.IsActive);
        var folded = innerStore.ListCommits().Single(record => record.Commit.CommitId == capCommit.CommitId).Commit;
        Assert.Equal(outboxItemId, Assert.Single(folded.StateChanges.PostCommitOutbox).StateId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await innerStore.FindAsync(outboxItemId))!.Status);
        Assert.True(session.OwnsOutboxItem(outboxItemId));

        // The drain delivers the continuation from the overlay during the next segment.
        session.RecordOutboxDelivery(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId,
            RuntimePostCommitOutboxStatus.Delivered,
            Now.AddTicks(3)));

        await CommitPreparedThroughStoreAsync(store,
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

    // T028 RED: an otherwise honest Immediate boundary may retain same-drain locality only when every committed
    // outbox row is an EnqueueSchedulerWork continuation and the execution context is empty and unchanged. The
    // boundary must be durable first, then its exact Pending rows move into the existing overlay; no new dispatcher
    // path or synthetic work item is allowed.
    [Fact]
    public async Task QualifyingImmediateSchedulerContinuation_ImportsExactPendingRowsIntoTheActiveOverlay()
    {
        await using var fixture = await SchedulerContinuationFixture.CreateAsync("wfexec-continuation");
        var boundary = fixture.NewQualifyingBoundary(1, "one", "two");
        var expectedIds = boundary.StateChanges.PostCommitOutbox.Select(change => change.StateId).Order().ToArray();
        var expectedWorkItemIds = boundary.PostCommitIntents
            .Select(intent => intent.MaterializedSchedulerWorkItem!.WorkItemId)
            .Order()
            .ToArray();

        var result = await fixture.CommitAsync(boundary);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, result.Status);
        var durableRows = await fixture.InnerStore.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            Now.AddMinutes(1),
            10,
            fixture.WorkflowExecutionId));
        Assert.Equal(expectedIds, durableRows.Select(item => item.OutboxItemId).Order());
        Assert.All(durableRows, item => Assert.Equal(RuntimePostCommitOutboxStatus.Pending, item.Status));
        Assert.Empty(await fixture.InnerQueue.ListAllAsync(new RuntimeSchedulerWorkQuery(fixture.WorkflowExecutionId)));

        // Intentional T028 RED: T029 must keep the existing session alive and move exactly these already durable
        // rows into its outbox/scheduler overlay. T027 correctly leaves a normal Immediate boundary terminal.
        Assert.True(fixture.Session.IsActive);
        Assert.True(fixture.Session.TryGetActivity("actexec-continuation", out var activity, out var tombstoned));
        Assert.False(tombstoned);
        Assert.Equal(ActivityExecutionStatus.Running, activity!.Status);
        Assert.All(expectedIds, id => Assert.True(fixture.Session.IsOutboxDurablyPersisted(id)));
        Assert.Equal(expectedIds, fixture.Session.GetDeliverableOutbox(new RuntimePostCommitOutboxQuery(
            Now.AddMinutes(1),
            10,
            fixture.WorkflowExecutionId)).Select(item => item.OutboxItemId).Order());

        await fixture.ProcessOverlayAsync();
        await fixture.ProcessOverlayAsync();

        var overlayItems = await fixture.ListOverlayAsync();
        Assert.Equal(expectedWorkItemIds, overlayItems
            .Where(item => item.WorkItemId != fixture.Source.WorkItemId)
            .Select(item => item.WorkItemId)
            .Order());
        foreach (var id in expectedIds)
            Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await fixture.InnerStore.FindAsync(id))!.Status);

        await fixture.CommitAsync(NewEmptyCommit(fixture.WorkflowExecutionId, 2, RuntimeCheckpointNames.WorkflowCompleted));
        foreach (var id in expectedIds)
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await fixture.InnerStore.FindAsync(id))!.Status);
    }

    [Fact]
    public async Task ActiveSessionReplayOfQualifyingBoundary_AdvancesAndDeactivatesWithoutRepeatingDurableEffects()
    {
        await using var fixture = await SchedulerContinuationFixture.CreateAsync("wfexec-continuation-replay");
        var boundary = fixture.NewQualifyingBoundary(1, "replay");
        var outboxItemId = Assert.Single(boundary.StateChanges.PostCommitOutbox).StateId;

        using (fixture.Accessor.Push(fixture.Session))
        {
            var preparation = await fixture.CheckpointStore.PrepareAsync(RuntimeCheckpointPrepareRequest.From(boundary));
            var token = Assert.IsType<RuntimeCheckpointPreparationToken>(preparation.Token);
            var preparedCommit = boundary with
            {
                Checkpoint = boundary.Checkpoint with { Provenance = token.Provenance },
                ExpectedFence = token.ExpectedFence
            };

            var committed = await fixture.CheckpointStore.CommitPreparedAsync(
                token,
                preparedCommit,
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, committed.Status);
            Assert.True(fixture.Session.IsActive);
            Assert.Empty(await fixture.InnerQueue.ListAllAsync(fixture.WorkflowExecutionId));

            // The replay finds no Prepared reservation, yet it must still retire the live frontier that the first
            // successful continuation boundary left active. It may not import a second row or scheduler work item.
            var replay = await fixture.CheckpointStore.CommitPreparedAsync(
                token,
                preparedCommit,
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Equal(RuntimeCheckpointCommitStoreStatus.Replay, replay.Status);
        }

        Assert.False(fixture.Session.IsActive);
        Assert.Empty(await fixture.InnerQueue.ListAllAsync(fixture.WorkflowExecutionId));
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);
        Assert.Single(await fixture.InnerStore.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            Now.AddMinutes(1),
            10,
            fixture.WorkflowExecutionId)));
    }

    // T028 RED: inline delivery is an optimization inside an active coalescing drain, not a durable acknowledgement.
    // The live overlay is not a durable acknowledgement. Its row stays Pending until a later successful
    // checkpoint/fold incorporates the inline effect and reconciliation runs.
    [Fact]
    public async Task QualifyingImmediateSchedulerContinuation_DelaysDeliveredAcknowledgementUntilLaterFold()
    {
        await using var fixture = await SchedulerContinuationFixture.CreateAsync("wfexec-continuation-delivery");
        var boundary = fixture.NewQualifyingBoundary(1, "only");
        var outboxItemId = Assert.Single(boundary.StateChanges.PostCommitOutbox).StateId;
        var continuation = Assert.Single(boundary.PostCommitIntents).MaterializedSchedulerWorkItem!;

        await fixture.CommitAsync(boundary);

        // Crash-before-inline-dispatch: the durable row is already a normal Pending redrive candidate.
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);
        Assert.True(fixture.Session.OwnsOutboxItem(outboxItemId));

        // Crash-after-inline-dispatch-before-fold: the normal processor/dispatcher enqueues the real serialized work
        // through the coalescing queue, while its Delivered mark remains an overlay fact until a later fold.
        await fixture.ProcessOverlayAsync();
        Assert.Equal([continuation.WorkItemId], (await fixture.ListOverlayAsync())
            .Where(item => item.WorkItemId != fixture.Source.WorkItemId)
            .Select(item => item.WorkItemId));
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);

        await fixture.CommitAsync(NewEmptyCommit(fixture.WorkflowExecutionId, 3, RuntimeCheckpointNames.WorkflowCompleted));

        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);
    }

    [Fact]
    public async Task CrashBeforeInlineDispatch_OrdinaryDurableRedrive_QueuesOneItemAndAcknowledgesAfterDurableEnqueue()
    {
        await using var fixture = await SchedulerContinuationFixture.CreateAsync("wfexec-crash-before-inline");
        var boundary = fixture.NewQualifyingBoundary(1, "redrive");
        var outboxItemId = Assert.Single(boundary.StateChanges.PostCommitOutbox).StateId;
        var continuation = Assert.Single(boundary.PostCommitIntents).MaterializedSchedulerWorkItem!;

        await fixture.CommitAsync(boundary);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);

        // The original drain/session is gone before it can deliver. A clean processor uses the ordinary durable
        // outbox/queue path and the real scheduler dispatcher; it must not need a direct handler invocation.
        var redrive = CreateOrdinaryDurableRedrive(fixture);
        var firstSweep = await redrive.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10, fixture.WorkflowExecutionId));

        Assert.Equal(1, firstSweep.AttemptedCount);
        Assert.Equal(1, firstSweep.DeliveredCount);

        Assert.Equal([continuation.WorkItemId], (await fixture.InnerQueue.ListAllAsync(
            new RuntimeSchedulerWorkQuery(fixture.WorkflowExecutionId))).Select(item => item.WorkItemId));
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);

        var secondSweep = await redrive.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10, fixture.WorkflowExecutionId));
        Assert.Equal(0, secondSweep.AttemptedCount);
        Assert.Equal([continuation.WorkItemId], (await fixture.InnerQueue.ListAllAsync(
            new RuntimeSchedulerWorkQuery(fixture.WorkflowExecutionId))).Select(item => item.WorkItemId));
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);
    }

    [Fact]
    public async Task CrashAfterInlineDispatchBeforeFold_OrdinaryDurableRedrive_ConvergesWithoutDuplicateWork()
    {
        await using var fixture = await SchedulerContinuationFixture.CreateAsync("wfexec-crash-after-inline");
        var boundary = fixture.NewQualifyingBoundary(1, "redrive");
        var outboxItemId = Assert.Single(boundary.StateChanges.PostCommitOutbox).StateId;
        var continuation = Assert.Single(boundary.PostCommitIntents).MaterializedSchedulerWorkItem!;

        await fixture.CommitAsync(boundary);
        await fixture.ProcessOverlayAsync();

        Assert.Equal([continuation.WorkItemId], (await fixture.ListOverlayAsync())
            .Where(item => item.WorkItemId != fixture.Source.WorkItemId)
            .Select(item => item.WorkItemId));
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);

        // Simulate process loss after the overlay dispatch but before another checkpoint can fold/reconcile it.
        // The first ordinary delivery reaches the durable queue, then loses the acknowledgement. The original Pending
        // row is the only recoverable durable state; scheduler enqueue idempotency makes the subsequent redrive safe.
        var interruptedRedrive = new RuntimePostCommitOutboxProcessor(
            new ThrowOnceOnDeliveryResultOutboxStore(fixture.InnerStore),
            new RuntimeSchedulerPostCommitIntentDispatcher(fixture.InnerQueue),
            TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => interruptedRedrive.ProcessAsync(
            new RuntimePostCommitOutboxProcessRequest(10, fixture.WorkflowExecutionId)).AsTask());

        Assert.Equal([continuation.WorkItemId], (await fixture.InnerQueue.ListAllAsync(
            new RuntimeSchedulerWorkQuery(fixture.WorkflowExecutionId))).Select(item => item.WorkItemId));
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);

        var redrive = CreateOrdinaryDurableRedrive(fixture);
        var firstSweep = await redrive.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10, fixture.WorkflowExecutionId));

        Assert.Equal(1, firstSweep.AttemptedCount);
        Assert.Equal(1, firstSweep.DeliveredCount);
        Assert.Equal([continuation.WorkItemId], (await fixture.InnerQueue.ListAllAsync(
            new RuntimeSchedulerWorkQuery(fixture.WorkflowExecutionId))).Select(item => item.WorkItemId));
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);

        var secondSweep = await redrive.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10, fixture.WorkflowExecutionId));
        Assert.Equal(0, secondSweep.AttemptedCount);
        Assert.Equal([continuation.WorkItemId], (await fixture.InnerQueue.ListAllAsync(
            new RuntimeSchedulerWorkQuery(fixture.WorkflowExecutionId))).Select(item => item.WorkItemId));
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);
    }

    [Theory]
    [InlineData("mixed")]
    [InlineData("external")]
    [InlineData("no-continuation")]
    public async Task NonQualifyingImmediateBoundaries_DeactivateInsteadOfWideningContinuationEligibility(string boundaryKind)
    {
        const string workflowExecutionId = "parent-dispatch";
        var innerStore = RuntimeCheckpointTestStores.Create();
        var session = new RuntimeCoalescingSession(
            workflowExecutionId,
            new InMemoryWorkflowSchedulerWorkQueue(),
            new CoalescingRuntimeCheckpointPersistenceOptions(),
            innerOutboxStore: innerStore);
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            new FixedCoalescingSessionAccessor(session));
        var boundary = boundaryKind switch
        {
            "mixed" => NewMixedSchedulerAndExternalBoundaryCommit(workflowExecutionId, 1),
            "external" => NewDispatchBoundaryCommit(),
            "no-continuation" => NewEmptyCommit(workflowExecutionId, 2, RuntimeCheckpointNames.WorkflowCompleted),
            _ => throw new ArgumentOutOfRangeException(nameof(boundaryKind))
        };

        await CommitPreparedThroughStoreAsync(store, boundary, new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.False(session.IsActive);
    }

    [Theory]
    [InlineData("context-only")]
    [InlineData("context-mutating")]
    public async Task ContextBearingImmediateBoundaries_DeactivateInsteadOfRetainingTheContinuationSession(string boundaryKind)
    {
        const string workflowExecutionId = "wfexec-context-boundary";
        var innerStore = RuntimeCheckpointTestStores.Create();
        var requestedContext = new RuntimeExecutionContextSnapshot(
            RuntimeExecutionContextSnapshot.CurrentVersion,
            new Dictionary<string, string> { ["continuation"] = boundaryKind });

        if (boundaryKind == "context-mutating")
        {
            await CommitPreparedThroughStoreAsync(
                innerStore,
                NewEmptyCommit(workflowExecutionId, 1, RuntimeCheckpointNames.ActivityStarted),
                new(RuntimeCheckpointPersistenceMode.Immediate),
                new RuntimeExecutionContextSnapshot(
                    RuntimeExecutionContextSnapshot.CurrentVersion,
                    new Dictionary<string, string> { ["continuation"] = "before" }));
        }

        var session = new RuntimeCoalescingSession(
            workflowExecutionId,
            new InMemoryWorkflowSchedulerWorkQueue(),
            new CoalescingRuntimeCheckpointPersistenceOptions(),
            innerOutboxStore: innerStore);
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            new FixedCoalescingSessionAccessor(session));
        var source = NewSchedulerWorkItem(workflowExecutionId, $"source-{boundaryKind}", WorkflowExecutionCommandKind.ScheduleActivity);

        await CommitPreparedThroughStoreAsync(
            store,
            NewSchedulerContinuationBoundaryCommit(source, 2, boundaryKind),
            new(RuntimeCheckpointPersistenceMode.Immediate),
            requestedContext);

        Assert.False(session.IsActive);
    }

    [Fact]
    public async Task FailedContinuationDelivery_DeactivatesIntoOrdinaryDurableProcessing()
    {
        await using var fixture = await SchedulerContinuationFixture.CreateAsync("wfexec-failed-delivery");
        var boundary = WithRetryPolicy(
            fixture.NewQualifyingBoundary(1, "retryable"),
            new RuntimePostCommitRetryPolicy(2, TimeSpan.FromSeconds(1)));
        var outboxItemId = Assert.Single(boundary.StateChanges.PostCommitOutbox).StateId;
        await fixture.CommitAsync(boundary);
        Assert.True(fixture.Session.IsActive);

        var failingProcessor = new RuntimePostCommitOutboxProcessor(
            fixture.OutboxStore,
            ThrowingSchedulerIntentDispatcher.Instance,
            TimeProvider.System);
        using (fixture.Accessor.Push(fixture.Session))
            await failingProcessor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10, fixture.WorkflowExecutionId));

        Assert.True(fixture.Session.TryFindOutboxItem(outboxItemId, out var overlayItem));
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, overlayItem!.Status);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);
        // After T029 retains the qualifying session, the retryable overlay delivery itself must deactivate it so that
        // the durable row becomes the ordinary processor's authority; a terminal checkpoint is not that transition.
        Assert.False(fixture.Session.IsActive);

        var ordinaryRedrive = CreateOrdinaryDurableRedrive(fixture);
        var recovered = await ordinaryRedrive.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10, fixture.WorkflowExecutionId));
        Assert.Equal(1, recovered.AttemptedCount);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);
    }

    [Fact]
    public async Task ExhaustedContinuationDelivery_DeactivatesIntoOrdinaryDurableProcessing()
    {
        await using var fixture = await SchedulerContinuationFixture.CreateAsync("wfexec-final-delivery");
        var boundary = WithRetryPolicy(
            fixture.NewQualifyingBoundary(1, "final"),
            new RuntimePostCommitRetryPolicy(1, TimeSpan.FromSeconds(1)));
        var outboxItemId = Assert.Single(boundary.StateChanges.PostCommitOutbox).StateId;
        await fixture.CommitAsync(boundary);

        var failingProcessor = new RuntimePostCommitOutboxProcessor(
            fixture.OutboxStore,
            ThrowingSchedulerIntentDispatcher.Instance,
            TimeProvider.System);
        using (fixture.Accessor.Push(fixture.Session))
            await failingProcessor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10, fixture.WorkflowExecutionId));

        Assert.True(fixture.Session.TryFindOutboxItem(outboxItemId, out var overlayItem));
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, overlayItem!.Status);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);
        Assert.False(fixture.Session.IsActive);

        var ordinaryRedrive = CreateOrdinaryDurableRedrive(fixture);
        var recovered = await ordinaryRedrive.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10, fixture.WorkflowExecutionId));
        Assert.Equal(1, recovered.AttemptedCount);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await fixture.InnerStore.FindAsync(outboxItemId))!.Status);
    }

    [Fact]
    public async Task ExactMaterializedRecoverySource_NewCommittedBoundaryAdvancesDurableSourceOnceAndKeepsOverlayLive()
    {
        await using var fixture = await OverlayRecoverySourceFixture.CreateAsync();
        var prepared = await fixture.PrepareAsync(fixture.RecoveryAuthority);

        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred, prepared.RequestedInitialPersistenceMode);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, prepared.Token.InitialPersistenceMode);
        Assert.Equal([fixture.Source.WorkItemId], (await fixture.InnerQueue.ListAllAsync(fixture.WorkflowExecutionId))
            .Select(item => item.WorkItemId));
        Assert.True(fixture.Session.RequiresDurableRecoveryHandoff);
        Assert.True(fixture.Session.OwnsOverlayClaim(fixture.OverlayClaim));

        var committed = await fixture.CommitAsync(prepared);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, committed.Status);
        Assert.Empty(await fixture.InnerQueue.ListAllAsync(fixture.WorkflowExecutionId));
        Assert.True(fixture.Session.OwnsOverlayClaim(fixture.OverlayClaim));
        Assert.True(fixture.Session.IsActive);
        Assert.True(fixture.Session.AppliesTo(fixture.WorkflowExecutionId));

        var durableLedger = Assert.Single(fixture.InnerStore.ListLogicalCheckpointLedgerEntries());
        Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Committed, durableLedger.Status);
        Assert.Equal(prepared.Token.LedgerToken, durableLedger.LedgerToken);
        Assert.Equal(prepared.Token.Provenance, durableLedger.Provenance);
        Assert.Equal(fixture.RecoveryAuthority, durableLedger.RecoveryAuthority);
    }

    [Fact]
    public async Task ExactMaterializedRecoverySource_ReplayRetiresWithoutASecondDurableEffect()
    {
        await using var fixture = await OverlayRecoverySourceFixture.CreateAsync();
        var prepared = await fixture.PrepareAsync(fixture.RecoveryAuthority);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, (await fixture.CommitAsync(prepared)).Status);

        var durableLedger = Assert.Single(fixture.InnerStore.ListLogicalCheckpointLedgerEntries());
        var durableCommitCount = fixture.InnerStore.ListCommits().Count;
        var replay = await fixture.CommitAsync(prepared);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Replay, replay.Status);
        Assert.False(fixture.Session.IsActive);
        Assert.Empty(await fixture.InnerQueue.ListAllAsync(fixture.WorkflowExecutionId));
        Assert.Equal(durableCommitCount, fixture.InnerStore.ListCommits().Count);
        var replayLedger = Assert.Single(fixture.InnerStore.ListLogicalCheckpointLedgerEntries());
        Assert.Equal(durableLedger.LedgerToken, replayLedger.LedgerToken);
        Assert.Equal(durableLedger.Provenance, replayLedger.Provenance);
        Assert.Equal(fixture.RecoveryAuthority, replayLedger.RecoveryAuthority);
    }

    [Theory]
    [InlineData(RuntimeCheckpointCommitStoreStatus.Conflict)]
    [InlineData(RuntimeCheckpointCommitStoreStatus.OwnershipLost)]
    public async Task ExactMaterializedRecoverySource_UnsuccessfulFinalizationKeepsSourceAndClaimForRetry(
        RuntimeCheckpointCommitStoreStatus finalizationStatus)
    {
        await using var fixture = await OverlayRecoverySourceFixture.CreateAsync(finalizationStatus);
        var prepared = await fixture.PrepareAsync(fixture.RecoveryAuthority);

        var result = await fixture.CommitAsync(prepared);

        Assert.Equal(finalizationStatus, result.Status);
        Assert.True(fixture.Session.IsActive);
        Assert.True(fixture.Session.AppliesTo(fixture.WorkflowExecutionId));
        Assert.True(fixture.Session.OwnsOverlayClaim(fixture.OverlayClaim));
        Assert.Equal([fixture.Source.WorkItemId], (await fixture.InnerQueue.ListAllAsync(fixture.WorkflowExecutionId))
            .Select(item => item.WorkItemId));
        var durableLedger = Assert.Single(fixture.InnerStore.ListLogicalCheckpointLedgerEntries());
        Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Prepared, durableLedger.Status);
        Assert.Equal(fixture.RecoveryAuthority, durableLedger.RecoveryAuthority);
    }

    [Fact]
    public async Task ExactMaterializedRecoverySource_ThrowingFinalizationKeepsSourceAndClaimForRetry()
    {
        await using var fixture = await OverlayRecoverySourceFixture.CreateAsync(
            finalizationException: new InvalidOperationException("Injected finalization failure."));
        var prepared = await fixture.PrepareAsync(fixture.RecoveryAuthority);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.CommitAsync(prepared));

        Assert.True(fixture.Session.IsActive);
        Assert.True(fixture.Session.AppliesTo(fixture.WorkflowExecutionId));
        Assert.True(fixture.Session.OwnsOverlayClaim(fixture.OverlayClaim));
        Assert.Equal([fixture.Source.WorkItemId], (await fixture.InnerQueue.ListAllAsync(fixture.WorkflowExecutionId))
            .Select(item => item.WorkItemId));
        Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Prepared,
            Assert.Single(fixture.InnerStore.ListLogicalCheckpointLedgerEntries()).Status);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("mismatched")]
    [InlineData("nonexact")]
    public async Task NonExactRecoveryAuthority_DoesNotMaterializeOrRewriteTheSource_AndNormalBoundaryDeactivates(string authorityCase)
    {
        await using var fixture = await OverlayRecoverySourceFixture.CreateAsync();
        var authority = authorityCase switch
        {
            "absent" => null,
            "mismatched" => new RuntimeCheckpointRecoveryAuthorityCodec().Encode(
                NewSchedulerWorkItem(fixture.WorkflowExecutionId, "different-source", WorkflowExecutionCommandKind.StartActivity)),
            "nonexact" => new RuntimeCheckpointRecoveryAuthorityCodec().Encode(
                NewSchedulerWorkItem(fixture.WorkflowExecutionId, fixture.Source.WorkItemId, WorkflowExecutionCommandKind.ScheduleActivity)),
            _ => throw new ArgumentOutOfRangeException(nameof(authorityCase))
        };
        var prepared = await fixture.PrepareAsync(authority, fixture.NewNonqualifyingBoundary());

        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred, prepared.RequestedInitialPersistenceMode);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred, prepared.Token.InitialPersistenceMode);
        Assert.False(fixture.Session.RequiresDurableRecoveryHandoff);
        Assert.Empty(await fixture.InnerQueue.ListAllAsync(fixture.WorkflowExecutionId));
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, (await fixture.CommitAsync(prepared)).Status);
        Assert.False(fixture.Session.IsActive);
        Assert.False(fixture.Session.AppliesTo(fixture.WorkflowExecutionId));
        Assert.Empty(await fixture.InnerQueue.ListAllAsync(fixture.WorkflowExecutionId));
        Assert.Equal(authority, Assert.Single(fixture.InnerStore.ListLogicalCheckpointLedgerEntries()).RecoveryAuthority);
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

        await CommitPreparedThroughStoreAsync(store, NewEmptyDeferredCommit(1) with
        {
            Checkpoint = new RuntimeCheckpoint(
                "checkpoint-buffered",
                "BufferedWork",
                "parent-dispatch",
                Now,
                [],
                new Dictionary<string, string>())
        }, new(RuntimeCheckpointPersistenceMode.Deferred));

        await CommitPreparedThroughStoreAsync(store, NewDispatchBoundaryCommit(), new(RuntimeCheckpointPersistenceMode.Deferred));

        var persisted = innerStore.ListCommits().Single(record => record.Commit.CommitId == "commit-dispatch").Commit;
        var dispatch = Assert.Single(persisted.StateChanges.WorkflowDispatches);
        var outbox = Assert.Single(persisted.StateChanges.PostCommitOutbox);
        Assert.Equal(dispatch.State.ParentWorkflowExecutionId, outbox.State.Intent.WorkflowExecutionId);
        Assert.Equal("elsa.dispatch-workflow.start-child.v1", outbox.State.Intent.Kind);
        Assert.False(session.IsActive);
    }

    private static RuntimePostCommitOutboxProcessor CreateOrdinaryDurableRedrive(SchedulerContinuationFixture fixture) =>
        new(
            fixture.InnerStore,
            new RuntimeSchedulerPostCommitIntentDispatcher(fixture.InnerQueue),
            TimeProvider.System);

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
        var poison = await provider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync("wfexec-1");
        Assert.Empty(poison);

        var snapshot = await SnapshotAsync(provider);
        var checkpointStore = provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        var commitCount = checkpointStore.ListCommits().Count;
        var state = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
        return (snapshot, commitCount, state);
    }

    private static async ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedThroughStoreAsync(
        IRuntimeCheckpointPreparedLedgerStore store,
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        RuntimeExecutionContextSnapshot? requestedExecutionContext = null)
    {
        var preparation = await store.PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit) with
        {
            InitialPersistenceMode = decision.Mode,
            RequestedExecutionContext = requestedExecutionContext ?? RuntimeExecutionContextSnapshot.Empty
        });
        var token = Assert.IsType<RuntimeCheckpointPreparationToken>(preparation.Token);
        var preparedCommit = commit with
        {
            Checkpoint = commit.Checkpoint with { Provenance = token.Provenance },
            ExpectedFence = token.ExpectedFence
        };
        return await store.CommitPreparedAsync(token, preparedCommit, decision);
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

    private static RuntimeCheckpointCommit NewSchedulerContinuationBoundaryCommit(
        RuntimeSchedulerWorkItem source,
        int checkpoint,
        params string[] suffixes)
    {
        var continuations = suffixes.Select(suffix => NewSchedulerWorkItem(
            source.WorkflowExecutionId,
            $"continuation-{suffix}",
            WorkflowExecutionCommandKind.StartActivity)).ToArray();
        var intents = continuations.Select(workItem =>
            SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(
                source,
                "actexec-continuation",
                workItem,
                Now.AddTicks(checkpoint))).ToArray();
        var commit = NewEmptyCommit(source.WorkflowExecutionId, checkpoint, RuntimeCheckpointNames.ActivityCompleted) with
        {
            PostCommitIntents = intents,
            StateChanges = ActivityUpsert(NewRunningActivityState(source.WorkflowExecutionId, "actexec-continuation"))
        };

        return commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(commit))
        };
    }

    private static RuntimeCheckpointCommit WithRetryPolicy(
        RuntimeCheckpointCommit commit,
        RuntimePostCommitRetryPolicy retryPolicy)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(retryPolicy);
        var contribution = new RuntimePostCommitIntentHandlerContribution(
            RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            typeof(RuntimeSchedulerPostCommitIntentDispatcher),
            retryPolicy);
        return commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox(
                RuntimePostCommitOutboxItems.CreatePendingChanges(commit, [contribution]))
        };
    }

    private static RuntimeCheckpointCommit NewMixedSchedulerAndExternalBoundaryCommit(
        string workflowExecutionId,
        int checkpoint)
    {
        var source = NewSchedulerWorkItem(workflowExecutionId, "source-mixed", WorkflowExecutionCommandKind.ScheduleActivity);
        var continuation = NewSchedulerWorkItem(workflowExecutionId, "continuation-mixed", WorkflowExecutionCommandKind.StartActivity);
        var commit = NewEmptyCommit(workflowExecutionId, checkpoint, RuntimeCheckpointNames.ActivityCompleted) with
        {
            PostCommitIntents =
            [
                SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(source, "actexec-continuation", continuation, Now.AddTicks(checkpoint)),
                new RuntimePostCommitIntent(
                    "intent-mixed-external",
                    workflowExecutionId,
                    "test.external.side-effect",
                    Now.AddTicks(checkpoint),
                    activityExecutionId: null,
                    idempotencyKey: $"{workflowExecutionId}:mixed:external",
                    JsonSerializer.SerializeToElement(new { successor = "external" }))
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
        NewRunningActivityState("wfexec-1", "actexec-1");

    private static ActivityExecutionState NewRunningActivityState(string workflowExecutionId, string activityExecutionId) =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: activityExecutionId,
                WorkflowExecutionId: workflowExecutionId,
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

    private static RuntimeSchedulerWorkItem NewSchedulerWorkItem(
        string workflowExecutionId,
        string workItemId,
        WorkflowExecutionCommandKind commandKind) =>
        new(
            workItemId,
            workflowExecutionId,
            $"command-{workItemId}",
            commandKind,
            $"envelope-{workItemId}",
            $"{workflowExecutionId}:{workItemId}",
            Now,
            Now,
            sequence: 1);

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

    private sealed class OverlayRecoverySourceFixture : IAsyncDisposable
    {
        private OverlayRecoverySourceFixture(
            RuntimeCheckpointCommitStoreStatus? finalizationStatus,
            Exception? finalizationException)
        {
            WorkflowExecutionId = "wfexec-recovery-handoff";
            InnerQueue = new InMemoryWorkflowSchedulerWorkQueue();
            InnerStore = RuntimeCheckpointTestStores.Create(schedulerWorkQueue: InnerQueue);
            Accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
            Session = new RuntimeCoalescingSession(
                WorkflowExecutionId,
                InnerQueue,
                new CoalescingRuntimeCheckpointPersistenceOptions(),
                InnerStore);
            Queue = new CoalescingWorkflowSchedulerWorkQueue(
                new CoalescingInner<IWorkflowSchedulerWorkQueue>(InnerQueue),
                Accessor);
            IRuntimeCheckpointCommitStore durableStore = finalizationStatus is null && finalizationException is null
                ? InnerStore
                : new FinalizationOutcomePreparedLedgerStore(InnerStore, finalizationStatus, finalizationException);
            CheckpointStore = new CoalescingRuntimeCheckpointCommitStore(
                new CoalescingInner<IRuntimeCheckpointCommitStore>(durableStore),
                Accessor);
            Source = NewSchedulerWorkItem(WorkflowExecutionId, "overlay-source", WorkflowExecutionCommandKind.StartActivity);
            RecoveryAuthority = new RuntimeCheckpointRecoveryAuthorityCodec().Encode(Source);
        }

        public string WorkflowExecutionId { get; }
        public InMemoryWorkflowSchedulerWorkQueue InnerQueue { get; }
        public InMemoryRuntimeCheckpointCommitStore InnerStore { get; }
        public AsyncLocalRuntimeCoalescingSessionAccessor Accessor { get; }
        public RuntimeCoalescingSession Session { get; }
        public CoalescingWorkflowSchedulerWorkQueue Queue { get; }
        public CoalescingRuntimeCheckpointCommitStore CheckpointStore { get; }
        public RuntimeSchedulerWorkItem Source { get; }
        public RuntimeCheckpointRecoveryAuthority RecoveryAuthority { get; }
        public RuntimeSchedulerWorkClaim OverlayClaim { get; private set; } = null!;

        public static async ValueTask<OverlayRecoverySourceFixture> CreateAsync(
            RuntimeCheckpointCommitStoreStatus? finalizationStatus = null,
            Exception? finalizationException = null)
        {
            var fixture = new OverlayRecoverySourceFixture(finalizationStatus, finalizationException);
            using (fixture.Accessor.Push(fixture.Session))
            {
                await fixture.Queue.EnqueueAsync(fixture.Source);
                fixture.OverlayClaim = Assert.IsType<RuntimeSchedulerWorkClaim>(
                    await fixture.Queue.ClaimAsync(NewClaimRequest(fixture.WorkflowExecutionId)));
            }

            return fixture;
        }

        public async ValueTask<PreparedRecoveryBoundary> PrepareAsync(
            RuntimeCheckpointRecoveryAuthority? recoveryAuthority,
            RuntimeCheckpointCommit? boundary = null)
        {
            boundary ??= NewSchedulerContinuationBoundaryCommit(Source, 1, "next");
            using var scope = Accessor.Push(Session);
            var preparation = await CheckpointStore.PrepareAsync(RuntimeCheckpointPrepareRequest.From(boundary) with
            {
                InitialPersistenceMode = RuntimeCheckpointPersistenceMode.Deferred,
                RecoveryAuthority = recoveryAuthority
            });
            var token = Assert.IsType<RuntimeCheckpointPreparationToken>(preparation.Token);
            Assert.Equal(RuntimeCheckpointPreparationStatus.Prepared, preparation.Status);
            var preparedCommit = boundary with
            {
                Checkpoint = boundary.Checkpoint with { Provenance = token.Provenance },
                ExpectedFence = token.ExpectedFence
            };
            return new PreparedRecoveryBoundary(
                RuntimeCheckpointPersistenceMode.Deferred,
                token,
                preparedCommit);
        }

        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(PreparedRecoveryBoundary prepared)
        {
            using var scope = Accessor.Push(Session);
            return await CheckpointStore.CommitPreparedAsync(
                prepared.Token,
                prepared.Commit,
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
        }

        public RuntimeCheckpointCommit NewNonqualifyingBoundary() =>
            NewEmptyCommit(WorkflowExecutionId, 2, RuntimeCheckpointNames.ActivityStarted);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record PreparedRecoveryBoundary(
        RuntimeCheckpointPersistenceMode RequestedInitialPersistenceMode,
        RuntimeCheckpointPreparationToken Token,
        RuntimeCheckpointCommit Commit);

    private sealed class FinalizationOutcomePreparedLedgerStore(
        IRuntimeCheckpointPreparedLedgerStore inner,
        RuntimeCheckpointCommitStoreStatus? finalizationStatus,
        Exception? finalizationException) : IRuntimeCheckpointPreparedLedgerStore
    {
        public ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(RuntimeCheckpointPrepareRequest request, CancellationToken cancellationToken = default) =>
            inner.PrepareAsync(request, cancellationToken);

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default) =>
            inner.CommitAsync(commit, decision, cancellationToken);

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedAsync(
            RuntimeCheckpointPreparationToken token,
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            if (finalizationException is not null)
                return ValueTask.FromException<RuntimeCheckpointCommitStoreResult>(finalizationException);
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]) { Status = finalizationStatus!.Value });
        }

        public ValueTask<RuntimeCheckpointPreparedPage> PagePreparedAsync(RuntimeCheckpointPreparedQuery query, CancellationToken cancellationToken = default) =>
            inner.PagePreparedAsync(query, cancellationToken);

        public ValueTask<RuntimeCheckpointPreparedAdoptionReceipt> AdoptPreparedAsync(RuntimeCheckpointPreparedAdoptionRequest request, CancellationToken cancellationToken = default) =>
            inner.AdoptPreparedAsync(request, cancellationToken);

        public ValueTask<RuntimeCheckpointPreparedFoldResult> CommitPreparedFoldAsync(RuntimeCheckpointPreparedFoldRequest request, CancellationToken cancellationToken = default) =>
            inner.CommitPreparedFoldAsync(request, cancellationToken);
    }

    private sealed class SchedulerContinuationFixture : IAsyncDisposable
    {
        private SchedulerContinuationFixture(string workflowExecutionId)
        {
            WorkflowExecutionId = workflowExecutionId;
            InnerQueue = new InMemoryWorkflowSchedulerWorkQueue();
            InnerStore = RuntimeCheckpointTestStores.Create(schedulerWorkQueue: InnerQueue);
            Accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
            Session = new RuntimeCoalescingSession(
                workflowExecutionId,
                InnerQueue,
                new CoalescingRuntimeCheckpointPersistenceOptions(),
                InnerStore);
            Queue = new CoalescingWorkflowSchedulerWorkQueue(
                new CoalescingInner<IWorkflowSchedulerWorkQueue>(InnerQueue),
                Accessor);
            OutboxStore = new CoalescingRuntimePostCommitOutboxStore(
                new CoalescingInner<IRuntimePostCommitOutboxStore>(InnerStore),
                Accessor);
            CheckpointStore = new CoalescingRuntimeCheckpointCommitStore(
                new CoalescingInner<IRuntimeCheckpointCommitStore>(InnerStore),
                Accessor);
            Processor = new RuntimePostCommitOutboxProcessor(
                OutboxStore,
                new RuntimeSchedulerPostCommitIntentDispatcher(Queue),
                TimeProvider.System);
            Source = NewSchedulerWorkItem(workflowExecutionId, "source-continuation", WorkflowExecutionCommandKind.ScheduleActivity);
        }

        public string WorkflowExecutionId { get; }
        public InMemoryWorkflowSchedulerWorkQueue InnerQueue { get; }
        public InMemoryRuntimeCheckpointCommitStore InnerStore { get; }
        public AsyncLocalRuntimeCoalescingSessionAccessor Accessor { get; }
        public RuntimeCoalescingSession Session { get; }
        public CoalescingWorkflowSchedulerWorkQueue Queue { get; }
        public CoalescingRuntimePostCommitOutboxStore OutboxStore { get; }
        public CoalescingRuntimeCheckpointCommitStore CheckpointStore { get; }
        public RuntimePostCommitOutboxProcessor Processor { get; }
        public RuntimeSchedulerWorkItem Source { get; }

        public static async ValueTask<SchedulerContinuationFixture> CreateAsync(string workflowExecutionId)
        {
            var fixture = new SchedulerContinuationFixture(workflowExecutionId);
            await fixture.InnerQueue.EnqueueAsync(fixture.Source);
            using (fixture.Accessor.Push(fixture.Session))
                Assert.NotNull(await fixture.Queue.ClaimAsync(NewClaimRequest(workflowExecutionId)));
            return fixture;
        }

        public RuntimeCheckpointCommit NewQualifyingBoundary(int checkpoint, params string[] suffixes) =>
            NewSchedulerContinuationBoundaryCommit(Source, checkpoint, suffixes);

        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit)
        {
            using var scope = Accessor.Push(Session);
            return await CommitPreparedThroughStoreAsync(
                CheckpointStore,
                commit,
                new(RuntimeCheckpointPersistenceMode.Immediate));
        }

        public async ValueTask ProcessOverlayAsync()
        {
            using var scope = Accessor.Push(Session);
            await Processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10, WorkflowExecutionId));
        }

        public async ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkItem>> ListOverlayAsync()
        {
            using var scope = Accessor.Push(Session);
            return (await Queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingSchedulerIntentDispatcher : IRuntimePostCommitIntentDispatcher
    {
        public static readonly ThrowingSchedulerIntentDispatcher Instance = new();

        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new InvalidOperationException("Injected retryable scheduler continuation delivery failure."));
    }

    private sealed class ThrowOnceOnDeliveryResultOutboxStore(IRuntimePostCommitOutboxStore inner) : IRuntimePostCommitOutboxStore
    {
        private int _remainingFailures = 1;

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(
            RuntimePostCommitOutboxQuery query,
            CancellationToken cancellationToken = default) => inner.GetDeliverableAsync(query, cancellationToken);

        public ValueTask RecordDeliveryResultAsync(
            RuntimePostCommitOutboxDeliveryResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _remainingFailures, 0) == 1)
                return ValueTask.FromException(new InvalidOperationException("Injected outbox acknowledgement loss after durable dispatch."));

            return inner.RecordDeliveryResultAsync(result, cancellationToken);
        }
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
