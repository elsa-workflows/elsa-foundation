using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
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
/// Spec 258 transitions, driven through the scope's callbacks with the state each evaluation committed: state v3 and
/// its upcast, hand-over on completion and its receipt, Confirm, Compensate and its cascade, and descendant faults.
/// </summary>
public sealed class CompensationScopeNestingTests
{
    private const string ScopeExecutionId = "actexec-scope";
    private const string ScopeAuthoredId = "authored-" + ScopeNodeId;
    private const string InnerScope = "actexec-inner";

    private static readonly ExecutableNode Journey = ReservationJourney();

    // One step without a handler: a Compensate in it leaves nothing to undo.
    private static readonly ExecutableNode CheckOnly = Scope(new Step("Check", Participant("check")));

    // A step with a handler, then one without.
    private static readonly ExecutableNode HandledThenCheck = Scope(
        new Step("Reserve inventory", Participant("reserve-inventory"), Participant("release-inventory")),
        new Step("Check", Participant("check")));

    [Fact]
    public void A_version_2_state_is_upgraded_with_no_confirmations_and_no_hand_overs()
    {
        var v2 = JsonSerializer.SerializeToElement(new
        {
            phase = "Running",
            nextStepIndex = 2,
            nextOrdinal = 2,
            entries = new object[]
            {
                new { id = "e1", step = "Reserve inventory", ordinal = 1, status = "Registered", forwardActivityExecutionId = "ae-1", operationKey = "k-1", forwardOutcome = "Completed", handlerAttempts = Array.Empty<object>() }
            }
        });

        var state = CompensationScopeStatePersister.Load(ScopeState(PrivateState(v2, version: 2)))!;

        Assert.Empty(state.Confirmations);
        Assert.Empty(state.ReceivedHandOvers);
        var entry = Assert.Single(state.Entries);
        Assert.Equal((CompensationEntryStatus.Registered, "k-1"), (entry.Status, entry.OperationKey));
        Assert.Null(entry.Origin);
        Assert.Null(entry.HandlerExecutableNodeId);

        // Written back as the current version (4 since spec 260), never downgraded.
        var staged = CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, state);
        Assert.Equal(4, staged.StateUpdate!.StateVersion);
    }

    [Fact]
    public void A_version_3_state_round_trips_with_its_origin_confirmations_and_hand_overs()
    {
        var state = CompensationScopeState.Initial with
        {
            Entries =
            [
                new CompensationEntry("e1", "Reserve A", 1, CompensationEntryStatus.Confirmed, "ae-1", "k-1",
                    handlerExecutableNodeId: "release-a", origin: new CompensationEntryOrigin(InnerScope, "e1"))
            ],
            Confirmations = [new CompensationConfirmation("ae-confirm", ["e1"])],
            ReceivedHandOvers = [InnerScope]
        };

        var staged = CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, state);
        var loaded = CompensationScopeStatePersister.Load(ScopeState(new ActivityPrivateState(ScopeExecutionId, staged.StateUpdate!.StateVersion, staged.StateUpdate.Value, "attempt-1", DateTimeOffset.UnixEpoch)))!;

        var entry = Assert.Single(loaded.Entries);
        Assert.Equal(new CompensationEntryOrigin(InnerScope, "e1"), entry.Origin);
        Assert.Equal("release-a", entry.HandlerExecutableNodeId);
        Assert.Equal(["e1"], Assert.Single(loaded.Confirmations).EntryIds);
        Assert.Equal([InnerScope], loaded.ReceivedHandOvers);
    }

    [Fact]
    public async Task Completing_Done_hands_every_eligible_entry_to_the_nearest_enclosing_scope()
    {
        var done = await RunAllStepsAsync(confirmOnComplete: false);

        Assert.True(done.Continuation.IsComplete);
        var request = Assert.Single(done.Context.GetAncestorNotificationRequests());
        Assert.Equal(CompensationScope.Notifications.Register, request.Code);
        Assert.Equal(CompensationScope.ActivityTypeKey, request.Selector.ActivityTypeKey);
        Assert.True(request.Selector.Optional);
        Assert.True(request.Selector.StopAtGraphBoundary);

        var handOver = Read<CompensationHandOver>(request.Payload);
        Assert.Equal((ScopeExecutionId, 0, 1), (handOver.OriginScope, handOver.Batch, handOver.BatchCount));
        Assert.Equal(["Reserve inventory", "Reserve delivery", "Confirm order"], handOver.Entries.Select(entry => entry.Step));
        Assert.Equal(["release-inventory", "cancel-delivery", null], handOver.Entries.Select(entry => entry.HandlerExecutableNodeId));
        Assert.Equal($"{ScopeExecutionId}/step/1", handOver.Entries[1].OperationKey);
    }

    [Fact]
    public async Task More_entries_than_one_message_carries_are_handed_over_in_ordered_batches()
    {
        var entries = Enumerable.Range(1, 20)
            .Select(ordinal => new CompensationEntry($"e{ordinal}", "Reserve inventory", ordinal, CompensationEntryStatus.Registered, $"ae-{ordinal}", $"k-{ordinal}"))
            .ToArray();
        var state = CompensationScopeState.Initial with { NextStepIndex = 3, NextOrdinal = 21, Entries = entries };

        var done = await CompletedAsync(Stage(state), Child("confirm-order", Step("Confirm order")), "actexec-confirm-order");

        var requests = done.Context.GetAncestorNotificationRequests().ToArray();
        Assert.Equal(2, requests.Length);
        var batches = requests.Select(request => Read<CompensationHandOver>(request.Payload)).ToArray();
        Assert.Equal([(0, 2), (1, 2)], batches.Select(batch => (batch.Batch, batch.BatchCount)));
        Assert.Equal(CompensationScope.HandOverBatchSize, batches[0].Entries.Count);
        Assert.Equal(Enumerable.Range(1, 21).Select(ordinal => $"e{ordinal}"), batches.SelectMany(batch => batch.Entries).Select(entry => entry.OriginEntryId));
    }

    [Fact]
    public async Task ConfirmOnComplete_confirms_the_entries_and_hands_nothing_over()
    {
        var done = await RunAllStepsAsync(confirmOnComplete: true);

        Assert.True(done.Continuation.IsComplete);
        Assert.Empty(done.Context.GetAncestorNotificationRequests());
        var state = StateOf(done);
        Assert.All(state.Entries, entry => Assert.Equal(CompensationEntryStatus.Confirmed, entry.Status));
        var confirmation = Assert.Single(state.Confirmations);
        Assert.Equal(ScopeExecutionId, confirmation.ActivityExecutionId);
        Assert.Equal(3, confirmation.EntryIds.Count);
    }

    [Fact]
    public async Task A_hand_over_is_journaled_with_fresh_ordinals_and_its_origin_and_a_repeat_changes_nothing()
    {
        var running = await StartAsync();
        var handOver = new CompensationHandOver(InnerScope, 0, 1,
        [
            HandedOver("e1", "Reserve A", "release-a"),
            HandedOver("e2", "Reserve B", "release-b")
        ]);

        var received = await NotifiedAsync(running.State, CompensationScope.Notifications.Register, handOver, via: "reserve-inventory");

        var state = StateOf(received);
        Assert.Equal(CompensationScopePhase.Running, state.Phase);
        Assert.Equal([1, 2], state.Entries.Select(entry => entry.Ordinal));
        Assert.Equal(["release-a", "release-b"], state.Entries.Select(entry => entry.HandlerExecutableNodeId));
        Assert.Equal(new CompensationEntryOrigin(InnerScope, "e2"), state.Entries[1].Origin);
        Assert.Equal([InnerScope], state.ReceivedHandOvers);

        var repeated = await NotifiedAsync(received.State, CompensationScope.Notifications.Register, handOver, via: "reserve-inventory");

        Assert.Null(repeated.Continuation.StateUpdate);
    }

    [Fact]
    public async Task A_hand_over_is_received_only_once_every_batch_has_arrived_in_any_order()
    {
        var running = await StartAsync();
        var first = new CompensationHandOver(InnerScope, 0, 2, [HandedOver("e1", "Reserve A", "release-a")]);
        var last = new CompensationHandOver(InnerScope, 1, 2, [HandedOver("e2", "Reserve B", "release-b")]);

        // The last batch arrives first: the origin is not complete yet, so the earlier batch is still accepted.
        var afterLast = await NotifiedAsync(running.State, CompensationScope.Notifications.Register, last, via: "reserve-inventory");
        Assert.Empty(StateOf(afterLast).ReceivedHandOvers);

        var afterFirst = await NotifiedAsync(afterLast.State, CompensationScope.Notifications.Register, first, via: "reserve-inventory");

        var state = StateOf(afterFirst);
        Assert.Equal(["release-b", "release-a"], state.Entries.Select(entry => entry.HandlerExecutableNodeId));
        Assert.Equal([InnerScope], state.ReceivedHandOvers);
        Assert.Empty(state.HandOversInProgress);

        // Once complete, a repeat of either batch changes nothing.
        var repeated = await NotifiedAsync(afterFirst.State, CompensationScope.Notifications.Register, last, via: "reserve-inventory");
        Assert.Null(repeated.Continuation.StateUpdate);
    }

    [Fact]
    public async Task A_transitive_hand_over_keeps_the_first_origin()
    {
        var running = await StartAsync();
        var first = new CompensationEntryOrigin("actexec-innermost", "e7");
        var handOver = new CompensationHandOver(InnerScope, 0, 1, [HandedOver("e3", "Reserve A", "release-a") with { Origin = first }]);

        var received = await NotifiedAsync(running.State, CompensationScope.Notifications.Register, handOver, via: "reserve-inventory");

        Assert.Equal(first, Assert.Single(StateOf(received).Entries).Origin);
    }

    [Fact]
    public async Task A_late_hand_over_during_recovery_is_undone_in_the_same_recovery_newest_first()
    {
        var (faulted, cancel) = await RunToFirstHandlerAsync();
        var handOver = new CompensationHandOver(InnerScope, 0, 1, [HandedOver("e1", "Reserve A", "release-a")]);

        var late = await NotifiedAsync(faulted.State, CompensationScope.Notifications.Register, handOver, via: "reserve-delivery");
        Assert.Empty(late.Requests);

        // The running handler finishes; the late entry is the newest, so its handler runs next with its own keys.
        var next = await CompletedAsync(late.State, cancel, "actexec-cancel-delivery");
        var release = Assert.Single(next.Requests);
        Assert.Equal("release-a", release.ExecutableNodeId);
        Assert.Equal("k-e1/compensate", release.Metadata[CompensationScope.MetadataKeys.OperationKey]);
        Assert.Equal(InnerScope, release.Metadata[CompensationScope.MetadataKeys.OriginScope]);
        Assert.Equal(InnerScope, release.SchedulingProvenance.Metadata[CompensationScope.MetadataKeys.OriginScope]);
        Assert.Equal(release.Metadata[CompensationScope.MetadataKeys.Entry], release.SchedulingProvenance.Metadata[CompensationScope.MetadataKeys.Entry]);
        Assert.Equal("ae-e1", release.SchedulingActivityExecutionId);
    }

    [Fact]
    public async Task Confirm_confirms_the_eligible_entries_once_and_keeps_them_as_evidence()
    {
        var afterInventory = await RunToDeliveryAsync();

        var confirmed = await NotifiedAsync(afterInventory.State, CompensationScope.Notifications.Confirm,
            new CompensationConfirmRequest("actexec-confirm"), via: "reserve-delivery");

        var state = StateOf(confirmed);
        Assert.Equal(CompensationEntryStatus.Confirmed, Assert.Single(state.Entries).Status);
        var confirmation = Assert.Single(state.Confirmations);
        Assert.Equal(("actexec-confirm", "e1"), (confirmation.ActivityExecutionId, Assert.Single(confirmation.EntryIds)));

        var repeated = await NotifiedAsync(confirmed.State, CompensationScope.Notifications.Confirm,
            new CompensationConfirmRequest("actexec-confirm"), via: "reserve-delivery");
        Assert.Null(repeated.Continuation.StateUpdate);
    }

    [Fact]
    public async Task Confirmed_entries_are_never_compensated()
    {
        var afterInventory = await RunToDeliveryAsync();
        var confirmed = await NotifiedAsync(afterInventory.State, CompensationScope.Notifications.Confirm,
            new CompensationConfirmRequest("actexec-confirm"), via: "reserve-delivery");

        var faulted = await FaultedAsync(confirmed.State, Child("reserve-delivery", Step("Reserve delivery")), "actexec-reserve-delivery", "incident-delivery");

        Assert.Empty(faulted.Requests);
        Assert.True(faulted.Continuation.IsComplete);
        Assert.Equal(CompensationEntryStatus.Confirmed, Assert.Single(StateOf(faulted).Entries).Status);
    }

    [Fact]
    public async Task Confirm_during_recovery_or_from_a_handler_changes_nothing()
    {
        var (faulted, _) = await RunToFirstHandlerAsync();

        var duringRecovery = await NotifiedAsync(faulted.State, CompensationScope.Notifications.Confirm,
            new CompensationConfirmRequest("actexec-confirm"), via: "cancel-delivery");

        Assert.Null(duringRecovery.Continuation.StateUpdate);
    }

    [Fact]
    public async Task Compensate_stops_the_running_step_journals_it_uncertain_and_recovers_with_an_explicit_cause()
    {
        var afterInventory = await RunToDeliveryAsync();

        var compensating = await NotifiedAsync(afterInventory.State, CompensationScope.Notifications.Compensate,
            new CompensationCompensateRequest("actexec-compensate", "Fraud check failed", null), via: "reserve-delivery", viaExecution: "actexec-reserve-delivery");

        var cancellation = Assert.Single(compensating.Context.GetChildSubtreeCancellationRequests());
        Assert.Equal("actexec-reserve-delivery", cancellation.ChildActivityExecutionId);
        var state = StateOf(compensating);
        Assert.Equal(CompensationScopePhase.Compensating, state.Phase);
        Assert.Equal((CompensationRecoveryCause.ExplicitKind, "Reserve delivery", "actexec-compensate"), (state.Cause!.Kind, state.Cause.Step, state.Cause.RequestedBy));
        var stopped = state.Entries.Single(entry => entry.Step == "Reserve delivery");
        Assert.Equal(CompensationForwardOutcome.Uncertain, stopped.ForwardOutcome);

        // The stopped step has a handler, so it is undone first, then the completed step before it.
        Assert.Equal("cancel-delivery", Assert.Single(compensating.Requests).ExecutableNodeId);
    }

    [Fact]
    public async Task An_explicit_recovery_completes_Compensated_even_when_faultAfterCompensation_is_set()
    {
        var started = await StartAsync(faultAfterCompensation: true);

        var compensating = await NotifiedAsync(started.State, CompensationScope.Notifications.Compensate,
            new CompensationCompensateRequest("actexec-compensate", null, null), via: "reserve-inventory", viaExecution: "actexec-reserve-inventory", faultAfterCompensation: true);
        Assert.Single(compensating.Context.GetChildSubtreeCancellationRequests());
        var release = Assert.Single(compensating.Requests);
        Assert.Equal("release-inventory", release.ExecutableNodeId);

        var finished = await CompletedAsync(compensating.State, release, "actexec-release-inventory");
        Assert.True(finished.Continuation.IsComplete);
        Assert.Equal(CompensationScope.CompensatedOutcome, finished.Continuation.OutcomeName);
    }

    [Fact]
    public async Task Compensate_stopping_a_step_without_a_handler_recovers_the_rest_then_faults_uncertain_unhandled()
    {
        var started = await StartAsync(graph: HandledThenCheck);
        var afterInventory = await CompletedAsync(started.State, started.Requests.Single(), "actexec-reserve-inventory", graph: HandledThenCheck);

        var compensating = await NotifiedAsync(afterInventory.State, CompensationScope.Notifications.Compensate,
            new CompensationCompensateRequest("actexec-compensate", null, null), via: "check", viaExecution: "actexec-check", graph: HandledThenCheck);
        Assert.Equal("actexec-check", Assert.Single(compensating.Context.GetChildSubtreeCancellationRequests()).ChildActivityExecutionId);
        var release = Assert.Single(compensating.Requests);

        var finished = await CompletedAsync(compensating.State, release, "actexec-release-inventory", graph: HandledThenCheck);

        // Its effect may exist and nothing can undo it, whatever faultAfterCompensation says: an operator decides (R9).
        Assert.True(finished.Continuation.Kind == RuntimeStructuralContinuationKind.Fault);
        Assert.Equal(CompensationScope.UncertainUnhandledFaultCode, finished.Continuation.Fault!.Code);
        Assert.Equal(CompensationScope.OutcomeUnknownCategory, finished.Continuation.Fault.Category);
        var check = StateOf(finished).Entries.Single(entry => entry.Step == "Check");
        Assert.Equal(CompensationEntryStatus.Uncertain, check.Status);
    }

    [Fact]
    public async Task Compensate_stopping_a_step_without_a_handler_with_nothing_to_undo_faults_at_once_without_cancelling()
    {
        var started = await StartAsync(graph: CheckOnly);

        var faulted = await NotifiedAsync(started.State, CompensationScope.Notifications.Compensate,
            new CompensationCompensateRequest("actexec-compensate", null, null), via: "check", viaExecution: "actexec-check", graph: CheckOnly);

        // A fault cannot ride the same commit as a cancellation: the step is left to an enclosing scope or an operator.
        Assert.Empty(faulted.Context.GetChildSubtreeCancellationRequests());
        Assert.True(faulted.Continuation.Kind == RuntimeStructuralContinuationKind.Fault);
        Assert.Equal(CompensationScope.UncertainUnhandledFaultCode, faulted.Continuation.Fault!.Code);
    }

    [Fact]
    public async Task Compensate_outside_the_running_step_or_during_recovery_changes_nothing()
    {
        var afterInventory = await RunToDeliveryAsync();
        var fromAnotherStep = await NotifiedAsync(afterInventory.State, CompensationScope.Notifications.Compensate,
            new CompensationCompensateRequest("actexec-compensate", null, null), via: "reserve-inventory");
        Assert.Null(fromAnotherStep.Continuation.StateUpdate);

        var (faulted, _) = await RunToFirstHandlerAsync();
        var fromAHandler = await NotifiedAsync(faulted.State, CompensationScope.Notifications.Compensate,
            new CompensationCompensateRequest("actexec-compensate", null, null), via: "cancel-delivery");
        Assert.Null(fromAHandler.Continuation.StateUpdate);
        Assert.Empty(fromAHandler.Context.GetChildSubtreeCancellationRequests());
    }

    [Fact]
    public async Task An_unhandled_stopped_step_wins_over_a_cascade_so_no_enclosing_scope_treats_it_as_undone()
    {
        var started = await StartAsync(graph: CheckOnly);

        var faulted = await NotifiedAsync(started.State, CompensationScope.Notifications.Compensate,
            new CompensationCompensateRequest("actexec-compensate", null, "authored-outer"), via: "check", viaExecution: "actexec-check", graph: CheckOnly);

        Assert.Empty(faulted.Context.GetChildSubtreeCancellationRequests());
        Assert.Equal(CompensationScope.UncertainUnhandledFaultCode, faulted.Continuation.Fault!.Code);
        Assert.Equal(CompensationScope.OutcomeUnknownCategory, faulted.Continuation.Fault.Category);
    }

    [Fact]
    public async Task Compensate_aimed_further_out_undoes_this_scope_first_then_faults_to_pass_it_on()
    {
        var afterInventory = await RunToDeliveryAsync();
        var compensating = await NotifiedAsync(afterInventory.State, CompensationScope.Notifications.Compensate,
            new CompensationCompensateRequest("actexec-compensate", null, "authored-outer"), via: "reserve-delivery", viaExecution: "actexec-reserve-delivery");
        var cancel = Assert.Single(compensating.Requests);

        var afterCancel = await CompletedAsync(compensating.State, cancel, "actexec-cancel-delivery");
        var release = Assert.Single(afterCancel.Requests);
        var finished = await CompletedAsync(afterCancel.State, release, "actexec-release-inventory");

        Assert.True(finished.Continuation.Kind == RuntimeStructuralContinuationKind.Fault);
        Assert.Equal(CompensationScope.CompensateRequestedFaultCode, finished.Continuation.Fault!.Code);
        Assert.Equal(CompensationScope.TargetCategoryPrefix + "authored-outer", finished.Continuation.Fault.Category);
    }

    [Fact]
    public async Task The_target_of_a_cascading_Compensate_recovers_when_its_step_passes_the_request_on()
    {
        var afterInventory = await RunToDeliveryAsync();

        var recovering = await FaultedAsync(afterInventory.State, Child("reserve-delivery", Step("Reserve delivery")), "actexec-reserve-delivery", "incident-requested",
            code: CompensationScope.CompensateRequestedFaultCode, category: CompensationScope.TargetCategoryPrefix + ScopeAuthoredId);

        Assert.Single(recovering.Context.GetChildFaultAbsorptionRequests());
        var state = StateOf(recovering);
        Assert.Equal(CompensationRecoveryCause.RequestedKind, state.Cause!.Kind);
        Assert.Null(state.Cause.Target);
        Assert.Equal("release-inventory", Assert.Single(recovering.Requests).ExecutableNodeId);

        var finished = await CompletedAsync(recovering.State, recovering.Requests[0], "actexec-release-inventory");
        Assert.Equal(CompensationScope.CompensatedOutcome, finished.Continuation.OutcomeName);
    }

    [Fact]
    public async Task A_scope_between_passes_a_cascading_Compensate_on_after_undoing_its_own_work()
    {
        var afterInventory = await RunToDeliveryAsync();

        var passing = await FaultedAsync(afterInventory.State, Child("reserve-delivery", Step("Reserve delivery")), "actexec-reserve-delivery", "incident-requested",
            code: CompensationScope.CompensateRequestedFaultCode, category: CompensationScope.TargetCategoryPrefix + "authored-outer");
        var finished = await CompletedAsync(passing.State, passing.Requests.Single(), "actexec-release-inventory");

        Assert.True(finished.Continuation.Kind == RuntimeStructuralContinuationKind.Fault);
        Assert.Equal(CompensationScope.TargetCategoryPrefix + "authored-outer", finished.Continuation.Fault!.Category);
    }

    [Fact]
    public async Task A_descendant_fault_is_handled_as_a_fault_of_the_step_it_came_through()
    {
        var afterInventory = await RunToDeliveryAsync();
        var step = Child("reserve-delivery", Step("Reserve delivery"));
        var context = NewContext(afterInventory.State, PoisonedRoutingMetadata());

        var continuation = await new CompensationScope().OnDescendantFaultedAsync(new ActivityDescendantFaultedContext(
            context, "actexec-leaf", "node-leaf", "actexec-reserve-delivery", "reserve-delivery", "incident-leaf",
            null, "participant.fault", null, step.Metadata));

        Assert.Equal("incident-leaf", Assert.Single(context.GetChildFaultAbsorptionRequests()).IncidentId);
        Assert.Equal("release-inventory", Assert.Single(context.GetChildActivityScheduleRequests()).ExecutableNodeId);
        Assert.Equal(CompensationScopePhase.Compensating, CompensationScopeStatePersister.Deserialize(continuation.StateUpdate!.Value.InlineValue!.Value).Phase);
    }

    [Fact]
    public async Task A_combined_child_outcome_routes_from_committed_child_metadata_not_delivery_metadata()
    {
        var afterInventory = await RunToDeliveryAsync();
        var step = Child("reserve-delivery", Step("Reserve delivery"));
        var context = NewContext(afterInventory.State, PoisonedRoutingMetadata());
        var notification = new ActivityChildNotifiedContext("actexec-reserve-delivery", "reserve-delivery", "test.signal");

        var continuation = await new CompensationScope().OnChildOutcomeAsync(new ActivityChildOutcomeContext(
            context,
            "actexec-reserve-delivery",
            "reserve-delivery",
            null,
            new ActivityFault("participant.fault", "the step failed"),
            "incident-delivery",
            [notification],
            step.Metadata));

        Assert.Equal("incident-delivery", Assert.Single(context.GetChildFaultAbsorptionRequests()).IncidentId);
        Assert.Equal("release-inventory", Assert.Single(context.GetChildActivityScheduleRequests()).ExecutableNodeId);
        Assert.Equal(CompensationScopePhase.Compensating,
            CompensationScopeStatePersister.Deserialize(continuation.StateUpdate!.Value.InlineValue!.Value).Phase);
    }

    private static CompensationHandedOverEntry HandedOver(string entryId, string step, string handler) =>
        new(entryId, step, $"k-{entryId}", $"ae-{entryId}", null, CompensationForwardOutcome.Completed, CompensationEntryStatus.Registered, handler, null);

    private static Dictionary<string, string> Step(string name) => new(StringComparer.Ordinal)
    {
        [CompensationScope.MetadataKeys.Role] = CompensationScope.MetadataKeys.StepRole,
        [CompensationScope.MetadataKeys.Scope] = ScopeExecutionId,
        [CompensationScope.MetadataKeys.Step] = name
    };

    private static Dictionary<string, string> PoisonedRoutingMetadata() => new(StringComparer.Ordinal)
    {
        [CompensationScope.MetadataKeys.Role] = CompensationScope.MetadataKeys.HandlerRole,
        [CompensationScope.MetadataKeys.Scope] = "spoofed-scope",
        [CompensationScope.MetadataKeys.Step] = "spoofed-step"
    };

    private static RuntimeChildActivityScheduleRequest Child(string nodeId, IReadOnlyDictionary<string, string> metadata) =>
        new(nodeId, ScopeExecutionId, metadata);

    private static async Task<Evaluation> StartAsync(bool faultAfterCompensation = false, ExecutableNode? graph = null)
    {
        var context = NewContext(state: null, new Dictionary<string, string>(), graph);
        var continuation = await ((IRuntimeStructuralActivity)new CompensationScope { FaultAfterCompensation = faultAfterCompensation }).ExecuteStructureAsync(context);
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray());
    }

    private static async Task<Evaluation> RunToDeliveryAsync()
    {
        var started = await StartAsync();
        return await CompletedAsync(started.State, started.Requests.Single(), "actexec-reserve-inventory");
    }

    private static async Task<Evaluation> RunAllStepsAsync(bool confirmOnComplete)
    {
        var afterInventory = await RunToDeliveryAsync();
        var afterDelivery = await CompletedAsync(afterInventory.State, afterInventory.Requests.Single(), "actexec-reserve-delivery");
        return await CompletedAsync(afterDelivery.State, afterDelivery.Requests.Single(), "actexec-confirm-order", confirmOnComplete);
    }

    private static async Task<(Evaluation Faulted, RuntimeChildActivityScheduleRequest Cancel)> RunToFirstHandlerAsync()
    {
        var afterInventory = await RunToDeliveryAsync();
        var afterDelivery = await CompletedAsync(afterInventory.State, afterInventory.Requests.Single(), "actexec-reserve-delivery");
        var faulted = await FaultedAsync(afterDelivery.State, afterDelivery.Requests.Single(), "actexec-confirm-order", "incident-confirm");
        return (faulted, faulted.Requests.Single());
    }

    private static async Task<Evaluation> CompletedAsync(RuntimeStructuralStateUpdate? state, RuntimeChildActivityScheduleRequest child, string childExecutionId, bool confirmOnComplete = false, ExecutableNode? graph = null)
    {
        var context = NewContext(state, child.Metadata, graph);
        var continuation = await new CompensationScope { ConfirmOnComplete = confirmOnComplete }.OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, childExecutionId, child.ExecutableNodeId, [ActivityOutcomes.Done], null, child.Metadata));
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray(), state);
    }

    private static async Task<Evaluation> FaultedAsync(
        RuntimeStructuralStateUpdate? state,
        RuntimeChildActivityScheduleRequest child,
        string childExecutionId,
        string incidentId,
        string code = "participant.fault",
        string? category = null)
    {
        var context = NewContext(state, child.Metadata);
        var continuation = await new CompensationScope().OnChildFaultedAsync(
            new ActivityChildFaultedContext(context, childExecutionId, child.ExecutableNodeId, incidentId, null, code, category, child.Metadata));
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray(), state);
    }

    private static async Task<Evaluation> NotifiedAsync<T>(
        RuntimeStructuralStateUpdate? state,
        string code,
        T payload,
        string via,
        string? viaExecution = null,
        bool faultAfterCompensation = false,
        ExecutableNode? graph = null)
    {
        var context = NewContext(state, new Dictionary<string, string>(), graph);
        var notification = new ActivityChildNotifiedContext(
            "actexec-notifier", "node-notifier", code, payload: CompensationMessages.Serialize(payload),
            viaChildActivityExecutionId: viaExecution ?? $"actexec-{via}", viaChildExecutableNodeId: via);
        var continuation = await new CompensationScope { FaultAfterCompensation = faultAfterCompensation }.OnChildNotifiedAsync(context, notification);
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray(), state);
    }

    private static T Read<T>(JsonElement? payload) where T : class =>
        CompensationMessages.Deserialize<T>(payload) ?? throw new InvalidOperationException("The payload is unreadable.");

    private static CompensationScopeState StateOf(Evaluation evaluation)
    {
        var update = evaluation.State ?? throw new InvalidOperationException("The evaluation has no committed state.");
        return CompensationScopeStatePersister.Deserialize(update.Value.InlineValue!.Value);
    }

    private static RuntimeStructuralStateUpdate Stage(CompensationScopeState state) =>
        CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, state).StateUpdate!;

    private static ActivityPrivateState PrivateState(JsonElement payload, int version) =>
        new(ScopeExecutionId, version,
            ValueEnvelope.Inline(new ValueTypeDescriptor(CompensationScopeStatePersister.StateTypeAlias, schemaVersion: version), payload, ValueProtectionPolicy.InstanceInline),
            "attempt-1", DateTimeOffset.UnixEpoch);

    private static SimpleActivityExecutionContext NewContext(RuntimeStructuralStateUpdate? state, IReadOnlyDictionary<string, string> commandMetadata, ExecutableNode? graph = null) =>
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
            graph ?? Journey,
            ScopeState(state is null ? null : new ActivityPrivateState(ScopeExecutionId, state.StateVersion, state.Value, "attempt-1", DateTimeOffset.UnixEpoch)));

    private static ActivityExecutionState ScopeState(ActivityPrivateState? privateState) =>
        new(
            Execution: new ActivityExecution(ScopeExecutionId, "wfexec-1", ScopeNodeId, ScopeAuthoredId, typeof(CompensationScope).FullName!, "1.0.0"),
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

    private sealed record Evaluation(
        RuntimeStructuralContinuation Continuation,
        SimpleActivityExecutionContext Context,
        RuntimeChildActivityScheduleRequest[] Requests,
        RuntimeStructuralStateUpdate? Previous = null)
    {
        public RuntimeStructuralStateUpdate? State => Continuation.StateUpdate ?? Previous;
    }
}
