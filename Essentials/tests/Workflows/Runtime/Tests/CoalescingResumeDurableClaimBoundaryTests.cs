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

    private static void AssertCoalescedVersions(IServiceProvider services, params long[] versions)
    {
        var commits = InnerCommitStore(services).ListCommits();
        Assert.All(commits, item => Assert.Equal(RuntimeCoalescingMetadataKeys.FlushCheckpointName, item.Commit.Checkpoint.Name));
        Assert.Equal(versions.Select(version => (long?)version), commits.Select(item => item.Commit.StateChanges.Scheduler?.State.Version));
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
        PersistenceMode mode = PersistenceMode.Balanced,
        bool resumptionSweep = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(shared.TimeProvider);
        services.AddSingleton(new RuntimeSchedulerWorkClaimOptions { UnavailableRetryDelay = RetryDelay });
        services.AddSingleton(drainOptions ?? new WorkflowDrainOrchestratorOptions(outboxDeliveryBatchSize: 1));
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(visibilitySnapshots
            ? shared.Queue
            : new QueueWithoutVisibility(shared.Queue));
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
        serviceProvider.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>().Value as InMemoryRuntimeCheckpointCommitStore ??
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
            bool resumeAfterFirstStep = false)
        {
            var race = await HoldAsync(claimVisibility, mode, resumeAfterFirstStep: resumeAfterFirstStep);
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
            await Shared.Queue.EnqueueAsync(Held);
            Assert.Equal(RuntimePostCommitOutboxClaimCompletionOutcome.Persisted, await Commits.CompleteClaimAsync(
                new RuntimePostCommitOutboxClaimCompletion(
                    OtherClaim,
                    new RuntimePostCommitOutboxDeliveryResult(OtherClaim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now))));
            Clock.Advance(poll);
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
        bool retainingBoundaryBeforeDeferral = false)
    {
        private bool _didDefer;
        private bool _didEnqueueScheduleSuffix;

        public List<long?> ScheduleObservedVersions { get; } = [];
        public List<int> ScheduleDurableCommitCounts { get; } = [];
        public List<ResumeSnapshot> ResumeSnapshots { get; } = [];
        public int ScheduleSuffixCount => scheduleSuffixCount;
        public bool RetainingBoundaryBeforeDeferral => retainingBoundaryBeforeDeferral;

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
        ProbeRecorder probe) : IWorkflowSchedulerWorkHandler
    {
        private readonly InMemoryRuntimeCheckpointCommitStore _commits =
            innerCommitStore.Value as InMemoryRuntimeCheckpointCommitStore ??
            throw new InvalidOperationException("The coalescing probe requires the in-memory checkpoint store.");

        public string Name => nameof(CoalescingProbeHandler);

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) =>
            workItem.CommandKind is WorkflowExecutionCommandKind.ScheduleActivity or WorkflowExecutionCommandKind.ResumeBookmark or
                WorkflowExecutionCommandKind.InvokeActivity or WorkflowExecutionCommandKind.CompleteActivity or
                WorkflowExecutionCommandKind.NotifyParentActivity or WorkflowExecutionCommandKind.DeliverChildControl or WorkflowExecutionCommandKind.Checkpoint;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            if (workItem.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity)
            {
                var current = await schedulerStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken);
                probe.ScheduleObservedVersions.Add(current?.Version);
                var nextState = new SchedulerState(workItem.WorkflowExecutionId, (current?.Version ?? 0) + 1);
                await committer.CommitAsync(Commit(workItem, nextState), cancellationToken);
                probe.ScheduleDurableCommitCounts.Add(_commits.ListCommits().Count);
                return;
            }

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
            bool consumeBookmark = false)
        {
            var checkpointMetadata = consumeBookmark
                ? new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory
                }
                : new Dictionary<string, string>();
            var checkpoint = new RuntimeCheckpoint(
                $"checkpoint-{workItem.WorkItemId}",
                consumeBookmark ? RuntimeCheckpointNames.BookmarkConsumed : "CoalescingProbe",
                workItem.WorkflowExecutionId,
                Now,
                [],
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
