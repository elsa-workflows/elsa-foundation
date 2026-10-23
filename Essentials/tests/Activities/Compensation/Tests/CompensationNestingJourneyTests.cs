using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// The spec 258 quickstart journeys and its four restart injection points through the real workflow agent, in every
/// persistence mode, on a SQLite store and the shared <see cref="Participant"/>.
/// </summary>
public sealed class CompensationNestingJourneyTests
{
    private static readonly string OuterAuthoredId = AuthoredId("node-outer");

    public static TheoryData<PersistenceMode> Modes() => new(Enum.GetValues<PersistenceMode>());

    /// <summary>N1: a completed inner scope's entries are undone by the outer scope, newest first, with their own keys.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task N1_the_outer_scope_undoes_a_completed_inner_scope_s_steps_newest_first(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("charge");

        var run = await host.RunAsync(participant, Nested());

        AssertNestedRecovered(run, participant);
    }

    /// <summary>N2: a hand-over passed on by a middle scope keeps the scope the entry was first journaled in.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task N2_a_transitive_hand_over_keeps_the_first_origin(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("x");
        var root = Scope("node-outer", faultAfterCompensation: false,
            new Step("Middle", Scope("node-middle", faultAfterCompensation: false,
                new Step("Inner", Scope("node-inner", faultAfterCompensation: false,
                    new Step("Reserve A", Participant("reserve-a"), Participant("release-a")))))),
            new Step("X", Participant("x")));

        var run = await host.RunAsync(participant, root);

        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-middle", ActivityOutcomes.Done);
        run.AssertOutcomes("node-outer", CompensationScope.CompensatedOutcome);
        Assert.Equal(["reserve-a", "release-a"], participant.EffectOrder);
        Assert.Empty(participant.Present);
        var handler = run.State("release-a");
        Assert.Equal(run.State("node-outer").Execution.ActivityExecutionId, handler.ParentActivityExecutionId);
        Assert.Equal(run.State("node-inner").Execution.ActivityExecutionId, handler.Provenance.Metadata[CompensationScope.MetadataKeys.OriginScope]);
    }

    /// <summary>N3: a scope with no enclosing scope keeps its entries and sends nothing.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task N3_a_scope_without_an_enclosing_scope_completes_and_hands_nothing_over(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant();

        var run = await host.RunAsync(participant, Scope("node-inner", faultAfterCompensation: false,
            new Step("Reserve A", Participant("reserve-a"), Participant("release-a"))));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-inner", ActivityOutcomes.Done);
        run.AssertDidNotRun("release-a");
        Assert.Single(participant.Present);
    }

    /// <summary>L1: a scope inside a loop hands one entry over per iteration; recovery undoes the newest iteration first.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task L1_each_iteration_s_scope_is_undone_newest_iteration_first(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("ship");

        var run = await host.RunAsync(participant, Looped());

        AssertLoopRecovered(run, participant);
    }

    /// <summary>L2: a crash re-attempt inside an iteration adds no entry.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task L2_a_crash_re_attempt_in_an_iteration_adds_no_entry(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("ship").KillingHostAfterEffectOf("reserve-item");

        await using (var first = await host.StartHostAsync(participant))
            await CompensationHost.StartAsync(first, Looped());

        await using var second = await host.StartHostAsync(participant);
        var run = await host.RecoverAsync(second);

        AssertLoopRecovered(run, participant);
        Assert.Equal(4, participant.Calls.Count(call => call.NodeId == "reserve-item"));
    }

    /// <summary>B1: each Parallel branch's scope hands over; the outer scope undoes both.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task B1_both_parallel_branches_hand_over_and_both_are_undone(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("x");
        var root = Scope("node-outer", faultAfterCompensation: false,
            new Step("Branches", ParallelNode("node-parallel",
                Scope("node-left", faultAfterCompensation: false, new Step("Reserve A", Participant("reserve-a"), Participant("release-a"))),
                Scope("node-right", faultAfterCompensation: false, new Step("Reserve B", Participant("reserve-b"), Participant("release-b"))))),
            new Step("X", Participant("x")));

        var run = await host.RunAsync(participant, root);

        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-outer", CompensationScope.CompensatedOutcome);
        Assert.Equal(4, participant.EffectOrder.Count);
        Assert.Equal(["release-a", "release-b"], participant.EffectOrder.Skip(2).Order());
        Assert.Empty(participant.Present);
    }

    /// <summary>C1: a confirmed step is kept when a later step fails.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task C1_a_confirmed_step_is_kept_when_a_later_step_fails(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("reserve-b");

        var run = await host.RunAsync(participant, Confirmed());

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(["reserve-a"], participant.EffectOrder);
        run.AssertDidNotRun("release-a", "release-b");
        Assert.Single(participant.Present);
    }

    /// <summary>C2: a Confirm naming the outer scope confirms that scope's entries as well as its own scope's.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task C2_a_named_Confirm_reaches_the_outer_scope(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("x");
        var root = Scope("node-outer", faultAfterCompensation: false,
            new Step("Reserve P", Participant("reserve-p"), Participant("release-p")),
            new Step("Order", Scope("node-inner", faultAfterCompensation: false,
                new Step("Reserve A", Participant("reserve-a"), Participant("release-a")),
                new Step("Confirm", ConfirmNode("node-confirm", scope: OuterAuthoredId)))),
            new Step("X", Participant("x")));

        var run = await host.RunAsync(participant, root);

        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-outer", CompensationScope.CompensatedOutcome);
        Assert.Equal(["reserve-p", "reserve-a"], participant.EffectOrder);
        run.AssertDidNotRun("release-p", "release-a");
    }

    /// <summary>C3: an inner scope with confirmOnComplete hands nothing over.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task C3_confirmOnComplete_hands_nothing_over(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("x");
        var root = Scope("node-outer", faultAfterCompensation: false,
            new Step("Order", Scope("node-inner", faultAfterCompensation: false, handlerMaxAttempts: 3, confirmOnComplete: true,
                new Step("Reserve A", Participant("reserve-a"), Participant("release-a")))),
            new Step("X", Participant("x")));

        var run = await host.RunAsync(participant, root);

        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-outer", CompensationScope.CompensatedOutcome);
        Assert.Equal(["reserve-a"], participant.EffectOrder);
        run.AssertDidNotRun("release-a");
    }

    /// <summary>E1: a Compensate inside a Flowchart step stops the step and recovers with an explicit cause.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task E1_Compensate_stops_the_running_step_and_undoes_the_completed_ones(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant();

        var run = await host.RunAsync(participant, Explicit(withHandler: true));

        AssertExplicitRecovered(run, participant);
    }

    /// <summary>E2: a Compensate aimed at the outer scope: the inner scope compensates itself, then the outer recovers.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task E2_a_cascading_Compensate_compensates_the_inner_scope_then_the_outer(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant();
        var root = Scope("node-outer", faultAfterCompensation: false,
            new Step("Reserve P", Participant("reserve-p"), Participant("release-p")),
            new Step("Order", Scope("node-inner", faultAfterCompensation: false,
                new Step("Reserve A", Participant("reserve-a"), Participant("release-a")),
                new Step("Give up", CompensateNode("node-compensate", scope: OuterAuthoredId, reason: "Payment declined")))),
            new Step("Ship", Participant("ship")));

        var run = await host.RunAsync(participant, root);

        run.AssertWorkflowCompleted();
        Assert.Equal(CompensationScope.CompensateRequestedFaultCode, run.State("node-inner").Fault?.Code);
        run.AssertOutcomes("node-outer", CompensationScope.CompensatedOutcome);
        Assert.Equal(["reserve-p", "reserve-a", "release-a", "release-p"], participant.EffectOrder);
        run.AssertDidNotRun("ship");
        Assert.Empty(participant.Present);
        Assert.Empty(host.BlockingIncidents);
    }

    /// <summary>E3: a Compensate that stops a step without a handler recovers the rest, then faults for an operator.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task E3_stopping_a_step_without_a_handler_faults_uncertain_unhandled_after_recovery(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant();

        var run = await host.RunAsync(participant, Explicit(withHandler: false));

        Assert.Equal(WorkflowExecutionStatus.Faulted, run.WorkflowState?.Status);
        Assert.Equal(CompensationScope.UncertainUnhandledFaultCode, run.State(ScopeNodeId).Fault?.Code);
        Assert.Equal(["reserve-a", "check", "release-a"], participant.EffectOrder);
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("node-check-flow").Status);
    }

    public static TheoryData<string, PersistenceMode> CancellationPoints()
    {
        var data = new TheoryData<string, PersistenceMode>();
        foreach (var mode in Enum.GetValues<PersistenceMode>())
        foreach (var point in new[] { "forward", "recovery", "waiting" })
            data.Add(point, mode);
        return data;
    }

    /// <summary>X1: cancelling the run during forward work, during recovery or while waiting runs no handler.</summary>
    [Theory]
    [MemberData(nameof(CancellationPoints))]
    public async Task X1_cancelling_the_run_runs_no_handler_and_leaves_the_journal_readable(string point, PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = point switch
        {
            "forward" => new Participant().Rejecting("charge").KillingHostBeforeEffectOf("reserve-b"),
            "recovery" => new Participant().Rejecting("charge").KillingHostBeforeEffectOf("release-b"),
            _ => new Participant().Rejecting("charge", "release-b")
        };

        await using (var first = await host.StartHostAsync(participant))
            await CompensationHost.StartAsync(first, Nested());

        await using var second = await host.StartHostAsync(participant);
        host.ExpireLeases();
        await second.CancelAsync(CompensationHost.WorkflowExecutionId, CompensationHost.DefaultPartition);
        var run = await host.RecoverAsync(second);

        Assert.Equal(WorkflowExecutionStatus.Cancelled, run.WorkflowState?.Status);
        // Cancellation starts no handler. A handler already in flight when it arrived ("recovery") may finish once.
        Assert.DoesNotContain("release-a", participant.EffectOrder);
        Assert.True(participant.EffectOrder.Count(effect => effect == "release-b") <= (point == "recovery" ? 1 : 0));
        Assert.NotNull(CompensationScopeStatePersister.Load(run.State("node-outer")));
    }

    public static TheoryData<string, PersistenceMode> CompositeSteps()
    {
        var data = new TheoryData<string, PersistenceMode>();
        foreach (var mode in Enum.GetValues<PersistenceMode>())
        foreach (var composite in new[] { "sequence", "foreach", "while", "for" })
            data.Add(composite, mode);
        return data;
    }

    /// <summary>S1: a fault inside a Sequence or loop step reaches the scope, which undoes the completed steps.</summary>
    [Theory]
    [MemberData(nameof(CompositeSteps))]
    public async Task S1_a_fault_inside_a_composite_step_reaches_the_scope(string composite, PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("fail");

        var run = await host.RunAsync(participant, Composed(composite));

        AssertCompositeRecovered(run, participant);
        Assert.Empty(host.BlockingIncidents);
    }

    /// <summary>
    /// S3: an inner scope that faults while evaluating a descendant's fault reports that fault to the outer scope as an
    /// ordinary child fault, and the outer scope recovers.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task S3_an_inner_scope_faulting_on_a_descendant_fault_is_recovered_by_the_outer_scope(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("fail");
        var root = Scope("node-outer", faultAfterCompensation: false,
            new Step("Reserve P", Participant("reserve-p"), Participant("release-p")),
            new Step("Order", Scope("node-inner", faultAfterCompensation: true,
                new Step("Work", SequenceNode("node-work", Participant("fail"))))),
            new Step("Ship", Participant("ship")));

        var run = await host.RunAsync(participant, root);

        run.AssertWorkflowCompleted();
        Assert.Equal(CompensationScope.CompensatedFaultCode, run.State("node-inner").Fault?.Code);
        run.AssertOutcomes("node-outer", CompensationScope.CompensatedOutcome);
        Assert.Equal(["reserve-p", "release-p"], participant.EffectOrder);
        run.AssertDidNotRun("ship");
    }

    /// <summary>S2: a Flowchart step is unchanged; a Sequence inside a Flowchart faults the run, unrecovered (known limit).</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task S2_a_Flowchart_step_recovers_and_a_Sequence_inside_a_Flowchart_faults_the_run_unrecovered(PersistenceMode mode)
    {
        using (var host = new CompensationHost(mode))
        {
            var participant = new Participant().Rejecting("fail");
            var run = await host.RunAsync(participant, Scope(
                new Step("Reserve A", Participant("reserve-a"), Participant("release-a")),
                new Step("Work", FlowchartNode("node-flow", Participant("fail")))));

            run.AssertWorkflowCompleted();
            run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
            Assert.Equal(["reserve-a", "release-a"], participant.EffectOrder);
        }

        using (var host = new CompensationHost(mode))
        {
            var participant = new Participant().Rejecting("fail");
            var run = await host.RunAsync(participant, Scope(
                new Step("Reserve A", Participant("reserve-a"), Participant("release-a")),
                new Step("Work", FlowchartNode("node-flow", SequenceNode("node-sequence", Participant("fail"))))));

            // The Flowchart handles its children's faults, so the walk to the scope ends there and nothing is undone.
            Assert.Equal(WorkflowExecutionStatus.Faulted, run.WorkflowState?.Status);
            run.AssertDidNotRun("release-a");
        }
    }

    /// <summary>
    /// FR-304: an operator retry of a handed-over handler rebuilds its command from provenance and still reaches the outer
    /// scope, which finishes the recovery.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task An_operator_retry_of_a_handed_over_handler_routes_back_to_the_outer_scope(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("charge").FailingTimes("release-b", 3);

        await using var harness = await host.StartHostAsync(participant);
        await CompensationHost.StartAsync(harness, Nested());
        await harness.SweepUntilQuietAsync();
        var waiting = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(CompensationHost.WorkflowExecutionId));
        Assert.Equal("release-b", (await harness.ReadRunAsync(CompensationHost.WorkflowExecutionId)).ActivityStates
            .Single(state => state.Execution.ActivityExecutionId == waiting.ActivityExecutionId).Execution.ExecutableNodeId);

        await OperatorRetry.RetryAsync(harness, CompensationHost.WorkflowExecutionId, waiting.ActivityExecutionId!, "retry-handed-over", CompensationHost.DefaultPartition);

        var run = await harness.ReadRunAsync(CompensationHost.WorkflowExecutionId);
        AssertNestedRecovered(run, participant);
        Assert.Equal(4, participant.Calls.Count(call => call.NodeId == "release-b"));
        Assert.Single(participant.Calls, call => call.NodeId == "release-a");
    }

    /// <summary>
    /// An uncertain step without a handler that is a Sequence waits for an operator. The operator retries the faulted
    /// activity inside it, which runs again under the same Sequence; the Sequence's completion reaches the waiting scope,
    /// which moves on.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task An_operator_retry_inside_a_waiting_Sequence_step_lets_the_scope_move_on(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().LosingResponseAfterEffectOf("confirm-order");
        var root = Scope(
            new Step("Reserve A", Participant("reserve-a"), Participant("release-a")),
            new Step("Confirm order", SequenceNode("node-confirm", Participant("confirm-order"), Participant("notify"))),
            new Step("Ship", Participant("ship")));

        await using var harness = await host.StartHostAsync(participant);
        await CompensationHost.StartAsync(harness, root);
        await harness.SweepUntilQuietAsync();
        var waiting = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(CompensationHost.WorkflowExecutionId));
        var stopped = await harness.ReadRunAsync(CompensationHost.WorkflowExecutionId);
        Assert.Equal("confirm-order", stopped.ActivityStates.Single(state => state.Execution.ActivityExecutionId == waiting.ActivityExecutionId).Execution.ExecutableNodeId);
        Assert.Equal(CompensationScopePhase.AwaitingIntervention, CompensationScopeStatePersister.Load(stopped.State(ScopeNodeId))?.Phase);

        await OperatorRetry.RetryAsync(harness, CompensationHost.WorkflowExecutionId, waiting.ActivityExecutionId!, "retry-in-sequence", CompensationHost.DefaultPartition);

        var run = await harness.ReadRunAsync(CompensationHost.WorkflowExecutionId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, ActivityOutcomes.Done);
        Assert.Single(run.States("node-confirm"));
        Assert.Equal(["reserve-a", "confirm-order", "notify", "ship"], participant.EffectOrder);
        Assert.Empty(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(CompensationHost.WorkflowExecutionId));
    }

    /// <summary>
    /// A compensation that is a Sequence fails on every attempt and waits for an operator. The operator retries the
    /// faulted activity inside the last attempt's Sequence; that Sequence's completion finishes the recovery.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task An_operator_retry_inside_a_waiting_Sequence_compensation_finishes_the_recovery(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("charge").FailingTimes("cancel-delivery", 3);
        var root = Scope(
            new Step("Reserve A", Participant("reserve-a"), Participant("release-a")),
            new Step("Reserve delivery", Participant("reserve-delivery"), SequenceNode("node-cancel", Participant("cancel-delivery"))),
            new Step("Charge", Participant("charge")));

        await using var harness = await host.StartHostAsync(participant);
        await CompensationHost.StartAsync(harness, root);
        await harness.SweepUntilQuietAsync();
        var waiting = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(CompensationHost.WorkflowExecutionId));
        var stopped = await harness.ReadRunAsync(CompensationHost.WorkflowExecutionId);
        Assert.Equal("cancel-delivery", stopped.ActivityStates.Single(state => state.Execution.ActivityExecutionId == waiting.ActivityExecutionId).Execution.ExecutableNodeId);
        Assert.Equal(3, stopped.States("node-cancel").Count);
        Assert.Equal(CompensationScopePhase.AwaitingIntervention, CompensationScopeStatePersister.Load(stopped.State(ScopeNodeId))?.Phase);

        await OperatorRetry.RetryAsync(harness, CompensationHost.WorkflowExecutionId, waiting.ActivityExecutionId!, "retry-in-compensation", CompensationHost.DefaultPartition);

        var run = await harness.ReadRunAsync(CompensationHost.WorkflowExecutionId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(3, run.States("node-cancel").Count);
        // An activity inside a composite compensation gets its own default operation key (spec 251), so the participant
        // witnesses the order of the calls here, not which effect they undid.
        Assert.Equal(["reserve-a", "reserve-delivery", "cancel-delivery", "release-a"], participant.EffectOrder);
        Assert.Equal(4, participant.Calls.Count(call => call.NodeId == "cancel-delivery"));
        Assert.Empty(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(CompensationHost.WorkflowExecutionId));
    }

    /// <summary>Injection point 1: the host stops after the hand-over commits and before the outer scope journals it.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task P1_a_stop_before_the_hand_over_is_journaled_delivers_it_once_after_restart(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("charge");

        await using (var first = await host.StartHostAsync(participant, HostStop.BeforeNotification(CompensationScope.Notifications.Register)))
        {
            await CompensationHost.StartAsync(first, Nested());
            Assert.Equal(["reserve-a", "reserve-b"], participant.EffectOrder);
        }

        await using var second = await host.StartHostAsync(participant);
        var run = await host.RecoverAsync(second);

        AssertNestedRecovered(run, participant);
        Assert.Single(participant.Calls, call => call.NodeId == "release-a");
        Assert.Single(participant.Calls, call => call.NodeId == "release-b");
    }

    /// <summary>Injection point 2: the host stops between two handed-over handlers (N1 and L1).</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task P2_a_stop_between_handed_over_handlers_runs_each_once(PersistenceMode mode)
    {
        using (var host = new CompensationHost(mode))
        {
            var participant = new Participant().Rejecting("charge").KillingHostBeforeEffectOf("release-a");
            await using (var first = await host.StartHostAsync(participant))
                await CompensationHost.StartAsync(first, Nested());
            Assert.DoesNotContain("release-a", participant.EffectOrder);

            await using var second = await host.StartHostAsync(participant);
            var run = await host.RecoverAsync(second);

            AssertNestedRecovered(run, participant);
            Assert.Single(participant.Calls, call => call.NodeId == "release-a");
            Assert.Single(participant.Calls, call => call.NodeId == "release-b");
        }

        using (var host = new CompensationHost(mode))
        {
            var participant = new Participant().Rejecting("ship").KillingHostBeforeCall("release-item", 2);
            await using (var first = await host.StartHostAsync(participant))
                await CompensationHost.StartAsync(first, Looped());

            await using var second = await host.StartHostAsync(participant);
            var run = await host.RecoverAsync(second);

            AssertLoopRecovered(run, participant);
            Assert.Equal(3, participant.Calls.Count(call => call.NodeId == "release-item"));
        }
    }

    /// <summary>Injection point 3: the host stops after Compensate's notification commits, before the step is stopped.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task P3_a_stop_before_Compensate_is_evaluated_recovers_once_after_restart(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant();

        await using (var first = await host.StartHostAsync(participant, HostStop.BeforeNotification(CompensationScope.Notifications.Compensate)))
        {
            await CompensationHost.StartAsync(first, Explicit(withHandler: true));
            Assert.Equal(["reserve-a", "check"], participant.EffectOrder);
        }

        await using var second = await host.StartHostAsync(participant);
        var run = await host.RecoverAsync(second);

        AssertExplicitRecovered(run, participant);
        Assert.Single(participant.Calls, call => call.NodeId == "undo-check");
        Assert.Single(participant.Calls, call => call.NodeId == "release-a");
    }

    /// <summary>Injection point 4: the host stops after a descendant fault is delivered, before its absorption commits.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task P4_a_stop_before_a_descendant_fault_is_absorbed_recovers_once_after_restart(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("fail");

        await using (var first = await host.StartHostAsync(participant, HostStop.BeforeDescendantFault()))
        {
            await CompensationHost.StartAsync(first, Composed("sequence"));
            Assert.Equal(["reserve-a"], participant.EffectOrder);
        }

        await using var second = await host.StartHostAsync(participant);
        var run = await host.RecoverAsync(second);

        AssertCompositeRecovered(run, participant);
        Assert.Single(participant.Calls, call => call.NodeId == "release-a");
    }

    // Outer [Reserve: inner [A, B], Charge].
    private static ExecutableNode Nested() =>
        Scope("node-outer", faultAfterCompensation: false,
            new Step("Reserve", Scope("node-inner", faultAfterCompensation: false,
                new Step("Reserve A", Participant("reserve-a"), Participant("release-a")),
                new Step("Reserve B", Participant("reserve-b"), Participant("release-b")))),
            new Step("Charge", Participant("charge")));

    private static void AssertNestedRecovered(WorkflowExecutionRun run, Participant participant)
    {
        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-inner", ActivityOutcomes.Done);
        run.AssertOutcomes("node-outer", CompensationScope.CompensatedOutcome);
        Assert.Equal(["reserve-a", "reserve-b", "release-b", "release-a"], participant.EffectOrder);
        Assert.Empty(participant.Present);

        // The handlers ran under the outer scope, routed by the origin they were handed over with.
        var inner = run.State("node-inner").Execution.ActivityExecutionId;
        var outer = run.State("node-outer").Execution.ActivityExecutionId;
        foreach (var handler in new[] { "release-a", "release-b" })
        {
            var state = run.States(handler).Single(candidate => candidate.Status == ActivityExecutionStatus.Completed);
            Assert.Equal(outer, state.ParentActivityExecutionId);
            Assert.Equal(inner, state.Provenance.Metadata[CompensationScope.MetadataKeys.OriginScope]);
        }
    }

    // Outer [Reserve: ForEach(3) { scope (Reserve item) }, Ship].
    private static ExecutableNode Looped() =>
        Scope("node-outer", faultAfterCompensation: false,
            new Step("Reserve", ForEachNode("node-items",
                Scope("node-item", faultAfterCompensation: false, new Step("Reserve item", Participant("reserve-item"), Participant("release-item"))),
                "first", "second", "third")),
            new Step("Ship", Participant("ship")));

    private static void AssertLoopRecovered(WorkflowExecutionRun run, Participant participant)
    {
        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-outer", CompensationScope.CompensatedOutcome);
        Assert.Equal(3, run.States("node-item").Count);
        var forwardKeys = participant.Effects.Where(effect => effect.NodeId == "reserve-item").Select(effect => effect.OperationKey).ToArray();
        var undoneKeys = participant.Effects.Where(effect => effect.NodeId == "release-item").Select(effect => effect.TargetOperationKey).ToArray();
        Assert.Equal(3, forwardKeys.Distinct().Count());
        Assert.Equal(forwardKeys.Reverse(), undoneKeys);
        Assert.Empty(participant.Present);
    }

    // Scope [Reserve A, Confirm, Reserve B].
    private static ExecutableNode Confirmed() =>
        Scope(
            new Step("Reserve A", Participant("reserve-a"), Participant("release-a")),
            new Step("Confirm", ConfirmNode("node-confirm")),
            new Step("Reserve B", Participant("reserve-b"), Participant("release-b")));

    // Scope [Reserve A, Check: Flowchart { check -> Compensate }].
    private static ExecutableNode Explicit(bool withHandler) =>
        Scope(
            new Step("Reserve A", Participant("reserve-a"), Participant("release-a")),
            new Step("Check", FlowchartNode("node-check-flow", Participant("check"), CompensateNode("node-compensate", reason: "Fraud check failed")),
                withHandler ? Participant("undo-check") : null));

    private static void AssertExplicitRecovered(WorkflowExecutionRun run, Participant participant)
    {
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("node-check-flow").Status);
        Assert.Equal(["reserve-a", "check", "undo-check", "release-a"], participant.EffectOrder);
    }

    // Scope [Reserve A, Work: <composite> { fail }].
    private static ExecutableNode Composed(string composite) =>
        Scope(
            new Step("Reserve A", Participant("reserve-a"), Participant("release-a")),
            new Step("Work", composite switch
            {
                "sequence" => SequenceNode("node-work", Participant("fail")),
                "foreach" => ForEachNode("node-work", Participant("fail"), "first", "second"),
                "while" => WhileNode("node-work", Participant("fail")),
                _ => ForNode("node-work", Participant("fail"), end: 3)
            }));

    private static void AssertCompositeRecovered(WorkflowExecutionRun run, Participant participant)
    {
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(["reserve-a", "release-a"], participant.EffectOrder);
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("node-work").Status);
    }
}
