using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// The spec 260 quickstart journeys through the real workflow agent, in every persistence mode, on a SQLite store and
/// the shared <see cref="Participant"/>: retry, resolve with both outcomes, abandon (also nested and with a descendant
/// fault), the refusals with their unchanged-state evidence, and the three restart injection points. Another tenant's
/// operator (O7) is refused at target capture, which <c>WorkflowAlterationOrchestrationTests</c> covers, and at the API,
/// which <c>ActivityStateViewAndOperatorAccessTests</c> covers; this harness binds a single persistence scope.
/// </summary>
public sealed class CompensationOperatorActionJourneyTests
{
    private const string RunId = CompensationHost.WorkflowExecutionId;
    private const string ResolveAbsent = CompensationScope.Decisions.ResolveEffectAbsent;
    private const string ResolveAccepted = CompensationScope.Decisions.ResolveEffectAccepted;
    private const string Abandon = CompensationScope.Decisions.Abandon;
    private static readonly WorkflowExecutionPartition Partition = CompensationHost.DefaultPartition;

    public static TheoryData<PersistenceMode> Modes() => new(Enum.GetValues<PersistenceMode>());

    /// <summary>O1: the handler is back; an operator retry finishes the recovery.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O1_a_retry_of_the_failed_handler_finishes_the_recovery(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = WaitingParticipant();
        await using var harness = await StartWaitingAsync(host, participant, ReservationJourney());
        var waiting = await WaitingIncidentAsync(harness);

