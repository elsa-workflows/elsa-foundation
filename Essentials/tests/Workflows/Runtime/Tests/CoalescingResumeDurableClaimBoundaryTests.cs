using System.Text.Json;
using System.Threading.Channels;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class CoalescingResumeDurableClaimBoundaryTests
{
    private const string WorkflowExecutionId = "wf-coalesced-resume-boundary";
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(17);

    [Fact]
    public async Task Resume_boundary_flushes_coalesced_work_then_durable_deferral_survives_provider_rebuild()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: true, enqueueScheduleSuffixAfterSuccessfulResume: true);
        var drainOptions = new WorkflowDrainOrchestratorOptions(maxCyclesWithoutProgress: 1, outboxDeliveryBatchSize: 1);
        await using var firstProvider = BuildProvider(shared, probe, drainOptions);
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-2", 2));
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 3));

        RuntimeSchedulerDrainResult deferred;
        await using (var scope = firstProvider.CreateAsyncScope())
        {
            var orchestrator = scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>();
            deferred = await orchestrator.DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
        }

        Assert.True(deferred.StoppedOnDeferral);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Deferred, deferred.StopReason);
        Assert.Equal(
            ["schedule-1", "schedule-2", "resume-1"],
            deferred.Items.Select(item => item.WorkItemId));
        Assert.Equal(
            [
                RuntimeSchedulerWorkItemResultStatus.Completed,
                RuntimeSchedulerWorkItemResultStatus.Completed,
                RuntimeSchedulerWorkItemResultStatus.Deferred
            ],
            deferred.Items.Select(item => item.Status));

        Assert.Equal(new long?[] { null, 1 }, probe.ScheduleObservedVersions);
        Assert.Equal(new[] { 0, 0 }, probe.ScheduleDurableCommitCounts);
        var resumeAtDeferral = Assert.Single(probe.ResumeSnapshots);
        AssertDurableResumeSnapshot(resumeAtDeferral, schedulerVersion: 2, durableCommitCount: 1);

        await using (var auditScope = firstProvider.CreateAsyncScope())
        {
            var durableFlush = Assert.Single(InnerCommitStore(auditScope.ServiceProvider).ListCommits());
            Assert.Equal(RuntimeCoalescingMetadataKeys.FlushCheckpointName, durableFlush.Commit.Checkpoint.Name);
            Assert.Equal(2L, durableFlush.Commit.StateChanges.Scheduler?.State.Version);
            Assert.Empty(await auditScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>()
                .ListAsync(WorkflowExecutionId));
        }
        Assert.Equal("resume-1", Assert.Single(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).WorkItemId);

        // A newly built provider sees the same queue row and its future visibility. The early read is through the
        // coalescing decorator with no active session, so it exercises the durable provider path used after restart.
        await using var reopenedProvider = BuildProvider(shared, probe, drainOptions);
        await using var reopenedScope = reopenedProvider.CreateAsyncScope();
        var reopenedQueue = reopenedScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var notYetVisibleAt = Now + RetryDelay - TimeSpan.FromTicks(1);
        Assert.Empty(await reopenedQueue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(notYetVisibleAt)));

        shared.TimeProvider.Advance(RetryDelay);
        Assert.Equal(
            [WorkflowExecutionId],
            await reopenedQueue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(Now + RetryDelay)));

        var retried = await reopenedScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.False(retried.StoppedOnDeferral);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, retried.StopReason);
        Assert.Equal(
            ["resume-1", "schedule-3", "schedule-4"],
            retried.Items.Select(item => item.WorkItemId));
        Assert.All(retried.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Equal(2, probe.ResumeSnapshots.Count);
        Assert.All(probe.ResumeSnapshots, snapshot => AssertDurableResumeSnapshot(snapshot, schedulerVersion: 2, durableCommitCount: 1));
        Assert.Equal(new long?[] { null, 1, 2, 3 }, probe.ScheduleObservedVersions);
        Assert.Equal(new[] { 0, 0, 2, 2 }, probe.ScheduleDurableCommitCounts);
        var commitsAfterRetry = InnerCommitStore(reopenedScope.ServiceProvider).ListCommits();
        Assert.Equal(3, commitsAfterRetry.Count);
        var resumeCheckpoint = Assert.Single(commitsAfterRetry.Where(item => item.Commit.Checkpoint.Name == RuntimeCheckpointNames.BookmarkConsumed));
        Assert.Equal(2, resumeCheckpoint.Commit.StateChanges.PostCommitOutbox.Count);
        Assert.Equal(
            ["schedule-3", "schedule-4"],
            resumeCheckpoint.Commit.StateChanges.PostCommitOutbox
                .Select(change => change.State.Intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!.WorkItemId));
        var suffixWorkOutboxIds = resumeCheckpoint.Commit.StateChanges.PostCommitOutbox
            .Select(change => change.State.OutboxItemId)
            .ToArray();
        var innerOutbox = reopenedScope.ServiceProvider.GetRequiredService<CoalescingInner<IRuntimePostCommitOutboxStore>>().Value;
        var outboxLookup = reopenedScope.ServiceProvider.GetRequiredService<CoalescingInner<IPostCommitOutboxLookupStore>>().Value;
        foreach (var outboxId in suffixWorkOutboxIds)
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await outboxLookup.FindAsync(outboxId))?.Status);
        Assert.Empty(await innerOutbox.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            DateTimeOffset.MaxValue,
            limit: 10,
            workflowExecutionId: WorkflowExecutionId,
            intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork)));
        var flushCommits = commitsAfterRetry
            .Where(item => item.Commit.Checkpoint.Name == RuntimeCoalescingMetadataKeys.FlushCheckpointName)
            .ToArray();
        Assert.Equal(2, flushCommits.Length);
        Assert.Equal(new long?[] { 2, 4 }, flushCommits.Select(item => item.Commit.StateChanges.Scheduler?.State.Version));
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task Single_resume_continuation_runs_within_the_original_one_item_drain_request()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(
            deferFirstResume: false,
            enqueueScheduleSuffixAfterSuccessfulResume: true,
            scheduleSuffixCount: 1);
        await using var provider = BuildProvider(shared, probe);
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 1));

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId, maxWorkItems: 1));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(["resume-1", "schedule-3"], result.Items.Select(item => item.WorkItemId));
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Equal(1, result.OutboxDeliveredCount);
        Assert.Equal(new long?[] { null }, probe.ScheduleObservedVersions);
        Assert.Equal(1L, (await shared.SchedulerStateStore.FindAsync(WorkflowExecutionId))?.Version);

        var resumeCheckpoint = Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits()
            .Where(item => item.Commit.Checkpoint.Name == RuntimeCheckpointNames.BookmarkConsumed));
        var continuationOutbox = Assert.Single(resumeCheckpoint.Commit.StateChanges.PostCommitOutbox);
        Assert.Equal("schedule-3", continuationOutbox.State.Intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!.WorkItemId);
        var outboxLookup = scope.ServiceProvider.GetRequiredService<CoalescingInner<IPostCommitOutboxLookupStore>>().Value;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await outboxLookup.FindAsync(continuationOutbox.State.OutboxItemId))?.Status);
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task Resume_reprobes_all_durable_continuations_after_external_delivery_supersedes_each_batch()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false, enqueueScheduleSuffixAfterSuccessfulResume: true);
        var externalDelivery = new ExternalOutboxDeliveryProbe();
        var drainOptions = new WorkflowDrainOrchestratorOptions(maxCyclesWithoutProgress: 1, outboxDeliveryBatchSize: 1);
        await using var provider = BuildProvider(shared, probe, drainOptions, externalDelivery);
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 1));

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(["resume-1", "schedule-3", "schedule-4"], result.Items.Select(item => item.WorkItemId));
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Equal(new long?[] { null, 1 }, probe.ScheduleObservedVersions);
        Assert.Equal([1, 1], probe.ScheduleDurableCommitCounts);

        var commits = InnerCommitStore(scope.ServiceProvider).ListCommits();
        var resumeCheckpoint = Assert.Single(commits.Where(item => item.Commit.Checkpoint.Name == RuntimeCheckpointNames.BookmarkConsumed));
        Assert.Equal(2, resumeCheckpoint.Commit.StateChanges.PostCommitOutbox.Count);
        var outboxIds = resumeCheckpoint.Commit.StateChanges.PostCommitOutbox
            .Select(change => change.State.OutboxItemId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var workItemIds = resumeCheckpoint.Commit.StateChanges.PostCommitOutbox
            .Select(change => change.State.Intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!.WorkItemId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(outboxIds, externalDelivery.Settlements.Select(item => item.OutboxItemId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(workItemIds, externalDelivery.Settlements.Select(item => item.WorkItemId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(["schedule-3", "schedule-4"], workItemIds);

        var flush = Assert.Single(commits.Where(item => item.Commit.Checkpoint.Name == RuntimeCoalescingMetadataKeys.FlushCheckpointName));
        Assert.Equal(2L, flush.Commit.StateChanges.Scheduler?.State.Version);
        var innerOutbox = scope.ServiceProvider.GetRequiredService<CoalescingInner<IRuntimePostCommitOutboxStore>>().Value;
        var outboxLookup = scope.ServiceProvider.GetRequiredService<CoalescingInner<IPostCommitOutboxLookupStore>>().Value;
        foreach (var outboxId in outboxIds)
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await outboxLookup.FindAsync(outboxId))?.Status);
        Assert.Empty(await innerOutbox.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            DateTimeOffset.MaxValue,
            limit: 10,
            workflowExecutionId: WorkflowExecutionId,
            intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork)));
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task Ordinary_schedule_work_remains_coalesced_until_drain_quiescence()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false);
        await using var provider = BuildProvider(shared, probe);
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-2", 2));

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(2, result.DrainedCount);
        Assert.Equal(new long?[] { null, 1 }, probe.ScheduleObservedVersions);
        Assert.Equal(new[] { 0, 0 }, probe.ScheduleDurableCommitCounts);
        var folded = Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits());
        Assert.Equal(RuntimeCoalescingMetadataKeys.FlushCheckpointName, folded.Commit.Checkpoint.Name);
        Assert.Equal(2L, folded.Commit.StateChanges.Scheduler?.State.Version);
        Assert.Equal(2L, (await shared.SchedulerStateStore.FindAsync(WorkflowExecutionId))?.Version);
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    /// <summary>
    /// Another deliverer holds a durable continuation when the drain begins, and delivers it only after the drain's first
    /// pass has filled the overlay from the durable queue. The overlay never shows that work, so the drain must start a
    /// fresh segment from durable state rather than report quiescence and leave the step to the sweep (#3659).
    /// </summary>
    [Theory]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task Ordinary_segment_drains_work_a_competing_delivery_queued_after_the_overlay_was_filled(PersistenceMode mode)
    {
        await using var race = await HeldContinuationRace.StartAsync(TimeSpan.FromMinutes(10), mode);

        await race.DeliverHeldAtFirstPollAsync();

        await race.AssertDrainedBothStepsAsync();
    }

    /// <summary>
    /// The same race, but the other deliverer finishes while schedule-1 still runs, after the overlay was filled. When the
    /// drain settles nothing is held any more, and the overlay it would read for queued work never shows schedule-2. The
    /// listing of held continuations also returns one another deliverer finished since the overlay was filled, so the drain
    /// starts a fresh segment from durable state rather than report quiescence and leave the step to the sweep (#3661).
    /// </summary>
    [Theory]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task Ordinary_segment_drains_work_a_competing_delivery_finished_during_the_first_pass(PersistenceMode mode)
    {
        await using var race = await HeldContinuationRace.StartAsync(TimeSpan.FromMinutes(10), mode, deliverDuringFirstStep: true);

        await race.AssertDrainedBothStepsAsync();
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await race.Commits.FindAsync(race.OtherClaim.OutboxItemId))?.Status);
    }

    [Theory]
    [InlineData(PersistenceMode.Balanced, 0, 1)]
    [InlineData(PersistenceMode.Durable, 0, 1)]
    [InlineData(PersistenceMode.Ephemeral, 0, 1)]
    [InlineData(PersistenceMode.Balanced, 1, 1)]
    [InlineData(PersistenceMode.Durable, 1, 1)]
    [InlineData(PersistenceMode.Ephemeral, 1, 1)]
    [InlineData(PersistenceMode.Balanced, 1, 2)]
    [InlineData(PersistenceMode.Durable, 1, 2)]
    [InlineData(PersistenceMode.Ephemeral, 1, 2)]
    [InlineData(PersistenceMode.Balanced, 1, int.MaxValue)]
    public async Task Previously_observed_deliveries_cannot_hide_a_later_delivery_behind_the_listing_limit(
        PersistenceMode mode, int deliveryClockAheadSeconds, int listingLimit)
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false);
        await using var provider = BuildProvider(shared, probe, mode: mode,
            drainOptions: new WorkflowDrainOrchestratorOptions(outboxDeliveryBatchSize: listingLimit));
        await using var scope = provider.CreateAsyncScope();
        var commits = InnerCommitStore(scope.ServiceProvider);
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));
        probe.DuringSchedule = () => DeliverNextStepAsync(2);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId), timeout.Token);

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(["schedule-1", "schedule-2", "schedule-3", "schedule-4"], result.Items.Select(item => item.WorkItemId));
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
        Assert.Equal(4L, (await shared.SchedulerStateStore.FindAsync(WorkflowExecutionId))?.Version);

        async Task DeliverNextStepAsync(int number)
        {
            // Each delivery lands after this segment seeded its overlay. Earlier delivered rows still match the
            // reseed time (equal clocks, or a slightly faster deliverer) and sort before this new delivery.
            var step = ScheduleWork($"schedule-{number}", number);
            var id = $"outbox-foreign-{number}";
            await commits.AddPendingForTestingAsync(new RuntimePostCommitOutboxItem(id,
                SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(step, $"activity-{step.WorkItemId}", step, Now),
                RuntimePostCommitOutboxStatus.Pending, Now, Now, RuntimePostCommitRetryPolicy.None));
            var claim = Assert.Single(await commits.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                "other-continuation-worker", Now, TimeSpan.FromMinutes(10), limit: 1,
                WorkflowExecutionId, RuntimePostCommitIntentKinds.EnqueueSchedulerWork)));
            await shared.Queue.EnqueueAsync(step);
            Assert.Equal(RuntimePostCommitOutboxClaimCompletionOutcome.Persisted, await commits.CompleteClaimAsync(
                new RuntimePostCommitOutboxClaimCompletion(claim, new RuntimePostCommitOutboxDeliveryResult(
                    id, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(deliveryClockAheadSeconds)))));
            if (number < 4)
                probe.DuringSchedule = () => DeliverNextStepAsync(number + 1);
        }
    }

    /// <summary>
    /// The #3647 re-drive delivers the execution's flushed continuations before its first segment. It queues schedule-1 that
    /// way, but another deliverer holds the continuation for schedule-2, and queues that step only after the first segment
    /// filled its overlay. The re-drive must run it too before it reports quiescence (#3659).
    /// </summary>
    [Theory]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task Yield_redrive_drains_work_a_competing_delivery_queued_after_the_overlay_was_filled(PersistenceMode mode)
    {
        await using var race = await HeldContinuationRace.StartYieldRedriveAsync(TimeSpan.FromMinutes(10), mode);

        await race.DeliverHeldAtFirstPollAsync();

        await race.AssertDrainedBothStepsAsync();
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await race.Commits.FindAsync(HeldContinuationRace.FlushedOutboxId))?.Status);
    }

    /// <summary>
    /// The same race through recovery. The resumption sweep finds the continuation held, so it delivers nothing itself and
    /// re-drives the queued backlog through the execution's mailbox with a <c>RunSchedulerWork</c> command. That drain must
    /// run the step the other deliverer queued after its overlay was filled before the sweep's dispatch returns (#3659).
    /// </summary>
    [Theory]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task Resumption_sweep_redrive_drains_work_a_competing_delivery_queued_after_the_overlay_was_filled(PersistenceMode mode)
    {
        await using var race = await HeldContinuationRace.StartSweepAsync(TimeSpan.FromMinutes(10), mode);

        await race.DeliverHeldAtFirstPollAsync();

        var sweep = await race.Sweeping.WaitAsync(race.Timeout);
        Assert.Equal(0, sweep.OutboxDeliveredCount);
        Assert.Equal(RuntimeResumptionDispatchOutcome.Accepted, Assert.Single(sweep.Dispatches).Outcome);
        await race.AssertRanBothStepsAsync();
    }

    /// <summary>
    /// The same race across a durable claim boundary. The resume after schedule-1 ends the first segment and runs durably,
    /// and the drain waits for the held continuation before it starts the post-boundary segment. That segment fills its
    /// overlay from durable state, so it runs schedule-2, coalesced, before the drain reports quiescence. This boundary
    /// restart predates #3659, which leaves it unchanged.
    /// </summary>
    [Theory]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task Post_boundary_segment_drains_work_a_competing_delivery_queued_while_the_resume_settled(PersistenceMode mode)
    {
        await using var race = await HeldContinuationRace.StartAsync(TimeSpan.FromMinutes(10), mode, resumeAfterFirstStep: true);

        await race.DeliverHeldAtFirstPollAsync();

        var result = await race.Draining.WaitAsync(race.Timeout);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(["schedule-1", "resume-1", "schedule-2"], result.Items.Select(item => item.WorkItemId));
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        AssertDurableResumeSnapshot(Assert.Single(race.Probe.ResumeSnapshots), schedulerVersion: 1, durableCommitCount: 1);
        // Only the boundary flush is durable when schedule-2 commits: it ran in a fresh segment, not the closed one.
        Assert.Equal([0, 1], race.Probe.ScheduleDurableCommitCounts);
        await race.AssertRanBothStepsAsync();
    }

    /// <summary>
    /// The other deliverer died holding the continuation. Once its claim lapses the drain takes the item back through the
    /// durable claim path, which an active segment would route to its overlay and never find (#3659).
    /// </summary>
    [Theory]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task Ordinary_segment_takes_back_a_continuation_whose_deliverer_died(PersistenceMode mode)
    {
        await using var race = await HeldContinuationRace.StartAsync(TimeSpan.FromSeconds(1), mode);

        // Fire each poll until the claim lapses.
        while (true)
        {
            var poll = race.Clock.NextTimerAsync(due => due <= TimeSpan.FromSeconds(1), race.Timeout);
            if (await Task.WhenAny(race.Draining, poll) == race.Draining)
                break;
            race.Clock.Advance(await poll);
        }

        await race.AssertDrainedBothStepsAsync();
        Assert.Equal(Now + TimeSpan.FromSeconds(1), race.Clock.GetUtcNow());
        var continuation = await race.Commits.FindAsync(race.OtherClaim.OutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, continuation?.Status);
        Assert.Equal(2, continuation?.DeliveryFencingToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public async Task Resume_at_queue_head_coalesces_following_work_within_the_requested_budget(int? maxWorkItems)
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false);
        await using var provider = BuildProvider(shared, probe);
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 1));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 2));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-2", 3));

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId, maxWorkItems));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        if (maxWorkItems == 1)
        {
            Assert.Equal("resume-1", Assert.Single(result.Items).WorkItemId);
            Assert.Empty(probe.ScheduleObservedVersions);
            Assert.Empty(InnerCommitStore(scope.ServiceProvider).ListCommits());
            Assert.Equal(["schedule-1", "schedule-2"],
                (await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Select(item => item.WorkItemId));
            return;
        }
        Assert.Equal(["resume-1", "schedule-1", "schedule-2"], result.Items.Select(item => item.WorkItemId));
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Equal(new long?[] { null, 1 }, probe.ScheduleObservedVersions);
        Assert.Equal(new[] { 0, 0 }, probe.ScheduleDurableCommitCounts);
        AssertDurableResumeSnapshot(Assert.Single(probe.ResumeSnapshots), schedulerVersion: null, durableCommitCount: 0);

        var flush = Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits());
        Assert.Equal(RuntimeCoalescingMetadataKeys.FlushCheckpointName, flush.Commit.Checkpoint.Name);
        Assert.Equal(2L, flush.Commit.StateChanges.Scheduler?.State.Version);
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task Successful_resume_between_schedule_segments_coalesces_both_segments()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false);
        await using var provider = BuildProvider(shared, probe);
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-2", 2));
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 3));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-3", 4));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-4", 5));

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(
            ["schedule-1", "schedule-2", "resume-1", "schedule-3", "schedule-4"],
            result.Items.Select(item => item.WorkItemId));
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Equal(new long?[] { null, 1, 2, 3 }, probe.ScheduleObservedVersions);
        Assert.Equal(new[] { 0, 0, 1, 1 }, probe.ScheduleDurableCommitCounts);
        AssertDurableResumeSnapshot(Assert.Single(probe.ResumeSnapshots), schedulerVersion: 2, durableCommitCount: 1);

        AssertCoalescedVersions(scope.ServiceProvider, 2, 4);
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task Repeated_successful_resumes_start_a_fresh_coalesced_segment_each_time()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false);
        await using var provider = BuildProvider(shared, probe);
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 1));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 2));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-2", 3));
        await shared.Queue.EnqueueAsync(ResumeWork("resume-2", 4));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-3", 5));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-4", 6));

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(
            ["resume-1", "schedule-1", "schedule-2", "resume-2", "schedule-3", "schedule-4"],
            result.Items.Select(item => item.WorkItemId));
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Equal(new long?[] { null, 1, 2, 3 }, probe.ScheduleObservedVersions);
        Assert.Equal(new[] { 0, 0, 1, 1 }, probe.ScheduleDurableCommitCounts);
        Assert.Equal(2, probe.ResumeSnapshots.Count);
        AssertDurableResumeSnapshot(probe.ResumeSnapshots[0], schedulerVersion: null, durableCommitCount: 0, workItemId: "resume-1");
        AssertDurableResumeSnapshot(probe.ResumeSnapshots[1], schedulerVersion: 2, durableCommitCount: 1, workItemId: "resume-2");

        AssertCoalescedVersions(scope.ServiceProvider, 2, 4);
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task Visibility_snapshot_keeps_successful_resume_and_surrounding_work_in_one_segment()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false);
        await using var provider = BuildProvider(shared, probe, visibilitySnapshots: true);
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 2));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-2", 3));
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(["schedule-1", "resume-1", "schedule-2"], result.Items.Select(item => item.WorkItemId));
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        var resume = Assert.Single(probe.ResumeSnapshots);
        Assert.True(resume.SessionActive);
        Assert.Empty(resume.ActiveClaimWorkItemIds);
        Assert.Equal(0, resume.DurableCommitCount);
        AssertCoalescedVersions(scope.ServiceProvider, 2);
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overlay_deferral_flushes_prefix_and_preserves_delay_across_provider_rebuild_and_early_drain(bool retainingBoundary)
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: true, retainingBoundaryBeforeDeferral: retainingBoundary);
        await using (var first = BuildProvider(shared, probe, visibilitySnapshots: true))
        {
            await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));
            await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 2));
            await shared.Queue.EnqueueAsync(ScheduleWork("schedule-2", 3));
            await using var scope = first.CreateAsyncScope();
            var deferred = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
            Assert.True(deferred.StoppedOnDeferral);
            Assert.Equal(["schedule-1", "resume-1"], deferred.Items.Select(item => item.WorkItemId));
            var persisted = Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits());
            Assert.Equal(1L, persisted.Commit.StateChanges.Scheduler?.State.Version);
            Assert.Equal(retainingBoundary ? RuntimeCheckpointNames.WorkflowSuspended : RuntimeCoalescingMetadataKeys.FlushCheckpointName,
                persisted.Commit.Checkpoint.Name);
            Assert.True(Assert.Single(probe.ResumeSnapshots).SessionActive);
        }

        await using var reopened = BuildProvider(shared, probe, visibilitySnapshots: true);
        await using var reopenedScope = reopened.CreateAsyncScope();
        var orchestrator = reopenedScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>();
        shared.TimeProvider.Advance(RetryDelay - TimeSpan.FromTicks(1));
        var early = await orchestrator.DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
        Assert.Empty(early.Items);
        Assert.Single(probe.ResumeSnapshots);
        Assert.Equal(["resume-1", "schedule-2"],
            (await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Select(item => item.WorkItemId));

        shared.TimeProvider.Advance(TimeSpan.FromTicks(1));
        var retried = await orchestrator.DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
        Assert.Equal(["resume-1", "schedule-2"], retried.Items.Select(item => item.WorkItemId));
        Assert.All(retried.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Equal(2, probe.ResumeSnapshots.Count);
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
        Assert.Empty(await reopenedScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_after_is_released_only_while_the_active_segment_holds_no_effect_of_the_faulted_item(bool? retainingCommit)
    {
        // #3275 inside an ACTIVE segment. Outside a session the first non-retaining commit consumes the claimed item, so
        // a RetryAfter after it is acknowledged and poisoned. Inside one that commit is buffered and consumes nothing,
        // so the release used to succeed through the overlay: the quiescence flush then committed the buffered effect
        // and kept the released copy with its delay, and the sweep would re-drive an item whose effect is durable.
        // A fault before any commit, or after one that retains the item for re-drive, is still released.
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false, faultInvoke: true, commitBeforeInvokeFault: retainingCommit);
        await using var provider = BuildProvider(shared, probe, visibilitySnapshots: true, retryAfter: RetryDelay);
        await shared.Queue.EnqueueAsync(Work("invoke-1", WorkflowExecutionCommandKind.InvokeActivity, 1));
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Faulted, result.StopReason);
        Assert.True(Assert.Single(probe.InvokeSessionActive));
        var record = Assert.Single(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId));
        var queued = (await shared.Queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items;
        var commits = InnerCommitStore(scope.ServiceProvider).ListCommits();
        if (retainingCommit is not null)
        {
            var flush = Assert.Single(commits, item => item.Commit.Checkpoint.Name == RuntimeCoalescingMetadataKeys.FlushCheckpointName);
            Assert.Equal(1L, flush.Commit.StateChanges.Scheduler?.State.Version);
        }
        else
        {
            Assert.Empty(commits);
        }

        if (retainingCommit == false)
        {
            // The buffered effect is durable, and nothing is left to run it again.
            Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, record.Disposition);
            Assert.Null(record.NextRetryAt);
            Assert.Empty(queued);
        }
        else
        {
            Assert.Equal(RuntimeSchedulerPoisonDisposition.RetryScheduled, record.Disposition);
            Assert.Equal(Now + RetryDelay, record.NextRetryAt);
            var retained = Assert.Single(queued);
            Assert.Equal(("invoke-1", Now + RetryDelay), (retained.Item.WorkItemId, retained.VisibleAfter));
        }
    }

    [Fact]
    public async Task Retry_after_is_released_for_an_item_whose_segment_holds_only_an_earlier_items_effect()
    {
        // #3275: what an active segment took is tracked per dispatch. schedule-1's buffered checkpoint is its own
        // effect; invoke-1, dispatched next in the same drain, faults before committing anything, so it is released.
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false, faultInvoke: true);
        await using var provider = BuildProvider(shared, probe, visibilitySnapshots: true, retryAfter: RetryDelay);
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));
        await shared.Queue.EnqueueAsync(Work("invoke-1", WorkflowExecutionCommandKind.InvokeActivity, 2));
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(["schedule-1", "invoke-1"], result.Items.Select(item => item.WorkItemId));
        Assert.True(Assert.Single(probe.InvokeSessionActive));
        var record = Assert.Single(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId));
        Assert.Equal(("invoke-1", RuntimeSchedulerPoisonDisposition.RetryScheduled), (record.WorkItemId, record.Disposition));
        Assert.Equal(Now + RetryDelay, record.NextRetryAt);
        var retained = Assert.Single((await shared.Queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items);
        Assert.Equal(("invoke-1", Now + RetryDelay), (retained.Item.WorkItemId, retained.VisibleAfter));
        Assert.Equal(1L, (await shared.SchedulerStateStore.FindAsync(WorkflowExecutionId))?.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delayed_release_of_an_overlay_continuation_keeps_its_delay_after_an_external_enqueue_moves_the_durable_head(
        bool unavailable)
    {
        // #3690. schedule-1's buffered checkpoint enqueues invoke-1, which exists only in the overlay. While invoke-1 is
        // dispatched, another writer enqueues external-1 durably; the overlay seeded once and never sees it. invoke-1
        // then faults before committing anything and is released with a delay (an honored RetryAfter, or temporary
        // unavailability). The quiescence flush persists invoke-1 behind external-1, so the durable head is no longer the
        // released item: the flush used to refuse the release and throw after its commit had landed, leaving invoke-1
        // visible at once, with a RetryScheduled record whose NextRetryAt nothing honored.
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false, faultInvoke: true, scheduleEnqueuesInvoke: true,
            invokeUnavailable: unavailable,
            beforeInvokeFault: async () =>
                await shared.Queue.EnqueueAsync(Work("external-1", WorkflowExecutionCommandKind.NotifyParentActivity, 3)),
            recordPumpablePeeks: true);
        await using var provider = BuildProvider(shared, probe, visibilitySnapshots: true, retryAfter: unavailable ? null : RetryDelay);
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

            Assert.Equal(["schedule-1", "invoke-1"], result.Items.Select(item => item.WorkItemId));
            Assert.Equal(unavailable ? RuntimeSchedulerDrainStopReason.Deferred : RuntimeSchedulerDrainStopReason.Faulted, result.StopReason);
            Assert.True(Assert.Single(probe.InvokeSessionActive));
            var records = await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId);
            if (unavailable)
            {
                Assert.Empty(records);
            }
            else
            {
                var record = Assert.Single(records);
                Assert.Equal(("invoke-1", RuntimeSchedulerPoisonDisposition.RetryScheduled), (record.WorkItemId, record.Disposition));
                Assert.Equal(Now + RetryDelay, record.NextRetryAt);
            }

            // The flush landed schedule-1's effect, and invoke-1 waits behind external-1 with its own delay.
            var flush = Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits());
            Assert.Equal(RuntimeCoalescingMetadataKeys.FlushCheckpointName, flush.Commit.Checkpoint.Name);
            Assert.Equal(1L, flush.Commit.StateChanges.Scheduler?.State.Version);
            Assert.Equal([("external-1", (DateTimeOffset?)null), ("invoke-1", Now + RetryDelay)],
                (await shared.Queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items
                .Select(entry => (entry.Item.WorkItemId, entry.VisibleAfter)));
        }

        // FIFO still decides: external-1, ahead of it, runs now, and invoke-1, now the head, holds back until it is due.
        await using (var earlyScope = provider.CreateAsyncScope())
        {
            var early = await earlyScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
            Assert.Equal(["external-1"], early.Items.Select(item => item.WorkItemId));
        }

        // A fused span external-1 started would find nothing to pump inline: invoke-1 behind it is still delayed.
        Assert.Equal([null], probe.PumpablePeeks);

        Assert.Single(probe.InvokeSessionActive);
        Assert.Single(probe.ResumeSnapshots);
        shared.TimeProvider.Advance(RetryDelay);
        await using (var retryScope = provider.CreateAsyncScope())
        {
            var retried = await retryScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
            Assert.Equal(["invoke-1"], retried.Items.Select(item => item.WorkItemId));
        }

        Assert.Equal(2, probe.InvokeSessionActive.Count);
    }

    [Theory]
    [InlineData(PersistenceMode.Durable, "CoalescingProbe", "CoalescingProbe")]
    [InlineData(PersistenceMode.Balanced, RuntimeCheckpointNames.ActivitySuspended, RuntimeCheckpointNames.ActivitySuspended)]
    [InlineData(PersistenceMode.Balanced, "CoalescingProbe", RuntimeCoalescingMetadataKeys.FlushCheckpointName)]
    // A non-retaining attempt claim that is the work's effect, as background admission commits one.
    [InlineData(PersistenceMode.Balanced, RuntimeCheckpointNames.ActivityAttemptClaimed, RuntimeCheckpointNames.ActivityAttemptClaimed)]
    public async Task Retry_after_is_not_released_for_a_continuation_whose_effect_the_active_segment_committed(
        PersistenceMode mode, string invokeCheckpointName, string committedBy)
    {
        // #3275 for a continuation that exists only in the overlay. The schedule's buffered commit enqueues invoke-1,
        // which the drain claims and whose checkpoint then lands at once while the session stays active: the cap folds
        // it (Durable folds every activity), or a boundary the segment continues past commits it. Neither consumes an
        // item the durable queue never held, so the release used to succeed, the quiescence flush persisted the copy
        // with its delay, and the sweep would run invoke-1 again over its committed effect. Buffering it is the same.
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false, faultInvoke: true, commitBeforeInvokeFault: false,
            scheduleEnqueuesInvoke: true, invokeCheckpointName: invokeCheckpointName);
        await using var provider = BuildProvider(shared, probe, visibilitySnapshots: true, retryAfter: RetryDelay, mode: mode);
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

            Assert.Equal(["schedule-1", "invoke-1"], result.Items.Select(item => item.WorkItemId));
            Assert.Equal(RuntimeSchedulerDrainStopReason.Faulted, result.StopReason);
            Assert.True(Assert.Single(probe.InvokeSessionActive));
            var record = Assert.Single(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId));
            Assert.Equal(("invoke-1", RuntimeSchedulerPoisonDisposition.Poisoned), (record.WorkItemId, record.Disposition));
            Assert.Null(record.NextRetryAt);
            // invoke-1's effect (scheduler version 2) is committed once: by the cap fold or the boundary, which carry
            // invoke-1's own checkpoint, or by the quiescence flush.
            var effect = Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits(),
                item => item.Commit.StateChanges.Scheduler?.State.Version == 2);
            Assert.Equal(committedBy, effect.Commit.Checkpoint.Name);
        }

        // Nothing is left to run invoke-1 again, before or after its delay would have passed.
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
        shared.TimeProvider.Advance(RetryDelay);
        await using (var retryScope = provider.CreateAsyncScope())
        {
            var retried = await retryScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
            Assert.Empty(retried.Items);
        }

        Assert.Single(probe.InvokeSessionActive);
        Assert.Equal(2L, (await shared.SchedulerStateStore.FindAsync(WorkflowExecutionId))?.Version);
    }

    [Fact]
    public async Task Retry_after_is_not_released_when_advancing_the_queue_after_a_landed_boundary_fails()
    {
        // #3275 when the boundary's durable commit lands but removing invoke-1 from the durable queue then fails. The
        // store abandons the segment and rethrows, so the committer never records the effect, and the now inactive
        // overlay used to persist the release onto invoke-1, still the durable head: the sweep ran it again after the
        // delay over its committed effect. It is acknowledged and poisoned instead, leaving no copy to run.
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var queue = new FirstDeleteTimesOutQueue(shared.Queue);
        var probe = new ProbeRecorder(deferFirstResume: false, faultInvoke: true, commitBeforeInvokeFault: false,
            invokeCheckpointName: RuntimeCheckpointNames.ActivitySuspended);
        await using var provider = BuildProvider(shared, probe, retryAfter: RetryDelay, queue: queue);
        await shared.Queue.EnqueueAsync(Work("invoke-1", WorkflowExecutionCommandKind.InvokeActivity, 1));

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

            Assert.Equal(RuntimeSchedulerDrainStopReason.Faulted, result.StopReason);
            Assert.True(queue.DeleteTimedOut);
            var record = Assert.Single(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId));
            Assert.Equal(("invoke-1", RuntimeSchedulerPoisonDisposition.Poisoned), (record.WorkItemId, record.Disposition));
            Assert.Equal(typeof(TimeoutException).FullName, record.Fault.ExceptionType);
            Assert.Null(record.NextRetryAt);
            var effect = Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits(),
                item => item.Commit.StateChanges.Scheduler is not null);
            Assert.Equal(RuntimeCheckpointNames.ActivitySuspended, effect.Commit.Checkpoint.Name);
        }

        // The acknowledgement removed the durable copy the failed advance left behind.
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
        shared.TimeProvider.Advance(RetryDelay);
        await using (var retryScope = provider.CreateAsyncScope())
        {
            var retried = await retryScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
            Assert.Empty(retried.Items);
        }

        Assert.Single(probe.InvokeSessionActive);
    }

    [Fact]
    public async Task Retry_after_is_not_released_when_a_continuing_attempt_claim_boundary_lands_but_its_reconciliation_fails()
    {
        // #3275 for a non-retaining attempt claim the segment continues past, as background admission commits one. The
        // boundary lands durably with its continuation, then the reconciliation after it fails reading the durable
        // queue, so the store abandons the segment and rethrows. The attempt claim is the work's effect, but this
        // boundary consumes no in-flight claim, so nothing recorded it, and invoke-1 is still the durable head: the
        // inactive segment would release it with its delay, and the sweep would run it again over its committed effect.
        // It went to the store, so it is acknowledged and poisoned instead, and no copy is left to run.
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var queue = new ArmedListFailureQueue(shared.Queue);
        var probe = new ProbeRecorder(deferFirstResume: false, faultInvoke: true, commitBeforeInvokeFault: false,
            invokeCheckpointName: RuntimeCheckpointNames.ActivityAttemptClaimed, invokeEnqueuesCompletion: true,
            beforeInvokeCommit: () => queue.Armed = true);
        await using var provider = BuildProvider(shared, probe, retryAfter: RetryDelay, queue: queue);
        await shared.Queue.EnqueueAsync(Work("invoke-1", WorkflowExecutionCommandKind.InvokeActivity, 1));

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

            Assert.Equal("invoke-1", Assert.Single(result.Items).WorkItemId);
            Assert.Equal(RuntimeSchedulerDrainStopReason.Faulted, result.StopReason);
            Assert.True(queue.ListTimedOut);
            Assert.True(Assert.Single(probe.InvokeSessionActive));
            var record = Assert.Single(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId));
            Assert.Equal(("invoke-1", RuntimeSchedulerPoisonDisposition.Poisoned), (record.WorkItemId, record.Disposition));
            Assert.Equal(typeof(TimeoutException).FullName, record.Fault.ExceptionType);
            Assert.Null(record.NextRetryAt);
            Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits(),
                item => item.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityAttemptClaimed);
        }

        Assert.DoesNotContain(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)),
            item => item.WorkItemId == "invoke-1");
        shared.TimeProvider.Advance(RetryDelay);
        await using (var retryScope = provider.CreateAsyncScope())
        {
            var retried = await retryScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
            Assert.DoesNotContain(retried.Items, item => item.WorkItemId == "invoke-1");
        }

        Assert.Single(probe.InvokeSessionActive);
    }

    [Fact]
    public async Task Retry_after_is_not_released_when_the_inner_commit_lands_and_then_throws_in_an_active_segment()
    {
        // #3275 for an ambiguous store call: the boundary's inner commit lands durably and then throws, as an
        // acknowledgement that times out after the transaction committed does. The throw comes before the coalescing
        // store's own recovery and before the committer learns of the commit, so nothing after the fact can tell the
        // effect landed; it used to be released, and the sweep ran invoke-1 again over its committed effect. The attempt
        // is recorded before the store call, so the item is acknowledged and poisoned.
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false, faultInvoke: true, commitBeforeInvokeFault: false,
            invokeCheckpointName: RuntimeCheckpointNames.ActivitySuspended);
        await using var provider = BuildProvider(shared, probe, visibilitySnapshots: true, retryAfter: RetryDelay,
            decorateInnerCommitStore: inner => new ThrowAfterCommitStore(inner, RuntimeCheckpointNames.ActivitySuspended));
        await shared.Queue.EnqueueAsync(Work("invoke-1", WorkflowExecutionCommandKind.InvokeActivity, 1));

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

            Assert.Equal(RuntimeSchedulerDrainStopReason.Faulted, result.StopReason);
            Assert.True(Assert.Single(probe.InvokeSessionActive));
            Assert.True(Assert.IsType<ThrowAfterCommitStore>(scope.ServiceProvider.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>().Value).Threw);
            var record = Assert.Single(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId));
            Assert.Equal(("invoke-1", RuntimeSchedulerPoisonDisposition.Poisoned), (record.WorkItemId, record.Disposition));
            Assert.Equal(typeof(TimeoutException).FullName, record.Fault.ExceptionType);
            Assert.Null(record.NextRetryAt);
            Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits(),
                item => item.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivitySuspended);
        }

        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
        shared.TimeProvider.Advance(RetryDelay);
        await using (var retryScope = provider.CreateAsyncScope())
        {
            var retried = await retryScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
            Assert.Empty(retried.Items);
        }

        Assert.Single(probe.InvokeSessionActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_after_sees_a_checkpoint_a_resume_commits_through_a_scope_of_its_own(bool retainingCommit)
    {
        // #3275: WorkflowResumeBookmarkSchedulerWorkHandler commits through a committer it resolves from a fresh DI scope,
        // whose scoped claim accessor holds no pending claim, so nothing scoped to the drain sees that commit. The
        // dispatch frame flows with the call instead, so a resume that commits a non-retaining checkpoint and then
        // faults is acknowledged and poisoned, while a retaining one is still released.
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false, resumeCommitsInOwnScopeThenFaults: retainingCommit);
        await using var provider = BuildProvider(shared, probe, visibilitySnapshots: true, retryAfter: RetryDelay);
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 1));

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

            Assert.Equal(RuntimeSchedulerDrainStopReason.Faulted, result.StopReason);
            var record = Assert.Single(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId));
            var queued = (await shared.Queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items;
            if (retainingCommit)
            {
                Assert.Equal((RuntimeSchedulerPoisonDisposition.RetryScheduled, Now + RetryDelay), (record.Disposition, record.NextRetryAt));
                var retained = Assert.Single(queued);
                Assert.Equal(("resume-1", Now + RetryDelay), (retained.Item.WorkItemId, retained.VisibleAfter));
                return;
            }

            Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, record.Disposition);
            Assert.Null(record.NextRetryAt);
            Assert.Empty(queued);
        }

        shared.TimeProvider.Advance(RetryDelay);
        await using (var retryScope = provider.CreateAsyncScope())
        {
            var retried = await retryScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
            Assert.Empty(retried.Items);
        }

        Assert.Equal(1, probe.OwnScopeResumeRuns);
        Assert.Equal(1L, (await shared.SchedulerStateStore.FindAsync(WorkflowExecutionId))?.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_work_is_released_only_while_the_active_segment_holds_no_effect_of_it(bool? retainingCommit)
    {
        // #3683: the unavailable arm inside an ACTIVE segment, on a host with no resumption sweep. A non-retaining commit
        // there is buffered and consumes nothing, so the item used to be released with its delay: the quiescence flush
        // then committed the buffered effect and kept the released copy, and the item ran again once the delay passed.
        // It is handled as a handler fault instead: acknowledged through the overlay, so the flush commits the effect and
        // removes the durable copy, and recorded. Unavailability before any commit, or after one that retains the item
        // for re-drive, is still released.
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false, faultInvoke: true, commitBeforeInvokeFault: retainingCommit,
            unavailableFault: true);
        await using var provider = BuildProvider(shared, probe, visibilitySnapshots: true);
        await shared.Queue.EnqueueAsync(Work("invoke-1", WorkflowExecutionCommandKind.InvokeActivity, 1));

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

            Assert.True(Assert.Single(probe.InvokeSessionActive));
            var poison = await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId);
            var commits = InnerCommitStore(scope.ServiceProvider).ListCommits();
            if (retainingCommit is null)
            {
                Assert.Empty(commits);
            }
            else
            {
                var flush = Assert.Single(commits, item => item.Commit.Checkpoint.Name == RuntimeCoalescingMetadataKeys.FlushCheckpointName);
                Assert.Equal(1L, flush.Commit.StateChanges.Scheduler?.State.Version);
            }

            var queued = (await shared.Queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items;
            if (retainingCommit != false)
            {
                Assert.Equal(RuntimeSchedulerDrainStopReason.Deferred, result.StopReason);
                Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Deferred, Assert.Single(result.Items).Status);
                Assert.Empty(poison);
                var retained = Assert.Single(queued);
                Assert.Equal(("invoke-1", Now + RetryDelay), (retained.Item.WorkItemId, retained.VisibleAfter));
                return;
            }

            Assert.Equal(RuntimeSchedulerDrainStopReason.Faulted, result.StopReason);
            AssertPoisonedUnavailability(Assert.Single(poison), "invoke-1");
            Assert.Empty(queued);
        }

        // The buffered effect is durable, and nothing is left to run it again.
        shared.TimeProvider.Advance(RetryDelay);
        await using (var retryScope = provider.CreateAsyncScope())
        {
            var retried = await retryScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
            Assert.Empty(retried.Items);
        }

        Assert.Single(probe.InvokeSessionActive);
        Assert.Equal(1L, (await shared.SchedulerStateStore.FindAsync(WorkflowExecutionId))?.Version);
    }

    [Theory]
    [InlineData(PersistenceMode.Durable, "CoalescingProbe", "CoalescingProbe", true)]
    [InlineData(PersistenceMode.Balanced, RuntimeCheckpointNames.ActivitySuspended, RuntimeCheckpointNames.ActivitySuspended, true)]
    [InlineData(PersistenceMode.Balanced, "CoalescingProbe", RuntimeCoalescingMetadataKeys.FlushCheckpointName, true)]
    [InlineData(PersistenceMode.Balanced, RuntimeCheckpointNames.ActivityAttemptClaimed, RuntimeCheckpointNames.ActivityAttemptClaimed, true)]
    // A queue without visibility snapshots cannot persist an overlay delay, so unavailability used to propagate and skip
    // the flush, even when the cap had already folded the effect durably.
    [InlineData(PersistenceMode.Durable, "CoalescingProbe", "CoalescingProbe", false)]
    [InlineData(PersistenceMode.Balanced, "CoalescingProbe", RuntimeCoalescingMetadataKeys.FlushCheckpointName, false)]
    public async Task Unavailable_work_is_not_released_for_a_continuation_whose_effect_the_active_segment_committed(
        PersistenceMode mode, string invokeCheckpointName, string committedBy, bool visibilitySnapshots)
    {
        // #3683 for a continuation that exists only in the overlay, whose checkpoint the cap folds, a boundary the segment
        // continues past commits, or the segment buffers for its flush. Neither consumes an item the durable queue never
        // held, so the release used to succeed, the flush persisted the copy with its delay, and the item ran again over
        // its committed effect. It is acknowledged and recorded instead, and its effect is committed once.
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false, faultInvoke: true, commitBeforeInvokeFault: false,
            scheduleEnqueuesInvoke: true, invokeCheckpointName: invokeCheckpointName, unavailableFault: true);
        await using var provider = BuildProvider(shared, probe, visibilitySnapshots: visibilitySnapshots, mode: mode);
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

            Assert.Equal(["schedule-1", "invoke-1"], result.Items.Select(item => item.WorkItemId));
            Assert.Equal(RuntimeSchedulerDrainStopReason.Faulted, result.StopReason);
            Assert.True(Assert.Single(probe.InvokeSessionActive));
            AssertPoisonedUnavailability(
                Assert.Single(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId)),
                "invoke-1");
            var effect = Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits(),
                item => item.Commit.StateChanges.Scheduler?.State.Version == 2);
            Assert.Equal(committedBy, effect.Commit.Checkpoint.Name);
        }

        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
        shared.TimeProvider.Advance(RetryDelay);
        await using (var retryScope = provider.CreateAsyncScope())
        {
            var retried = await retryScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
            Assert.Empty(retried.Items);
        }

        Assert.Single(probe.InvokeSessionActive);
        Assert.Equal(2L, (await shared.SchedulerStateStore.FindAsync(WorkflowExecutionId))?.Version);
    }

    [Fact]
    public async Task Unavailable_work_is_not_released_when_the_inner_commit_lands_and_then_throws_in_an_active_segment()
    {
        // #3683 for an ambiguous store call the handler reports as unavailability: the boundary's inner commit lands and
        // then throws, before the committer learns of it. The attempt is recorded before the store call, so the item is
        // acknowledged and recorded rather than released to run again over the committed effect.
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false, faultInvoke: true, commitBeforeInvokeFault: false,
            invokeCheckpointName: RuntimeCheckpointNames.ActivitySuspended, unavailableFault: true);
        await using var provider = BuildProvider(shared, probe, visibilitySnapshots: true,
            decorateInnerCommitStore: inner => new ThrowAfterCommitStore(inner, RuntimeCheckpointNames.ActivitySuspended));
        await shared.Queue.EnqueueAsync(Work("invoke-1", WorkflowExecutionCommandKind.InvokeActivity, 1));

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

            Assert.Equal(RuntimeSchedulerDrainStopReason.Faulted, result.StopReason);
            Assert.True(Assert.Single(probe.InvokeSessionActive));
            Assert.True(Assert.IsType<ThrowAfterCommitStore>(scope.ServiceProvider.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>().Value).Threw);
            AssertPoisonedUnavailability(
                Assert.Single(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId)),
                "invoke-1");
            Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits(),
                item => item.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivitySuspended);
        }

        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
        shared.TimeProvider.Advance(RetryDelay);
        await using (var retryScope = provider.CreateAsyncScope())
        {
            var retried = await retryScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
            Assert.Empty(retried.Items);
        }

        Assert.Single(probe.InvokeSessionActive);
    }

    [Fact]
    public async Task Unavailable_refusal_of_the_commit_itself_in_an_active_segment_still_releases_the_work()
    {
        // #3683: the inner store refuses the boundary's commit as unavailable without applying it, as EF refuses a native
        // worker's admission whose model decision changed. That commit is no attempt, so with nothing else committed in
        // the dispatch the item is released with its delay through the overlay, as before #3683, and is not recorded.
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false, faultInvoke: true, commitBeforeInvokeFault: false,
            invokeCheckpointName: RuntimeCheckpointNames.ActivitySuspended, unavailableFault: true);
        await using var provider = BuildProvider(shared, probe, visibilitySnapshots: true,
            decorateInnerCommitStore: inner => new ThrowAfterCommitStore(inner, RuntimeCheckpointNames.ActivitySuspended, refuseAsUnavailable: true));
        await shared.Queue.EnqueueAsync(Work("invoke-1", WorkflowExecutionCommandKind.InvokeActivity, 1));

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Deferred, result.StopReason);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Deferred, Assert.Single(result.Items).Status);
        Assert.True(Assert.Single(probe.InvokeSessionActive));
        Assert.True(Assert.IsType<ThrowAfterCommitStore>(scope.ServiceProvider.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>().Value).Threw);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId));
        Assert.DoesNotContain(InnerCommitStore(scope.ServiceProvider).ListCommits(),
            item => item.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivitySuspended);
        var retained = Assert.Single((await shared.Queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items);
        Assert.Equal(("invoke-1", Now + RetryDelay), (retained.Item.WorkItemId, retained.VisibleAfter));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_resume_sees_a_checkpoint_it_commits_through_a_scope_of_its_own(bool retainingCommit)
    {
        // #3683: a resume commits through a committer from a DI scope of its own, which consumes no claim, and then
        // reports unavailability. The dispatch frame flows with the call, so a non-retaining commit is acknowledged and
        // recorded, and a retaining one is still released with its delay.
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false, resumeCommitsInOwnScopeThenFaults: retainingCommit, unavailableFault: true);
        await using var provider = BuildProvider(shared, probe, visibilitySnapshots: true);
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 1));

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

            var poison = await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId);
            var queued = (await shared.Queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items;
            if (retainingCommit)
            {
                Assert.Equal(RuntimeSchedulerDrainStopReason.Deferred, result.StopReason);
                Assert.Empty(poison);
                var retained = Assert.Single(queued);
                Assert.Equal(("resume-1", Now + RetryDelay), (retained.Item.WorkItemId, retained.VisibleAfter));
                return;
            }

            Assert.Equal(RuntimeSchedulerDrainStopReason.Faulted, result.StopReason);
            AssertPoisonedUnavailability(Assert.Single(poison), "resume-1");
            Assert.Empty(queued);
        }

        shared.TimeProvider.Advance(RetryDelay);
        await using (var retryScope = provider.CreateAsyncScope())
        {
            var retried = await retryScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
            Assert.Empty(retried.Items);
        }

        Assert.Equal(1, probe.OwnScopeResumeRuns);
        Assert.Equal(1L, (await shared.SchedulerStateStore.FindAsync(WorkflowExecutionId))?.Version);
    }

    [Theory]
    [InlineData(WorkflowExecutionCommandKind.InvokeActivity, false)]
    [InlineData(WorkflowExecutionCommandKind.CompleteActivity, false)]
    [InlineData(WorkflowExecutionCommandKind.NotifyParentActivity, false)]
    [InlineData(WorkflowExecutionCommandKind.DeliverChildControl, false)]
    [InlineData(WorkflowExecutionCommandKind.Checkpoint, false)]
    [InlineData(WorkflowExecutionCommandKind.Checkpoint, true)]
    public async Task Unsupported_overlay_deferral_preserves_source_and_original_unavailability_without_a_false_delay(
        WorkflowExecutionCommandKind kind, bool deactivate)
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: true, retainingBoundaryBeforeDeferral: deactivate);
        await using var provider = BuildProvider(shared, probe);
        var source = Work("unsupported-source", kind, 1);
        await shared.Queue.EnqueueAsync(source);
        await using (var scope = provider.CreateAsyncScope())
        {
            var exception = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() =>
                scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                    .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());
            Assert.Equal("probe.resume-unavailable", exception.Code);
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId));
        }
        Assert.Equal(source.WorkItemId, Assert.Single(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).WorkItemId);
        Assert.True(Assert.Single(probe.ResumeSnapshots).SessionActive);
        // A new provider retries the unchanged source. No durable retry delay was advertised or invented.
        await using var reopened = BuildProvider(shared, new ProbeRecorder(deferFirstResume: false));
        await using var retryScope = reopened.CreateAsyncScope();
        var result = await retryScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
        Assert.False(result.StoppedOnDeferral);
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Visibility_reader_without_deferral_by_identity_keeps_the_conservative_path(bool unavailable)
    {
        // A queue that lists visibility but cannot delay an item by its identity cannot persist a delay at the flush, so
        // the overlay never records one (#3690). An honored RetryAfter is not released and is poisoned, and temporary
        // unavailability propagates with its source retained, as for a queue without visibility snapshots. The flush
        // never reaches the deferral the queue lacks.
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false, faultInvoke: true, invokeUnavailable: unavailable);
        await using var provider = BuildProvider(shared, probe, retryAfter: unavailable ? null : RetryDelay,
            queue: new VisibilityReaderOnlyQueue(shared.Queue));
        await shared.Queue.EnqueueAsync(Work("invoke-1", WorkflowExecutionCommandKind.InvokeActivity, 1));

        await using (var scope = provider.CreateAsyncScope())
        {
            var orchestrator = scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>();
            var records = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>();
            if (unavailable)
            {
                var exception = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() =>
                    orchestrator.DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());
                Assert.Equal("probe.invoke-unavailable", exception.Code);
                Assert.Empty(await records.ListAsync(WorkflowExecutionId));
            }
            else
            {
                var result = await orchestrator.DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
                Assert.Equal(RuntimeSchedulerDrainStopReason.Faulted, result.StopReason);
                var record = Assert.Single(await records.ListAsync(WorkflowExecutionId));
                Assert.Equal(("invoke-1", RuntimeSchedulerPoisonDisposition.Poisoned), (record.WorkItemId, record.Disposition));
                Assert.Null(record.NextRetryAt);
            }
        }

        Assert.True(Assert.Single(probe.InvokeSessionActive));
        // No durable delay was advertised or invented: the unavailable source stays runnable at once, and the poisoned
        // step is gone.
        var queued = (await shared.Queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items
            .Select(entry => (entry.Item.WorkItemId, entry.VisibleAfter));
        if (unavailable)
            Assert.Equal([("invoke-1", (DateTimeOffset?)null)], queued);
        else
            Assert.Empty(queued);
    }

    private static void AssertCoalescedVersions(IServiceProvider services, params long[] versions)
    {
        var commits = InnerCommitStore(services).ListCommits();
        Assert.All(commits, item => Assert.Equal(RuntimeCoalescingMetadataKeys.FlushCheckpointName, item.Commit.Checkpoint.Name));
        Assert.Equal(versions.Select(version => (long?)version), commits.Select(item => item.Commit.StateChanges.Scheduler?.State.Version));
    }

    // Unavailability after a non-retaining checkpoint is recorded like a handler fault the default policy does not retry.
    private static void AssertPoisonedUnavailability(RuntimeSchedulerPoisonRecord record, string workItemId)
    {
        Assert.Equal((workItemId, RuntimeSchedulerPoisonDisposition.Poisoned), (record.WorkItemId, record.Disposition));
        Assert.Equal(typeof(RuntimeSchedulerWorkUnavailableException).FullName, record.Fault.ExceptionType);
        Assert.Null(record.NextRetryAt);
    }

    private static void AssertDurableResumeSnapshot(
        ResumeSnapshot snapshot,
        long? schedulerVersion,
        int durableCommitCount,
        string workItemId = "resume-1")
    {
        Assert.Equal(schedulerVersion, snapshot.SchedulerVersion);
        Assert.Equal(durableCommitCount, snapshot.DurableCommitCount);
        Assert.False(snapshot.SessionActive);
        Assert.Equal([workItemId], snapshot.ActiveClaimWorkItemIds);
    }

    private static ServiceProvider BuildProvider(
        SharedPersistence shared,
        ProbeRecorder probe,
        WorkflowDrainOrchestratorOptions? drainOptions = null,
        ExternalOutboxDeliveryProbe? externalOutboxDelivery = null,
        bool visibilitySnapshots = false,
        TimeSpan? retryAfter = null,
        PersistenceMode mode = PersistenceMode.Balanced,
        IWorkflowSchedulerWorkQueue? queue = null,
        Func<IRuntimeCheckpointCommitStore, IRuntimeCheckpointCommitStore>? decorateInnerCommitStore = null,
        bool resumptionSweep = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(shared.TimeProvider);
        services.AddSingleton(new RuntimeSchedulerWorkClaimOptions { UnavailableRetryDelay = RetryDelay });
        services.AddSingleton(drainOptions ?? new WorkflowDrainOrchestratorOptions(outboxDeliveryBatchSize: 1));
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(queue ?? (visibilitySnapshots
            ? shared.Queue
            : new QueueWithoutVisibility(shared.Queue)));
        services.AddSingleton<ISchedulerStateStore>(shared.SchedulerStateStore);
        services.AddSingleton(shared.CommitState);
        services.AddSingleton<IRuntimePersistenceModeResolver>(new FixedPersistenceModeResolver(mode));
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.RemoveAll<IWorkflowSchedulerWorkHandler>();
        services.AddSingleton(probe);
        services.AddScoped<IWorkflowSchedulerWorkHandler, CoalescingProbeHandler>();
        if (resumptionSweep)
        {
            // The sweep re-drives through the mailbox, which queues a RunSchedulerWork item that the runtime's no-op
            // handler completes.
            services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
            services.AddScoped<IWorkflowSchedulerWorkHandler, NoopWorkflowSchedulerWorkHandler>();
        }
        services.AddPersistenceModes();
        if (retryAfter is { } delay)
        {
            services.RemoveAll<IRuntimeDomainRetryPolicy>();
            services.AddSingleton<IRuntimeDomainRetryPolicy>(new RetryAfterPolicy(delay));
            services.AddSingleton<IWorkflowDispatchDurabilityEvidence>(new WorkflowDispatchDurabilityEvidence(
                WorkflowDispatchDurabilityComponents.Resumption, WorkflowDispatchDurabilityLevel.Durable));
        }
        if (decorateInnerCommitStore is not null)
        {
            var inner = services.Single(descriptor => descriptor.ServiceType == typeof(CoalescingInner<IRuntimeCheckpointCommitStore>));
            services.Remove(inner);
            services.Add(new ServiceDescriptor(
                typeof(CoalescingInner<IRuntimeCheckpointCommitStore>),
                serviceProvider => new CoalescingInner<IRuntimeCheckpointCommitStore>(decorateInnerCommitStore(
                    ((CoalescingInner<IRuntimeCheckpointCommitStore>)inner.ImplementationFactory!(serviceProvider)).Value)),
                inner.Lifetime));
        }
        if (externalOutboxDelivery is not null)
            ReplaceOutboxProcessorWithExternalDeliveryProbe(services, externalOutboxDelivery);
        return services.BuildServiceProvider();
    }

    private static void ReplaceOutboxProcessorWithExternalDeliveryProbe(
        IServiceCollection services,
        ExternalOutboxDeliveryProbe probe)
    {
        var originalRegistration = services.Single(descriptor => descriptor.ServiceType == typeof(IRuntimePostCommitOutboxProcessor));
        var originalFactory = originalRegistration.ImplementationFactory ??
            throw new InvalidOperationException("The runtime outbox processor must be registered through a factory in this fixture.");
        services.Remove(originalRegistration);
        services.Add(new ServiceDescriptor(
            typeof(IRuntimePostCommitOutboxProcessor),
            serviceProvider =>
            {
                var original = (IRuntimePostCommitOutboxProcessor)(originalFactory(serviceProvider) ??
                    throw new InvalidOperationException("The runtime outbox processor factory returned null."));
                var innerOutbox = serviceProvider.GetRequiredService<CoalescingInner<IRuntimePostCommitOutboxStore>>().Value;
                return new ExternalOutboxDeliveryProcessor(
                    original,
                    serviceProvider.GetRequiredService<IRuntimeCoalescingSessionAccessor>(),
                    innerOutbox as IRuntimePostCommitOutboxClaimStore ??
                        throw new InvalidOperationException("The coalescing probe requires durable outbox claim support."),
                    innerOutbox as IRuntimePostCommitOutboxClaimCompletionStore ??
                        throw new InvalidOperationException("The coalescing probe requires durable outbox completion support."),
                    serviceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>(),
                    serviceProvider.GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>().Value,
                    serviceProvider.GetRequiredService<TimeProvider>(),
                    probe);
            },
            originalRegistration.Lifetime));
    }

    private static InMemoryRuntimeCheckpointCommitStore InnerCommitStore(IServiceProvider serviceProvider) =>
        InMemoryCommits(serviceProvider.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>().Value);

    private static InMemoryRuntimeCheckpointCommitStore InMemoryCommits(IRuntimeCheckpointCommitStore store) =>
        (store is ThrowAfterCommitStore decorated ? decorated.Inner : store) as InMemoryRuntimeCheckpointCommitStore ??
        throw new InvalidOperationException("The coalescing probe requires the in-memory checkpoint store.");

    private static RuntimeSchedulerWorkItem ScheduleWork(string workItemId, long sequence) => Work(
        workItemId,
        WorkflowExecutionCommandKind.ScheduleActivity,
        sequence);

    private static RuntimeSchedulerWorkItem ResumeWork(string workItemId, long sequence) => Work(
        workItemId,
        WorkflowExecutionCommandKind.ResumeBookmark,
        sequence);

    private static RuntimeSchedulerWorkItem Work(string workItemId, WorkflowExecutionCommandKind kind, long sequence) =>
        new(
            workItemId,
            WorkflowExecutionId,
            $"command-{workItemId}",
            kind,
            $"envelope-{workItemId}",
            $"{WorkflowExecutionId}:{workItemId}",
            Now,
            Now,
            sequence,
            commandMetadata: new Dictionary<string, string>());

    private static WorkflowExecutionCommandEnvelope Envelope(string? source = null)
    {
        var command = new WorkflowExecutionCommand(
            "drain-command",
            WorkflowExecutionId,
            WorkflowExecutionCommandKind.RunSchedulerWork,
            Now,
            Payload: null,
            Metadata: source is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["source"] = source });
        return new WorkflowExecutionCommandEnvelope(
            "drain-envelope",
            WorkflowExecutionId,
            command,
            "drain-idempotency",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            Now);
    }

    // Keeps the conservative provider path covered after the shipped in-memory queue adopts visibility snapshots.
    private sealed class SharedPersistence(FakeTimeProvider timeProvider)
    {
        public FakeTimeProvider TimeProvider { get; } = timeProvider;
        public InMemoryWorkflowSchedulerWorkQueue Queue { get; } = new();
        public InMemorySchedulerStateStore SchedulerStateStore { get; } = new();
        public InMemoryRuntimeCheckpointStoreState CommitState { get; } = new();
    }

    /// <summary>
    /// A coalesced drain of schedule-1 while another deliverer holds the durable continuation that queues schedule-2. The
    /// drain runs from a command, a yield re-drive that finds schedule-1 as a flushed continuation, or the resumption sweep.
    /// </summary>
    private sealed class HeldContinuationRace : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(10));

        private HeldContinuationRace(ServiceProvider provider, SharedPersistence shared, TimerClock clock, ProbeRecorder probe)
        {
            _provider = provider;
            _scope = provider.CreateAsyncScope();
            Shared = shared;
            Clock = clock;
            Probe = probe;
            Commits = InnerCommitStore(_scope.ServiceProvider);
        }

        public SharedPersistence Shared { get; }
        public TimerClock Clock { get; }
        public ProbeRecorder Probe { get; }
        public InMemoryRuntimeCheckpointCommitStore Commits { get; }
        public RuntimeSchedulerWorkItem Held { get; } = ScheduleWork("schedule-2", 2);
        public RuntimePostCommitOutboxClaim OtherClaim { get; private set; } = null!;
        public Task<RuntimeSchedulerDrainResult> Draining { get; private set; } = null!;
        public Task<RuntimeResumptionSweepResult> Sweeping { get; private set; } = null!;
        public CancellationToken Timeout => _timeout.Token;

        public const string FlushedOutboxId = "outbox-flushed";

        public static async Task<HeldContinuationRace> StartAsync(
            TimeSpan claimVisibility,
            PersistenceMode mode,
            bool resumeAfterFirstStep = false,
            bool deliverDuringFirstStep = false)
        {
            var race = await HoldAsync(claimVisibility, mode, resumeAfterFirstStep: resumeAfterFirstStep);
            if (deliverDuringFirstStep)
                race.Probe.DuringSchedule = race.DeliverHeldAsync;
            race.Draining = race._scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask();
            return race;
        }

        public static async Task<HeldContinuationRace> StartYieldRedriveAsync(TimeSpan claimVisibility, PersistenceMode mode)
        {
            var race = await HoldAsync(claimVisibility, mode, flushedFirstStep: true);
            race.Draining = race._scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
                .DrainAsync(Envelope(InProcessWorkflowExecutionActorProvider.YieldRedriveSource), new RuntimeSchedulerDrainRequest(WorkflowExecutionId))
                .AsTask();
            return race;
        }

        public static async Task<HeldContinuationRace> StartSweepAsync(TimeSpan claimVisibility, PersistenceMode mode)
        {
            var race = await HoldAsync(claimVisibility, mode, resumptionSweep: true);
            race.Sweeping = race._scope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>()
                .SweepAsync(new RuntimeResumptionSweepRequest()).AsTask();
            return race;
        }

        private static async Task<HeldContinuationRace> HoldAsync(
            TimeSpan claimVisibility,
            PersistenceMode mode,
            bool flushedFirstStep = false,
            bool resumptionSweep = false,
            bool resumeAfterFirstStep = false)
        {
            var clock = new TimerClock(Now);
            var shared = new SharedPersistence(clock);
            var probe = new ProbeRecorder(deferFirstResume: false);
            var race = new HeldContinuationRace(
                BuildProvider(shared, probe, mode: mode, resumptionSweep: resumptionSweep), shared, clock, probe);
            var firstStep = ScheduleWork("schedule-1", 1);
            if (!flushedFirstStep)
                await shared.Queue.EnqueueAsync(firstStep);
            // A resume ends the first step's segment at a durable claim boundary.
            if (resumeAfterFirstStep)
                await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 2));
            await race.Commits.AddPendingForTestingAsync(Continuation("outbox-held", race.Held));
            race.OtherClaim = Assert.Single(await race.Commits.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                "other-continuation-worker",
                Now,
                claimVisibility,
                limit: 1,
                WorkflowExecutionId,
                RuntimePostCommitIntentKinds.EnqueueSchedulerWork)));
            // A yielded segment flushed this one undelivered, after the other deliverer claimed its batch.
            if (flushedFirstStep)
                await race.Commits.AddPendingForTestingAsync(Continuation(FlushedOutboxId, firstStep));
            return race;
        }

        private static RuntimePostCommitOutboxItem Continuation(string outboxItemId, RuntimeSchedulerWorkItem step) =>
            new(
                outboxItemId,
                SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(step, $"activity-{step.WorkItemId}", step, Now),
                RuntimePostCommitOutboxStatus.Pending,
                Now,
                Now,
                RuntimePostCommitRetryPolicy.None);

        // The drain ran schedule-1 from its overlay and now polls for the held continuation. Deliver it as the other
        // deliverer would: queue the step durably, then complete the claim.
        public async Task DeliverHeldAtFirstPollAsync()
        {
            var poll = await Clock.NextTimerAsync(due => due == TimeSpan.FromMilliseconds(10), Timeout);
            await DeliverHeldAsync();
            Clock.Advance(poll);
        }

        public async Task DeliverHeldAsync()
        {
            await Shared.Queue.EnqueueAsync(Held);
            Assert.Equal(RuntimePostCommitOutboxClaimCompletionOutcome.Persisted, await Commits.CompleteClaimAsync(
                new RuntimePostCommitOutboxClaimCompletion(
                    OtherClaim,
                    new RuntimePostCommitOutboxDeliveryResult(OtherClaim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Clock.GetUtcNow()))));
        }

        public async Task AssertDrainedBothStepsAsync()
        {
            var result = await Draining.WaitAsync(Timeout);
            Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
            Assert.Equal(["schedule-1", "schedule-2"], result.Items.Select(item => item.WorkItemId));
            Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
            await AssertRanBothStepsAsync();
        }

        public async Task AssertRanBothStepsAsync()
        {
            Assert.Equal(new long?[] { null, 1 }, Probe.ScheduleObservedVersions);
            Assert.Equal(2L, (await Shared.SchedulerStateStore.FindAsync(WorkflowExecutionId))?.Version);
            Assert.Empty(await Shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            _timeout.Dispose();
        }
    }

    /// <summary>A fake clock that reports every timer the code under test parks on, so the test can fire it.</summary>
    private sealed class TimerClock(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private readonly Channel<TimeSpan> _timers = Channel.CreateUnbounded<TimeSpan>();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            _timers.Writer.TryWrite(dueTime);
            return timer;
        }

        // Other timers (claim renewals) park too; skip those the match rejects.
        public async Task<TimeSpan> NextTimerAsync(Func<TimeSpan, bool> match, CancellationToken cancellationToken)
        {
            TimeSpan next;
            do next = await _timers.Reader.ReadAsync(cancellationToken);
            while (!match(next));
            return next;
        }
    }

    private sealed class ProbeRecorder(
        bool deferFirstResume,
        bool enqueueScheduleSuffixAfterSuccessfulResume = false,
        int scheduleSuffixCount = 2,
        bool retainingBoundaryBeforeDeferral = false,
        bool faultInvoke = false,
        bool? commitBeforeInvokeFault = null,
        bool scheduleEnqueuesInvoke = false,
        string invokeCheckpointName = "CoalescingProbe",
        bool? resumeCommitsInOwnScopeThenFaults = null,
        bool invokeEnqueuesCompletion = false,
        Action? beforeInvokeCommit = null,
        bool unavailableFault = false,
        bool invokeUnavailable = false,
        Func<ValueTask>? beforeInvokeFault = null,
        bool recordPumpablePeeks = false)
    {
        private bool _didDefer;
        private bool _didEnqueueScheduleSuffix;

        public List<long?> ScheduleObservedVersions { get; } = [];
        public List<int> ScheduleDurableCommitCounts { get; } = [];
        public List<ResumeSnapshot> ResumeSnapshots { get; } = [];
        public int ScheduleSuffixCount => scheduleSuffixCount;
        public bool RetainingBoundaryBeforeDeferral => retainingBoundaryBeforeDeferral;
        public bool FaultInvoke => faultInvoke;
        // Null faults before any commit; otherwise the invoke commits once first, retaining its item when true.
        public bool? CommitBeforeInvokeFault => commitBeforeInvokeFault;
        public List<bool> InvokeSessionActive { get; } = [];
        // The schedule enqueues invoke-1 as its continuation, and each records its own activity so a cap can fold them.
        public bool ScheduleEnqueuesInvoke => scheduleEnqueuesInvoke;
        public string InvokeCheckpointName => invokeCheckpointName;
        // The invoke's checkpoint enqueues complete-1 as its continuation.
        public bool InvokeEnqueuesCompletion => invokeEnqueuesCompletion;
        public Action? BeforeInvokeCommit => beforeInvokeCommit;
        // The faulting invoke reports temporary unavailability instead of an ordinary failure.
        public bool InvokeUnavailable => invokeUnavailable;
        // Runs while the faulting invoke still holds its claim, after any commit and before it throws.
        public Func<ValueTask>? BeforeInvokeFault => beforeInvokeFault;
        // Other work records what a fused span it started would pump inline next, while its own claim is in flight.
        public bool RecordPumpablePeeks => recordPumpablePeeks;
        public List<string?> PumpablePeeks { get; } = [];
        // Null resumes normally; otherwise the resume commits through a committer from a DI scope of its own, as
        // WorkflowResumeBookmarkSchedulerWorkHandler does, retaining its item when true, and then faults.
        public bool? ResumeCommitsInOwnScopeThenFaults => resumeCommitsInOwnScopeThenFaults;
        public int OwnScopeResumeRuns { get; set; }
        // Runs once inside a schedule's body; it can rearm itself for a later step.
        public Func<Task>? DuringSchedule { get; set; }
        // The faulting invoke and resume throw temporary unavailability rather than a handler fault, and the invoke
        // reports a commit that throws the same way, as a handler whose dependency times out does.
        public bool UnavailableFault => unavailableFault;

        public Exception Fault(string message) => unavailableFault
            ? new RuntimeSchedulerWorkUnavailableException("probe.unavailable-after-commit", "A dependency became unavailable after the commit.")
            : new InvalidOperationException(message);

        public bool ShouldDeferResume()
        {
            if (!deferFirstResume || _didDefer)
                return false;

            _didDefer = true;
            return true;
        }

        public bool ShouldEnqueueScheduleSuffixAfterSuccessfulResume()
        {
            if (!enqueueScheduleSuffixAfterSuccessfulResume || _didEnqueueScheduleSuffix)
                return false;

            _didEnqueueScheduleSuffix = true;
            return true;
        }
    }

    private sealed record ResumeSnapshot(
        long? SchedulerVersion,
        int DurableCommitCount,
        bool SessionActive,
        IReadOnlyCollection<string> ActiveClaimWorkItemIds);

    private sealed class RetryAfterPolicy(TimeSpan delay) : IRuntimeDomainRetryPolicy
    {
        public RuntimeDomainRetryDecision Decide(RuntimeDomainRetryRequest request) =>
            new(RuntimeDomainRetryMode.RetryAfter, delay, "retry-after");
    }

    // Lists visibility but cannot delay an item by its identity, as a third-party provider written before #3690 does.
    private sealed class VisibilityReaderOnlyQueue(InMemoryWorkflowSchedulerWorkQueue inner)
        : QueueWithoutVisibility(inner), IWorkflowSchedulerWorkVisibilityReader
    {
        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkVisibility>> ListWithVisibilityAsync(RuntimeSchedulerWorkQuery query, CancellationToken token = default) =>
            inner.ListWithVisibilityAsync(query, token);
    }

    // Fails the first targeted deletion after the boundary's durable commit, as a provider timeout would.
    private sealed class FirstDeleteTimesOutQueue(InMemoryWorkflowSchedulerWorkQueue inner)
        : QueueWithoutVisibility(inner), IWorkflowSchedulerWorkVisibilityReader, IWorkflowSchedulerWorkDeferral
    {
        public bool DeleteTimedOut { get; private set; }

        public override ValueTask<bool> DeleteAsync(string id, string workId, CancellationToken token = default)
        {
            if (DeleteTimedOut)
                return base.DeleteAsync(id, workId, token);
            DeleteTimedOut = true;
            throw new TimeoutException("The durable queue timed out removing the consumed work item.");
        }

        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkVisibility>> ListWithVisibilityAsync(RuntimeSchedulerWorkQuery query, CancellationToken token = default) =>
            inner.ListWithVisibilityAsync(query, token);
        public ValueTask<bool> DeferAsync(string id, string workId, DateTimeOffset now, DateTimeOffset visibleAt, CancellationToken token = default) =>
            inner.DeferAsync(id, workId, now, visibleAt, token);
    }

    // Fails the first durable listing once armed, as a provider read that times out mid-reconciliation does.
    private sealed class ArmedListFailureQueue(InMemoryWorkflowSchedulerWorkQueue inner)
        : QueueWithoutVisibility(inner), IWorkflowSchedulerWorkVisibilityReader, IWorkflowSchedulerWorkDeferral
    {
        public bool Armed { get; set; }
        public bool ListTimedOut { get; private set; }

        public override ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken token = default)
        {
            if (!Armed)
                return base.ListAsync(query, token);
            Armed = false;
            ListTimedOut = true;
            throw new TimeoutException("The durable queue timed out listing tracked work.");
        }

        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkVisibility>> ListWithVisibilityAsync(RuntimeSchedulerWorkQuery query, CancellationToken token = default) =>
            inner.ListWithVisibilityAsync(query, token);
        public ValueTask<bool> DeferAsync(string id, string workId, DateTimeOffset now, DateTimeOffset visibleAt, CancellationToken token = default) =>
            inner.DeferAsync(id, workId, now, visibleAt, token);
    }

    // Lands the named checkpoint in the inner store once and then throws, as an acknowledgement that times out after the
    // transaction committed does.
    // Throws once for the named checkpoint: by default after the inner commit landed, an ambiguous acknowledgement, or,
    // with refuseAsUnavailable, before it, the way a store refuses a commit it rolled back.
    private sealed class ThrowAfterCommitStore(IRuntimeCheckpointCommitStore inner, string checkpointName, bool refuseAsUnavailable = false)
        : IRuntimeCheckpointCommitStore
    {
        public IRuntimeCheckpointCommitStore Inner => inner;
        public bool Threw { get; private set; }
        public bool SupportsModelCheckpoint => inner.SupportsModelCheckpoint;
        public bool CanCommitModelState(IRuntimeModelExecutionReader reader) => inner.CanCommitModelState(reader);
        public bool SupportsModelParentAdmission => inner.SupportsModelParentAdmission;
        public bool SupportsModelAdmission => inner.SupportsModelAdmission;
        public bool CanCommitModelAdmission(IRuntimeModelExecutionReader reader, IWorkflowExecutionStateStore executions,
            IActivityExecutionStateStore activities, IRuntimePostCommitOutboxStore outbox) => inner.CanCommitModelAdmission(reader, executions, activities, outbox);
        public bool SupportsExactBookmarkResume => inner.SupportsExactBookmarkResume;
        public bool CanCommitExactBookmarkResume(IBookmarkStateStore bookmarks, IWorkflowExecutionStateStore physicalExecutions,
            IActivityExecutionStateStore activities) => inner.CanCommitExactBookmarkResume(bookmarks, physicalExecutions, activities);

        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            if (refuseAsUnavailable && !Threw && commit.Checkpoint.Name == checkpointName)
            {
                Threw = true;
                throw new RuntimeSchedulerWorkUnavailableException("probe.commit-refused", "The store rolled the commit back.");
            }

            var result = await inner.CommitAsync(commit, decision, cancellationToken);
            if (refuseAsUnavailable || Threw || commit.Checkpoint.Name != checkpointName)
                return result;
            Threw = true;
            throw new TimeoutException("The commit acknowledgement timed out after the transaction committed.");
        }
    }

    private sealed class ExternalOutboxDeliveryProbe
    {
        public List<ExternalOutboxSettlement> Settlements { get; } = [];
    }

    private sealed record ExternalOutboxSettlement(string OutboxItemId, string WorkItemId);

    private sealed class ExternalOutboxDeliveryProcessor(
        IRuntimePostCommitOutboxProcessor inner,
        IRuntimeCoalescingSessionAccessor sessionAccessor,
        IRuntimePostCommitOutboxClaimStore claimStore,
        IRuntimePostCommitOutboxClaimCompletionStore claimCompletionStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher,
        IWorkflowSchedulerWorkQueue innerQueue,
        TimeProvider timeProvider,
        ExternalOutboxDeliveryProbe probe) : IRuntimePostCommitOutboxProcessor
    {
        public async ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            if (sessionAccessor.Current?.IsActive != false)
                return await inner.ProcessAsync(request, cancellationToken);

            var now = timeProvider.GetUtcNow();
            var claims = await claimStore.ClaimAsync(
                new RuntimePostCommitOutboxClaimRequest(
                    "other-continuation-worker",
                    now,
                    RuntimePostCommitOutboxProcessing.ClaimVisibilityTimeout,
                    request.Limit,
                    request.WorkflowExecutionId,
                    request.IntentKind),
                cancellationToken);
            if (claims.Count == 0)
                return await inner.ProcessAsync(request, cancellationToken);

            var results = new List<RuntimePostCommitOutboxProcessedItem>(claims.Count);
            foreach (var claim in claims)
            {
                var schedulerWorkItem = claim.Item.Intent.Payload?.Deserialize<RuntimeSchedulerWorkItem>() ??
                    throw new InvalidOperationException($"Outbox intent '{claim.Item.Intent.IntentId}' has no scheduler-work payload.");
                await intentDispatcher.DispatchAsync(claim.Item.Intent, cancellationToken);
                var durableQueueItems = await innerQueue.ListAsync(
                    new RuntimeSchedulerWorkQuery(claim.Item.Intent.WorkflowExecutionId, limit: 100),
                    cancellationToken);
                if (durableQueueItems.Items.All(item => !StringComparer.Ordinal.Equals(item.WorkItemId, schedulerWorkItem.WorkItemId)))
                    throw new InvalidOperationException($"External continuation delivery did not persist scheduler work '{schedulerWorkItem.WorkItemId}'.");

                var completion = await claimCompletionStore.CompleteClaimAsync(
                    new RuntimePostCommitOutboxClaimCompletion(
                        claim,
                        new RuntimePostCommitOutboxDeliveryResult(
                            claim.OutboxItemId,
                            RuntimePostCommitOutboxStatus.Delivered,
                            timeProvider.GetUtcNow())),
                    cancellationToken);
                if (completion != RuntimePostCommitOutboxClaimCompletionOutcome.Persisted)
                    throw new InvalidOperationException($"External continuation delivery did not complete outbox item '{claim.OutboxItemId}'.");

                probe.Settlements.Add(new ExternalOutboxSettlement(claim.OutboxItemId, schedulerWorkItem.WorkItemId));
                results.Add(new RuntimePostCommitOutboxProcessedItem(
                    claim.OutboxItemId,
                    claim.Item.Intent.IntentId,
                    RuntimePostCommitOutboxStatus.Delivered,
                    FailureMessage: null,
                    Outcome: RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner));
            }

            return new RuntimePostCommitOutboxProcessResult(results);
        }
    }

    private sealed class CoalescingProbeHandler(
        RuntimeCheckpointCommitter committer,
        ISchedulerStateStore schedulerStateStore,
        IRuntimeCoalescingSessionAccessor sessionAccessor,
        CoalescingInner<IWorkflowSchedulerWorkQueue> innerQueue,
        CoalescingInner<IRuntimeCheckpointCommitStore> innerCommitStore,
        TimeProvider timeProvider,
        IServiceScopeFactory scopeFactory,
        ProbeRecorder probe) : IWorkflowSchedulerWorkHandler
    {
        private readonly InMemoryRuntimeCheckpointCommitStore _commits = InMemoryCommits(innerCommitStore.Value);

        public string Name => nameof(CoalescingProbeHandler);

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) =>
            workItem.CommandKind is WorkflowExecutionCommandKind.ScheduleActivity or WorkflowExecutionCommandKind.ResumeBookmark or
                WorkflowExecutionCommandKind.InvokeActivity or WorkflowExecutionCommandKind.CompleteActivity or
                WorkflowExecutionCommandKind.NotifyParentActivity or WorkflowExecutionCommandKind.DeliverChildControl or WorkflowExecutionCommandKind.Checkpoint;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            if (probe.FaultInvoke && workItem.CommandKind == WorkflowExecutionCommandKind.InvokeActivity)
            {
                // A deferrable checkpoint, which an active segment buffers rather than commits, unless it trips the cap
                // or the probe names a boundary.
                probe.InvokeSessionActive.Add(sessionAccessor.Current?.AppliesTo(workItem.WorkflowExecutionId) == true);
                if (probe.CommitBeforeInvokeFault is { } retain)
                {
                    var invoke = probe.ScheduleEnqueuesInvoke
                        ? Commit(workItem, new SchedulerState(workItem.WorkflowExecutionId, 2), activityExecutionId: "activity-invoke-1",
                            checkpointName: probe.InvokeCheckpointName)
                        : Commit(workItem, new SchedulerState(workItem.WorkflowExecutionId, 1), probe.InvokeEnqueuesCompletion
                            ? [SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(
                                workItem, "activity-invoke-1", Work("complete-1", WorkflowExecutionCommandKind.CompleteActivity, 2), Now)]
                            : null, checkpointName: probe.InvokeCheckpointName);
                    probe.BeforeInvokeCommit?.Invoke();
                    try
                    {
                        await committer.CommitAsync(invoke with { RetainSchedulerWork = retain }, cancellationToken);
                    }
                    catch (TimeoutException exception) when (probe.UnavailableFault)
                    {
                        throw new RuntimeSchedulerWorkUnavailableException(
                            "probe.commit-unavailable", "The checkpoint store timed out.", exception);
                    }
                }
                if (probe.BeforeInvokeFault is { } beforeFault)
                    await beforeFault();
                if (probe.InvokeUnavailable)
                    throw new RuntimeSchedulerWorkUnavailableException("probe.invoke-unavailable", "The invoke dependency is temporarily unavailable.");
                throw probe.Fault($"The probe faulted after invoking {workItem.WorkItemId}.");
            }

            if (probe.ResumeCommitsInOwnScopeThenFaults is { } retainResume && workItem.CommandKind == WorkflowExecutionCommandKind.ResumeBookmark)
            {
                probe.OwnScopeResumeRuns++;
                await using (var resumeScope = scopeFactory.CreateAsyncScope())
                {
                    var resume = Commit(workItem, new SchedulerState(workItem.WorkflowExecutionId, 1));
                    await resumeScope.ServiceProvider.GetRequiredService<RuntimeCheckpointCommitter>()
                        .CommitAsync(resume with { RetainSchedulerWork = retainResume }, cancellationToken);
                }
                throw probe.Fault($"The probe faulted after resuming {workItem.WorkItemId}.");
            }

            if (workItem.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity)
            {
                if (probe.DuringSchedule is { } during)
                {
                    probe.DuringSchedule = null;
                    await during();
                }
                var current = await schedulerStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken);
                probe.ScheduleObservedVersions.Add(current?.Version);
                var nextState = new SchedulerState(workItem.WorkflowExecutionId, (current?.Version ?? 0) + 1);
                await committer.CommitAsync(probe.ScheduleEnqueuesInvoke
                    ? Commit(workItem, nextState, [SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(
                        workItem, "activity-schedule-1", Work("invoke-1", WorkflowExecutionCommandKind.InvokeActivity, 2), Now)],
                        activityExecutionId: "activity-schedule-1")
                    : Commit(workItem, nextState), cancellationToken);
                probe.ScheduleDurableCommitCounts.Add(_commits.ListCommits().Count);
                return;
            }

            if (probe.RecordPumpablePeeks && sessionAccessor.Current is { } pumpSession && pumpSession.AppliesTo(workItem.WorkflowExecutionId))
                probe.PumpablePeeks.Add((await pumpSession.PeekNextPumpableOverlayItemAsync(cancellationToken))?.WorkItemId);

            var persistedState = await schedulerStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken);
            var activeClaims = await (innerQueue.Value as IWorkflowSchedulerWorkClaimInspection ??
                                      throw new InvalidOperationException("The coalescing probe requires claim inspection."))
                .ListActiveClaimsAsync(workItem.WorkflowExecutionId, timeProvider.GetUtcNow(), cancellationToken);
            probe.ResumeSnapshots.Add(new ResumeSnapshot(
                persistedState?.Version,
                _commits.ListCommits().Count,
                sessionAccessor.Current?.IsActive ?? false,
                activeClaims.Select(claim => claim.Item.WorkItemId).ToArray()));

            if (probe.ShouldDeferResume())
            {
                if (probe.RetainingBoundaryBeforeDeferral)
                {
                    var boundary = Commit(workItem, state: null);
                    await committer.CommitAsync(boundary with
                    {
                        Checkpoint = boundary.Checkpoint with { Name = RuntimeCheckpointNames.WorkflowSuspended },
                        RetainSchedulerWork = true
                    }, cancellationToken);
                }
                throw new RuntimeSchedulerWorkUnavailableException(
                    "probe.resume-unavailable",
                    "The resume dependency is temporarily unavailable.");
            }

            if (probe.ShouldEnqueueScheduleSuffixAfterSuccessfulResume())
            {
                var suffix = Enumerable.Range(0, probe.ScheduleSuffixCount)
                    .Select(index => ScheduleWork($"schedule-{index + 3}", index + 4))
                    .ToArray();
                var continuationIntents = suffix
                    .Select(schedule => SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(
                        workItem,
                        "activity-resume-1",
                        schedule,
                        Now))
                    .ToArray();
                await committer.CommitAsync(Commit(
                    workItem,
                    state: null,
                    postCommitIntents: continuationIntents,
                    consumeBookmark: true), cancellationToken);
            }
        }

        private static RuntimeCheckpointCommit Commit(
            RuntimeSchedulerWorkItem workItem,
            SchedulerState? state,
            IReadOnlyList<RuntimePostCommitIntent>? postCommitIntents = null,
            bool consumeBookmark = false,
            string? activityExecutionId = null,
            string checkpointName = "CoalescingProbe")
        {
            var checkpointMetadata = consumeBookmark
                ? new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory
                }
                : new Dictionary<string, string>();
            var checkpoint = new RuntimeCheckpoint(
                $"checkpoint-{workItem.WorkItemId}",
                consumeBookmark ? RuntimeCheckpointNames.BookmarkConsumed : checkpointName,
                workItem.WorkflowExecutionId,
                Now,
                activityExecutionId is null ? [] : [activityExecutionId],
                checkpointMetadata);
            var changes = new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: state is null
                    ? null
                    : new RuntimeStateChange<SchedulerState>(
                        workItem.WorkflowExecutionId,
                        RuntimeStateChangeOperation.Upsert,
                        state,
                        new Dictionary<string, string>()),
                activityExecutions: [],
                bookmarks: consumeBookmark
                    ? [new RuntimeStateChange<BookmarkState>(
                        "bookmark-resume-1",
                        RuntimeStateChangeOperation.Delete,
                        new BookmarkState(
                            "bookmark-resume-1",
                            workItem.WorkflowExecutionId,
                            "activity-resume-1",
                            "resume-node",
                            "resume-target",
                            "stimulus",
                            "stimulus-hash",
                            null,
                            new Dictionary<string, string>(),
                            Now,
                            null),
                        checkpointMetadata)]
                    : [],
                durableValues: [],
                incidents: [],
                operational: []);
            return new RuntimeCheckpointCommit(
                $"commit-{workItem.WorkItemId}",
                checkpoint,
                changes,
                postCommitIntents ?? [],
                new Dictionary<string, string>());
        }
    }
}
