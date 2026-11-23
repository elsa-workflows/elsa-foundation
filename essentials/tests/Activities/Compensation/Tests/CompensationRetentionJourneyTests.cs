using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// Spec 261 FR-519, retention R1–R3, on the SQLite store in every persistence mode: deleting a compensated run removes
/// its activity executions and incidents, and so its operator history; the provider-fenced deletion refuses a run that
/// waits for intervention and deletes nothing; and a pending delayed retry keeps the run's pinned executable from the
/// reference collector while an unpinned one is collected in the same sweep. R4, the Studio test-run window, is settled
/// in research R10 by reading the code: the window's end does not cancel a plain test run, so its waiting scope stays
/// resumable. The API's own deletion behaviour (it stops a running run first) is proven by
/// <c>WorkflowInstanceDeletionIntegrationTests</c>.
/// </summary>
public sealed class CompensationRetentionJourneyTests
{
    private const string RunId = CompensationHost.WorkflowExecutionId;

    public static TheoryData<PersistenceMode> Modes() => new(Enum.GetValues<PersistenceMode>());

    /// <summary>R1: a compensated run with an operator's history is deleted with its activity executions and incidents.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task R1_deleting_a_compensated_run_removes_its_activity_executions_and_incidents(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 3);
        await using var harness = await host.StartHostAsync(participant);
        await CompensationHost.StartAsync(harness, ReservationJourney());
        await harness.SweepUntilQuietAsync();
        var waiting = Assert.Single(await Incidents(harness).ListBlockingAsync(RunId));
        await OperatorActions.RetryAsync(harness, RunId, waiting.ActivityExecutionId!, "r1-retry", CompensationHost.DefaultPartition);
        var run = await harness.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.NotEmpty(await Incidents(harness).ListAsync(RunId));

