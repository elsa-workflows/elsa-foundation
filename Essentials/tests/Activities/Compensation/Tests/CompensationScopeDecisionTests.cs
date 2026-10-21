using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Exceptions;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// Spec 260 transitions, driven through the scope's callbacks with the state each evaluation committed: the offer at
/// both intervention points, resolve and abandon on a waiting handler and on a waiting uncertain step, descendant-fault
/// cancellation, repeats, stale incidents, nesting of an abandoned scope, and state version 4.
/// </summary>
public sealed class CompensationScopeDecisionTests
{
    private const string ScopeExecutionId = "actexec-scope";
    private const string UnknownCategory = CompensationScope.OutcomeUnknownCategory;
    private const string ResolveAbsent = CompensationScope.Decisions.ResolveEffectAbsent;
    private const string ResolveAccepted = CompensationScope.Decisions.ResolveEffectAccepted;
    private const string Abandon = CompensationScope.Decisions.Abandon;

    private static readonly ExecutableNode Journey = ReservationJourney();

    // An uncertain step without a handler in the middle, so effect-accepted has a next step to run.
    private static readonly ExecutableNode UncertainMiddle = Scope(
        new Step("Reserve inventory", Participant("reserve-inventory"), Participant("release-inventory")),
        new Step("Book carrier", Participant("book-carrier")),
        new Step("Confirm order", Participant("confirm-order")));

    // --- T410: the offer ---

    [Fact]
    public async Task A_handler_out_of_attempts_offers_the_three_decisions_in_order()
    {
        var waiting = await HandlerWaitingAsync();

        var intervention = Assert.Single(waiting.Context.GetChildFaultInterventionRequests());
        Assert.Equal([ResolveAbsent, ResolveAccepted, Abandon], intervention.Decisions);
        Assert.Equal("incident-cancel-1", intervention.IncidentId);
    }

    [Fact]
    public async Task An_uncertain_step_without_a_handler_offers_the_three_decisions_in_order()
    {
        var waiting = await StepWaitingAsync(Journey, "confirm-order");

        var intervention = Assert.Single(waiting.Context.GetChildFaultInterventionRequests());
        Assert.Equal([ResolveAbsent, ResolveAccepted, Abandon], intervention.Decisions);
    }

    // --- T411: resolve on a waiting handler ---

    [Theory]
    [InlineData(ResolveAbsent, "EffectAbsent")]
    [InlineData(ResolveAccepted, "EffectAccepted")]
    public async Task Resolving_a_failed_handler_settles_the_entry_and_recovery_continues(string decision, string expectedOutcome)
    {
        var outcome = Enum.Parse<CompensationResolutionOutcome>(expectedOutcome);
        var waiting = await HandlerWaitingAsync();

        var resolved = await DecidedAsync(Journey, waiting.State, decision, "incident-cancel-1", "actexec-cancel-1", "cancel-delivery");

        Assert.True(resolved.Continuation.IsDeferred);
        Assert.Empty(resolved.Context.GetChildSubtreeCancellationRequests());
        var state = StateOf(resolved);
        Assert.Equal(CompensationScopePhase.Compensating, state.Phase);
        var entry = state.Entries.Single(candidate => candidate.Step == "Reserve delivery");
        Assert.Equal(CompensationEntryStatus.ResolvedManually, entry.Status);
        Assert.Equal(new CompensationEntryResolution(outcome, "incident-cancel-1"), entry.Resolution);
        Assert.Null(entry.InterventionIncidentId);
        Assert.Equal("release-inventory", Assert.Single(resolved.Requests).ExecutableNodeId);
        Assert.Equal(CompensationRecoveryCause.FaultKind, state.Cause!.Kind);
    }

    [Fact]
    public async Task Resolving_the_last_failed_handler_finishes_compensated()
    {
        // Inventory's handler already ran: resolving delivery leaves nothing to undo.
        var waiting = await HandlerWaitingAsync(inventoryAlreadyCompensated: true);

        var resolved = await DecidedAsync(Journey, waiting.State, ResolveAbsent, "incident-cancel-1", "actexec-cancel-1", "cancel-delivery");

        Assert.True(resolved.Continuation.IsComplete);
        Assert.Equal(CompensationScope.CompensatedOutcome, resolved.Continuation.OutcomeName);
        Assert.Equal(CompensationScopePhase.Compensated, StateOf(resolved).Phase);
    }

