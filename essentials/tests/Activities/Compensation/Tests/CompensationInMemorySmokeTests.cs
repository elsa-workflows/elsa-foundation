using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// Spec 261 Q7: compensation on the runtime's in-memory stores, which are for development and tests only. J1, N1 and
/// O2h run on one host generation, without a restart: the in-memory stores make no restart guarantee, and a second
/// generation is refused rather than silently starting from nothing.
/// </summary>
public sealed class CompensationInMemorySmokeTests
{
    private const string RunId = CompensationHost.WorkflowExecutionId;

    public static TheoryData<PersistenceMode> Modes() => new(Enum.GetValues<PersistenceMode>());

    /// <summary>J1: the journal of a waiting scope is served, then a completed scope keeps none.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task J1_the_journal_of_a_waiting_scope_is_served_and_a_completed_scope_keeps_none(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode, CompensationStore.InMemory());
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 3);
        await using var harness = await host.StartHostAsync(participant);
        await CompensationHost.StartAsync(harness, ReservationJourney());
        await harness.SweepUntilQuietAsync();
        var waiting = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(RunId));

        var run = await harness.ReadRunAsync(RunId);
        var incidents = await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(RunId);
        var projector = harness.Services.GetServices<IActivityStateViewProjector>().Single();
        var view = await projector.ProjectAsync(new ActivityStateViewContext(
            run.WorkflowState!, run.State(ScopeNodeId), WorkflowExecutionHarness.NewExecutable(ReservationJourney()).RootActivity, incidents, false));
        Assert.Equal("AwaitingIntervention", view.GetProperty("phase").GetString());
        Assert.Equal(waiting.IncidentId, view.GetProperty("entries")[1].GetProperty("waiting").GetProperty("incidentId").GetString());

        await OperatorActions.RetryAsync(harness, RunId, waiting.ActivityExecutionId!, "smoke-j1-retry", CompensationHost.DefaultPartition);

        var completed = await harness.ReadRunAsync(RunId);
        completed.AssertWorkflowCompleted();
        completed.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Null(completed.State(ScopeNodeId).PrivateState);
        Assert.Empty(participant.Present);
    }

    /// <summary>N1: a completed inner scope's entries are undone by the outer scope, newest first.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task N1_the_outer_scope_undoes_a_completed_inner_scope_s_steps_newest_first(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode, CompensationStore.InMemory());
        var participant = new Participant().Rejecting("charge");
        var root = Scope("node-outer", faultAfterCompensation: false,
            new Step("Reserve", Scope("node-inner", faultAfterCompensation: false,
                new Step("Reserve A", Participant("reserve-a"), Participant("release-a")),
                new Step("Reserve B", Participant("reserve-b"), Participant("release-b")))),
            new Step("Charge", Participant("charge")));

        var run = await host.RunAsync(participant, root);

        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-inner", ActivityOutcomes.Done);
        run.AssertOutcomes("node-outer", CompensationScope.CompensatedOutcome);
        Assert.Equal(["reserve-a", "reserve-b", "release-b", "release-a"], participant.EffectOrder);
        Assert.Empty(participant.Present);
    }

    /// <summary>O2h: a failed handler resolved as effect absent: recovery continues with the next handler.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O2h_a_failed_handler_resolved_effect_absent_lets_recovery_continue(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode, CompensationStore.InMemory());
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 3);
        await using var harness = await host.StartHostAsync(participant);
        await CompensationHost.StartAsync(harness, ReservationJourney());
        await harness.SweepUntilQuietAsync();
        var waiting = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(RunId));

        var job = await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, CompensationScope.Decisions.ResolveEffectAbsent,
            "smoke-o2h-resolve", CompensationHost.DefaultPartition);

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await harness.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Contains("release-inventory", participant.EffectOrder);
        Assert.Single(participant.Present);
        Assert.Equal(3, participant.Calls.Count(call => call.NodeId == "cancel-delivery"));
    }

    /// <summary>The in-memory stores make no restart claim: a second host generation is refused.</summary>
    [Fact]
    public async Task A_second_host_generation_on_in_memory_stores_is_refused()
    {
        using var host = new CompensationHost(PersistenceMode.Durable, CompensationStore.InMemory());
        await using (await host.StartHostAsync(new Participant()))
        {
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartHostAsync(new Participant()));
    }
}