        var job = await OperatorActions.RetryAsync(harness, RunId, waiting.ActivityExecutionId!, "o1-retry", Partition);

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await harness.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Empty(participant.Present);
        Assert.Single(participant.Effects, effect => effect.NodeId == "cancel-delivery");
        Assert.Equal(4, participant.Calls.Count(call => call.NodeId == "cancel-delivery"));
        var incident = await IncidentAsync(harness, waiting.IncidentId);
        Assert.Equal(IncidentResolutionActionKinds.OperatorRetry, incident.ResolutionOutcome!.ActionKind);
        Assert.Equal(OperatorActions.Operator, incident.ResolutionOutcome.Metadata["runtime.alteration.submittedBy"]);
        Assert.Equal("Participant restored", incident.ResolutionOutcome.Metadata["runtime.alteration.reason"]);
        Assert.Equal(job.JobId, incident.ResolutionOutcome.Metadata["runtime.alteration.jobId"]);
        Assert.Equal(job.PlanId, incident.ResolutionOutcome.Metadata["runtime.alteration.planId"]);
    }

    /// <summary>O2: an uncertain step without a handler resolved as effect absent: the earlier steps are undone.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O2_an_uncertain_step_resolved_effect_absent_recovers_the_earlier_steps(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().LosingResponseAfterEffectOf("reserve-delivery");
        await using var harness = await StartWaitingAsync(host, participant, UncertainDelivery());
        var waiting = await WaitingIncidentAsync(harness);

        var job = await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, ResolveAbsent, "o2-resolve", Partition);

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await harness.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(["reserve-inventory", "reserve-delivery", "release-inventory"], participant.EffectOrder);
        // The delivery stays as the participant left it: the operator said its effect does not exist.
        Assert.Single(participant.Present);
        Assert.Equal(IncidentResolutionActionKinds.OperatorDecision, (await IncidentAsync(harness, waiting.IncidentId)).ResolutionOutcome!.ActionKind);
    }

    /// <summary>O2h: a failed handler resolved as effect absent: recovery continues with the next handler.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O2h_a_failed_handler_resolved_effect_absent_lets_recovery_continue(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = WaitingParticipant();
        await using var harness = await StartWaitingAsync(host, participant, ReservationJourney());
        var waiting = await WaitingIncidentAsync(harness);

        var job = await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, ResolveAbsent, "o2h-resolve", Partition, "Refunded manually, ticket FIN-221");

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        AssertResolvedHandlerRecovered(await harness.ReadRunAsync(RunId), participant);
        var incident = await IncidentAsync(harness, waiting.IncidentId);
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        var outcome = incident.ResolutionOutcome!;
        Assert.Equal(IncidentResolutionActionKinds.OperatorDecision, outcome.ActionKind);
        Assert.Equal(IncidentResolutionSystemSources.OperatorDecisionAlteration, outcome.SystemSource);
        Assert.Equal(ResolveAbsent, outcome.Metadata[RuntimeMetadataKeys.InterventionDecision]);
        Assert.Equal("Refunded manually, ticket FIN-221", outcome.Metadata["runtime.alteration.reason"]);
        Assert.Equal(OperatorActions.Operator, outcome.Metadata["runtime.alteration.submittedBy"]);
        var scope = (await harness.ReadRunAsync(RunId)).State(ScopeNodeId);
        Assert.Equal(scope.Execution.ActivityExecutionId, outcome.Metadata[RuntimeMetadataKeys.InterventionRequestedBy]);
    }

    /// <summary>O3: an uncertain step resolved as effect accepted: the scope runs the next step; recovery never undoes it.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O3_an_uncertain_step_resolved_effect_accepted_runs_the_next_step_and_is_never_undone(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().LosingResponseAfterEffectOf("reserve-delivery").Rejecting("confirm-order");
        await using var harness = await StartWaitingAsync(host, participant, UncertainDelivery());
        var waiting = await WaitingIncidentAsync(harness);

        await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, ResolveAccepted, "o3-resolve", Partition);

        var run = await harness.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        run.AssertRan("confirm-order");
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(["reserve-inventory", "reserve-delivery", "release-inventory"], participant.EffectOrder);
        Assert.Single(participant.Present);
    }

    /// <summary>O3h: a failed handler resolved as effect accepted: the delivery is kept and recovery finishes.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O3h_a_failed_handler_resolved_effect_accepted_keeps_the_effect(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = WaitingParticipant();
        await using var harness = await StartWaitingAsync(host, participant, ReservationJourney());
        var waiting = await WaitingIncidentAsync(harness);

        await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, ResolveAccepted, "o3h-resolve", Partition);

        var run = await harness.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Contains("release-inventory", participant.EffectOrder);
        Assert.Single(participant.Present);
        Assert.Equal(3, participant.Calls.Count(call => call.NodeId == "cancel-delivery"));
    }

    /// <summary>O4: abandon: nothing more is undone, the scope faults with the coded fault and the run faults.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O4_abandon_faults_the_scope_and_the_run_and_keeps_the_journal(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = WaitingParticipant();
        await using var harness = await StartWaitingAsync(host, participant, ReservationJourney());
        var waiting = await WaitingIncidentAsync(harness);

        var job = await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, Abandon, "o4-abandon", Partition, "Order written off by finance");

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await harness.ReadRunAsync(RunId);
        Assert.Equal(WorkflowExecutionStatus.Faulted, run.WorkflowState!.Status);
        var scope = run.State(ScopeNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, scope.Status);
        Assert.Equal(CompensationScope.AbandonedFaultCode, scope.Fault!.Code);
        Assert.Equal(2, participant.Present.Count);
        Assert.DoesNotContain(participant.Calls, call => call.NodeId == "release-inventory");
        var journal = CompensationScopeStatePersister.Load(scope)!;
        Assert.Equal(CompensationScopePhase.Abandoned, journal.Phase);
        Assert.Equal(waiting.IncidentId, journal.Abandonment!.IncidentId);
        Assert.Equal([CompensationEntryStatus.Registered, CompensationEntryStatus.Failed], journal.Entries.Select(entry => entry.Status));
    }

    /// <summary>O4n: an abandoned inner scope is an uncertain step of the outer scope, which waits; resolving it undoes P.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O4n_an_abandoned_inner_scope_makes_the_outer_scope_wait_and_its_resolve_undoes_the_earlier_step(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = WaitingParticipant();
        var root = Scope("node-outer", faultAfterCompensation: false,
            new Step("Reserve P", Participant("reserve-p"), Participant("release-p")),
            new Step("Order", Scope("node-inner", faultAfterCompensation: false,
                new Step("Reserve inventory", Participant("reserve-inventory"), Participant("release-inventory")),
                new Step("Reserve delivery", Participant("reserve-delivery"), Participant("cancel-delivery")),
                new Step("Confirm order", Participant("confirm-order")))),
            new Step("Ship", Participant("ship")));
        await using var harness = await StartWaitingAsync(host, participant, root);
        var innerWait = await WaitingIncidentAsync(harness);

        await OperatorActions.DecideAsync(harness, RunId, innerWait.IncidentId, Abandon, "o4n-abandon", Partition);

        var waiting = await harness.ReadRunAsync(RunId);
        Assert.Equal(WorkflowExecutionStatus.Running, waiting.WorkflowState!.Status);
        Assert.Equal(CompensationScope.AbandonedFaultCode, waiting.State("node-inner").Fault!.Code);
        var outer = CompensationScopeStatePersister.Load(waiting.State("node-outer"))!;
        Assert.Equal(CompensationScopePhase.AwaitingIntervention, outer.Phase);
        Assert.Equal((CompensationEntryStatus.Uncertain, CompensationForwardOutcome.Uncertain),
            (outer.Entries.Single(entry => entry.Step == "Order").Status, outer.Entries.Single(entry => entry.Step == "Order").ForwardOutcome));
        var outerWait = await WaitingIncidentAsync(harness);
        Assert.Equal(waiting.State("node-inner").Execution.ActivityExecutionId, outerWait.ActivityExecutionId);
        Assert.Equal(CompensationScope.Decisions.Offer, JsonSerializer.Deserialize<string[]>(outerWait.ResolutionOutcome!.Metadata[RuntimeMetadataKeys.InterventionDecisions]));

        await OperatorActions.DecideAsync(harness, RunId, outerWait.IncidentId, ResolveAbsent, "o4n-resolve", Partition);

        var run = await harness.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-outer", CompensationScope.CompensatedOutcome);
        Assert.Contains("release-p", participant.EffectOrder);
        run.AssertDidNotRun("ship");
        Assert.Equal(2, participant.Present.Count);
    }

    /// <summary>O4d: the failed handler is a Sequence (descendant fault); resolving it cancels the suspended Sequence.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O4d_resolving_a_handler_whose_descendant_faulted_cancels_the_suspended_sequence(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = WaitingParticipant();
        var root = Scope(
            new Step("Reserve inventory", Participant("reserve-inventory"), Participant("release-inventory")),
            new Step("Reserve delivery", Participant("reserve-delivery"), SequenceNode("node-cancel", Participant("cancel-delivery"))),
            new Step("Confirm order", Participant("confirm-order")));
        await using var harness = await StartWaitingAsync(host, participant, root);
        var waiting = await WaitingIncidentAsync(harness);

        await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, ResolveAbsent, "o4d-resolve", Partition);

        var run = await harness.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        var sequences = run.States("node-cancel");
        Assert.Equal(3, sequences.Count);
        Assert.Equal(ActivityExecutionStatus.Cancelled, sequences.Single(state => state.Execution.ActivityExecutionId == SequenceOf(run, waiting)).Status);
        Assert.Contains("release-inventory", participant.EffectOrder);
        Assert.Empty(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(RunId));
    }

    /// <summary>O5: a second decision and a retry on the decided incident are refused and change nothing (SC-403).</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O5_a_stale_decision_and_a_stale_retry_are_refused_and_change_nothing(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        // Release inventory fails too, so the scope waits again and the run stays running for the stale actions.
        var participant = WaitingParticipant().FailingTimes("release-inventory", 3);
        await using var harness = await StartWaitingAsync(host, participant, ReservationJourney());
        var waiting = await WaitingIncidentAsync(harness);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded,
            (await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, ResolveAbsent, "o5-first", Partition)).Status);
        var before = await SnapshotAsync(harness);

        var second = await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, ResolveAccepted, "o5-second", Partition);
        var retry = await OperatorActions.RetryAsync(harness, RunId, waiting.ActivityExecutionId!, "o5-retry", Partition);

        Assert.Equal("InterventionNotPending", Assert.Single(second.Outcomes).Code);
        Assert.Equal("NoBlockingIncident", Assert.Single(retry.Outcomes).Code);
        Assert.Equal(before, await SnapshotAsync(harness));
        Assert.Equal(3, participant.Calls.Count(call => call.NodeId == "cancel-delivery"));
    }

    /// <summary>O5r: the same submission twice with the same key is replayed and applied once.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O5r_the_same_key_twice_is_replayed_and_applied_once(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = WaitingParticipant();
        await using var harness = await StartWaitingAsync(host, participant, ReservationJourney());
        var waiting = await WaitingIncidentAsync(harness);

        var first = await OperatorActions.SubmitDecisionAsync(harness, RunId, waiting.IncidentId, ResolveAbsent, "o5r-key", Partition);
        var replay = await OperatorActions.SubmitDecisionAsync(harness, RunId, waiting.IncidentId, ResolveAbsent, "o5r-key", Partition);
        await OperatorActions.OrchestrateAsync(harness, RunId, first.Plan.PlanId);

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Plan.PlanId, replay.Plan.PlanId);
        AssertResolvedHandlerRecovered(await harness.ReadRunAsync(RunId), participant);
        Assert.Single(participant.Calls, call => call.NodeId == "release-inventory");
    }

    /// <summary>
    /// O5c: the waiting scope was cancelled while the run goes on (here its subtree is cancelled directly, as a Compensate
    /// in an outer scope cancels it). A retry and a decision are refused and no handler starts (spec 260 R9).
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O5c_a_cancelled_scope_refuses_a_retry_and_a_decision_and_starts_no_handler(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = WaitingParticipant();
        await using var harness = await StartWaitingAsync(host, participant, ReservationJourney());
        var waiting = await WaitingIncidentAsync(harness);
        await CancelScopeAsync(harness);
        var before = await SnapshotAsync(harness);

        var retry = await OperatorActions.RetryAsync(harness, RunId, waiting.ActivityExecutionId!, "o5c-retry", Partition);
        var decision = await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, ResolveAbsent, "o5c-resolve", Partition);

        Assert.Equal("SourceParentNotRunning", Assert.Single(retry.Outcomes).Code);
        Assert.Equal("RequesterNotRunning", Assert.Single(decision.Outcomes).Code);
        Assert.Equal(before, await SnapshotAsync(harness));
        Assert.Equal(3, participant.Calls.Count(call => call.NodeId == "cancel-delivery"));
        Assert.DoesNotContain(participant.Calls, call => call.NodeId == "release-inventory");
    }

    /// <summary>O5t: a cancelled run refuses every action.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O5t_a_cancelled_run_refuses_a_decision(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        await using var harness = await StartWaitingAsync(host, WaitingParticipant(), ReservationJourney());
        var waiting = await WaitingIncidentAsync(harness);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await OperatorActions.CancelAsync(harness, RunId, "o5t-cancel", Partition)).Status);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await harness.ReadRunAsync(RunId)).WorkflowState!.Status);

        var decision = await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, ResolveAbsent, "o5t-resolve", Partition);

        Assert.Equal("WorkflowTerminal", Assert.Single(decision.Outcomes).Code);
    }

    /// <summary>O5x: an invalid reason or a code the scope did not offer is refused and changes nothing.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O5x_an_invalid_reason_or_an_unoffered_code_is_refused_and_changes_nothing(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        await using var harness = await StartWaitingAsync(host, WaitingParticipant(), ReservationJourney());
        var waiting = await WaitingIncidentAsync(harness);
        var before = await SnapshotAsync(harness);

        var blank = await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, ResolveAbsent, "o5x-blank", Partition, reason: "   ");
        var tooLong = await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, ResolveAbsent, "o5x-long", Partition, reason: new string('r', 501));
        var notOffered = await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, "compensation.confirm", "o5x-code", Partition);

        Assert.Equal("InvalidDecideInterventionPayload", Assert.Single(blank.Outcomes).Code);
        Assert.Equal("InvalidDecideInterventionPayload", Assert.Single(tooLong.Outcomes).Code);
        Assert.Equal("DecisionNotOffered", Assert.Single(notOffered.Outcomes).Code);
        Assert.Equal(before, await SnapshotAsync(harness));
    }

    /// <summary>O7a (FR-406): a run started under another execution authority is altered by id by an operator of its tenant.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O7a_a_run_started_under_another_authority_is_decided_by_id(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = WaitingParticipant();
        await using var harness = await StartWaitingAsync(host, participant, ReservationJourney());
        var waiting = await WaitingIncidentAsync(harness);

        // The runtime REST API seals its plans to root initiator runtime-api; this run was started by another initiator.
        var job = await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, ResolveAbsent, "o7a-resolve", Partition, rootInitiator: "runtime-api");

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        Assert.Equal(OperatorActions.HarnessAuthority, job.CapturedConcurrency!.Authority!.RootInitiator);
        AssertResolvedHandlerRecovered(await harness.ReadRunAsync(RunId), participant);
    }

    /// <summary>O8, injection point 1: the plan is accepted and the host stops before its job runs.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O8_1_a_stop_before_the_job_runs_applies_the_decision_once_after_restart(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = WaitingParticipant();
        IncidentState waiting;
        string planId;
        await using (var first = await StartWaitingAsync(host, participant, ReservationJourney()))
        {
            waiting = await WaitingIncidentAsync(first);
            planId = (await OperatorActions.SubmitDecisionAsync(first, RunId, waiting.IncidentId, ResolveAbsent, "o8-1", Partition)).Plan.PlanId;
        }

        await using var second = await host.StartHostAsync(participant);
        host.ExpireLeases();
        var job = await OperatorActions.OrchestrateAsync(second, RunId, planId);

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        AssertResolvedHandlerRecovered(await second.ReadRunAsync(RunId), participant);
        Assert.Single(participant.Calls, call => call.NodeId == "release-inventory");
        Assert.Single(await DecidedIncidentsAsync(second));
    }

    /// <summary>O8, injection point 2: the alteration checkpoint commits and the host stops before the delivery.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O8_2_a_stop_after_the_alteration_checkpoint_delivers_the_decision_once_after_restart(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = WaitingParticipant();
        await using (var first = await host.StartHostAsync(participant, HostStop.BeforeDecisionDelivery()))
        {
            await CompensationHost.StartAsync(first, ReservationJourney());
            await first.SweepUntilQuietAsync();
            var waiting = await WaitingIncidentAsync(first);
            var job = await OperatorActions.DecideAsync(first, RunId, waiting.IncidentId, ResolveAbsent, "o8-2", Partition, hostMayStop: true);
            Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
            Assert.Equal(IncidentStatus.Resolved, (await IncidentAsync(first, waiting.IncidentId)).Status);
            Assert.DoesNotContain(participant.Calls, call => call.NodeId == "release-inventory");
        }

        await using var second = await host.StartHostAsync(participant);
        var run = await host.RecoverAsync(second);

        AssertResolvedHandlerRecovered(run, participant);
        Assert.Single(participant.Calls, call => call.NodeId == "release-inventory");
        Assert.Single(await DecidedIncidentsAsync(second));
    }

    /// <summary>O8, injection point 3: the decision evaluation commits and the host stops before the next handler runs.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task O8_3_a_stop_after_the_decision_evaluation_runs_the_next_handler_once_after_restart(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = WaitingParticipant().KillingHostBeforeEffectOf("release-inventory");
        await using (var first = await StartWaitingAsync(host, participant, ReservationJourney()))
        {
            var waiting = await WaitingIncidentAsync(first);
            await OperatorActions.DecideAsync(first, RunId, waiting.IncidentId, ResolveAbsent, "o8-3", Partition, hostMayStop: true);
            Assert.DoesNotContain("release-inventory", participant.EffectOrder);
            var scope = CompensationScopeStatePersister.Load((await first.ReadRunAsync(RunId)).State(ScopeNodeId))!;
            Assert.Equal(CompensationEntryStatus.ResolvedManually, scope.Entries.Single(entry => entry.Step == "Reserve delivery").Status);
        }

        await using var second = await host.StartHostAsync(participant);
        var run = await host.RecoverAsync(second);

        AssertResolvedHandlerRecovered(run, participant);
        Assert.Single(participant.Effects, effect => effect.NodeId == "release-inventory");
        Assert.Single(await DecidedIncidentsAsync(second));
    }

    /// <summary>
    /// J1: the journal of a real run while it waits and after it is abandoned, with and without the values grant; a
    /// completed scope keeps no private state, so the state view answers <c>StateCleared</c> for it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task J1_the_journal_follows_the_run_through_waiting_and_abandoned(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        await using var harness = await StartWaitingAsync(host, WaitingParticipant(), ReservationJourney());
        var waiting = await WaitingIncidentAsync(harness);

        var waitingView = await JournalAsync(harness, canInspectValues: false);
        Assert.Equal("AwaitingIntervention", waitingView.GetProperty("phase").GetString());
        var delivery = waitingView.GetProperty("entries")[1];
        Assert.Equal(3, delivery.GetProperty("attempts").GetArrayLength());
        Assert.Equal(waiting.IncidentId, delivery.GetProperty("waiting").GetProperty("incidentId").GetString());
        Assert.Equal(CompensationScope.Decisions.Offer, delivery.GetProperty("waiting").GetProperty("decisions").EnumerateArray().Select(item => item.GetString()!));
        Assert.True(waitingView.GetProperty("cause").GetProperty("messageWithheld").GetBoolean());
        Assert.False((await JournalAsync(harness, canInspectValues: true)).GetProperty("cause").GetProperty("messageWithheld").GetBoolean());

        await OperatorActions.DecideAsync(harness, RunId, waiting.IncidentId, Abandon, "j1-abandon", Partition);

        var abandoned = await JournalAsync(harness, canInspectValues: false);
        Assert.Equal("Abandoned", abandoned.GetProperty("phase").GetString());
        Assert.Equal(waiting.IncidentId, abandoned.GetProperty("abandonment").GetProperty("incidentId").GetString());
        Assert.Empty(abandoned.GetProperty("remaining").EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task J1_a_completed_scope_keeps_no_journal(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var run = await host.RunAsync(new Participant(), ReservationJourney());

        run.AssertOutcomes(ScopeNodeId, ActivityOutcomes.Done);
        Assert.Null(run.State(ScopeNodeId).PrivateState);
    }

    private static async Task<JsonElement> JournalAsync(WorkflowExecutionHarness harness, bool canInspectValues)
    {
        var run = await harness.ReadRunAsync(RunId);
        var incidents = await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(RunId);
        var projector = harness.Services.GetServices<IActivityStateViewProjector>().Single();
        var executable = WorkflowExecutionHarness.NewExecutable(ReservationJourney());
        return await projector.ProjectAsync(new ActivityStateViewContext(run.WorkflowState!, run.State(ScopeNodeId), executable.RootActivity, incidents, canInspectValues));
    }

    // W: Cancel delivery fails three times (handlerMaxAttempts 3), so the scope waits on its last attempt's incident.
    private static Participant WaitingParticipant() => new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 3);

    // An uncertain Reserve delivery without a handler (spec 251 J6).
    private static ExecutableNode UncertainDelivery() =>
        Scope(
            new Step("Reserve inventory", Participant("reserve-inventory"), Participant("release-inventory")),
            new Step("Reserve delivery", Participant("reserve-delivery")),
            new Step("Confirm order", Participant("confirm-order")));

    private static async Task<WorkflowExecutionHarness> StartWaitingAsync(CompensationHost host, Participant participant, ExecutableNode root)
    {
        var harness = await host.StartHostAsync(participant);
        await CompensationHost.StartAsync(harness, root);
        await harness.SweepUntilQuietAsync();
        return harness;
    }

    private static async Task<IncidentState> WaitingIncidentAsync(WorkflowExecutionHarness harness)
    {
        var waiting = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(RunId));
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, waiting.ResolutionOutcome?.ActionKind);
        return waiting;
    }

    private static async Task<IncidentState> IncidentAsync(WorkflowExecutionHarness harness, string incidentId) =>
        (await harness.Services.GetRequiredService<IIncidentStateStore>().FindAsync(RunId, incidentId))!;

    private static async Task<IReadOnlyCollection<IncidentState>> DecidedIncidentsAsync(WorkflowExecutionHarness harness) =>
        (await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(RunId))
            .Where(incident => incident.ResolutionOutcome?.ActionKind == IncidentResolutionActionKinds.OperatorDecision)
            .ToArray();

    // The handler's last attempt ran inside the Sequence its occurrence belongs to.
    private static string SequenceOf(WorkflowExecutionRun run, IncidentState waiting) =>
        run.ActivityStates.Single(state => state.Execution.ActivityExecutionId == waiting.ActivityExecutionId).ParentActivityExecutionId!;

    private static void AssertResolvedHandlerRecovered(WorkflowExecutionRun run, Participant participant)
    {
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Contains("release-inventory", participant.EffectOrder);
        // The operator said the delivery was undone elsewhere: it is still reserved here, and its handler never ran again.
        Assert.Single(participant.Present);
        Assert.Equal(3, participant.Calls.Count(call => call.NodeId == "cancel-delivery"));
    }

    // Cancels the waiting scope and its live descendants while the run goes on, as a cancellation from an enclosing
    // scope leaves them: the faulted handler and its blocking incident are untouched.
    private static async Task CancelScopeAsync(WorkflowExecutionHarness harness)
    {
        var store = harness.Services.GetRequiredService<IActivityExecutionStateStore>();
        var run = await harness.ReadRunAsync(RunId);
        var scope = run.State(ScopeNodeId);
        await store.SaveAsync(scope with { Status = ActivityExecutionStatus.Cancelled, SubStatus = "ParentCancelled", CompletedAt = DateTimeOffset.UtcNow });
    }

    // Activity, incident and scope state, byte for byte (SC-403).
    private static async Task<string> SnapshotAsync(WorkflowExecutionHarness harness)
    {
        var run = await harness.ReadRunAsync(RunId);
        var incidents = await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(RunId);
        return JsonSerializer.Serialize(new
        {
            activities = run.ActivityStates.OrderBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal).ToArray(),
            incidents = incidents.OrderBy(incident => incident.IncidentId, StringComparer.Ordinal).ToArray()
        });
    }
}
