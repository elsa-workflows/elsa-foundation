using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Activities.Testing;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// In-process coverage of the compensation scope's transitions (spec 250, contracts/semantics.md) through the real
/// workflow agent on in-memory stores.
/// </summary>
public sealed class CompensationScopeRuntimeTests
{
    [Fact]
    public async Task All_steps_complete_so_the_scope_finishes_Done_and_runs_no_handler()
    {
        var participant = new Participant();
        await using var harness = NewHarness(participant);

        var run = await harness.RunAsync(Executable(ReservationJourney()));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, ActivityOutcomes.Done);
        Assert.Equal(["reserve-inventory", "reserve-delivery", "confirm-order"], participant.EffectOrder);
        run.AssertDidNotRun("release-inventory", "cancel-delivery");
    }

    [Fact]
    public async Task A_faulted_step_undoes_the_completed_steps_newest_first_and_the_scope_finishes_Compensated()
    {
        var participant = new Participant().Rejecting("confirm-order");
        await using var harness = NewHarness(participant);

        var run = await harness.RunAsync(Executable(ReservationJourney()));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(["reserve-inventory", "reserve-delivery", "cancel-delivery", "release-inventory"], participant.EffectOrder);
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("confirm-order").Status);

        Assert.Equal("e2", run.State("cancel-delivery").Provenance.Metadata[CompensationScope.MetadataKeys.Entry]);
        Assert.Equal("e1", run.State("release-inventory").Provenance.Metadata[CompensationScope.MetadataKeys.Entry]);

        // The step's fault was absorbed by the scope, so its incident is resolved rather than left blocking.
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(harness.ExecutionId));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(run.State(ScopeNodeId).Execution.ActivityExecutionId, incident.Metadata[RuntimeMetadataKeys.FaultAbsorbedBy]);
    }

    [Fact]
    public async Task A_handler_is_scheduled_from_its_forward_occurrence_and_labelled_as_compensation()
    {
        var participant = new Participant().Rejecting("confirm-order");
        await using var harness = NewHarness(participant);

        var run = await harness.RunAsync(Executable(ReservationJourney()));

        var forward = run.State("reserve-delivery");
        var handler = run.State("cancel-delivery");
        var scope = run.State(ScopeNodeId);
        Assert.Equal(forward.Execution.ActivityExecutionId, handler.SchedulingActivityExecutionId);
        Assert.Equal(scope.Execution.ActivityExecutionId, handler.ParentActivityExecutionId);
        Assert.Equal(CompensationScope.HandlerSchedulingCause, handler.Provenance.SchedulingCause);
        Assert.Equal(CompensationScope.MetadataKeys.HandlerRole, handler.Provenance.Metadata[CompensationScope.MetadataKeys.Role]);
        Assert.Equal("Reserve delivery", handler.Provenance.Metadata[CompensationScope.MetadataKeys.Step]);
        Assert.Equal(forward.Execution.ActivityExecutionId, handler.Provenance.Metadata[CompensationScope.MetadataKeys.Forward]);
    }

    [Fact]
    public async Task The_faulted_step_s_own_handler_does_not_run_because_it_never_completed()
    {
        var participant = new Participant().Rejecting("confirm-order");
        await using var harness = NewHarness(participant);
        var scope = Scope(
            new Step("Reserve inventory", Participant("reserve-inventory"), Participant("release-inventory")),
            new Step("Confirm order", Participant("confirm-order"), Participant("undo-confirm")));

        var run = await harness.RunAsync(Executable(scope));

        run.AssertWorkflowCompleted();
        run.AssertDidNotRun("undo-confirm");
        Assert.Equal(["reserve-inventory", "release-inventory"], participant.EffectOrder);
    }

    [Fact]
    public async Task A_first_step_fault_has_nothing_to_undo_and_still_finishes_Compensated()
    {
        var participant = new Participant().Rejecting("reserve-inventory");
        await using var harness = NewHarness(participant);

        var run = await harness.RunAsync(Executable(ReservationJourney()));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Empty(participant.EffectOrder);
        run.AssertDidNotRun("reserve-delivery", "confirm-order");
    }

    [Fact]
    public async Task FaultAfterCompensation_faults_the_scope_after_its_handlers_ran()
    {
        var participant = new Participant().Rejecting("confirm-order");
        await using var harness = NewHarness(participant);

        var run = await harness.RunAsync(Executable(ReservationJourney(faultAfterCompensation: true)), allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);

        Assert.Equal(WorkflowExecutionStatus.Faulted, run.WorkflowState?.Status);
        Assert.Equal(CompensationScope.CompensatedFaultCode, run.State(ScopeNodeId).Fault?.Code);
        Assert.Equal(["reserve-inventory", "reserve-delivery", "cancel-delivery", "release-inventory"], participant.EffectOrder);
    }

    [Fact]
    public async Task FaultAfterCompensation_with_nothing_to_undo_faults_without_absorbing()
    {
        var participant = new Participant().Rejecting("reserve-inventory");
        await using var harness = NewHarness(participant);

        var run = await harness.RunAsync(Executable(ReservationJourney(faultAfterCompensation: true)), allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);

        Assert.Equal(WorkflowExecutionStatus.Faulted, run.WorkflowState?.Status);
        Assert.Equal(CompensationScope.CompensatedFaultCode, run.State(ScopeNodeId).Fault?.Code);
        Assert.Empty(participant.EffectOrder);
    }

    [Fact]
    public async Task A_handler_fault_stays_a_blocking_incident_and_its_entry_is_never_reported_compensated()
    {
        var participant = new Participant().Rejecting("confirm-order", "cancel-delivery");
        await using var harness = NewHarness(participant);

        var run = await harness.RunAsync(Executable(ReservationJourney()), allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);

        // The default incident strategy faults the run; the remaining handler does not run.
        Assert.Equal(WorkflowExecutionStatus.Faulted, run.WorkflowState?.Status);
        run.AssertDidNotRun("release-inventory");

        var state = ScopeState(run);
        Assert.Equal(CompensationScopePhase.Compensating, state.Phase);
        Assert.Equal(CompensationEntryStatus.Registered, state.Entries[0].Status);
        Assert.Equal(CompensationEntryStatus.Compensating, state.Entries[1].Status);
    }

    [Fact]
    public async Task An_empty_scope_finishes_Done()
    {
        await using var harness = NewHarness(new Participant());

        var run = await harness.RunAsync(Executable(Scope()));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, ActivityOutcomes.Done);
    }

    private static CompensationScopeState ScopeState(WorkflowExecutionRun run) =>
        CompensationScopeStatePersister.Load(run.State(ScopeNodeId)) ?? throw new InvalidOperationException("The scope has no state.");

    private static WorkflowExecutionHarness NewHarness(Participant participant) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesCompensationFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(participant);
                HostKillSwitch.Install(services, new HostKillSwitch());
                services.Replace(ServiceDescriptor.Singleton<IRuntimeExecutionIdGenerator>(_ => new ShortRuntimeExecutionIdGenerator(TimeProvider.System)));
            })
            .Build();
}