    [Fact]
    public async Task Resolving_the_last_failed_handler_with_faultAfterCompensation_faults()
    {
        var journey = ReservationJourney(faultAfterCompensation: true);
        var waiting = await HandlerWaitingAsync(inventoryAlreadyCompensated: true, journey: journey);

        var resolved = await DecidedAsync(journey, waiting.State, ResolveAccepted, "incident-cancel-1", "actexec-cancel-1", "cancel-delivery", faultAfterCompensation: true);

        Assert.Equal(RuntimeStructuralContinuationKind.Fault, resolved.Continuation.Kind);
        Assert.Equal(CompensationScope.CompensatedFaultCode, resolved.Continuation.Fault!.Code);
    }

    // --- T411: resolve on a waiting uncertain step ---

    [Fact]
    public async Task Resolving_an_uncertain_step_as_effect_absent_recovers_the_earlier_steps()
    {
        var waiting = await StepWaitingAsync(UncertainMiddle, "book-carrier");

        var resolved = await DecidedAsync(UncertainMiddle, waiting.State, ResolveAbsent, "incident-book-carrier", "actexec-book-carrier", "book-carrier");

        var state = StateOf(resolved);
        Assert.Equal(CompensationScopePhase.Compensating, state.Phase);
        var entry = state.Entries.Single(candidate => candidate.Step == "Book carrier");
        Assert.Equal(CompensationEntryStatus.ResolvedManually, entry.Status);
        Assert.Equal(CompensationResolutionOutcome.EffectAbsent, entry.Resolution!.Outcome);
        Assert.Equal(CompensationRecoveryCause.UncertainKind, state.Cause!.Kind);
        Assert.Equal("release-inventory", Assert.Single(resolved.Requests).ExecutableNodeId);
    }

    [Fact]
    public async Task Resolving_an_uncertain_step_as_effect_accepted_runs_the_next_step()
    {
        var waiting = await StepWaitingAsync(UncertainMiddle, "book-carrier");

        var resolved = await DecidedAsync(UncertainMiddle, waiting.State, ResolveAccepted, "incident-book-carrier", "actexec-book-carrier", "book-carrier");

        Assert.True(resolved.Continuation.IsDeferred);
        var state = StateOf(resolved);
        Assert.Equal(CompensationScopePhase.Running, state.Phase);
        var next = Assert.Single(resolved.Requests);
        Assert.Equal("confirm-order", next.ExecutableNodeId);
        Assert.Equal("actexec-book-carrier", next.SchedulingActivityExecutionId);
        var entry = state.Entries.Single(candidate => candidate.Step == "Book carrier");
        Assert.Equal((CompensationEntryStatus.ResolvedManually, CompensationResolutionOutcome.EffectAccepted), (entry.Status, entry.Resolution!.Outcome));
    }

