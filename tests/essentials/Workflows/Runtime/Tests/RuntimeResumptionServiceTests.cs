using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeResumptionServiceTests
{
    private const string MarkerKind = "Test.Marker";
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SweepAsync_WithNothingToDo_ReturnsNoWorkResult()
    {
        var harness = new Harness();

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.False(result.DidWork);
        Assert.Equal(0, result.OutboxAttemptedCount);
        Assert.Empty(result.Dispatches);
        Assert.Empty(harness.AgentProvider.Activations);

        var outboxRequest = Assert.Single(harness.OutboxProcessor.Requests);
        Assert.Null(outboxRequest.WorkflowExecutionId);
        Assert.Null(outboxRequest.IntentKind);
    }

    [Fact]
    public async Task SweepAsync_DeliversContributedIntentCommittedThroughRealCheckpointAndOutbox()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddSingleton<Marker>();
        services.AddRuntimePostCommitIntentHandler<MarkerHandler>(MarkerKind);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var scopedProvider = scope.ServiceProvider;
        var intent = NewMarkerIntent();
        var commit = new RuntimeCheckpointCommit(
            CommitId: "commit-marker-1",
            Checkpoint: new RuntimeCheckpoint(
                "checkpoint-marker-1",
                RuntimeCheckpointNames.PostCommitIntentRecorded,
                "wfexec-marker-1",
                Now,
                [],
                new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            PostCommitIntents: [intent],
            Metadata: new Dictionary<string, string>());

        var commitResult = await scopedProvider.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(commit);
        Assert.True(commitResult.Succeeded);
        var store = scopedProvider.GetRequiredService<IRuntimePostCommitOutboxStore>();
        Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10)));

        var marker = scopedProvider.GetRequiredService<Marker>();
        var agentProvider = new FakeAgentProvider();
        var service = new RuntimeResumptionService(
            scopedProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>(),
            new FakeWorkQueue(),
            new FakeRecoveryScanner(),
            agentProvider,
            new ShortRuntimeExecutionIdGenerator(),
            new FakeTimeProvider(Now),
            new InMemoryWorkflowExecutionStateStore(),
            OpenPauseGate(),
            new RuntimeResumptionDiscoveryStateStore());

        var result = await service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Equal(1, result.OutboxDeliveredCount);
        var deliveredIntent = Assert.Single(marker.Intents);
        Assert.Equal(intent.IntentId, deliveredIntent.IntentId);
        Assert.Equal(intent.Kind, deliveredIntent.Kind);
        Assert.Equal(intent.WorkflowExecutionId, deliveredIntent.WorkflowExecutionId);
        Assert.Equal(intent.ActivityExecutionId, deliveredIntent.ActivityExecutionId);
        Assert.Equal(intent.IdempotencyKey, deliveredIntent.IdempotencyKey);
        Assert.Equal(intent.RecordedAt, deliveredIntent.RecordedAt);
        Assert.Empty(agentProvider.Activations);
        Assert.Empty(agentProvider.Agent.Envelopes);
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10)));
    }

    [Fact]
    public async Task SweepAsync_PropagatesOutboxCountsAndBatchSizes()
    {
        var harness = new Harness();
        harness.OutboxProcessor.Result = new RuntimePostCommitOutboxProcessResult(
        [
            new RuntimePostCommitOutboxProcessedItem("outbox-1", "intent-1", RuntimePostCommitOutboxStatus.Delivered, null),
            new RuntimePostCommitOutboxProcessedItem("outbox-2", "intent-2", RuntimePostCommitOutboxStatus.Delivered, null),
            new RuntimePostCommitOutboxProcessedItem("outbox-3", "intent-3", RuntimePostCommitOutboxStatus.FailedRetryable, "boom")
        ]);

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(outboxBatchSize: 7, backlogBatchSize: 5));

        Assert.True(result.DidWork);
        Assert.Equal(3, result.OutboxAttemptedCount);
        Assert.Equal(2, result.OutboxDeliveredCount);
        Assert.Equal(1, result.OutboxFailedCount);
        Assert.Equal(7, Assert.Single(harness.OutboxProcessor.Requests).Limit);
        Assert.Equal(5, Assert.Single(harness.WorkQueue.BacklogLimits));
    }

    [Fact]
    public async Task SweepAsync_RedrivesBacklogThroughAgentWithRecoveryEnvelope()
    {
        var harness = new Harness();
        harness.WorkQueue.PendingExecutionIds = ["wfexec-1"];

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        var dispatch = Assert.Single(result.Dispatches);
        Assert.Equal("wfexec-1", dispatch.WorkflowExecutionId);
        Assert.Equal(RuntimeResumptionDispatchOutcome.Accepted, dispatch.Outcome);
        Assert.Null(dispatch.Failure);

        var activation = Assert.Single(harness.AgentProvider.Activations);
        Assert.Equal(WorkflowExecutionActorActivationReason.Recovery, activation.Reason);
        Assert.Equal("runtime-resumption", activation.RequestedBy);

        var envelope = Assert.Single(harness.AgentProvider.Agent.Envelopes);
        Assert.Equal("wfexec-1", envelope.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, envelope.Command.Kind);
        Assert.Equal(WorkflowExecutionCommandDeliveryMode.AtLeastOnce, envelope.DeliveryMode);
        Assert.Equal(dispatch.EnvelopeId, envelope.EnvelopeId);
        Assert.StartsWith("runtime-resumption:wfexec-1:", envelope.IdempotencyKey);
        Assert.Equal("runtime-resumption", envelope.Metadata["source"]);
        Assert.Equal("runtime-resumption", envelope.Command.Metadata["source"]);
    }

    [Fact]
    public async Task SweepAsync_UnionsBacklogAndScannerCandidatesWithoutDuplicates()
    {
        var harness = new Harness();
        harness.WorkQueue.PendingExecutionIds = ["wfexec-b", "wfexec-a"];
        harness.RecoveryScanner.Candidates =
        [
            NewCandidate("wfexec-b"),
            NewCandidate("wfexec-c")
        ];

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Equal(
            new[] { "wfexec-a", "wfexec-b", "wfexec-c" },
            result.Dispatches.Select(dispatch => dispatch.WorkflowExecutionId));

        var scan = Assert.Single(harness.RecoveryScanner.Requests);
        Assert.Equal(Now, scan.Now);
    }

    [Fact]
    public async Task SweepAsync_RetainsRecoveryContinuationAcrossBoundedSweeps()
    {
        var harness = new Harness();
        harness.RecoveryScanner.Pages.Enqueue(new RecoveryPage(
            [NewCandidate("wfexec-recovery-a")],
            "recovery-next-1"));
        harness.RecoveryScanner.Pages.Enqueue(new RecoveryPage(
            [NewCandidate("wfexec-recovery-b")],
            null));

        var first = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(recoveryScanBatchSize: 1));
        var second = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(recoveryScanBatchSize: 1));

        Assert.Equal("wfexec-recovery-a", Assert.Single(first.Dispatches).WorkflowExecutionId);
        Assert.Equal("wfexec-recovery-b", Assert.Single(second.Dispatches).WorkflowExecutionId);
        Assert.Equal(2, harness.RecoveryScanner.Requests.Count);
        Assert.Null(harness.RecoveryScanner.Requests[0].ContinuationToken);
        Assert.Equal("recovery-next-1", harness.RecoveryScanner.Requests[1].ContinuationToken);
    }

    [Fact]
    public async Task SweepAsync_RetainsProgressWhenRecoveryPageIsEmptyButFiltered()
    {
        var harness = new Harness();
        harness.RecoveryScanner.Pages.Enqueue(new RecoveryPage([], "recovery-filtered-next"));
        harness.RecoveryScanner.Pages.Enqueue(new RecoveryPage([NewCandidate("wfexec-recovery")], null));

        var first = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(recoveryScanBatchSize: 1));
        var second = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(recoveryScanBatchSize: 1));

        Assert.Empty(first.Dispatches);
        Assert.Equal("wfexec-recovery", Assert.Single(second.Dispatches).WorkflowExecutionId);
        Assert.Equal("recovery-filtered-next", harness.RecoveryScanner.Requests[1].ContinuationToken);
    }

    /// <summary>
    /// Spec 184, FR-027: candidates a source supplies, as the distributed runtime's reclaim does, are re-driven by the
    /// next sweep although no lease timed out; one the sweep re-drove is settled, one whose re-drive faulted is listed
    /// again and re-driven by the following sweep.
    /// </summary>
    [Fact]
    public async Task SweepAsync_ReDrivesSourcedCandidatesAndSettlesOnlyThoseItDealtWith()
    {
        var source = new FakeCandidateSource(NewCandidate("wfexec-reclaimed"), NewCandidate("wfexec-retry"));
        var harness = new Harness(source);
        harness.AgentProvider.FailFor = "wfexec-retry";

        var first = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Equal(RuntimeResumptionDispatchOutcome.Accepted, first.Dispatches.Single(dispatch => dispatch.WorkflowExecutionId == "wfexec-reclaimed").Outcome);
        Assert.Equal(RuntimeResumptionDispatchOutcome.Faulted, first.Dispatches.Single(dispatch => dispatch.WorkflowExecutionId == "wfexec-retry").Outcome);
        Assert.Equal(["wfexec-reclaimed"], source.Settled);
        Assert.Equal(["wfexec-retry"], source.Listed);

        harness.AgentProvider.FailFor = null;
        var second = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Equal("wfexec-retry", Assert.Single(second.Dispatches).WorkflowExecutionId);
        Assert.Empty(source.Listed);
    }

    [Fact]
    public async Task SweepAsync_RewindsRecoveryCursorWhenARecoveryDispatchFails()
    {
        var harness = new Harness();
        harness.RecoveryScanner.Pages.Enqueue(new RecoveryPage([NewCandidate("wfexec-retry")], "recovery-next"));
        harness.RecoveryScanner.Pages.Enqueue(new RecoveryPage([NewCandidate("wfexec-retry")], null));
        harness.AgentProvider.FailFor = "wfexec-retry";

        var first = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(recoveryScanBatchSize: 1));

        harness.AgentProvider.FailFor = null;
        var second = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(recoveryScanBatchSize: 1));

        Assert.Equal(RuntimeResumptionDispatchOutcome.Faulted, Assert.Single(first.Dispatches).Outcome);
        Assert.Equal(RuntimeResumptionDispatchOutcome.Accepted, Assert.Single(second.Dispatches).Outcome);
        Assert.Null(harness.RecoveryScanner.Requests[1].ContinuationToken);
    }

    [Fact]
    public async Task SweepAsync_PreservesLegacyScannerCollectionCompatibilityWithoutFabricatingPaging()
    {
        var legacyScanner = new LegacyRecoveryScanner([NewCandidate("wfexec-legacy")]);
        var agentProvider = new FakeAgentProvider();
        var service = new RuntimeResumptionService(
            new FakeOutboxProcessor(),
            new FakeWorkQueue(),
            legacyScanner,
            agentProvider,
            new ShortRuntimeExecutionIdGenerator(),
            new FakeTimeProvider(Now),
            new InMemoryWorkflowExecutionStateStore(),
            OpenPauseGate(),
            new RuntimeResumptionDiscoveryStateStore());

        var result = await service.SweepAsync(new RuntimeResumptionSweepRequest(recoveryScanBatchSize: 1));

        Assert.Equal("wfexec-legacy", Assert.Single(result.Dispatches).WorkflowExecutionId);
        Assert.Null(Assert.Single(legacyScanner.Requests).ContinuationToken);
    }

    [Fact]
    public async Task SweepAsync_UsesLegacyPathForInMemoryScannerOverACustomLivenessStore()
    {
        var liveness = new LegacyLivenessStore(new ExecutionLivenessState(
            "op-legacy",
            "wfexec-legacy-store",
            new RuntimeExecutionLease(
                "lease-legacy",
                "wfexec-legacy-store",
                "worker-legacy",
                Now.AddMinutes(-2),
                Now.AddMinutes(-1),
                fencingToken: 1),
            heartbeat: null,
            drain: null,
            interruptedExecution: null));
        var scanner = new InMemoryRuntimeRecoveryScanner(liveness);
        Assert.False(scanner.SupportsPaging);
        var agentProvider = new FakeAgentProvider();
        var service = new RuntimeResumptionService(
            new FakeOutboxProcessor(),
            new FakeWorkQueue(),
            scanner,
            agentProvider,
            new ShortRuntimeExecutionIdGenerator(),
            new FakeTimeProvider(Now),
            new InMemoryWorkflowExecutionStateStore(),
            OpenPauseGate(),
            new RuntimeResumptionDiscoveryStateStore());

        var result = await service.SweepAsync(new RuntimeResumptionSweepRequest(recoveryScanBatchSize: 1));

        Assert.Equal("wfexec-legacy-store", Assert.Single(result.Dispatches).WorkflowExecutionId);
    }

    /// <summary>
    /// #2188: a backlog that filled <c>MaxExecutionsPerSweep</c> used to leave the recovery scanner no capacity, so it did
    /// not run at all. It now keeps half the cap; the backlog IDs that did not fit are listed by the next sweep rather
    /// than skipped by the backlog bound.
    /// </summary>
    [Fact]
    public async Task SweepAsync_KeepsTheRecoveryShareWhenTheBacklogFillsTheCap()
    {
        var harness = new Harness(workQueue: await QueueWithBacklogAsync("wfexec-a", "wfexec-b", "wfexec-c", "wfexec-d"));
        harness.RecoveryScanner.Pages.Enqueue(new RecoveryPage(
            [NewCandidate("wfexec-r1"), NewCandidate("wfexec-r2"), NewCandidate("wfexec-r3")],
            "recovery-next"));
        var request = new RuntimeResumptionSweepRequest(maxExecutionsPerSweep: 4);

        var first = await harness.Service.SweepAsync(request);
        var second = await harness.Service.SweepAsync(request);

        Assert.Equal(2, harness.RecoveryScanner.Requests[0].Limit);
        Assert.Equal(["wfexec-a", "wfexec-b", "wfexec-r1", "wfexec-r2"], first.Dispatches.Select(dispatch => dispatch.WorkflowExecutionId));
        Assert.Equal(["wfexec-c", "wfexec-d"], second.Dispatches.Select(dispatch => dispatch.WorkflowExecutionId));
        Assert.Equal("recovery-next", harness.RecoveryScanner.Requests[1].ContinuationToken);
    }

    /// <summary>
    /// The recovery share holds on the earlier discovery path too, down to a cap of one, where the scanner has the first
    /// turn: it runs and its candidate takes the only slot.
    /// </summary>
    [Fact]
    public async Task SweepAsync_RunsTheRecoveryScannerWhenALegacyBacklogFillsTheCap()
    {
        var harness = new Harness();
        harness.WorkQueue.PendingExecutionIds = ["wfexec-backlog"];
        harness.RecoveryScanner.Pages.Enqueue(new RecoveryPage(
            [NewCandidate("wfexec-recovery")],
            "recovery-next-1"));

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(
            recoveryScanBatchSize: 1,
            maxExecutionsPerSweep: 1));

        Assert.Equal("wfexec-recovery", Assert.Single(result.Dispatches).WorkflowExecutionId);
        Assert.Equal(1, Assert.Single(harness.RecoveryScanner.Requests).Limit);
    }

    /// <summary>
    /// #2188: executions that stay claimable but never drain (a paused execution releases its head straight away) used
    /// to take the first backlog page on every sweep. The backlog bound walks past them and starts over after a short
    /// page, so every execution with claimable work is reached.
    /// </summary>
    [Fact]
    public async Task SweepAsync_WalksTheBacklogSoTheSameExecutionsCannotHoldTheWindow()
    {
        var harness = new Harness(workQueue: await QueueWithBacklogAsync("wfexec-a", "wfexec-b", "wfexec-c", "wfexec-d", "wfexec-e"));
        var request = new RuntimeResumptionSweepRequest(backlogBatchSize: 2);

        var sweeps = new List<string[]>();
        for (var sweep = 0; sweep < 4; sweep++)
            sweeps.Add((await harness.Service.SweepAsync(request)).Dispatches.Select(dispatch => dispatch.WorkflowExecutionId).ToArray());

        Assert.Equal(
            [["wfexec-a", "wfexec-b"], ["wfexec-c", "wfexec-d"], ["wfexec-e"], ["wfexec-a", "wfexec-b"]],
            sweeps);
    }

    /// <summary>
    /// Unlike the recovery cursor, the backlog bound moves on past a failed re-drive. The failed execution keeps its
    /// work and is reached again by the next walk; rewinding would let failing executions hold the window.
    /// </summary>
    [Fact]
    public async Task SweepAsync_MovesTheBacklogBoundPastAFailedRedrive()
    {
        var harness = new Harness(workQueue: await QueueWithBacklogAsync("wfexec-a", "wfexec-b", "wfexec-c"));
        harness.AgentProvider.FailFor = "wfexec-a";
        var request = new RuntimeResumptionSweepRequest(backlogBatchSize: 2);

        var first = await harness.Service.SweepAsync(request);
        var second = await harness.Service.SweepAsync(request);

        Assert.Equal(RuntimeResumptionDispatchOutcome.Faulted, first.Dispatches.Single(dispatch => dispatch.WorkflowExecutionId == "wfexec-a").Outcome);
        Assert.Equal("wfexec-c", Assert.Single(second.Dispatches).WorkflowExecutionId);
    }

    /// <summary>
    /// A cap of one is #2188 mirrored: a scanner with a candidate on every sweep would keep the only slot and starve
    /// the backlog. The slot alternates instead, and the scanner is not asked on the backlog's turn, so its cursor
    /// skips nothing.
    /// </summary>
    [Fact]
    public async Task SweepAsync_AlternatesASingleSlotBetweenTheRecoveryScannerAndTheBacklog()
    {
        var harness = new Harness();
        harness.WorkQueue.PendingExecutionIds = ["wfexec-backlog"];
        harness.RecoveryScanner.Candidates = [NewCandidate("wfexec-recovery")];
        var request = new RuntimeResumptionSweepRequest(recoveryScanBatchSize: 1, maxExecutionsPerSweep: 1);

        var dispatched = new List<string>();
        for (var sweep = 0; sweep < 4; sweep++)
            dispatched.Add(Assert.Single((await harness.Service.SweepAsync(request)).Dispatches).WorkflowExecutionId);

        Assert.Equal(["wfexec-recovery", "wfexec-backlog", "wfexec-recovery", "wfexec-backlog"], dispatched);
        Assert.Equal(2, harness.RecoveryScanner.Requests.Count);
    }

    /// <summary>
    /// #2188 review: a paused execution's head stays claimable, because the drainer releases it at the closed gate, so
    /// it took a backlog slot on every pass. The sweep asks the drainer's pause gate first: a held execution is skipped,
    /// and the first pass after its hold is lifted re-drives it.
    /// </summary>
    [Fact]
    public async Task SweepAsync_SkipsAHeldExecutionUntilItsHoldIsReleased()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(NewWorkItem("wfexec-held", WorkflowExecutionCommandKind.StartActivity));
        var harness = new Harness(workQueue: queue);
        await harness.Holds.SaveAsync(HoldOn("wfexec-held"));

        var whileHeld = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());
        await harness.Holds.SaveAsync(new WorkflowHoldState(controlPlaneStateId: "control-wfexec-held", workflowExecutionId: "wfexec-held"));
        var afterRelease = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Empty(whileHeld.Dispatches);
        Assert.Equal("wfexec-held", Assert.Single(afterRelease.Dispatches).WorkflowExecutionId);
    }

    /// <summary>
    /// The direction that would look fine: hiding work a drain would advance. A hold stops only pause-gated kinds, so a
    /// held execution whose head is a bookmark resume still drains it and must still be re-driven.
    /// </summary>
    [Fact]
    public async Task SweepAsync_StillRedrivesAHeldExecutionWhoseHeadThePauseGateDoesNotStop()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(NewWorkItem("wfexec-held", WorkflowExecutionCommandKind.ResumeBookmark));
        var harness = new Harness(workQueue: queue);
        await harness.Holds.SaveAsync(HoldOn("wfexec-held"));

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Equal("wfexec-held", Assert.Single(result.Dispatches).WorkflowExecutionId);
    }

    /// <summary>
    /// #2188 review: held recovery-scanner and source candidates were re-driven through the closed gate. They are now
    /// checked like backlog: neither is re-driven, the source's candidate is settled, and the scanner's cursor moves on,
    /// so the scan does not stall on a held execution.
    /// </summary>
    [Fact]
    public async Task SweepAsync_DealsWithHeldRecoveryAndSourcedCandidatesWithoutRedrivingThem()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var source = new FakeCandidateSource(NewCandidate("wfexec-sourced"));
        var harness = new Harness(source, queue);
        foreach (var workflowExecutionId in new[] { "wfexec-sourced", "wfexec-scanned" })
        {
            await harness.Holds.SaveAsync(HoldOn(workflowExecutionId));
            await queue.EnqueueAsync(NewWorkItem(workflowExecutionId, WorkflowExecutionCommandKind.StartActivity));
        }
        harness.RecoveryScanner.Pages.Enqueue(new RecoveryPage([NewCandidate("wfexec-scanned")], "recovery-next"));

        var first = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());
        await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Empty(first.Dispatches);
        Assert.Empty(harness.AgentProvider.Activations);
        Assert.Equal(["wfexec-sourced"], source.Settled);
        Assert.Equal("recovery-next", harness.RecoveryScanner.Requests[1].ContinuationToken);
    }

    /// <summary>
    /// When the pause gate cannot be consulted, a re-drive could not make progress either: its drain consults the same
    /// gate. The sweep re-drives none of those executions, leaves their work queued, logs one warning for the sweep
    /// however many checks failed, and re-drives them once the gate works again.
    /// </summary>
    [Fact]
    public async Task SweepAsync_SkipsAndWarnsOnceWhenThePauseGateFails()
    {
        var queue = await QueueWithBacklogAsync("wfexec-a", "wfexec-b");
        var gate = new FailingPauseGate();
        var harness = new Harness(workQueue: queue, pauseGate: gate);

        var whileFailing = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());
        gate.Fails = false;
        var afterRecovery = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Empty(whileFailing.Dispatches);
        Assert.Single(await queue.ListAllAsync("wfexec-a"));
        var warning = Assert.Single(harness.Logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal("RuntimeResumptionPauseCheckFailed", warning.EventId.Name);
        Assert.Contains("for 2 execution(s)", warning.Message);
        Assert.Equal(["wfexec-a", "wfexec-b"], afterRecovery.Dispatches.Select(dispatch => dispatch.WorkflowExecutionId));
    }

    /// <summary>
    /// #2188 review: a recovery candidate whose pause check failed was never looked at, so the scan cursor keeps its
    /// place, as it does for a failed dispatch, and the scanner offers the candidate again on the next sweep. The head is
    /// under a live claim, so only the scanner offers it.
    /// </summary>
    [Fact]
    public async Task SweepAsync_OffersAScannerCandidateAgainAfterItsPauseCheckFailed()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(NewWorkItem("wfexec-scanned", WorkflowExecutionCommandKind.StartActivity));
        Assert.NotNull(await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wfexec-scanned", "owner-crashed", Now, TimeSpan.FromHours(1))));
        var gate = new FailingPauseGate();
        var harness = new Harness(workQueue: queue, pauseGate: gate);
        harness.RecoveryScanner.Pages.Enqueue(new RecoveryPage([NewCandidate("wfexec-scanned")], "recovery-next"));
        harness.RecoveryScanner.Pages.Enqueue(new RecoveryPage([NewCandidate("wfexec-scanned")], null));

        var whileFailing = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());
        gate.Fails = false;
        var afterRecovery = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Empty(whileFailing.Dispatches);
        Assert.Null(harness.RecoveryScanner.Requests[1].ContinuationToken);
        Assert.Equal("wfexec-scanned", Assert.Single(afterRecovery.Dispatches).WorkflowExecutionId);
    }

    /// <summary>
    /// #2188 review: when the batched next-item read fails, the page is read one execution at a time, so one unreadable
    /// row costs only its own execution its check: that one is not re-driven, and the others are checked and re-driven.
    /// </summary>
    [Fact]
    public async Task SweepAsync_ReadsNextItemsOneByOneWhenTheBatchedReadFails()
    {
        var queue = new CountingWorkQueue { FailNextItemReadWhen = ids => ids.Count > 1 || ids.Contains("wfexec-b") };
        foreach (var workflowExecutionId in new[] { "wfexec-a", "wfexec-b", "wfexec-c" })
            await queue.EnqueueAsync(NewWorkItem(workflowExecutionId, WorkflowExecutionCommandKind.StartActivity));
        var harness = new Harness(workQueue: queue);

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Equal(["wfexec-a", "wfexec-c"], result.Dispatches.Select(dispatch => dispatch.WorkflowExecutionId));
        Assert.Equal(4, queue.NextItemReads);
        var warning = Assert.Single(harness.Logger.Entries);
        Assert.Equal("RuntimeResumptionPauseCheckFailed", warning.EventId.Name);
        Assert.Contains("for 1 execution(s)", warning.Message);
        Assert.Contains("wfexec-b", warning.Message);
    }

    /// <summary>
    /// #2188 review: a held execution that reached a terminal status was never purged, so it kept its residual work and
    /// every pass through the backlog spent a visit on it. It is now purged and reaped like any terminal execution,
    /// without a re-drive, and the backlog no longer lists it.
    /// </summary>
    [Fact]
    public async Task SweepAsync_PurgesAHeldTerminalExecution()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(NewWorkItem("wfexec-done", WorkflowExecutionCommandKind.StartActivity));
        var harness = new Harness(workQueue: queue);
        await harness.Holds.SaveAsync(HoldOn("wfexec-done"));
        await harness.StateStore.SaveAsync(NewState("wfexec-done", WorkflowExecutionStatus.Completed));

        var first = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());
        var second = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Empty(first.Dispatches);
        Assert.Empty(harness.AgentProvider.Activations);
        Assert.Equal((1, 1), (first.TerminalExecutionsPurged, first.PurgedWorkItemCount));
        Assert.Equal("wfexec-done", Assert.Single(harness.AgentProvider.Passivations).WorkflowExecutionId);
        Assert.Empty(await queue.ListPendingWorkflowExecutionIdsAsync(10));
        Assert.Equal(0, second.TerminalExecutionsPurged);
    }

    /// <summary>
    /// #2188 review: the path after a release, through one service and one discovery state, as the pump drives it. A
    /// crashed owner left an expired lease and a lapsed claim on a held execution. Sweeps while it is held leave its
    /// queue as it is; once the hold is lifted, the next pass through the claimable backlog re-drives it.
    /// </summary>
    [Fact]
    public async Task SweepAsync_RedrivesAReleasedExecutionThroughTheBacklogWalk()
    {
        var leaseDuration = TimeSpan.FromMinutes(1);
        var later = Now + leaseDuration + leaseDuration;
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(later));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var runtime = scope.ServiceProvider;
        var liveness = runtime.GetRequiredService<IExecutionLivenessStateStore>();
        var holds = runtime.GetRequiredService<IWorkflowHoldStateStore>();
        var inner = runtime.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        await new RuntimeExecutionOwnershipService(
                liveness,
                new FakeTimeProvider(Now),
                new RuntimeExecutionOwnershipOptions { OwnerId = "owner-crashed", LeaseDuration = leaseDuration })
            .AcquireAsync("wfexec-held");
        await holds.SaveAsync(HoldOn("wfexec-held"));
        await inner.EnqueueAsync(NewWorkItem("wfexec-held", WorkflowExecutionCommandKind.StartActivity));
        Assert.NotNull(await inner.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wfexec-held", "owner-crashed", Now, leaseDuration)));
        var queue = new CountingWorkQueue(inner);
        var service = new RuntimeResumptionService(
            new FakeOutboxProcessor(),
            queue,
            new InMemoryRuntimeRecoveryScanner(liveness),
            runtime.GetRequiredService<IWorkflowExecutionActorProvider>(),
            new ShortRuntimeExecutionIdGenerator(),
            new FakeTimeProvider(later),
            runtime.GetRequiredService<IWorkflowExecutionStateStore>(),
            runtime.GetRequiredService<IWorkflowSchedulerPauseGate>(),
            runtime.GetRequiredService<RuntimeResumptionDiscoveryStateStore>());
        var request = new RuntimeResumptionSweepRequest(leaseTimeout: leaseDuration, heartbeatTimeout: leaseDuration);

        for (var sweep = 0; sweep < 5; sweep++)
            Assert.Empty((await service.SweepAsync(request)).Dispatches);
        Assert.Single(await inner.ListAllAsync("wfexec-held"));
        await holds.SaveAsync(new WorkflowHoldState(controlPlaneStateId: "control-wfexec-held", workflowExecutionId: "wfexec-held"));
        queue.ListedByWalk.Clear();
        var afterRelease = await service.SweepAsync(request);

        Assert.Contains("wfexec-held", queue.ListedByWalk);
        Assert.Equal("wfexec-held", Assert.Single(afterRelease.Dispatches).WorkflowExecutionId);
        // The drain now passes the open gate and dispatches the held item. A bare runtime has no executable for it, so the
        // dispatch faults (the outcome is not what this test is about), but the item no longer waits at the gate.
        Assert.DoesNotContain(await inner.ListAllAsync("wfexec-held"), item => item.WorkItemId == "work-1");
    }

    /// <summary>
    /// #2188 review: one pass reads at most ten backlog pages while passing held executions, and reads their next items
    /// with one request per page rather than one per execution. The walk carries on from there on the next pass.
    /// </summary>
    [Fact]
    public async Task SweepAsync_ReadsAtMostTenBacklogPagesWithOneNextItemReadEach()
    {
        var queue = new CountingWorkQueue();
        var harness = new Harness(workQueue: queue);
        for (var index = 0; index < 22; index++)
        {
            var workflowExecutionId = $"wfexec-{index:D2}";
            await harness.Holds.SaveAsync(HoldOn(workflowExecutionId));
            await queue.EnqueueAsync(NewWorkItem(workflowExecutionId, WorkflowExecutionCommandKind.StartActivity));
        }
        await queue.EnqueueAsync(NewWorkItem("wfexec-ready", WorkflowExecutionCommandKind.StartActivity));
        var request = new RuntimeResumptionSweepRequest(backlogBatchSize: 2);

        var first = await harness.Service.SweepAsync(request);
        var (claimablePages, nextItemReads, itemListings) = (queue.ClaimablePages, queue.NextItemReads, queue.ItemListings);
        var second = await harness.Service.SweepAsync(request);

        Assert.Empty(first.Dispatches);
        Assert.Equal((10, 10, 0), (claimablePages, nextItemReads, itemListings));
        Assert.Equal("wfexec-ready", Assert.Single(second.Dispatches).WorkflowExecutionId);
    }

    /// <summary>
    /// #2188 review: the pause gate is asked only about backlog the sweep can use. With half a cap of four taken by
    /// recovery candidates, two backlog executions are checked, not the whole page.
    /// </summary>
    [Fact]
    public async Task SweepAsync_ChecksOnlyTheBacklogItCanUse()
    {
        var gate = new RecordingPauseGate();
        var harness = new Harness(workQueue: await QueueWithBacklogAsync("wfexec-a", "wfexec-b", "wfexec-c", "wfexec-d"), pauseGate: gate);
        harness.RecoveryScanner.Candidates = [NewCandidate("wfexec-r1"), NewCandidate("wfexec-r2")];

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(maxExecutionsPerSweep: 4));

        Assert.Equal(["wfexec-a", "wfexec-b", "wfexec-r1", "wfexec-r2"], result.Dispatches.Select(dispatch => dispatch.WorkflowExecutionId));
        Assert.Equal(["wfexec-a", "wfexec-b"], gate.Evaluated);
    }

    /// <summary>
    /// #2188 review: every re-drive of a held execution left one more <c>RunSchedulerWork</c> row behind its head,
    /// because the drain stops at the closed gate before reaching it. Through the real runtime, the sweep without a
    /// pause check grows the queue on its first pass; the sweep with one leaves it as it is however often it runs.
    /// </summary>
    [Fact]
    public async Task SweepAsync_DoesNotGrowTheQueueOfAHeldExecutionAcrossSweeps()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var runtime = scope.ServiceProvider;
        var queue = runtime.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        await runtime.GetRequiredService<IWorkflowHoldStateStore>().SaveAsync(HoldOn("wfexec-held"));
        await queue.EnqueueAsync(NewWorkItem("wfexec-held", WorkflowExecutionCommandKind.StartActivity));
        RuntimeResumptionService Sweeper(IWorkflowSchedulerPauseGate pauseGate) => new(
            new FakeOutboxProcessor(),
            queue,
            new FakeRecoveryScanner(),
            runtime.GetRequiredService<IWorkflowExecutionActorProvider>(),
            new ShortRuntimeExecutionIdGenerator(),
            new FakeTimeProvider(Now),
            runtime.GetRequiredService<IWorkflowExecutionStateStore>(),
            pauseGate,
            new RuntimeResumptionDiscoveryStateStore());

        var checkingSweeper = Sweeper(runtime.GetRequiredService<IWorkflowSchedulerPauseGate>());
        for (var sweep = 0; sweep < 10; sweep++)
            Assert.Empty((await checkingSweeper.SweepAsync(new RuntimeResumptionSweepRequest())).Dispatches);
        Assert.Single(await queue.ListAllAsync("wfexec-held"));

        await Sweeper(new RecordingPauseGate()).SweepAsync(new RuntimeResumptionSweepRequest());
        Assert.Equal(2, (await queue.ListAllAsync("wfexec-held")).Count);
    }

    /// <summary>
    /// #2188 review: a held execution whose liveness lease expired is a recovery-scanner candidate on every scan, and
    /// re-driving it through the closed gate queued one more <c>RunSchedulerWork</c> row each time. Its head is under a
    /// live claim here, so only the scanner offers it. Skipping it costs ownership nothing: the next drain acquires a
    /// strictly greater fencing token whatever the stale lease says.
    /// </summary>
    [Fact]
    public async Task SweepAsync_DoesNotGrowTheQueueOfAHeldExecutionWhoseLeaseExpired()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var runtime = scope.ServiceProvider;
        var queue = runtime.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var liveness = runtime.GetRequiredService<IExecutionLivenessStateStore>();
        var leaseDuration = TimeSpan.FromMinutes(1);
        await new RuntimeExecutionOwnershipService(
                liveness,
                new FakeTimeProvider(Now),
                new RuntimeExecutionOwnershipOptions { OwnerId = "owner-crashed", LeaseDuration = leaseDuration })
            .AcquireAsync("wfexec-held");
        await runtime.GetRequiredService<IWorkflowHoldStateStore>().SaveAsync(HoldOn("wfexec-held"));
        await queue.EnqueueAsync(NewWorkItem("wfexec-held", WorkflowExecutionCommandKind.StartActivity));
        Assert.NotNull(await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wfexec-held", "owner-crashed", Now, TimeSpan.FromHours(1))));
        var request = new RuntimeResumptionSweepRequest(leaseTimeout: leaseDuration, heartbeatTimeout: leaseDuration);
        RuntimeResumptionService Sweeper(IWorkflowSchedulerPauseGate pauseGate) => new(
            new FakeOutboxProcessor(),
            queue,
            new InMemoryRuntimeRecoveryScanner(liveness),
            runtime.GetRequiredService<IWorkflowExecutionActorProvider>(),
            new ShortRuntimeExecutionIdGenerator(),
            new FakeTimeProvider(Now + leaseDuration + TimeSpan.FromSeconds(1)),
            runtime.GetRequiredService<IWorkflowExecutionStateStore>(),
            pauseGate,
            new RuntimeResumptionDiscoveryStateStore());

        var checkingSweeper = Sweeper(runtime.GetRequiredService<IWorkflowSchedulerPauseGate>());
        for (var sweep = 0; sweep < 10; sweep++)
            Assert.Empty((await checkingSweeper.SweepAsync(request)).Dispatches);
        Assert.Single(await queue.ListAllAsync("wfexec-held"));

        var withoutCheck = await Sweeper(new RecordingPauseGate()).SweepAsync(request);
        Assert.Equal("wfexec-held", Assert.Single(withoutCheck.Dispatches).WorkflowExecutionId);
        Assert.Equal(2, (await queue.ListAllAsync("wfexec-held")).Count);
    }

    /// <summary>
    /// A queue without claimable discovery keeps the earlier first-page listing, which can starve executions past that
    /// page. That must not pass silently: the sweep warns, once per host for the queue type rather than on every tick.
    /// </summary>
    [Fact]
    public async Task SweepAsync_WarnsOnceWhenTheQueueOnlyListsAFirstPage()
    {
        var harness = new Harness();

        await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());
        await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        var warning = Assert.Single(harness.Logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal("RuntimeResumptionFirstPageBacklogDiscovery", warning.EventId.Name);
        Assert.Contains(nameof(FakeWorkQueue), warning.Message);
    }

    [Fact]
    public async Task SweepAsync_DoesNotReuseARecoveryCursorAcrossDifferentExclusionSets()
    {
        var harness = new Harness();
        harness.RecoveryScanner.Pages.Enqueue(new RecoveryPage(
            [NewCandidate("wfexec-excluded")],
            "recovery-next-excluded"));
        harness.RecoveryScanner.Pages.Enqueue(new RecoveryPage(
            [NewCandidate("wfexec-next")],
            null));

        await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(
            recoveryScanBatchSize: 1,
            excludedWorkflowExecutionIds: new HashSet<string>(StringComparer.Ordinal) { "wfexec-excluded" }));
        var second = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(recoveryScanBatchSize: 1));

        Assert.Equal("wfexec-next", Assert.Single(second.Dispatches).WorkflowExecutionId);
        Assert.Null(harness.RecoveryScanner.Requests[1].ContinuationToken);
    }

    [Fact]
    public async Task SweepAsync_ExcludesRequestedExecutionIds()
    {
        var harness = new Harness();
        harness.WorkQueue.PendingExecutionIds = ["wfexec-a", "wfexec-b", "wfexec-c"];

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(
            excludedWorkflowExecutionIds: new HashSet<string>(StringComparer.Ordinal) { "wfexec-b" }));

        Assert.Equal(
            new[] { "wfexec-a", "wfexec-c" },
            result.Dispatches.Select(dispatch => dispatch.WorkflowExecutionId));
    }

    [Fact]
    public async Task SweepAsync_CapsExecutionsPerSweep()
    {
        var harness = new Harness();
        harness.WorkQueue.PendingExecutionIds = ["wfexec-a", "wfexec-b", "wfexec-c", "wfexec-d"];

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(maxExecutionsPerSweep: 2));

        // Deterministic ordinal order, capped to the first two.
        Assert.Equal(
            new[] { "wfexec-a", "wfexec-b" },
            result.Dispatches.Select(dispatch => dispatch.WorkflowExecutionId));
    }

    [Fact]
    public async Task SweepAsync_FaultedRedriveIsRecordedAndDoesNotAbortTheSweep()
    {
        var harness = new Harness();
        harness.WorkQueue.PendingExecutionIds = ["wfexec-1", "wfexec-2"];
        harness.AgentProvider.FailFor = "wfexec-1";

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Collection(
            result.Dispatches,
            first =>
            {
                Assert.Equal("wfexec-1", first.WorkflowExecutionId);
                Assert.Equal(RuntimeResumptionDispatchOutcome.Faulted, first.Outcome);
                Assert.Equal("activation failed", first.Failure);
                Assert.Null(first.EnvelopeId);
            },
            second =>
            {
                Assert.Equal("wfexec-2", second.WorkflowExecutionId);
                Assert.Equal(RuntimeResumptionDispatchOutcome.Accepted, second.Outcome);
            });
    }

    [Fact]
    public async Task SweepAsync_MapsDispatchStatusesToOutcomes()
    {
        var harness = new Harness();
        harness.WorkQueue.PendingExecutionIds = ["wfexec-1"];
        harness.AgentProvider.Agent.StatusToReturn = WorkflowExecutionCommandDispatchStatus.Duplicate;
        Assert.Equal(
            RuntimeResumptionDispatchOutcome.Duplicate,
            Assert.Single((await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest())).Dispatches).Outcome);

        harness.AgentProvider.Agent.StatusToReturn = WorkflowExecutionCommandDispatchStatus.Deferred;
        Assert.Equal(
            RuntimeResumptionDispatchOutcome.Deferred,
            Assert.Single((await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest())).Dispatches).Outcome);

        harness.AgentProvider.Agent.StatusToReturn = WorkflowExecutionCommandDispatchStatus.Rejected;
        var rejected = Assert.Single((await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest())).Dispatches);
        Assert.Equal(RuntimeResumptionDispatchOutcome.Rejected, rejected.Outcome);
        Assert.NotNull(rejected.Failure);
    }

    [Fact]
    public async Task SweepAsync_RejectsInvalidInput()
    {
        var harness = new Harness();

        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Service.SweepAsync(null!).AsTask());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeResumptionSweepRequest(outboxBatchSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeResumptionSweepRequest(backlogBatchSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeResumptionSweepRequest(recoveryScanBatchSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeResumptionSweepRequest(leaseTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeResumptionSweepRequest(heartbeatTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeResumptionSweepRequest(maxExecutionsPerSweep: 0));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(), cancelled.Token).AsTask());
    }

    [Fact]
    public async Task SweepAsync_DiscoversWindowCExecution_FromOwnershipLeaseLeftByCrash()
    {
        // Window C (RT-2 × W2): a drain acquired a single-writer lease and then crashed before releasing it
        // (the item had already been dequeue-deleted, so the durable backlog is empty). W5's lease population is
        // exactly what makes this interrupted execution visible: the real recovery scanner reads the persisted
        // lease from operational state, the sweep discovers it, and re-drives it through the agent mailbox.
        var operationalStore = new InMemoryExecutionLivenessStateStore();
        var leaseDuration = TimeSpan.FromMinutes(1);
        var ownership = new RuntimeExecutionOwnershipService(
            operationalStore,
            new FakeTimeProvider(Now),
            new RuntimeExecutionOwnershipOptions { OwnerId = "owner-under-test", LeaseDuration = leaseDuration });

        // Acquire but never release: this is the crash mid-drain.
        await ownership.AcquireAsync("wfexec-window-c");

        var outboxProcessor = new FakeOutboxProcessor();
        var workQueue = new FakeWorkQueue(); // empty backlog — the item was already dequeue-deleted.
        var recoveryScanner = new InMemoryRuntimeRecoveryScanner(operationalStore);
        var agentProvider = new FakeAgentProvider();
        var afterLeaseExpiry = Now + leaseDuration + TimeSpan.FromSeconds(1);
        var service = new RuntimeResumptionService(
            outboxProcessor,
            workQueue,
            recoveryScanner,
            agentProvider,
            new ShortRuntimeExecutionIdGenerator(),
            new FakeTimeProvider(afterLeaseExpiry),
            new InMemoryWorkflowExecutionStateStore(),
            OpenPauseGate(),
            new RuntimeResumptionDiscoveryStateStore());

        var result = await service.SweepAsync(new RuntimeResumptionSweepRequest(
            leaseTimeout: leaseDuration,
            heartbeatTimeout: leaseDuration));

        var dispatch = Assert.Single(result.Dispatches);
        Assert.Equal("wfexec-window-c", dispatch.WorkflowExecutionId);
        Assert.Equal(RuntimeResumptionDispatchOutcome.Accepted, dispatch.Outcome);

        var activation = Assert.Single(agentProvider.Activations);
        Assert.Equal(WorkflowExecutionActorActivationReason.Recovery, activation.Reason);

        var envelope = Assert.Single(agentProvider.Agent.Envelopes);
        Assert.Equal("wfexec-window-c", envelope.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, envelope.Command.Kind);
    }

    [Fact]
    public async Task SweepAsync_PurgesResidualWorkForTerminalExecutionInsteadOfRedriving()
    {
        // spec 113: the drainer's terminal-status guard strands a RunSchedulerWork item in the durable queue when a
        // workflow reaches a terminal status. Backlog discovery has no terminal filter, so the completed execution is
        // surfaced every sweep. Re-driving it would enqueue yet another stranded item and emit a fresh drain span each
        // tick — perpetual churn. The sweep must instead purge the residue and never re-drive a terminal execution.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(NewResidualWorkItem("wfexec-terminal", 1));
        await queue.EnqueueAsync(NewResidualWorkItem("wfexec-terminal", 2));
        var stateStore = new InMemoryWorkflowExecutionStateStore();
        await stateStore.SaveAsync(NewState("wfexec-terminal", WorkflowExecutionStatus.Completed));
        var agentProvider = new FakeAgentProvider();
        var service = new RuntimeResumptionService(
            new FakeOutboxProcessor(),
            queue,
            new FakeRecoveryScanner(),
            agentProvider,
            new ShortRuntimeExecutionIdGenerator(),
            new FakeTimeProvider(Now),
            stateStore,
            OpenPauseGate(),
            new RuntimeResumptionDiscoveryStateStore());

        var result = await service.SweepAsync(new RuntimeResumptionSweepRequest());

        // No re-drive: the terminal execution is never activated and no command envelope is sent.
        Assert.Empty(result.Dispatches);
        Assert.Empty(agentProvider.Activations);
        Assert.Empty(agentProvider.Agent.Envelopes);
        // The residual work is purged, so backlog discovery can no longer resurface the execution.
        Assert.Equal(1, result.TerminalExecutionsPurged);
        Assert.Equal(2, result.PurgedWorkItemCount);
        Assert.True(result.DidWork);
        Assert.Empty(await queue.ListPendingWorkflowExecutionIdsAsync(10));
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-terminal"))).Items);
    }

    [Fact]
    public async Task SweepAsync_StillRedrivesNonTerminalExecutionWithBacklog()
    {
        // Regression guard: a suspended/running execution with genuine backlog must still be re-driven (Window C
        // recovery). Only terminal executions are purged; non-terminal ones keep their redelivery.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(NewResidualWorkItem("wfexec-live", 1));
        var stateStore = new InMemoryWorkflowExecutionStateStore();
        await stateStore.SaveAsync(NewState("wfexec-live", WorkflowExecutionStatus.Suspended));
        var agentProvider = new FakeAgentProvider();
        var service = new RuntimeResumptionService(
            new FakeOutboxProcessor(),
            queue,
            new FakeRecoveryScanner(),
            agentProvider,
            new ShortRuntimeExecutionIdGenerator(),
            new FakeTimeProvider(Now),
            stateStore,
            OpenPauseGate(),
            new RuntimeResumptionDiscoveryStateStore());

        var result = await service.SweepAsync(new RuntimeResumptionSweepRequest());

        var dispatch = Assert.Single(result.Dispatches);
        Assert.Equal("wfexec-live", dispatch.WorkflowExecutionId);
        Assert.Equal(RuntimeResumptionDispatchOutcome.Accepted, dispatch.Outcome);
        Assert.Equal(0, result.TerminalExecutionsPurged);
        Assert.Equal(0, result.PurgedWorkItemCount);
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, Assert.Single(agentProvider.Agent.Envelopes).Command.Kind);
        // The non-terminal execution is re-driven, never reaped.
        Assert.Empty(agentProvider.Passivations);
    }

    [Fact]
    public async Task SweepAsync_ReapsLingeringTerminalMailbox_AfterPurgingResidualWork()
    {
        // #542 / spec 128 straggler reaper: a terminal execution whose mailbox outlived it (eager eviction disabled,
        // skipped, or the terminal status arrived from a post-commit intent rather than the dispatched command) is
        // collected by the sweep — it purges residual work AND passivates the mailbox through the agent provider.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(NewResidualWorkItem("wfexec-terminal", 1));
        var stateStore = new InMemoryWorkflowExecutionStateStore();
        await stateStore.SaveAsync(NewState("wfexec-terminal", WorkflowExecutionStatus.Completed));
        var agentProvider = new FakeAgentProvider();
        var service = new RuntimeResumptionService(
            new FakeOutboxProcessor(),
            queue,
            new FakeRecoveryScanner(),
            agentProvider,
            new ShortRuntimeExecutionIdGenerator(),
            new FakeTimeProvider(Now),
            stateStore,
            OpenPauseGate(),
            new RuntimeResumptionDiscoveryStateStore());

        var result = await service.SweepAsync(new RuntimeResumptionSweepRequest());

        // Never re-driven, but reaped: exactly one passivation for the terminal id.
        Assert.Empty(result.Dispatches);
        Assert.Empty(agentProvider.Activations);
        var passivation = Assert.Single(agentProvider.Passivations);
        Assert.Equal("wfexec-terminal", passivation.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionActorPassivationBoundary.ProviderSafeBoundary, passivation.Boundary);
        Assert.Equal(1, result.TerminalExecutionsPurged);
    }

    private static RuntimeSchedulerWorkItem NewResidualWorkItem(string workflowExecutionId, int index) =>
        new(
            workItemId: $"work-{index}",
            workflowExecutionId: workflowExecutionId,
            commandId: $"command-{index}",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            envelopeId: $"envelope-{index}",
            idempotencyKey: $"{workflowExecutionId}:command-{index}",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: index);

    private static RuntimeSchedulerWorkItem NewWorkItem(string workflowExecutionId, WorkflowExecutionCommandKind kind) =>
        new(
            workItemId: "work-1",
            workflowExecutionId: workflowExecutionId,
            commandId: "command-1",
            commandKind: kind,
            envelopeId: "envelope-1",
            idempotencyKey: $"{workflowExecutionId}:command-1",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: 1);

    private static WorkflowHoldState HoldOn(string workflowExecutionId) =>
        new(
            controlPlaneStateId: $"control-{workflowExecutionId}",
            workflowExecutionId: workflowExecutionId,
            activeHolds: [WorkflowHold.ForWorkflowExecution($"pause-{workflowExecutionId}", workflowExecutionId, Now, "operator", "Paused for maintenance.")]);

    private static WorkflowSchedulerPauseGate PauseGateOver(IWorkflowHoldStateStore holds) =>
        new(new RuntimePauseDecisionProvider(holds), new FakeTimeProvider(Now));

    private static WorkflowSchedulerPauseGate OpenPauseGate() => PauseGateOver(new InMemoryWorkflowHoldStateStore());

    private static async Task<InMemoryWorkflowSchedulerWorkQueue> QueueWithBacklogAsync(params string[] workflowExecutionIds)
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        foreach (var workflowExecutionId in workflowExecutionIds)
            await queue.EnqueueAsync(NewResidualWorkItem(workflowExecutionId, 1));
        return queue;
    }

    private static WorkflowExecutionState NewState(string workflowExecutionId, WorkflowExecutionStatus status) =>
        new(
            WorkflowExecutionId: workflowExecutionId,
            PinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            Status: status,
            SubStatus: null,
            CreatedAt: Now,
            StartedAt: Now,
            UpdatedAt: Now,
            CompletedAt: status.IsTerminal() ? Now : null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());

    private static RuntimeRecoveryCandidate NewCandidate(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            operationalStateId: null,
            lastCheckpointId: "checkpoint-1",
            reason: RuntimeInterruptionReason.HostStopped,
            detectedAt: Now,
            requeueFromLastCheckpoint: true);

    private static RuntimePostCommitIntent NewMarkerIntent() =>
        new(
            intentId: "intent-marker-1",
            workflowExecutionId: "wfexec-marker-1",
            kind: MarkerKind,
            recordedAt: Now,
            activityExecutionId: "actexec-marker-1",
            idempotencyKey: "marker-1",
            payload: null,
            metadata: new Dictionary<string, string>());

    private sealed class Marker
    {
        public List<RuntimePostCommitIntent> Intents { get; } = [];
    }

    private sealed class MarkerHandler(Marker marker) : IRuntimePostCommitIntentHandler
    {
        public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        {
            marker.Intents.Add(intent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public Harness(
            IRuntimeRecoveryCandidateSource? candidateSource = null,
            IWorkflowSchedulerWorkQueue? workQueue = null,
            IWorkflowSchedulerPauseGate? pauseGate = null)
        {
            Service = new RuntimeResumptionService(
                OutboxProcessor,
                workQueue ?? WorkQueue,
                RecoveryScanner,
                AgentProvider,
                new ShortRuntimeExecutionIdGenerator(),
                new FakeTimeProvider(Now),
                StateStore,
                pauseGate ?? PauseGateOver(Holds),
                DiscoveryStates,
                recoveryCandidateSources: candidateSource is null ? null : [candidateSource],
                logger: Logger);
        }

        public InMemoryWorkflowHoldStateStore Holds { get; } = new();
        public RuntimeResumptionDiscoveryStateStore DiscoveryStates { get; } = new();
        public RecordingLogger<RuntimeResumptionService> Logger { get; } = new();
        public FakeOutboxProcessor OutboxProcessor { get; } = new();
        public FakeWorkQueue WorkQueue { get; } = new();
        public FakeRecoveryScanner RecoveryScanner { get; } = new();
        public FakeAgentProvider AgentProvider { get; } = new();
        public InMemoryWorkflowExecutionStateStore StateStore { get; } = new();
        public RuntimeResumptionService Service { get; }
    }

    /// <summary>A candidate source like the distributed runtime's reclaim registry: it lists until settled.</summary>
    private sealed class FakeCandidateSource(params RuntimeRecoveryCandidate[] candidates) : IRuntimeRecoveryCandidateSource
    {
        private readonly List<RuntimeRecoveryCandidate> _listed = [.. candidates];

        public List<string> Settled { get; } = [];

        public IReadOnlyList<string> Listed => _listed.Select(candidate => candidate.WorkflowExecutionId).ToArray();

        public ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ListAsync(int limit, CancellationToken cancellationToken = default) =>
            new(_listed.Take(limit).ToArray());

        public ValueTask SettleAsync(IReadOnlyCollection<string> workflowExecutionIds, CancellationToken cancellationToken = default)
        {
            Settled.AddRange(workflowExecutionIds);
            _listed.RemoveAll(candidate => workflowExecutionIds.Contains(candidate.WorkflowExecutionId));
            return default;
        }
    }

    private sealed class FakeOutboxProcessor : IRuntimePostCommitOutboxProcessor
    {
        public List<RuntimePostCommitOutboxProcessRequest> Requests { get; } = [];
        public RuntimePostCommitOutboxProcessResult Result { get; set; } = new([]);

        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(RuntimePostCommitOutboxProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return new(Result);
        }
    }

    private sealed class FailingPauseGate : IWorkflowSchedulerPauseGate
    {
        public bool Fails { get; set; } = true;

        public ValueTask<SchedulerPauseDecision?> EvaluateAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            Fails ? throw new InvalidOperationException("The hold store is unavailable.") : new((SchedulerPauseDecision?)null);
    }

    // Lets every item through, as if nothing were held, and records what it was asked about.
    private sealed class RecordingPauseGate : IWorkflowSchedulerPauseGate
    {
        public List<string> Evaluated { get; } = [];

        public ValueTask<SchedulerPauseDecision?> EvaluateAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            Evaluated.Add(workItem.WorkflowExecutionId);
            return new((SchedulerPauseDecision?)null);
        }
    }

    // The in-memory queue, counting the reads a sweep makes.
    private sealed class CountingWorkQueue(IWorkflowSchedulerWorkQueue? inner = null) : IWorkflowSchedulerWorkQueue
    {
        private readonly IWorkflowSchedulerWorkQueue _inner = inner ?? new InMemoryWorkflowSchedulerWorkQueue();

        public int ClaimablePages { get; private set; }
        public int NextItemReads { get; private set; }
        public int ItemListings { get; private set; }
        public List<string> ListedByWalk { get; } = [];

        // Next-item reads for which this returns true throw, as an unreadable row would.
        public Func<IReadOnlyCollection<string>, bool> FailNextItemReadWhen { get; init; } = _ => false;

        public bool SupportsClaimableBacklogDiscovery => true;

        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            _inner.EnqueueAsync(workItem, cancellationToken);

        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default)
        {
            ItemListings++;
            return _inner.ListAsync(query, cancellationToken);
        }

        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            _inner.DequeueAsync(workflowExecutionId, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) =>
            _inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);

        public async ValueTask<IReadOnlyCollection<string>> ListClaimableWorkflowExecutionIdsAsync(
            RuntimeSchedulerClaimableBacklogQuery query,
            CancellationToken cancellationToken = default)
        {
            ClaimablePages++;
            var listed = await _inner.ListClaimableWorkflowExecutionIdsAsync(query, cancellationToken);
            ListedByWalk.AddRange(listed);
            return listed;
        }

        public ValueTask<IReadOnlyDictionary<string, RuntimeSchedulerWorkItem>> ListNextWorkItemsAsync(
            IReadOnlyCollection<string> workflowExecutionIds,
            CancellationToken cancellationToken = default)
        {
            NextItemReads++;
            if (FailNextItemReadWhen(workflowExecutionIds))
                throw new InvalidDataException("The scheduler-work row is unreadable.");
            return _inner.ListNextWorkItemsAsync(workflowExecutionIds, cancellationToken);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception)));
    }

    private sealed class FakeWorkQueue : IWorkflowSchedulerWorkQueue
    {
        public IReadOnlyCollection<string> PendingExecutionIds { get; set; } = [];
        public List<int> BacklogLimits { get; } = [];

        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) =>
            new(new RuntimeStorePage<RuntimeSchedulerWorkItem>(query, []));

        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            new((RuntimeSchedulerWorkItem?)null);

        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default)
        {
            BacklogLimits.Add(limit);
            return new(PendingExecutionIds);
        }
    }

    private sealed class FakeRecoveryScanner : IRuntimeRecoveryPagedScanner
    {
        public IReadOnlyCollection<RuntimeRecoveryCandidate> Candidates { get; set; } = [];
        public List<RuntimeRecoveryScanRequest> Requests { get; } = [];
        public Queue<RecoveryPage> Pages { get; } = new();

        public ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ScanAsync(RuntimeRecoveryScanRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return new(Candidates);
        }

        public ValueTask<RuntimeRecoveryPage> ScanPageAsync(RuntimeRecoveryScanRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var page = Pages.Count > 0
                ? Pages.Dequeue()
                : new RecoveryPage(Candidates, null);
            return new(new RuntimeRecoveryPage(request, page.Items.Take(request.Limit).ToArray(), page.NextContinuationToken));
        }
    }

    private sealed record RecoveryPage(
        IReadOnlyCollection<RuntimeRecoveryCandidate> Items,
        string? NextContinuationToken);

    private sealed class LegacyRecoveryScanner(IReadOnlyCollection<RuntimeRecoveryCandidate> candidates) : IRuntimeRecoveryScanner
    {
        public List<RuntimeRecoveryScanRequest> Requests { get; } = [];

        public ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ScanAsync(
            RuntimeRecoveryScanRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return new(candidates);
        }
    }

    // Deliberately implements only the legacy liveness store surface. The scanner must not advertise a resumable
    // page when this custom store cannot provide due-ordered recovery reads; ordinary resumption still uses its
    // historical collection path.
    private sealed class LegacyLivenessStore : IExecutionLivenessStateStore
    {
        private readonly InMemoryExecutionLivenessStateStore inner = new();

        public LegacyLivenessStore(ExecutionLivenessState state) => inner.SaveAsync(state).GetAwaiter().GetResult();

        public ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(state, cancellationToken);

        public ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(ExecutionLivenessState state, long expectedRevision, CancellationToken cancellationToken = default) =>
            inner.TrySaveAsync(state, expectedRevision, cancellationToken);

        public ValueTask<ExecutionLivenessState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, operationalStateId, cancellationToken);

        public ValueTask<VersionedExecutionLivenessState?> FindVersionedAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default) =>
            inner.FindVersionedAsync(workflowExecutionId, operationalStateId, cancellationToken);

        public ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListAllPageAsync(RuntimeStorePageRequest query, CancellationToken cancellationToken = default) =>
            inner.ListAllPageAsync(query, cancellationToken);
    }

    private sealed class FakeAgentProvider : IWorkflowExecutionActorProvider
    {
        public List<WorkflowExecutionActorActivationRequest> Activations { get; } = [];
        public List<WorkflowExecutionActorPassivationRequest> Passivations { get; } = [];
        public FakeAgent Agent { get; } = new();
        public string? FailFor { get; set; }

        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.InProcessMailbox;

        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)
        {
            if (string.Equals(request.WorkflowExecutionId, FailFor, StringComparison.Ordinal))
                throw new InvalidOperationException("activation failed");

            Activations.Add(request);
            return new(Agent);
        }

        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default)
        {
            Passivations.Add(request);
            return default;
        }
    }

    private sealed class FakeAgent : IWorkflowExecutionActor
    {
        public List<WorkflowExecutionCommandEnvelope> Envelopes { get; } = [];
        public WorkflowExecutionCommandDispatchStatus StatusToReturn { get; set; } = WorkflowExecutionCommandDispatchStatus.Accepted;

        public WorkflowExecutionActorDescriptor Descriptor { get; } = new(
            workflowExecutionId: "wfexec-agent",
            agentId: "agent-1",
            providerName: "test",
            status: WorkflowExecutionActorStatus.Active,
            capabilities: WorkflowExecutionActorCapabilities.InProcessMailbox,
            activatedAt: Now);

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return new(new WorkflowExecutionCommandDispatchResult(
                envelope.EnvelopeId,
                envelope.WorkflowExecutionId,
                StatusToReturn,
                Now,
                reason: StatusToReturn is WorkflowExecutionCommandDispatchStatus.Rejected or WorkflowExecutionCommandDispatchStatus.Deferred
                    ? "set by test"
                    : null));
        }
    }
}
