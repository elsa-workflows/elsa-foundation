using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

// T029a: the inspection reader has two separate responsibilities. A matching live session first exposes its ordered
// logical projection, while the CoalesceInspectionReads switch only controls durable-baseline locality when no logical
// projection exists. This preserves FromState/Merge semantics across an otherwise-equivalent Deferred versus Immediate
// transport boundary without exposing volatile state outside that session.
public sealed class CoalescingInspectionReadTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
    private const string WorkflowExecutionId = "wfexec-1";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquivalentLogicalSequence_IsByteIdenticalAcrossDeferredAndGenericImmediateTransport(bool coalesceInspectionReads)
    {
        var deferred = await RunLogicalSequenceAsync(coalesceInspectionReads, firstContributionImmediate: false);
        var immediate = await RunLogicalSequenceAsync(coalesceInspectionReads, firstContributionImmediate: true);

        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred, deferred.FirstPersistenceDecision.Mode);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, immediate.FirstPersistenceDecision.Mode);
        Assert.Contains("post-commit work", immediate.FirstPersistenceDecision.Reason, StringComparison.Ordinal);
        Assert.Equal(immediate.BuiltProjections, deferred.BuiltProjections);
        Assert.Equal(immediate.FlushedCommits, deferred.FlushedCommits);
        Assert.Equal(immediate.FinalDurableProjections, deferred.FinalDurableProjections);

        var final = Assert.Single(deferred.FinalProjections);
        Assert.Equal("checkpoint-01", final.FirstCheckpointId);
        Assert.Equal("checkpoint-02", final.LastCheckpointId);
        Assert.Equal(new[] { "completed" }, final.OutcomeNames);
        Assert.Equal(new[] { "bookmark-1", "bookmark-2" }, final.Bookmarks.Select(x => x.Identity.BookmarkId).Order());
        Assert.Equal(new[] { "incident-1", "incident-2" }, final.Incidents.Select(x => x.IncidentId).Order());
        Assert.Equal(2, final.ValueSnapshots.Count);
        Assert.Equal("first", final.Metadata["first"]);
        Assert.Equal("second", final.Metadata["second"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedDeferredContribution_IsVisibleToTheNextInSessionInspectionBuild(bool coalesceInspectionReads)
    {
        var result = await RunLogicalSequenceAsync(coalesceInspectionReads, firstContributionImmediate: false);

        Assert.Equal("checkpoint-01", result.BuiltProjectionModels[1].FirstCheckpointId);
        Assert.Equal(2, result.BuiltProjectionModels[1].Bookmarks.Count);
        Assert.Equal(2, result.BuiltProjectionModels[1].Incidents.Count);
        Assert.Equal(2, result.BuiltProjectionModels[1].ValueSnapshots.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveSessionLogicalProjection_WinsOverTheDurableReader_InBothReadModes(bool coalesceInspectionReads)
    {
        var result = await RunLogicalSequenceAsync(coalesceInspectionReads, firstContributionImmediate: false);

        Assert.Equal(JsonSerializer.Serialize(result.BuiltProjectionModels[0]), JsonSerializer.Serialize(result.VisibleAfterFirstDeferredCommit));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminallyDeactivatedSession_ReadsOnlyTheDurableProjection(bool coalesceInspectionReads)
    {
        var result = await RunLogicalSequenceAsync(coalesceInspectionReads, firstContributionImmediate: false);

        Assert.Equal(JsonSerializer.Serialize(Assert.Single(result.FinalProjections)), JsonSerializer.Serialize(result.VisibleAfterTerminal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FoldTrailingProjection_IsNotPublishedBeforeNewCommittedFinalization_AndDoesNotDoubleApply(bool coalesceInspectionReads)
    {
        var result = await RunLogicalSequenceAsync(coalesceInspectionReads, firstContributionImmediate: false);

        var visibleBeforeFinalization = Assert.IsType<ActivityExecutionInspectionProjection>(result.VisibleBeforeSecondFinalization);
        Assert.Equal("checkpoint-01", visibleBeforeFinalization.FirstCheckpointId);
        var final = Assert.Single(result.FinalProjections);
        Assert.Equal("checkpoint-02", final.LastCheckpointId);
        Assert.Equal(2, final.ValueSnapshots.Count);
        Assert.Single(final.ValueSnapshots, snapshot => snapshot.EvaluationId == "value-1");
    }

    [Theory]
    [InlineData(RuntimeCheckpointCommitStoreStatus.Conflict)]
    [InlineData(RuntimeCheckpointCommitStoreStatus.OwnershipLost)]
    [InlineData(RuntimeCheckpointCommitStoreStatus.Skipped)]
    [InlineData(RuntimeCheckpointCommitStoreStatus.Failed)]
    [InlineData(RuntimeCheckpointCommitStoreStatus.Replay)]
    public async Task UnsuccessfulOrReplayFinalization_DoesNotPublishAnInspectionCandidate(RuntimeCheckpointCommitStoreStatus status)
    {
        var result = await FinalizeWithOutcomeAsync(status, exception: null);

        Assert.Equal(status, result.Status);
        Assert.Null(result.VisibleProjection);
    }

    [Fact]
    public async Task ThrowingFinalization_DoesNotPublishAnInspectionCandidate()
    {
        var exception = new InvalidOperationException("Injected provider failure.");
        var result = await FinalizeWithOutcomeAsync(status: null, exception);

        Assert.Same(exception, result.Exception);
        Assert.Null(result.VisibleProjection);
    }

    [Theory]
    [InlineData(RuntimeCheckpointCommitStoreStatus.Committed)]
    [InlineData(RuntimeCheckpointCommitStoreStatus.Conflict)]
    public async Task CapFlush_PublishesTrailingProjectionOnlyAfterSuccessfulPersistence(RuntimeCheckpointCommitStoreStatus status)
    {
        var observation = await CapFlushAsync(status);
        var visible = Assert.IsType<ActivityExecutionInspectionProjection>(observation.VisibleProjection);

        if (status == RuntimeCheckpointCommitStoreStatus.Committed)
        {
            Assert.Equal("checkpoint-cap-1", visible.FirstCheckpointId);
            Assert.Equal("checkpoint-cap-2", visible.LastCheckpointId);
            Assert.Equal(2, visible.ValueSnapshots.Count);
            Assert.Equal("first", visible.Metadata["cap-1"]);
            Assert.Equal("second", visible.Metadata["cap-2"]);
        }
        else
        {
            Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict, observation.Status);
            Assert.Equal("checkpoint-cap-1", visible.FirstCheckpointId);
            Assert.Equal("checkpoint-cap-1", visible.LastCheckpointId);
        }
    }

    [Fact]
    public async Task WithoutActiveLogicalProjection_MemoOnCachesDurableBaseline_AndMemoOffReadsPerCall()
    {
        var memoized = await NewReadOnlyFixtureAsync(coalesceInspectionReads: true);
        var perCall = await NewReadOnlyFixtureAsync(coalesceInspectionReads: false);

        await memoized.Overlay.FindAsync(WorkflowExecutionId, "actexec-read");
        await memoized.Overlay.FindAsync(WorkflowExecutionId, "actexec-read");
        await perCall.Overlay.FindAsync(WorkflowExecutionId, "actexec-read");
        await perCall.Overlay.FindAsync(WorkflowExecutionId, "actexec-read");

        Assert.Equal(1, memoized.Counting.FindCount);
        Assert.Equal(2, perCall.Counting.FindCount);
    }

    // Retained original two-segment fixture: it continues to prove byte identity across the prior durable baseline,
    // a mid-drain flush, and a terminal fold. The formerly fixed 7→4 count is deliberately replaced by locality
    // assertions: OFF reads every build, while ON performs strictly fewer durable baseline reads for this fixture.
    [Fact]
    public async Task TwoSegmentInspectionReads_RemainByteIdentical_AndRetainReadLocality()
    {
        var control = await RunTwoSegmentScenarioAsync(coalesceInspectionReads: false);
        var memoized = await RunTwoSegmentScenarioAsync(coalesceInspectionReads: true);

        Assert.Equal(control.BuiltProjections, memoized.BuiltProjections);
        Assert.Equal(control.FlushedCommits, memoized.FlushedCommits);
        Assert.Equal(control.FinalDurableProjections, memoized.FinalDurableProjections);
        Assert.Equal(control.BuildCount, control.DurableReadCount);
        Assert.True(memoized.DurableReadCount < control.DurableReadCount,
            "The memoized read mode must retain an observable durable-baseline locality benefit for this fixture.");
    }

    [Fact]
    public async Task SuccessfulDurableFinalization_InvalidatesCachedBaseline_WhenNoLogicalProjectionExists()
    {
        const string activityExecutionId = "actexec-baseline";
        var durableInspections = new InMemoryActivityExecutionInspectionStore();
        var baseline = ActivityExecutionInspectionProjection.FromState(
            NewActivityState(activityExecutionId, ActivityExecutionStatus.Running, 1), "checkpoint-baseline-1", Now);
        var current = ActivityExecutionInspectionProjection.FromState(
            NewActivityState(activityExecutionId, ActivityExecutionStatus.Completed, 2), "checkpoint-baseline-2", Now.AddTicks(1));
        await durableInspections.SaveAsync(baseline);
        var counting = new CountingInspectionStore(durableInspections);
        var durableCommits = new InMemoryRuntimeCheckpointCommitStore(activityExecutionInspectionWriter: durableInspections);
        var options = new CoalescingRuntimeCheckpointPersistenceOptions { CoalesceInspectionReads = true };
        var session = new RuntimeCoalescingSession(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), options);
        var accessor = new FixedCoalescingSessionAccessor(session);
        var overlay = new CoalescingActivityExecutionInspectionStore(
            new CoalescingInner<IActivityExecutionInspectionStore>(counting), accessor, options);
        var commitStore = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(durableCommits), accessor);

        Assert.Equal("checkpoint-baseline-1", (await overlay.FindAsync(WorkflowExecutionId, activityExecutionId))!.LastCheckpointId);
        await durableInspections.SaveAsync(current);
        Assert.Equal(1, counting.FindCount);

        var boundary = NewCommit(90, RuntimeCheckpointNames.ActivityAttemptClaimed);
        var preparation = await commitStore.PrepareAsync(RuntimeCheckpointPrepareRequest.From(boundary));
        var token = Assert.IsType<RuntimeCheckpointPreparationToken>(preparation.Token);
        var result = await commitStore.CommitPreparedAsync(token, boundary with
        {
            Checkpoint = boundary.Checkpoint with { Provenance = token.Provenance },
            ExpectedFence = token.ExpectedFence
        }, new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, result.Status);
        Assert.True(session.IsActive);
        Assert.Equal("checkpoint-baseline-2", (await overlay.FindAsync(WorkflowExecutionId, activityExecutionId))!.LastCheckpointId);
        Assert.Equal(2, counting.FindCount);
    }

    [Fact]
    public async Task QuiescentEmptyDrain_DeactivatesAndLeavesInspectionReadsDurableOnly()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var inspections = new InMemoryActivityExecutionInspectionStore();
        var durable = new InMemoryRuntimeCheckpointCommitStore(activityExecutionInspectionWriter: inspections);
        await inspections.SaveAsync(ActivityExecutionInspectionProjection.FromState(
            NewActivityState("actexec-quiescent", ActivityExecutionStatus.Running, 1), "checkpoint-quiescent", Now));
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var options = new CoalescingRuntimeCheckpointPersistenceOptions { CoalesceInspectionReads = true };
        var factory = new RuntimeCoalescingDrainScopeFactory(
            accessor,
            new CoalescingInner<IWorkflowSchedulerWorkQueue>(queue),
            new CoalescingInner<IRuntimePostCommitOutboxStore>(durable),
            durable,
            options);
        await using var scope = factory.Begin(WorkflowExecutionId);
        var counting = new CountingInspectionStore(inspections);
        var overlay = new CoalescingActivityExecutionInspectionStore(
            new CoalescingInner<IActivityExecutionInspectionStore>(counting), accessor, options);

        await scope.FlushAtQuiescenceAsync();

        Assert.False(scope.Session.IsActive);
        Assert.NotNull(await overlay.FindAsync(WorkflowExecutionId, "actexec-quiescent"));
        Assert.NotNull(await overlay.FindAsync(WorkflowExecutionId, "actexec-quiescent"));
        Assert.Equal(2, counting.FindCount);
    }

    [Fact]
    public async Task SessionLoss_RecoversPreparedProjection_AndReusesItsDurableIdentityOnReplay()
    {
        var result = await RunSessionLossReconstructionAsync();

        Assert.Equal(JsonSerializer.Serialize(result.DurableBaseline), JsonSerializer.Serialize(result.DeactivatedReader));
        Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Committed, result.RecoveryLedgerStatus);
        Assert.Equal(RuntimeCheckpointPreparationStatus.Replay, result.ReplayStatus);
        Assert.Equal(result.PreparedToken.LedgerToken, result.RecoveredToken.LedgerToken);
        Assert.Equal(result.PreparedToken.Provenance, result.RecoveredToken.Provenance);
        Assert.Equal(result.PreparedToken.LedgerToken, result.ReplayToken.LedgerToken);
        Assert.Equal(result.PreparedToken.Provenance, result.ReplayToken.Provenance);
        Assert.Equal(result.PreparedCommitId, result.RecoveredCommitId);
        Assert.Equal(1, result.DurableCommitCountAfterRecovery);
        Assert.Equal(result.DurableCommitCountAfterRecovery, result.DurableCommitCountAfterReplay);
        Assert.Equal(JsonSerializer.Serialize(result.BufferedLogicalProjection), JsonSerializer.Serialize(result.FinalDurableProjection));
    }

    [Fact]
    public async Task WithoutActiveSession_OverlayIsPassThrough_EvenWithMemoEnabled()
    {
        var durable = new InMemoryActivityExecutionInspectionStore();
        var options = new CoalescingRuntimeCheckpointPersistenceOptions { CoalesceInspectionReads = true };
        var session = new RuntimeCoalescingSession(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), options);
        session.Deactivate();
        var counting = new CountingInspectionStore(durable);
        var overlay = new CoalescingActivityExecutionInspectionStore(
            new CoalescingInner<IActivityExecutionInspectionStore>(counting),
            new FixedCoalescingSessionAccessor(session),
            options);

        Assert.Null(await overlay.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await overlay.FindAsync(WorkflowExecutionId, "actexec-1"));

        // A deactivated session never serves the memo, so out-of-drain readers (and post-terminal reads) keep hitting
        // the durable store directly.
        Assert.Equal(2, counting.FindCount);
    }

    private static async Task<LogicalSequenceResult> RunLogicalSequenceAsync(
        bool coalesceInspectionReads,
        bool firstContributionImmediate)
    {
        var durableInspections = new InMemoryActivityExecutionInspectionStore();
        var countingInspections = new CountingInspectionStore(durableInspections);
        var innerCommits = new InMemoryRuntimeCheckpointCommitStore(
            activityExecutionStateStore: new InMemoryActivityExecutionStateStore(),
            activityExecutionInspectionWriter: durableInspections);
        var options = new CoalescingRuntimeCheckpointPersistenceOptions { CoalesceInspectionReads = coalesceInspectionReads };
        var session = new RuntimeCoalescingSession(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), options);
        var accessor = new FixedCoalescingSessionAccessor(session);
        var commitStore = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerCommits), accessor);
        var committer = new RuntimeCheckpointCommitter(
            new CoalescingRuntimeCheckpointPersistencePolicy(),
            commitStore,
            ownershipContextAccessor: null,
            tracer: null,
            enrichers: [],
            intentHandlerContributions: []);
        var overlay = new CoalescingActivityExecutionInspectionStore(
            new CoalescingInner<IActivityExecutionInspectionStore>(countingInspections), accessor, options);
        var accumulator = new RuntimeActivityExecutionInspectionAccumulator(overlay);

        async Task CommitPreparedAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision)
        {
            var preparation = await commitStore.PrepareAsync(
                RuntimeCheckpointPrepareRequest.From(commit) with { InitialPersistenceMode = decision.Mode });
            var token = Assert.IsType<RuntimeCheckpointPreparationToken>(preparation.Token);
            var preparedCommit = commit with
            {
                Checkpoint = commit.Checkpoint with { Provenance = token.Provenance },
                ExpectedFence = token.ExpectedFence
            };
            var result = await commitStore.CommitPreparedAsync(token, preparedCommit, decision);
            Assert.True(result.Status is RuntimeCheckpointCommitStoreStatus.Committed or RuntimeCheckpointCommitStoreStatus.Replay);
        }

        var firstState = NewActivityState("actexec-1", ActivityExecutionStatus.Running, sequence: 1);
        var firstProjection = await accumulator.BuildProjectionAsync(
            firstState,
            "checkpoint-01",
            Now.AddTicks(1),
            outcomeNames: ["running"],
            bookmarks: [NewBookmark("bookmark-1")],
            incidents: [NewIncident("incident-1")],
            valueSnapshots: [NewValue("value-1")],
            metadata: new Dictionary<string, string> { ["first"] = "first" });

        var firstCommit = NewHopCommit(1, firstState, firstProjection);
        if (firstContributionImmediate)
            firstCommit = WithGenericTransportWork(firstCommit);
        var firstCommitResult = await committer.CommitAsync(firstCommit);
        Assert.True(firstCommitResult.Succeeded);
        Assert.True(session.IsActive);
        var visibleAfterFirst = await overlay.FindAsync(WorkflowExecutionId, "actexec-1");

        var secondState = NewActivityState("actexec-1", ActivityExecutionStatus.Completed, sequence: 2);
        var secondProjection = await accumulator.BuildProjectionAsync(
            secondState,
            "checkpoint-02",
            Now.AddTicks(2),
            outcomeNames: ["completed"],
            bookmarks: [NewBookmark("bookmark-2")],
            incidents: [NewIncident("incident-2")],
            valueSnapshots: [NewValue("value-2")],
            metadata: new Dictionary<string, string> { ["second"] = "second" });
        var visibleBeforeSecondFinalization = await overlay.FindAsync(WorkflowExecutionId, "actexec-1");

        await CommitPreparedAsync(
            NewHopCommit(2, secondState, secondProjection),
            new(RuntimeCheckpointPersistenceMode.Immediate));

        var flushedCommits = innerCommits.ListCommits()
            .Select(record => record.Commit)
            .OrderBy(commit => commit.CommitId, StringComparer.Ordinal)
            .Select(WithoutTransientTransport)
            .Select(commit => JsonSerializer.Serialize(commit))
            .ToList();

        var final = Assert.IsType<ActivityExecutionInspectionProjection>(
            await durableInspections.FindAsync(WorkflowExecutionId, "actexec-1"));
        var visibleAfterTerminal = await overlay.FindAsync(WorkflowExecutionId, "actexec-1");

        return new LogicalSequenceResult(
            [JsonSerializer.Serialize(firstProjection), JsonSerializer.Serialize(secondProjection)],
            [firstProjection, secondProjection],
            flushedCommits,
            [JsonSerializer.Serialize(final)],
            [final],
            visibleAfterFirst,
            visibleAfterTerminal,
            visibleBeforeSecondFinalization,
            firstCommitResult.PersistenceDecision);
    }

    private static async Task<ScenarioResult> RunTwoSegmentScenarioAsync(bool coalesceInspectionReads)
    {
        var durableInspections = new InMemoryActivityExecutionInspectionStore();
        var countingInspections = new CountingInspectionStore(durableInspections);
        var innerCommits = new InMemoryRuntimeCheckpointCommitStore(
            activityExecutionStateStore: new InMemoryActivityExecutionStateStore(),
            activityExecutionInspectionWriter: durableInspections);
        var options = new CoalescingRuntimeCheckpointPersistenceOptions { CoalesceInspectionReads = coalesceInspectionReads };
        var session = new RuntimeCoalescingSession(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), options);
        var accessor = new FixedCoalescingSessionAccessor(session);
        var commitStore = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerCommits), accessor);
        var overlay = new CoalescingActivityExecutionInspectionStore(
            new CoalescingInner<IActivityExecutionInspectionStore>(countingInspections), accessor, options);
        var accumulator = new RuntimeActivityExecutionInspectionAccumulator(overlay);

        await durableInspections.SaveAsync(ActivityExecutionInspectionProjection.FromState(
            NewActivityState("actexec-prior", ActivityExecutionStatus.Running, sequence: 1),
            "checkpoint-prior-drain",
            Now.AddMinutes(-5)));

        var built = new List<string>();
        var checkpoint = 0;

        async Task CommitPreparedAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision)
        {
            var preparation = await commitStore.PrepareAsync(
                RuntimeCheckpointPrepareRequest.From(commit) with { InitialPersistenceMode = decision.Mode });
            var token = Assert.IsType<RuntimeCheckpointPreparationToken>(preparation.Token);
            var preparedCommit = commit with
            {
                Checkpoint = commit.Checkpoint with { Provenance = token.Provenance },
                ExpectedFence = token.ExpectedFence
            };
            var result = await commitStore.CommitPreparedAsync(token, preparedCommit, decision);
            Assert.True(result.Status is RuntimeCheckpointCommitStoreStatus.Committed or RuntimeCheckpointCommitStoreStatus.Replay);
        }

        async Task HopAsync(string activityExecutionId, ActivityExecutionStatus status, long sequence)
        {
            checkpoint++;
            var state = NewActivityState(activityExecutionId, status, sequence);
            var projection = await accumulator.BuildProjectionAsync(state, $"checkpoint-{checkpoint:D2}", Now.AddTicks(checkpoint));
            built.Add(JsonSerializer.Serialize(projection));
            await CommitPreparedAsync(NewHopCommit(checkpoint, state, projection), new(RuntimeCheckpointPersistenceMode.Deferred));
        }

        await HopAsync("actexec-1", ActivityExecutionStatus.Running, 2);
        await HopAsync("actexec-1", ActivityExecutionStatus.Completed, 2);
        await HopAsync("actexec-2", ActivityExecutionStatus.Running, 3);
        await HopAsync("actexec-2", ActivityExecutionStatus.Completed, 3);
        await HopAsync("actexec-prior", ActivityExecutionStatus.Completed, 1);

        checkpoint++;
        await CommitPreparedAsync(
            NewCommit(checkpoint, RuntimeCheckpointNames.ActivityAttemptClaimed),
            new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.True(session.IsActive);

        await HopAsync("actexec-1", ActivityExecutionStatus.Running, 4);
        await HopAsync("actexec-1", ActivityExecutionStatus.Completed, 4);

        checkpoint++;
        await CommitPreparedAsync(
            NewCommit(checkpoint, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.False(session.IsActive);

        var flushedCommits = innerCommits.ListCommits()
            .Select(record => record.Commit)
            .OrderBy(commit => commit.CommitId, StringComparer.Ordinal)
            .Select(commit => JsonSerializer.Serialize(commit))
            .ToList();
        var finalDurable = new List<string>();
        foreach (var activityExecutionId in new[] { "actexec-1", "actexec-2", "actexec-prior" })
        {
            var projection = await durableInspections.FindAsync(WorkflowExecutionId, activityExecutionId);
            Assert.NotNull(projection);
            finalDurable.Add(JsonSerializer.Serialize(projection));
        }

        return new ScenarioResult(built, flushedCommits, finalDurable, built.Count, countingInspections.FindCount);
    }

    private static async Task<FinalizationObservation> FinalizeWithOutcomeAsync(
        RuntimeCheckpointCommitStoreStatus? status,
        Exception? exception)
    {
        var durableInspections = new InMemoryActivityExecutionInspectionStore();
        var inner = new InMemoryRuntimeCheckpointCommitStore(
            activityExecutionStateStore: new InMemoryActivityExecutionStateStore(),
            activityExecutionInspectionWriter: durableInspections);
        var durable = new FinalizationOutcomePreparedLedgerStore(inner, status, exception);
        var options = new CoalescingRuntimeCheckpointPersistenceOptions { CoalesceInspectionReads = true };
        var session = new RuntimeCoalescingSession(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), options);
        var accessor = new FixedCoalescingSessionAccessor(session);
        var store = new CoalescingRuntimeCheckpointCommitStore(new CoalescingInner<IRuntimeCheckpointCommitStore>(durable), accessor);
        var overlay = new CoalescingActivityExecutionInspectionStore(
            new CoalescingInner<IActivityExecutionInspectionStore>(durableInspections), accessor, options);
        var state = NewActivityState("actexec-finalization", ActivityExecutionStatus.Running, 1);
        var projection = await new RuntimeActivityExecutionInspectionAccumulator(overlay)
            .BuildProjectionAsync(state, "checkpoint-finalization", Now);
        var commit = NewHopCommit(91, state, projection);
        var preparation = await store.PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        var token = Assert.IsType<RuntimeCheckpointPreparationToken>(preparation.Token);
        var prepared = commit with
        {
            Checkpoint = commit.Checkpoint with { Provenance = token.Provenance },
            ExpectedFence = token.ExpectedFence
        };

        try
        {
            var result = await store.CommitPreparedAsync(token, prepared, new(RuntimeCheckpointPersistenceMode.Immediate));
            return new(result.Status, await overlay.FindAsync(WorkflowExecutionId, state.Execution.ActivityExecutionId), null);
        }
        catch (Exception caught) when (ReferenceEquals(caught, exception))
        {
            return new(null, await overlay.FindAsync(WorkflowExecutionId, state.Execution.ActivityExecutionId), caught);
        }
    }

    private static async Task<FinalizationObservation> CapFlushAsync(RuntimeCheckpointCommitStoreStatus foldStatus)
    {
        var durableInspections = new InMemoryActivityExecutionInspectionStore();
        var inner = new InMemoryRuntimeCheckpointCommitStore(
            activityExecutionStateStore: new InMemoryActivityExecutionStateStore(),
            activityExecutionInspectionWriter: durableInspections);
        var durable = new FinalizationOutcomePreparedLedgerStore(inner, foldStatus, null, interceptFolds: true);
        var options = new CoalescingRuntimeCheckpointPersistenceOptions { CoalesceInspectionReads = true, MaxSegmentCheckpoints = 1 };
        var session = new RuntimeCoalescingSession(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), options);
        var accessor = new FixedCoalescingSessionAccessor(session);
        var store = new CoalescingRuntimeCheckpointCommitStore(new CoalescingInner<IRuntimeCheckpointCommitStore>(durable), accessor);
        var overlay = new CoalescingActivityExecutionInspectionStore(
            new CoalescingInner<IActivityExecutionInspectionStore>(durableInspections), accessor, options);
        var accumulator = new RuntimeActivityExecutionInspectionAccumulator(overlay);

        async Task<RuntimeCheckpointCommitStoreResult> CommitAsync(int checkpoint, ActivityExecutionStatus status)
        {
            var state = NewActivityState("actexec-cap", status, checkpoint);
            var projection = await accumulator.BuildProjectionAsync(
                state,
                $"checkpoint-cap-{checkpoint}",
                Now.AddTicks(checkpoint),
                valueSnapshots: [NewValue($"cap-value-{checkpoint}")],
                metadata: new Dictionary<string, string> { [$"cap-{checkpoint}"] = checkpoint == 1 ? "first" : "second" });
            var commit = NewHopCommit(checkpoint + 100, state, projection);
            var preparation = await store.PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
            var token = Assert.IsType<RuntimeCheckpointPreparationToken>(preparation.Token);
            return await store.CommitPreparedAsync(token, commit with
            {
                Checkpoint = commit.Checkpoint with { Provenance = token.Provenance },
                ExpectedFence = token.ExpectedFence
            }, new(RuntimeCheckpointPersistenceMode.Deferred));
        }

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, (await CommitAsync(1, ActivityExecutionStatus.Running)).Status);
        var result = await CommitAsync(2, ActivityExecutionStatus.Completed);
        return new(result.Status, await overlay.FindAsync(WorkflowExecutionId, "actexec-cap"), null);
    }

    private static async Task<SessionLossReconstruction> RunSessionLossReconstructionAsync()
    {
        const string activityExecutionId = "actexec-crash";
        var inspections = new InMemoryActivityExecutionInspectionStore();
        var liveness = new InMemoryExecutionLivenessStateStore();
        var commits = new InMemoryRuntimeCheckpointCommitStore(
            activityExecutionStateStore: new InMemoryActivityExecutionStateStore(),
            operationalStateStore: liveness,
            activityExecutionInspectionWriter: inspections);
        var durableBaseline = ActivityExecutionInspectionProjection.FromState(
            NewActivityState(activityExecutionId, ActivityExecutionStatus.Running, 1), "checkpoint-durable", Now);
        await inspections.SaveAsync(durableBaseline);
        var options = new CoalescingRuntimeCheckpointPersistenceOptions { CoalesceInspectionReads = true };
        var recoveryNow = DateTimeOffset.UtcNow;
        var lease = new RuntimeExecutionLease(
            "lease-recovery",
            WorkflowExecutionId,
            "recovery-owner",
            recoveryNow,
            recoveryNow.AddHours(1),
            1);
        await liveness.SaveAsync(new ExecutionLivenessState(
            $"ownership:{WorkflowExecutionId}",
            WorkflowExecutionId,
            lease,
            heartbeat: null,
            drain: null,
            interruptedExecution: null));

        (RuntimeCoalescingSession Session, CoalescingActivityExecutionInspectionStore Overlay, CoalescingRuntimeCheckpointCommitStore Store) NewSession()
        {
            var session = new RuntimeCoalescingSession(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), options);
            var accessor = new FixedCoalescingSessionAccessor(session);
            return (
                session,
                new CoalescingActivityExecutionInspectionStore(new CoalescingInner<IActivityExecutionInspectionStore>(inspections), accessor, options),
                new CoalescingRuntimeCheckpointCommitStore(new CoalescingInner<IRuntimeCheckpointCommitStore>(commits), accessor));
        }

        var laterState = NewActivityState(activityExecutionId, ActivityExecutionStatus.Completed, 2);
        var original = NewSession();
        var bufferedLogicalProjection = await new RuntimeActivityExecutionInspectionAccumulator(original.Overlay)
            .BuildProjectionAsync(laterState, "checkpoint-replayed", Now.AddTicks(1), outcomeNames: ["completed"]);
        var preparedCommit = NewHopCommit(201, laterState, bufferedLogicalProjection);
        var preparation = await original.Store.PrepareAsync(RuntimeCheckpointPrepareRequest.From(preparedCommit));
        var preparedToken = Assert.IsType<RuntimeCheckpointPreparationToken>(preparation.Token);
        preparedCommit = preparedCommit with
        {
            Checkpoint = preparedCommit.Checkpoint with { Provenance = preparedToken.Provenance },
            ExpectedFence = preparedToken.ExpectedFence
        };
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, (await original.Store.CommitPreparedAsync(
            preparedToken,
            preparedCommit,
            new(RuntimeCheckpointPersistenceMode.Deferred))).Status);
        original.Session.Deactivate();
        var deactivatedReader = await original.Overlay.FindAsync(WorkflowExecutionId, activityExecutionId);

        var preparedPage = await commits.PagePreparedAsync(new RuntimeCheckpointPreparedQuery(WorkflowExecutionId, 10, null));
        var durableReservation = Assert.Single(preparedPage.Reservations);
        var replayer = new RuntimeCheckpointPreparationReplayer(
            new CoalescingRuntimeCheckpointPersistencePolicy(),
            enrichers: [],
            intentHandlerContributions: []);
        var recovered = await replayer.RehydrateAsync(durableReservation);
        var ownershipAccessor = new AsyncLocalRuntimeExecutionOwnershipContextAccessor();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var recovery = new CoalescingRuntimeCheckpointRecoveryCoordinator(commits, queue, replayer, ownershipAccessor);
        RuntimeCheckpointPreparedRecoveryResult recoveryResult;
        using (ownershipAccessor.Push(lease))
            recoveryResult = await recovery.RecoverPreparedAsync(WorkflowExecutionId);
        Assert.True(recoveryResult.CanDispatch);
        var durableLedger = Assert.Single(commits.ListLogicalCheckpointLedgerEntries());
        var durableCommitCountAfterRecovery = commits.ListCommits().Count;
        var replay = await commits.PrepareAsync(
            RuntimeCheckpointPrepareRequest.From(NewHopCommit(201, laterState, bufferedLogicalProjection)) with
            {
                InitialPersistenceMode = RuntimeCheckpointPersistenceMode.Deferred
            });
        var replayToken = Assert.IsType<RuntimeCheckpointPreparationToken>(replay.Token);
        var finalDurableProjection = await inspections.FindAsync(WorkflowExecutionId, activityExecutionId);

        return new(
            durableBaseline,
            Assert.IsType<ActivityExecutionInspectionProjection>(deactivatedReader),
            bufferedLogicalProjection,
            Assert.IsType<ActivityExecutionInspectionProjection>(finalDurableProjection),
            preparedToken,
            recovered.Token,
            replayToken,
            preparedCommit.CommitId,
            recovered.Commit.CommitId,
            durableLedger.Status,
            replay.Status,
            durableCommitCountAfterRecovery,
            commits.ListCommits().Count);
    }

    private sealed record LogicalSequenceResult(
        IReadOnlyList<string> BuiltProjections,
        IReadOnlyList<ActivityExecutionInspectionProjection> BuiltProjectionModels,
        IReadOnlyList<string> FlushedCommits,
        IReadOnlyList<string> FinalDurableProjections,
        IReadOnlyList<ActivityExecutionInspectionProjection> FinalProjections,
        ActivityExecutionInspectionProjection? VisibleAfterFirstDeferredCommit,
        ActivityExecutionInspectionProjection? VisibleAfterTerminal,
        ActivityExecutionInspectionProjection? VisibleBeforeSecondFinalization,
        RuntimeCheckpointPersistenceDecision FirstPersistenceDecision);

    private sealed record ScenarioResult(
        IReadOnlyList<string> BuiltProjections,
        IReadOnlyList<string> FlushedCommits,
        IReadOnlyList<string> FinalDurableProjections,
        int BuildCount,
        int DurableReadCount);

    private sealed record FinalizationObservation(
        RuntimeCheckpointCommitStoreStatus? Status,
        ActivityExecutionInspectionProjection? VisibleProjection,
        Exception? Exception);

    private sealed record SessionLossReconstruction(
        ActivityExecutionInspectionProjection DurableBaseline,
        ActivityExecutionInspectionProjection DeactivatedReader,
        ActivityExecutionInspectionProjection BufferedLogicalProjection,
        ActivityExecutionInspectionProjection FinalDurableProjection,
        RuntimeCheckpointPreparationToken PreparedToken,
        RuntimeCheckpointPreparationToken RecoveredToken,
        RuntimeCheckpointPreparationToken ReplayToken,
        string PreparedCommitId,
        string RecoveredCommitId,
        RuntimeLogicalCheckpointLedgerStatus RecoveryLedgerStatus,
        RuntimeCheckpointPreparationStatus ReplayStatus,
        int DurableCommitCountAfterRecovery,
        int DurableCommitCountAfterReplay);

    private static async Task<ReadOnlyFixture> NewReadOnlyFixtureAsync(bool coalesceInspectionReads)
    {
        var durable = new InMemoryActivityExecutionInspectionStore();
        await durable.SaveAsync(ActivityExecutionInspectionProjection.FromState(
            NewActivityState("actexec-read", ActivityExecutionStatus.Running, sequence: 1),
            "checkpoint-read",
            Now));
        var options = new CoalescingRuntimeCheckpointPersistenceOptions { CoalesceInspectionReads = coalesceInspectionReads };
        var session = new RuntimeCoalescingSession(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), options);
        var counting = new CountingInspectionStore(durable);
        return new(
            new CoalescingActivityExecutionInspectionStore(
                new CoalescingInner<IActivityExecutionInspectionStore>(counting),
                new FixedCoalescingSessionAccessor(session),
                options),
            counting);
    }

    private static RuntimeCheckpointCommit WithGenericTransportWork(RuntimeCheckpointCommit commit)
        => commit with
        {
            PostCommitIntents =
            [
                new RuntimePostCommitIntent(
                    "intent-transport",
                    WorkflowExecutionId,
                    RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
                    commit.Checkpoint.OccurredAt,
                    activityExecutionId: null,
                    idempotencyKey: "transport-1",
                    JsonSerializer.SerializeToElement(new { transport = "generic" }))
            ]
        };

    private static RuntimeCheckpointCommit WithoutTransientTransport(RuntimeCheckpointCommit commit) => commit with
    {
        PostCommitIntents = [],
        StateChanges = commit.StateChanges.WithPostCommitOutbox([])
    };

    private static ActivityExecutionBookmarkSummary NewBookmark(string bookmarkId) =>
        new(new(bookmarkId, WorkflowExecutionId, "actexec-1", $"resume-{bookmarkId}"), "test", $"hash-{bookmarkId}", Now, null);

    private static ActivityExecutionIncidentSummary NewIncident(string incidentId) =>
        new(incidentId, IncidentSeverity.Warning, IncidentStatus.Open, null, "test", "test", Now, null, false, new Dictionary<string, string>());

    private static ActivityExecutionInspectionValueSnapshot NewValue(string name) =>
        new(name, ActivityExecutionInspectionValueSubject.ActivityInput, RuntimePayloadCaptureMode.MetadataOnly, null, Now, null, "test", false,
            new Dictionary<string, string>(), InputKey: name, EvaluationId: name);

    private sealed record ReadOnlyFixture(CoalescingActivityExecutionInspectionStore Overlay, CountingInspectionStore Counting);

    private static ActivityExecutionState NewActivityState(string activityExecutionId, ActivityExecutionStatus status, long sequence) =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: activityExecutionId,
                WorkflowExecutionId: WorkflowExecutionId,
                ExecutableNodeId: $"node-{activityExecutionId}",
                AuthoredActivityId: $"authored-{activityExecutionId}",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: status,
            SubStatus: null,
            ExecutionSequence: sequence,
            ScheduledAt: Now,
            StartedAt: Now,
            CompletedAt: status == ActivityExecutionStatus.Completed ? Now.AddSeconds(sequence) : null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: "branch-1",
            IterationId: null,
            Provenance: ActivitySchedulingProvenance.Empty,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static RuntimeCheckpointCommit NewCommit(int checkpoint, string checkpointName) =>
        new(
            CommitId: $"commit-{checkpoint:D2}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint-{checkpoint:D2}",
                Name: checkpointName,
                WorkflowExecutionId: WorkflowExecutionId,
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

    private static RuntimeCheckpointCommit NewHopCommit(
        int checkpoint,
        ActivityExecutionState state,
        ActivityExecutionInspectionProjection projection) =>
        NewCommit(checkpoint, RuntimeCheckpointNames.ActivityCompleted) with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
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
                operational: [],
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        projection.ActivityExecutionId,
                        RuntimeStateChangeOperation.Upsert,
                        projection,
                        new Dictionary<string, string>())
                ])
        };

    private sealed class FinalizationOutcomePreparedLedgerStore(
        IRuntimeCheckpointPreparedLedgerStore inner,
        RuntimeCheckpointCommitStoreStatus? status,
        Exception? exception,
        bool interceptFolds = false) : IRuntimeCheckpointPreparedLedgerStore
    {
        public ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(RuntimeCheckpointPrepareRequest request, CancellationToken cancellationToken = default) =>
            inner.PrepareAsync(request, cancellationToken);

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default) =>
            inner.CommitAsync(commit, decision, cancellationToken);

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedAsync(RuntimeCheckpointPreparationToken token, RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default) =>
            exception is not null
                ? ValueTask.FromException<RuntimeCheckpointCommitStoreResult>(exception)
                : ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]) { Status = status!.Value });

        public ValueTask<RuntimeCheckpointPreparedPage> PagePreparedAsync(RuntimeCheckpointPreparedQuery query, CancellationToken cancellationToken = default) =>
            inner.PagePreparedAsync(query, cancellationToken);

        public ValueTask<RuntimeCheckpointPreparedAdoptionReceipt> AdoptPreparedAsync(RuntimeCheckpointPreparedAdoptionRequest request, CancellationToken cancellationToken = default) =>
            inner.AdoptPreparedAsync(request, cancellationToken);

        public ValueTask<RuntimeCheckpointPreparedFoldResult> CommitPreparedFoldAsync(RuntimeCheckpointPreparedFoldRequest request, CancellationToken cancellationToken = default) =>
            interceptFolds
                ? ValueTask.FromResult(new RuntimeCheckpointPreparedFoldResult(status!.Value, new Dictionary<string, RuntimeCheckpointCommitStoreResult>()))
                : inner.CommitPreparedFoldAsync(request, cancellationToken);
    }

    private sealed class CountingInspectionStore(IActivityExecutionInspectionStore inner) : IActivityExecutionInspectionStore
    {
        public int FindCount { get; private set; }

        public ValueTask<ActivityExecutionInspectionProjection?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
        {
            FindCount++;
            return inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        }

        public ValueTask<ActivityExecutionInspectionSummaryPage> ListSummariesPageAsync(ActivityExecutionInspectionSummaryPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListSummariesPageAsync(query, cancellationToken);
    }

    private sealed class FixedCoalescingSessionAccessor(RuntimeCoalescingSession session) : IRuntimeCoalescingSessionAccessor
    {
        public RuntimeCoalescingSession? Current => session;

        public IDisposable Push(RuntimeCoalescingSession? pushedSession) =>
            throw new NotSupportedException("This test provides a fixed ambient session.");
    }
}