    [Fact]
    public async Task Resolving_the_last_step_as_effect_accepted_completes_Done_without_handing_the_step_over()
    {
        var waiting = await StepWaitingAsync(Journey, "confirm-order");

        var resolved = await DecidedAsync(Journey, waiting.State, ResolveAccepted, "incident-confirm-order", "actexec-confirm-order", "confirm-order");

        Assert.True(resolved.Continuation.IsComplete);
        Assert.Equal(ActivityOutcomes.Done, resolved.Continuation.OutcomeName);
        var handOver = Assert.Single(resolved.Context.GetAncestorNotificationRequests());
        var payload = handOver.Payload!.Value;
        var steps = payload.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("step").GetString()).ToArray();
        Assert.Equal(["Reserve inventory", "Reserve delivery"], steps);
    }

    [Fact]
    public async Task Resolving_a_step_whose_descendant_faulted_cancels_the_step_s_subtree()
    {
        var waiting = await StepWaitingAsync(UncertainMiddle, "book-carrier");

        var resolved = await DecidedAsync(UncertainMiddle, waiting.State, ResolveAccepted, "incident-book-carrier", "actexec-leaf-inside", "leaf-inside",
            viaChild: ("actexec-book-carrier", "book-carrier"));

        var cancellation = Assert.Single(resolved.Context.GetChildSubtreeCancellationRequests());
        Assert.Equal("actexec-book-carrier", cancellation.ChildActivityExecutionId);
        Assert.True(resolved.Continuation.IsDeferred);
    }

    [Fact]
    public async Task A_resolve_whose_finish_faults_does_not_stage_the_subtree_cancellation()
    {
        var journey = ReservationJourney(faultAfterCompensation: true);
        var waiting = await HandlerWaitingAsync(inventoryAlreadyCompensated: true, journey: journey);

        var resolved = await DecidedAsync(journey, waiting.State, ResolveAbsent, "incident-cancel-1", "actexec-leaf-inside", "leaf-inside",
            viaChild: ("actexec-cancel-1", "cancel-delivery"), faultAfterCompensation: true);

        Assert.Equal(RuntimeStructuralContinuationKind.Fault, resolved.Continuation.Kind);
        Assert.Empty(resolved.Context.GetChildSubtreeCancellationRequests());
    }

    // --- T411: abandon ---

    [Fact]
    public async Task Abandoning_a_waiting_handler_faults_the_scope_and_keeps_every_entry()
    {
        var waiting = await HandlerWaitingAsync();
        var before = StateOf(waiting);

        var abandoned = await DecidedAsync(Journey, waiting.State, Abandon, "incident-cancel-1", "actexec-cancel-1", "cancel-delivery");

        Assert.Equal(RuntimeStructuralContinuationKind.Fault, abandoned.Continuation.Kind);
        var fault = abandoned.Continuation.Fault!;
        Assert.Equal(CompensationScope.AbandonedFaultCode, fault.Code);
        Assert.Equal(UnknownCategory, fault.Category);
        Assert.Contains("2 step(s) were not undone", fault.Message, StringComparison.Ordinal);
        Assert.Empty(abandoned.Requests);
        Assert.Empty(abandoned.Context.GetChildSubtreeCancellationRequests());
        var state = StateOf(abandoned);
        Assert.Equal(CompensationScopePhase.Abandoned, state.Phase);
        Assert.Equal(new CompensationAbandonment("incident-cancel-1", "e2", CompensationScopePhase.Compensating), state.Abandonment);
        Assert.Equal(before.Entries.Select(entry => entry.Status), state.Entries.Select(entry => entry.Status));
        Assert.All(state.Entries, entry => Assert.Null(entry.InterventionIncidentId));
    }

    [Fact]
    public async Task Abandoning_a_waiting_step_records_that_it_waited_from_running()
    {
        var waiting = await StepWaitingAsync(Journey, "confirm-order");

        var abandoned = await DecidedAsync(Journey, waiting.State, Abandon, "incident-confirm-order", "actexec-leaf-inside", "leaf-inside",
            viaChild: ("actexec-confirm-order", "confirm-order"));

        Assert.Equal(CompensationScope.AbandonedFaultCode, abandoned.Continuation.Fault!.Code);
        Assert.Equal(CompensationScopePhase.Running, StateOf(abandoned).Abandonment!.FromPhase);
        // A fault cannot ride a subtree cancellation: the suspended step is left to the next scope out (spec 258 R13).
        Assert.Empty(abandoned.Context.GetChildSubtreeCancellationRequests());
    }

    [Fact]
    public async Task An_abandoned_scope_ignores_outcomes_hand_overs_confirms_and_decisions()
    {
        var waiting = await HandlerWaitingAsync();
        var abandoned = await DecidedAsync(Journey, waiting.State, Abandon, "incident-cancel-1", "actexec-cancel-1", "cancel-delivery");

        var completion = await CompletedAsync(Journey, abandoned.State, HandlerMetadata("e2", "Reserve delivery", attempt: 1), "actexec-cancel-late", "cancel-delivery");
        var decision = await DecidedAsync(Journey, abandoned.State, ResolveAbsent, "incident-cancel-1", "actexec-cancel-1", "cancel-delivery");
        var notification = await NotifiedAsync(Journey, abandoned.State, CompensationScope.Notifications.Confirm);

        Assert.All(new[] { completion, decision, notification }, evaluation =>
        {
            Assert.True(evaluation.Continuation.IsDeferred);
            Assert.Null(evaluation.Continuation.StateUpdate);
            Assert.Empty(evaluation.Requests);
        });
    }

    // --- T411: repeats and stale incidents ---

    [Fact]
    public async Task A_decision_for_an_incident_no_entry_waits_on_changes_nothing()
    {
        var waiting = await HandlerWaitingAsync();

        var stale = await DecidedAsync(Journey, waiting.State, ResolveAbsent, "incident-other", "actexec-cancel-1", "cancel-delivery");

        Assert.True(stale.Continuation.IsDeferred);
        Assert.Null(stale.Continuation.StateUpdate);
        Assert.Empty(stale.Requests);
    }

    [Fact]
    public async Task A_repeated_decision_after_the_entry_was_resolved_changes_nothing()
    {
        var waiting = await HandlerWaitingAsync();
        var resolved = await DecidedAsync(Journey, waiting.State, ResolveAbsent, "incident-cancel-1", "actexec-cancel-1", "cancel-delivery");

        var repeat = await DecidedAsync(Journey, resolved.State, ResolveAbsent, "incident-cancel-1", "actexec-cancel-1", "cancel-delivery");

        Assert.True(repeat.Continuation.IsDeferred);
        Assert.Null(repeat.Continuation.StateUpdate);
        Assert.Empty(repeat.Requests);
    }

    [Fact]
    public async Task A_decision_the_scope_never_offers_is_refused()
    {
        var waiting = await HandlerWaitingAsync();

        await Assert.ThrowsAsync<CompensationScopeExecutionException>(() =>
            DecidedAsync(Journey, waiting.State, "compensation.other", "incident-cancel-1", "actexec-cancel-1", "cancel-delivery"));
    }

    // --- FR-411: a resolved entry is never eligible, confirmed or handed over ---

    [Fact]
    public void A_manually_resolved_entry_is_never_confirmed_or_handed_over()
    {
        var state = CompensationScopeState.Initial with
        {
            NextStepIndex = 1,
            NextOrdinal = 2,
            Entries =
            [
                new CompensationEntry("e1", "Reserve inventory", 1, CompensationEntryStatus.ResolvedManually, "ae-1", "k-1",
                    resolution: new CompensationEntryResolution(CompensationResolutionOutcome.EffectAccepted, "inc-1"))
            ]
        };
        var context = NewContext(Journey, Stage(state), new Dictionary<string, string>());

        var continuation = new CompensationScope().OnChildNotifiedAsync(context, new ActivityChildNotifiedContext(
            "actexec-confirm", "confirm-node", CompensationScope.Notifications.Confirm,
            viaChildActivityExecutionId: "actexec-reserve-inventory", viaChildExecutableNodeId: "reserve-inventory")).Result;

        var after = CompensationScopeStatePersister.Deserialize(continuation.StateUpdate!.Value.InlineValue!.Value);
        Assert.Equal(CompensationEntryStatus.ResolvedManually, Assert.Single(after.Entries).Status);
        Assert.Empty(Assert.Single(after.Confirmations).EntryIds);
    }

    // --- T412: an abandoned inner scope inside an outer scope ---

    [Fact]
    public async Task An_abandoned_inner_scope_with_a_handler_is_journaled_uncertain_and_undone()
    {
        var outer = Scope(
            new Step("Inner", Participant("inner-scope"), Participant("undo-inner")),
            new Step("After", Participant("after")));
        var started = await ExecuteAsync(outer);

        var faulted = await FaultedAsync(outer, started.State, Assert.Single(started.Requests), "actexec-inner", "incident-inner",
            CompensationScope.AbandonedFaultCode, UnknownCategory);

        Assert.Single(faulted.Context.GetChildFaultAbsorptionRequests());
        var entry = Assert.Single(StateOf(faulted).Entries);
        Assert.Equal((CompensationEntryStatus.Compensating, CompensationForwardOutcome.Uncertain), (entry.Status, entry.ForwardOutcome));
        Assert.Equal("undo-inner", Assert.Single(faulted.Requests).ExecutableNodeId);
    }

    [Fact]
    public async Task An_abandoned_inner_scope_without_a_handler_makes_the_outer_scope_wait_with_the_same_offer()
    {
        var outer = Scope(
            new Step("Pay", Participant("pay"), Participant("refund")),
            new Step("Inner", Participant("inner-scope")));
        var started = await ExecuteAsync(outer);
        var paid = await CompletedAsync(outer, started.State, Assert.Single(started.Requests).Metadata, "actexec-pay", "pay");

        var faulted = await FaultedAsync(outer, paid.State, Assert.Single(paid.Requests), "actexec-inner", "incident-inner",
            CompensationScope.AbandonedFaultCode, UnknownCategory);

        var intervention = Assert.Single(faulted.Context.GetChildFaultInterventionRequests());
        Assert.Equal([ResolveAbsent, ResolveAccepted, Abandon], intervention.Decisions);
        Assert.Equal(CompensationScopePhase.AwaitingIntervention, StateOf(faulted).Phase);

        // Resolving it as effect absent undoes the step before it.
        var resolved = await DecidedAsync(outer, faulted.State, ResolveAbsent, "incident-inner", "actexec-inner", "inner-scope");
        Assert.Equal("refund", Assert.Single(resolved.Requests).ExecutableNodeId);
    }

    [Fact]
    public async Task An_abandoned_inner_scope_as_a_handler_is_a_failed_attempt()
    {
        var outer = Scope(ScopeNodeId, faultAfterCompensation: false, handlerMaxAttempts: 2,
            new Step("Reserve", Participant("reserve"), Participant("inner-undo-scope")),
            new Step("Fail", Participant("fail")));
        var started = await ExecuteAsync(outer);
        var reserved = await CompletedAsync(outer, started.State, Assert.Single(started.Requests).Metadata, "actexec-reserve", "reserve");
        var failed = await FaultedAsync(outer, reserved.State, Assert.Single(reserved.Requests), "actexec-fail", "incident-fail", "participant.fault", null);
        var undo = Assert.Single(failed.Requests);

        var first = await FaultedAsync(outer, failed.State, undo, "actexec-undo-1", "incident-undo-1", CompensationScope.AbandonedFaultCode, UnknownCategory, maxAttempts: 2);
        Assert.Single(first.Context.GetChildFaultAbsorptionRequests());
        var retry = Assert.Single(first.Requests);

        var second = await FaultedAsync(outer, first.State, retry, "actexec-undo-2", "incident-undo-2", CompensationScope.AbandonedFaultCode, UnknownCategory, maxAttempts: 2);
        var intervention = Assert.Single(second.Context.GetChildFaultInterventionRequests());
        Assert.Equal([ResolveAbsent, ResolveAccepted, Abandon], intervention.Decisions);
        Assert.Equal(CompensationEntryStatus.Failed, StateOf(second).Entries.Single(entry => entry.Step == "Reserve").Status);
    }

    // --- T409: state version 4 ---

    [Fact]
    public void A_version_4_state_round_trips_with_its_resolution_and_abandonment()
    {
        var state = CompensationScopeState.Initial with
        {
            Phase = CompensationScopePhase.Abandoned,
            Entries =
            [
                new CompensationEntry("e1", "Reserve A", 1, CompensationEntryStatus.ResolvedManually, "ae-1", "k-1",
                    resolution: new CompensationEntryResolution(CompensationResolutionOutcome.EffectAbsent, "inc-1")),
                new CompensationEntry("e2", "Reserve B", 2, CompensationEntryStatus.Failed, "ae-2", "k-2")
            ],
            Abandonment = new CompensationAbandonment("inc-2", "e2", CompensationScopePhase.Compensating)
        };

        var staged = Stage(state);
        Assert.Equal(4, staged.StateVersion);
        var json = staged.Value.InlineValue!.Value.GetRawText();
        Assert.Contains("\"ResolvedManually\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Abandoned\"", json, StringComparison.Ordinal);
        Assert.Contains("\"EffectAbsent\"", json, StringComparison.Ordinal);

        var loaded = Load(staged.Value.InlineValue!.Value, staged.StateVersion);
        Assert.Equal(CompensationScopePhase.Abandoned, loaded.Phase);
        Assert.Equal(state.Abandonment, loaded.Abandonment);
        Assert.Equal(new CompensationEntryResolution(CompensationResolutionOutcome.EffectAbsent, "inc-1"), loaded.Entries[0].Resolution);
    }

    [Fact]
    public void A_version_3_state_is_upgraded_unchanged_and_written_back_as_version_4()
    {
        var v3 = JsonSerializer.SerializeToElement(new
        {
            phase = "AwaitingIntervention",
            nextStepIndex = 3,
            nextOrdinal = 3,
            cause = new { kind = "fault", step = "Confirm order", activityExecutionId = "ae-3", incidentId = "inc-3" },
            entries = new object[]
            {
                new
                {
                    id = "e1", step = "Reserve inventory", ordinal = 1, status = "Failed", forwardActivityExecutionId = "ae-1", operationKey = "k-1",
                    forwardOutcome = "Completed", interventionIncidentId = "inc-5",
                    handlerAttempts = new object[] { new { result = "Faulted", activityExecutionId = "ae-9", incidentId = "inc-5" } }
                }
            },
            confirmations = Array.Empty<object>(),
            receivedHandOvers = new[] { "ae-inner" },
            handOversInProgress = Array.Empty<object>()
        });

        var state = Load(v3, version: 3);

        Assert.Equal(CompensationScopePhase.AwaitingIntervention, state.Phase);
        var entry = Assert.Single(state.Entries);
        Assert.Equal((CompensationEntryStatus.Failed, "inc-5"), (entry.Status, entry.InterventionIncidentId));
        Assert.Null(entry.Resolution);
        Assert.Null(state.Abandonment);
        Assert.Equal(["ae-inner"], state.ReceivedHandOvers);
        Assert.Equal(4, Stage(state).StateVersion);
    }

    [Fact]
    public void A_version_1_state_chains_through_every_upgrade()
    {
        var v1 = JsonSerializer.SerializeToElement(new
        {
            phase = "Compensating",
            nextStepIndex = 2,
            nextOrdinal = 2,
            entries = new object[]
            {
                new { id = "e1", step = "Reserve inventory", ordinal = 1, status = "Compensated", forwardActivityExecutionId = "ae-1", handlerActivityExecutionIds = new[] { "ae-4" } }
            }
        });

        var state = Load(v1, version: 1);

        var entry = Assert.Single(state.Entries);
        Assert.Equal(("ae-1", CompensationEntryStatus.Compensated), (entry.OperationKey, entry.Status));
        Assert.Empty(state.Confirmations);
        Assert.Null(state.Abandonment);
        Assert.Equal(4, Stage(state).StateVersion);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(99)]
    public void An_unknown_state_version_is_refused(int version)
    {
        var payload = JsonSerializer.SerializeToElement(new { phase = "Running", nextStepIndex = 0, nextOrdinal = 1 });

        Assert.Throws<CompensationScopeExecutionException>(() => Load(payload, version));
    }

    // --- Helpers ---

    // Runs the reservation journey to Cancel delivery waiting for an operator after one failed attempt (handlerMaxAttempts 1).
    private static async Task<Evaluation> HandlerWaitingAsync(bool inventoryAlreadyCompensated = false, ExecutableNode? journey = null)
    {
        journey ??= Journey;
        var started = await ExecuteAsync(journey);
        var inventory = await CompletedAsync(journey, started.State, Assert.Single(started.Requests).Metadata, "actexec-reserve-inventory", "reserve-inventory");
        var delivery = await CompletedAsync(journey, inventory.State, Assert.Single(inventory.Requests).Metadata, "actexec-reserve-delivery", "reserve-delivery");
        var confirm = Assert.Single(delivery.Requests);
        var faulted = await FaultedAsync(journey, delivery.State, confirm, "actexec-confirm-order", "incident-confirm", "participant.fault", null, maxAttempts: 1);
        var cancel = Assert.Single(faulted.Requests);
        var waiting = await FaultedAsync(journey, faulted.State, cancel, "actexec-cancel-1", "incident-cancel-1", "participant.fault", null, maxAttempts: 1);
        Assert.Equal(CompensationScopePhase.AwaitingIntervention, StateOf(waiting).Phase);
        if (!inventoryAlreadyCompensated)
            return waiting;

        var state = StateOf(waiting);
        var withInventoryDone = state with
        {
            Entries = state.Entries
                .Select(entry => entry.Step == "Reserve inventory" ? entry with { Status = CompensationEntryStatus.Compensated } : entry)
                .ToArray()
        };
        return waiting with { Continuation = CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, withInventoryDone) };
    }

    // Runs <paramref name="journey"/> until <paramref name="nodeId"/>'s step faults with an unknown outcome and waits.
    private static async Task<Evaluation> StepWaitingAsync(ExecutableNode journey, string nodeId)
    {
        var evaluation = await ExecuteAsync(journey);
        while (true)
        {
            var step = Assert.Single(evaluation.Requests);
            if (step.ExecutableNodeId == nodeId)
            {
                var waiting = await FaultedAsync(journey, evaluation.State, step, $"actexec-{nodeId}", $"incident-{nodeId}", "participant.fault", UnknownCategory);
                Assert.Equal(CompensationScopePhase.AwaitingIntervention, StateOf(waiting).Phase);
                return waiting;
            }

            evaluation = await CompletedAsync(journey, evaluation.State, step.Metadata, $"actexec-{step.ExecutableNodeId}", step.ExecutableNodeId);
        }
    }

    private static Dictionary<string, string> HandlerMetadata(string entryId, string step, int attempt) => new(StringComparer.Ordinal)
    {
        [CompensationScope.MetadataKeys.Role] = CompensationScope.MetadataKeys.HandlerRole,
        [CompensationScope.MetadataKeys.Scope] = ScopeExecutionId,
        [CompensationScope.MetadataKeys.Step] = step,
        [CompensationScope.MetadataKeys.Entry] = entryId,
        [CompensationScope.MetadataKeys.Attempt] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    private static async Task<Evaluation> ExecuteAsync(ExecutableNode journey)
    {
        var context = NewContext(journey, state: null, commandMetadata: new Dictionary<string, string>());
        var continuation = await ((IRuntimeStructuralActivity)new CompensationScope()).ExecuteStructureAsync(context);
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray());
    }

    private static async Task<Evaluation> CompletedAsync(
        ExecutableNode journey,
        RuntimeStructuralStateUpdate? state,
        IReadOnlyDictionary<string, string> metadata,
        string childExecutionId,
        string childNodeId,
        int maxAttempts = 3)
    {
        var context = NewContext(journey, state, metadata);
        var continuation = await new CompensationScope { HandlerMaxAttempts = maxAttempts }.OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, childExecutionId, childNodeId, [ActivityOutcomes.Done]));
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray(), state);
    }

    private static async Task<Evaluation> FaultedAsync(
        ExecutableNode journey,
        RuntimeStructuralStateUpdate? state,
        RuntimeChildActivityScheduleRequest child,
        string childExecutionId,
        string incidentId,
        string faultCode,
        string? category,
        int maxAttempts = 3)
    {
        var context = NewContext(journey, state, child.Metadata);
        var continuation = await new CompensationScope { HandlerMaxAttempts = maxAttempts }.OnChildFaultedAsync(
            new ActivityChildFaultedContext(context, childExecutionId, child.ExecutableNodeId, incidentId, faultCode: faultCode, faultCategory: category));
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray(), state);
    }

    private static async Task<Evaluation> DecidedAsync(
        ExecutableNode journey,
        RuntimeStructuralStateUpdate? state,
        string decision,
        string incidentId,
        string faultedId,
        string faultedNodeId,
        (string Id, string NodeId)? viaChild = null,
        bool faultAfterCompensation = false)
    {
        var context = NewContext(journey, state, new Dictionary<string, string>());
        var via = viaChild ?? (faultedId, faultedNodeId);
        var continuation = await new CompensationScope { FaultAfterCompensation = faultAfterCompensation }.OnInterventionDecidedAsync(
            context,
            new ActivityInterventionDecidedContext(incidentId, decision, faultedId, faultedNodeId, via.Id, via.NodeId));
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray(), state);
    }

    private static async Task<Evaluation> NotifiedAsync(ExecutableNode journey, RuntimeStructuralStateUpdate? state, string code)
    {
        var context = NewContext(journey, state, new Dictionary<string, string>());
        var continuation = await new CompensationScope().OnChildNotifiedAsync(
            context,
            new ActivityChildNotifiedContext("actexec-confirm", "confirm-node", code,
                viaChildActivityExecutionId: "actexec-reserve-inventory", viaChildExecutableNodeId: "reserve-inventory"));
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray(), state);
    }

    private static RuntimeStructuralStateUpdate Stage(CompensationScopeState state) =>
        CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, state).StateUpdate!;

    private static CompensationScopeState Load(JsonElement payload, int version) =>
        CompensationScopeStatePersister.Load(ScopeState(new ActivityPrivateState(
            ScopeExecutionId,
            version,
            ValueEnvelope.Inline(new ValueTypeDescriptor(CompensationScopeStatePersister.StateTypeAlias, schemaVersion: version), payload, ValueProtectionPolicy.InstanceInline),
            "attempt-1",
            DateTimeOffset.UnixEpoch)))!;

    private static CompensationScopeState StateOf(Evaluation evaluation)
    {
        var update = evaluation.State ?? throw new InvalidOperationException("The evaluation has no committed state.");
        return CompensationScopeStatePersister.Deserialize(update.Value.InlineValue!.Value);
    }

    private static SimpleActivityExecutionContext NewContext(ExecutableNode journey, RuntimeStructuralStateUpdate? state, IReadOnlyDictionary<string, string> commandMetadata) =>
        new(
            new CompensationScope(),
            CancellationToken.None,
            "wfexec-1",
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            new RuntimeSchedulerWorkItem(
                workItemId: "work-scope",
                workflowExecutionId: "wfexec-1",
                commandId: "command-scope",
                commandKind: WorkflowExecutionCommandKind.InvokeActivity,
                envelopeId: "envelope-1",
                idempotencyKey: "wfexec-1:scope",
                enqueuedAt: DateTimeOffset.UnixEpoch,
                recordedAt: DateTimeOffset.UnixEpoch,
                sequence: 1,
                payload: null,
                commandMetadata: commandMetadata,
                envelopeMetadata: new Dictionary<string, string>()),
            journey,
            ScopeState(state is null ? null : new ActivityPrivateState(ScopeExecutionId, state.StateVersion, state.Value, "attempt-1", DateTimeOffset.UnixEpoch)));

    private static ActivityExecutionState ScopeState(ActivityPrivateState? privateState) =>
        new(
            Execution: new ActivityExecution(ScopeExecutionId, "wfexec-1", ScopeNodeId, $"authored-{ScopeNodeId}", typeof(CompensationScope).FullName!, "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>())
        {
            PrivateState = privateState
        };

    // The state the evaluation committed: its staged update, or the state it was given when it staged none.
    private sealed record Evaluation(
        RuntimeStructuralContinuation Continuation,
        SimpleActivityExecutionContext Context,
        RuntimeChildActivityScheduleRequest[] Requests,
        RuntimeStructuralStateUpdate? Previous = null)
    {
        public RuntimeStructuralStateUpdate? State => Continuation.StateUpdate ?? Previous;
    }
}