        var deletion = harness.Services.GetRequiredService<IWorkflowExecutionDeletionStore>();
        Assert.True(deletion.IsSupported);
        var candidate = await deletion.ReadAsync(RunId);
        Assert.NotNull(candidate);
        Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await deletion.TryDeleteTerminalAsync(candidate));

        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(RunId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(RunId));
        Assert.Empty(await Incidents(harness).ListAsync(RunId));
    }

    /// <summary>R2: a run that waits for intervention is not terminal, so the deletion store refuses it and deletes nothing.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task R2_a_run_that_waits_for_intervention_is_refused_and_nothing_is_deleted(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        await using var harness = await host.StartHostAsync(new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 3));
        await CompensationHost.StartAsync(harness, ReservationJourney());
        await harness.SweepUntilQuietAsync();
        var before = await harness.ReadRunAsync(RunId);
        Assert.Equal(WorkflowExecutionStatus.Running, before.WorkflowState!.Status);
        var incidents = await Incidents(harness).ListAsync(RunId);

        var deletion = harness.Services.GetRequiredService<IWorkflowExecutionDeletionStore>();
        var candidate = await deletion.ReadAsync(RunId);
        Assert.NotNull(candidate);
        Assert.Equal(WorkflowExecutionDeletionResult.NotTerminal, await deletion.TryDeleteTerminalAsync(candidate));

        var after = await harness.ReadRunAsync(RunId);
        Assert.Equal(WorkflowExecutionStatus.Running, after.WorkflowState!.Status);
        Assert.Equal(before.ActivityStates.Count, after.ActivityStates.Count);
        Assert.Equal(incidents.Count, (await Incidents(harness).ListAsync(RunId)).Count);
        Assert.Single(await Incidents(harness).ListBlockingAsync(RunId));
    }

    /// <summary>
    /// R3: while a retry waits for its due time, its publication is retired and the reference collector sweeps a day
    /// later. The run's pinned executable is kept (the run is a retention root) while an unpinned, retired one is
    /// collected in the same sweep; the retry then runs the pinned handler.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task R3_a_pending_delayed_retry_keeps_its_executable_from_the_collector(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 1);
        var delay = TimeSpan.FromHours(2);
        // The collector logs what it keeps and removes, as the host's pump does.
        await using var harness = await host.StartHostAsync(participant, services => services.AddLogging());
        var root = Scope(ScopeNodeId, faultAfterCompensation: false, handlerMaxAttempts: 3, confirmOnComplete: false,
            new Backoff(delay, 2, TimeSpan.FromHours(4)),
            new Step("Reserve inventory", Participant("reserve-inventory"), Participant("release-inventory")),
            new Step("Reserve delivery", Participant("reserve-delivery"), Participant("cancel-delivery")),
            new Step("Confirm order", Participant("confirm-order")));
        var pinned = await harness.PublishAsync(Executable(root), "source-retention-pinned");
        var unpinnedIdentity = new WorkflowExecutableIdentity("artifact-unpinned", "definition-unpinned", "version-unpinned", "1.0.0", "sha256:unpinned");
        var unpinned = await harness.PublishAsync(WorkflowExecutionHarness.NewExecutable(ReservationJourney(), unpinnedIdentity), "source-retention-unpinned");
        await harness.StartPublishedAsync(pinned, RunId, partition: CompensationHost.DefaultPartition);
        await harness.SweepUntilQuietAsync();
        Assert.Equal(1, participant.Calls.Count(call => call.NodeId == "cancel-delivery"));

        await harness.RetirePublicationAsync(pinned, "republished");
        await harness.RetirePublicationAsync(unpinned, "republished");
        await using (var scope = harness.Services.CreateAsyncScope())
        {
            var sweep = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>()
                .SweepAsync(host.Now + TimeSpan.FromDays(1));
            Assert.Equal(1, sweep.DeletedArtifactCount);
        }

        var executables = harness.Services.GetRequiredService<IWorkflowExecutableStore>();
        Assert.NotNull(await executables.FindAsync(pinned.ArtifactId));
        Assert.Null(await executables.FindAsync(unpinned.ArtifactId));

        host.Advance(delay);
        await harness.SweepUntilQuietAsync();

        var run = await harness.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(2, participant.Calls.Count(call => call.NodeId == "cancel-delivery"));
        Assert.Empty(participant.Present);
    }

    /// <summary>
    /// R4 (research R10): a Studio test run whose scope waits for intervention outlives its 30-minute window. The window's
    /// end closes the test scope as expired and stops the run's test-run target, as the runtime's expiry path does; a
    /// plain test run has no target, so nothing cancels its root. The run keeps waiting, its executable stays pinned,
    /// and an operator retry after the window finishes the recovery.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task R4_a_test_run_that_waits_past_its_window_stays_resumable(PersistenceMode mode)
    {
        const string testRunId = "testrun-compensation-window";
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 3);
        await using var harness = await host.StartHostAsync(participant, services => services.AddLogging());
        // Publishing saves the pinned executable; the test run starts from a TestRun-scoped reference of its own.
        var published = await harness.PublishAsync(Executable(ReservationJourney()), "source-published");
        var executable = (await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(published.ArtifactId))!;
        var window = TimeSpan.FromMinutes(30);
        var expiresAt = host.Now + window;
        await harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(new WorkflowExecutableSourceReference(
            "source-test-run", executable.Identity.ArtifactId, "WorkflowDraftSnapshot", "draft-1", "draft",
            executable.Identity.DefinitionId, executable.Identity.DefinitionVersionId, executable.Identity.ArtifactVersion,
            host.Now, null, WorkflowExecutableReferenceScope.TestRun, expiresAt, null, null, null, null, null, null));
        var testScope = new WorkflowTestScope(testRunId, expiresAt, null, CompensationHost.DefaultPartition);
        await harness.Services.GetRequiredService<IWorkflowTestScopeStore>().CreateAsync(testScope, host.Now);
        await using (var scope = harness.Services.CreateAsyncScope())
        {
            var authority = new WorkflowExecutionAuthoritySnapshot(systemIdentity: OperatorActions.HarnessAuthority, rootInitiator: OperatorActions.HarnessAuthority);
            var start = await scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(new WorkflowExecutionStartDispatchRequest(
                executable.Identity.ArtifactId, OperatorActions.HarnessAuthority, RunId, $"start:{RunId}", null, null, null, null, null,
                WorkflowRunKind.TestRun, new WorkflowExecutableSourceSelection("source-test-run"), WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                null, null, null, CompensationHost.DefaultPartition, authority, null, 0, testScope), WorkflowExecutableReferenceScope.TestRun);
            Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, start.CommandDispatch.Status);
        }
        await harness.SweepUntilQuietAsync();
        var waiting = Assert.Single(await Incidents(harness).ListBlockingAsync(RunId));
        Assert.Equal(WorkflowRunKind.TestRun, (await harness.ReadRunAsync(RunId)).WorkflowState!.RunKind);

        // The window ends: the scope is closed as expired and the run's test-run target stopped, as at expiry.
        host.Advance(window + TimeSpan.FromMinutes(1));
        var close = await harness.Services.GetRequiredService<IWorkflowTestScopeStore>().CloseAsync(
            new WorkflowTestScopeCloseRequest(testRunId, WorkflowTestScopeCloseReason.Expired, host.Now));
        Assert.NotEqual(WorkflowTestScopeCloseDisposition.NotFound, close.Disposition);
        await harness.Services.GetRequiredService<ITestRunTargetCleaner>().StopAsync(testRunId);
        await harness.SweepUntilQuietAsync();

        var expired = await harness.ReadRunAsync(RunId);
        Assert.Equal(WorkflowExecutionStatus.Running, expired.WorkflowState!.Status);
        Assert.NotNull(await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(executable.Identity.ArtifactId));

        await OperatorActions.RetryAsync(harness, RunId, waiting.ActivityExecutionId!, "r4-retry", CompensationHost.DefaultPartition);

        var run = await harness.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Empty(participant.Present);
    }

    private static IIncidentStateStore Incidents(WorkflowExecutionHarness harness) => harness.Services.GetRequiredService<IIncidentStateStore>();
}
