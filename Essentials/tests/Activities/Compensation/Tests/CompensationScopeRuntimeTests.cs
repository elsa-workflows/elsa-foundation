using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Activities.Testing;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
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
    public async Task A_handler_that_fails_on_every_attempt_waits_for_intervention_and_the_run_keeps_running()
    {
        var participant = new Participant().Rejecting("confirm-order", "cancel-delivery");
        await using var harness = NewHarness(participant);

        var run = await harness.RunAsync(Executable(ReservationJourney()), allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);

        // Spec 251 J4: three attempts, then the last attempt's incident waits for an operator; nothing is compensated.
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
        Assert.Equal(3, run.States("cancel-delivery").Count);
        run.AssertDidNotRun("release-inventory");

        var state = ScopeState(run);
        Assert.Equal(CompensationScopePhase.AwaitingIntervention, state.Phase);
        Assert.Equal("Confirm order", state.Cause!.Step);
        Assert.Equal(CompensationEntryStatus.Registered, state.Entries[0].Status);
        var failed = state.Entries[1];
        Assert.Equal(CompensationEntryStatus.Failed, failed.Status);
        Assert.Equal(3, failed.HandlerAttempts.Count);

        var blocking = await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(run.WorkflowState!.WorkflowExecutionId);
        var waiting = Assert.Single(blocking);
        Assert.Equal(failed.InterventionIncidentId, waiting.IncidentId);
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, waiting.ResolutionOutcome?.ActionKind);
        Assert.Equal(IncidentResolutionSystemSources.StructuralIntervention, waiting.ResolutionOutcome?.SystemSource);
    }

    [Fact]
    public async Task An_operator_retry_of_the_waiting_handler_finishes_the_recovery_once()
    {
        // Spec 251 J5: the handler fails on all three attempts, the operator retries it, and the fourth call succeeds.
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 3);
        await using var harness = NewHarness(participant);
        // An alteration targets a run started from a published source in a tenant partition.
        var reference = await harness.PublishAsync(Executable(ReservationJourney()), "source-operator-retry");
        await harness.StartPublishedAsync(reference, harness.ExecutionId, tenantId: WorkflowExecutionHarness.Partition.Value);
        var executionId = harness.ExecutionId;
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(executionId));

        var job = await OperatorRetry.RetryAsync(harness, executionId, incident.ActivityExecutionId!, "retry-1", WorkflowExecutionHarness.Partition);

        Assert.True(job.Status == WorkflowAlterationJobStatus.Succeeded,
            $"{job.Status} {job.SafeFailure} {string.Join("; ", job.Outcomes.Select(outcome => $"{outcome.Code}: {outcome.Message}"))}");
        var run = await harness.ReadRunAsync(executionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(["reserve-inventory", "reserve-delivery", "cancel-delivery", "release-inventory"], participant.EffectOrder);
        Assert.Single(participant.Calls.Where(call => call.NodeId == "cancel-delivery").Select(call => call.OperationKey).Distinct());
        Assert.Empty(participant.Present);
    }

    [Fact]
    public async Task A_handler_that_recovers_within_its_attempts_undoes_once_with_one_operation_key()
    {
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 2);
        await using var harness = NewHarness(participant);

        var run = await harness.RunAsync(Executable(ReservationJourney()), allowPendingWorkOnTerminalCompletion: true);

        // Spec 251 J3: two failed attempts, the third succeeds, recovery continues.
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(3, run.States("cancel-delivery").Count);
        var cancels = participant.Calls.Where(call => call.NodeId == "cancel-delivery").ToArray();
        Assert.Equal(3, cancels.Length);
        Assert.Single(cancels.Select(call => call.OperationKey).Distinct());
        Assert.Single(participant.Effects, effect => effect.NodeId == "cancel-delivery");
        Assert.Empty(participant.Present);
    }

    [Fact]
    public async Task A_lost_response_is_undone_as_an_uncertain_step_and_leaves_no_reservation()
    {
        var participant = new Participant().LosingResponseAfterEffectOf("reserve-delivery");
        await using var harness = NewHarness(participant);

        var run = await harness.RunAsync(Executable(ReservationJourney()), allowPendingWorkOnTerminalCompletion: true);

        // Spec 251 J1: the reservation exists although its response was lost; recovery undoes it, then inventory.
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        run.AssertDidNotRun("confirm-order");
        Assert.Empty(participant.Present);
        // A completed scope keeps no journal, so the participant is the witness: delivery was undone, newest first.
        Assert.Equal(["reserve-inventory", "reserve-delivery", "cancel-delivery", "release-inventory"], participant.EffectOrder);
        var reserveKey = participant.Calls.Single(call => call.NodeId == "reserve-delivery").OperationKey;
        Assert.Equal(reserveKey, participant.Calls.Single(call => call.NodeId == "cancel-delivery").TargetOperationKey);
    }

    [Fact]
    public async Task A_forward_effect_that_arrives_after_its_compensation_is_refused()
    {
        var participant = new Participant().DelayingEffectOf("reserve-delivery");
        await using var harness = NewHarness(participant);

        var run = await harness.RunAsync(Executable(ReservationJourney()), allowPendingWorkOnTerminalCompletion: true);
        run.AssertWorkflowCompleted();
        var reserveKey = participant.Calls.Single(call => call.NodeId == "reserve-delivery").OperationKey!;

        // Spec 251 J2: the compensation found nothing to undo and left a tombstone, so the late request is refused.
        Assert.Contains(reserveKey, participant.Tombstones);
        Assert.Equal(ForwardResult.Refused, participant.ApplyLate(reserveKey));
        Assert.Empty(participant.Present);
    }

    [Fact]
    public async Task An_uncertain_step_without_a_handler_waits_for_intervention_instead_of_moving_on()
    {
        var participant = new Participant().LosingResponseAfterEffectOf("confirm-order");
        await using var harness = NewHarness(participant);

        var run = await harness.RunAsync(Executable(ReservationJourney()), allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);

        // Spec 251 J6: the scope never treats the step as "no effect", and undoes nothing on its own.
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
        run.AssertDidNotRun("release-inventory", "cancel-delivery");
        var state = ScopeState(run);
        Assert.Equal(CompensationScopePhase.AwaitingIntervention, state.Phase);
        Assert.Equal(CompensationEntryStatus.Uncertain, state.Entries.Single(entry => entry.Step == "Confirm order").Status);
        var waiting = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(run.WorkflowState!.WorkflowExecutionId));
        Assert.Equal("unknown", waiting.ResolutionOutcome?.Metadata[CompensationScope.MetadataKeys.ForwardOutcome]);
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
                services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
            })
            .Build();
}
