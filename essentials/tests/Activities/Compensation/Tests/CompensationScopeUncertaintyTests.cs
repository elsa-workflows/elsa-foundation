using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
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
/// Spec 251 transitions, driven through the scope's callbacks with the state each evaluation committed: uncertain
/// steps, bounded handler retries, the intervention wait and an operator retry's successor.
/// </summary>
public sealed class CompensationScopeUncertaintyTests
{
    private const string ScopeExecutionId = "actexec-scope";
    private const string UnknownCategory = CompensationScope.OutcomeUnknownCategory;

    private static readonly ExecutableNode Journey = ReservationJourney();

    [Fact]
    public async Task An_uncertain_step_with_a_handler_is_journaled_as_uncertain_and_undone_with_its_operation_key()
    {
        var (afterInventory, deliverStep) = await RunToDeliveryAsync();

        var uncertain = await FaultedAsync(afterInventory.State, deliverStep, "actexec-reserve-delivery", "incident-delivery", UnknownCategory);

        var absorption = Assert.Single(uncertain.Context.GetChildFaultAbsorptionRequests());
        Assert.Equal("unknown", absorption.Metadata![CompensationScope.MetadataKeys.ForwardOutcome]);
        Assert.Empty(uncertain.Context.GetChildFaultInterventionRequests());
        var cancel = Assert.Single(uncertain.Requests);
        Assert.Equal("cancel-delivery", cancel.ExecutableNodeId);
        Assert.Equal($"{ScopeExecutionId}/step/1/compensate", cancel.Metadata[CompensationScope.MetadataKeys.OperationKey]);
        Assert.Equal($"{ScopeExecutionId}/step/1", cancel.Metadata[CompensationScope.MetadataKeys.TargetOperationKey]);

        var state = StateOf(uncertain);
        Assert.Equal(CompensationScopePhase.Compensating, state.Phase);
        Assert.Equal(CompensationRecoveryCause.UncertainKind, state.Cause!.Kind);
        var entry = state.Entries.Single(candidate => candidate.Step == "Reserve delivery");
        Assert.Equal(CompensationForwardOutcome.Uncertain, entry.ForwardOutcome);
        Assert.Equal(CompensationEntryStatus.Compensating, entry.Status);

        var undone = await CompletedAsync(uncertain.State, cancel, "actexec-cancel-delivery");
        var compensated = StateOf(undone).Entries.Single(candidate => candidate.Step == "Reserve delivery");
        Assert.Equal(CompensationEntryStatus.Compensated, compensated.Status);
        Assert.Equal(CompensationForwardOutcome.Uncertain, compensated.ForwardOutcome);
        Assert.Equal("release-inventory", Assert.Single(undone.Requests).ExecutableNodeId);
    }

    [Fact]
    public async Task An_uncertain_step_without_a_handler_waits_for_intervention_and_is_never_treated_as_no_effect()
    {
        var (afterInventory, deliverStep) = await RunToDeliveryAsync();
        var afterDelivery = await CompletedAsync(afterInventory.State, deliverStep, "actexec-reserve-delivery");
        var confirmStep = Assert.Single(afterDelivery.Requests);

        var uncertain = await FaultedAsync(afterDelivery.State, confirmStep, "actexec-confirm-order", "incident-confirm", UnknownCategory);

        Assert.Empty(uncertain.Context.GetChildFaultAbsorptionRequests());
        var intervention = Assert.Single(uncertain.Context.GetChildFaultInterventionRequests());
        Assert.Equal("incident-confirm", intervention.IncidentId);
        Assert.Equal("unknown", intervention.Metadata![CompensationScope.MetadataKeys.ForwardOutcome]);
        Assert.Empty(uncertain.Requests);
        Assert.True(uncertain.Continuation.IsDeferred);

        var state = StateOf(uncertain);
        Assert.Equal(CompensationScopePhase.AwaitingIntervention, state.Phase);
        var entry = state.Entries.Single(candidate => candidate.Step == "Confirm order");
        Assert.Equal(CompensationEntryStatus.Uncertain, entry.Status);
        Assert.Equal("incident-confirm", entry.InterventionIncidentId);
    }

    [Fact]
    public async Task A_failing_handler_is_retried_with_the_same_keys_until_its_attempts_run_out_then_waits()
    {
        var (faulted, cancel) = await RunToFirstHandlerAsync(maxAttempts: 2);

        var first = await FaultedAsync(faulted.State, cancel, "actexec-cancel-1", "incident-cancel-1", category: null, maxAttempts: 2);
        var absorbed = Assert.Single(first.Context.GetChildFaultAbsorptionRequests());
        Assert.Equal("incident-cancel-1", absorbed.IncidentId);
        var retry = Assert.Single(first.Requests);
        Assert.Equal("cancel-delivery", retry.ExecutableNodeId);
        Assert.Equal(cancel.Metadata[CompensationScope.MetadataKeys.OperationKey], retry.Metadata[CompensationScope.MetadataKeys.OperationKey]);
        Assert.Equal("2", retry.Metadata[CompensationScope.MetadataKeys.Attempt]);
        Assert.Equal("actexec-reserve-delivery", retry.SchedulingActivityExecutionId);

        var second = await FaultedAsync(first.State, retry, "actexec-cancel-2", "incident-cancel-2", category: null, maxAttempts: 2);
        Assert.Empty(second.Context.GetChildFaultAbsorptionRequests());
        Assert.Empty(second.Requests);
        var intervention = Assert.Single(second.Context.GetChildFaultInterventionRequests());
        Assert.Equal("incident-cancel-2", intervention.IncidentId);
        Assert.Equal("2", intervention.Metadata![CompensationScope.MetadataKeys.Attempt]);
        Assert.Equal("2", intervention.Metadata[CompensationScope.MetadataKeys.MaxAttempts]);

        var state = StateOf(second);
        Assert.Equal(CompensationScopePhase.AwaitingIntervention, state.Phase);
        Assert.Equal(CompensationRecoveryCause.FaultKind, state.Cause!.Kind);
        Assert.Equal("Confirm order", state.Cause.Step);
        var entry = state.Entries.Single(candidate => candidate.Step == "Reserve delivery");
        Assert.Equal(CompensationEntryStatus.Failed, entry.Status);
        Assert.Equal(["actexec-cancel-1", "actexec-cancel-2"], entry.HandlerAttempts.Select(attempt => attempt.ActivityExecutionId));
        Assert.All(entry.HandlerAttempts, attempt => Assert.Equal(CompensationAttemptResult.Faulted, attempt.Result));
    }

    [Fact]
    public async Task An_operator_retry_of_a_failed_handler_completes_the_entry_and_resumes_recovery()
    {
        var (faulted, cancel) = await RunToFirstHandlerAsync(maxAttempts: 1);
        var waiting = await FaultedAsync(faulted.State, cancel, "actexec-cancel-1", "incident-cancel-1", category: null, maxAttempts: 1);
        Assert.Equal(CompensationScopePhase.AwaitingIntervention, StateOf(waiting).Phase);

        // The retry alteration's successor carries the source's scheduling metadata under a new execution id.
        var resumed = await CompletedAsync(waiting.State, cancel, "actexec-cancel-retry", maxAttempts: 1);

        var state = StateOf(resumed);
        Assert.Equal(CompensationScopePhase.Compensating, state.Phase);
        var entry = state.Entries.Single(candidate => candidate.Step == "Reserve delivery");
        Assert.Equal(CompensationEntryStatus.Compensated, entry.Status);
        Assert.Null(entry.InterventionIncidentId);
        Assert.Equal(["actexec-cancel-1", "actexec-cancel-retry"], entry.HandlerAttempts.Select(attempt => attempt.ActivityExecutionId));
        Assert.Equal("release-inventory", Assert.Single(resumed.Requests).ExecutableNodeId);
    }

    [Fact]
    public async Task A_repeated_handler_fault_neither_retries_twice_nor_requests_intervention()
    {
        var (faulted, cancel) = await RunToFirstHandlerAsync(maxAttempts: 3);
        var first = await FaultedAsync(faulted.State, cancel, "actexec-cancel-1", "incident-cancel-1", category: null, maxAttempts: 3);
        Assert.Single(first.Requests);

        var repeated = await FaultedAsync(first.State, cancel, "actexec-cancel-1", "incident-cancel-1", category: null, maxAttempts: 3);

        Assert.True(repeated.Continuation.IsDeferred);
        Assert.Null(repeated.Continuation.StateUpdate);
        Assert.Empty(repeated.Requests);
        Assert.Empty(repeated.Context.GetChildFaultAbsorptionRequests());
        Assert.Empty(repeated.Context.GetChildFaultInterventionRequests());
    }

    // A handler that is a Sequence reports a fault inside it as its own, under its own id. An operator retry of that
    // faulted activity runs under the same Sequence, so the Sequence's later outcome keeps the id its faulted attempt
    // recorded; only the incident tells a new fault from a repeat delivery.
    [Fact]
    public async Task A_waiting_handler_tells_a_repeat_fault_from_its_occurrence_s_outcome_after_an_operator_retry()
    {
        var (faulted, cancel) = await RunToFirstHandlerAsync(maxAttempts: 1);
        var waiting = await FaultedAsync(faulted.State, cancel, "actexec-cancel-1", "incident-cancel-1", category: null, maxAttempts: 1);
        Assert.Equal(CompensationScopePhase.AwaitingIntervention, StateOf(waiting).Phase);

        var repeated = await FaultedAsync(waiting.State, cancel, "actexec-cancel-1", "incident-cancel-1", category: null, maxAttempts: 1);
        Assert.Null(repeated.Continuation.StateUpdate);
        Assert.Empty(repeated.Context.GetChildFaultInterventionRequests());

        var faultedAgain = await FaultedAsync(waiting.State, cancel, "actexec-cancel-1", "incident-cancel-retry", category: null, maxAttempts: 1);
        var again = StateOf(faultedAgain).Entries.Single(candidate => candidate.Step == "Reserve delivery");
        Assert.Equal(["incident-cancel-1", "incident-cancel-retry"], again.HandlerAttempts.Select(attempt => attempt.IncidentId));
        Assert.Equal("incident-cancel-retry", again.InterventionIncidentId);

        var resumed = await CompletedAsync(waiting.State, cancel, "actexec-cancel-1", maxAttempts: 1);
        var entry = StateOf(resumed).Entries.Single(candidate => candidate.Step == "Reserve delivery");
        Assert.Equal(CompensationEntryStatus.Compensated, entry.Status);
        Assert.Equal("release-inventory", Assert.Single(resumed.Requests).ExecutableNodeId);

        var completedTwice = await CompletedAsync(resumed.State, cancel, "actexec-cancel-1", maxAttempts: 1);
        Assert.Null(completedTwice.Continuation.StateUpdate);
        Assert.Empty(completedTwice.Requests);
    }

    // The same for an uncertain step without a handler that is a Sequence: the waiting entry recorded the Sequence.
    [Fact]
    public async Task A_waiting_step_tells_a_repeat_fault_from_its_occurrence_s_outcome_after_an_operator_retry()
    {
        var (afterInventory, deliverStep) = await RunToDeliveryAsync();
        var afterDelivery = await CompletedAsync(afterInventory.State, deliverStep, "actexec-reserve-delivery");
        var confirmStep = Assert.Single(afterDelivery.Requests);
        var waiting = await FaultedAsync(afterDelivery.State, confirmStep, "actexec-confirm-order", "incident-confirm", UnknownCategory);
        Assert.Equal(CompensationScopePhase.AwaitingIntervention, StateOf(waiting).Phase);

        var repeated = await FaultedAsync(waiting.State, confirmStep, "actexec-confirm-order", "incident-confirm", UnknownCategory);
        Assert.Null(repeated.Continuation.StateUpdate);
        Assert.Empty(repeated.Context.GetChildFaultInterventionRequests());

        var stillUnknown = await FaultedAsync(waiting.State, confirmStep, "actexec-confirm-order", "incident-confirm-retry", UnknownCategory);
        Assert.Equal("incident-confirm-retry", Assert.Single(stillUnknown.Context.GetChildFaultInterventionRequests()).IncidentId);
        Assert.Equal("incident-confirm-retry", StateOf(stillUnknown).Entries.Single(candidate => candidate.Step == "Confirm order").InterventionIncidentId);

        var settled = await CompletedAsync(waiting.State, confirmStep, "actexec-confirm-order");
        Assert.True(settled.Continuation.IsComplete);
        var entry = StateOf(settled).Entries.Single(candidate => candidate.Step == "Confirm order");
        Assert.Equal(CompensationEntryStatus.NoHandler, entry.Status);
        Assert.Null(entry.InterventionIncidentId);
    }

    [Fact]
    public void A_version_1_state_is_upgraded_with_its_identities_and_the_keys_M1_participants_saw()
    {
        var v1 = JsonSerializer.SerializeToElement(new
        {
            phase = "Compensating",
            nextStepIndex = 3,
            nextOrdinal = 3,
            cause = new { kind = "fault", step = "Confirm order", activityExecutionId = "ae-3", incidentId = "inc-3" },
            entries = new object[]
            {
                new { id = "e1", step = "Reserve inventory", ordinal = 1, status = "Compensating", forwardActivityExecutionId = "ae-1", handlerActivityExecutionIds = Array.Empty<string>() },
                new { id = "e2", step = "Reserve delivery", ordinal = 2, status = "Compensated", forwardActivityExecutionId = "ae-2", handlerActivityExecutionIds = new[] { "ae-4" } }
            }
        });
        var activity = ScopeState(new ActivityPrivateState(ScopeExecutionId, 1,
            ValueEnvelope.Inline(new ValueTypeDescriptor(CompensationScopeStatePersister.StateTypeAlias, schemaVersion: 1), v1, ValueProtectionPolicy.InstanceInline),
            "attempt-1", DateTimeOffset.UnixEpoch));

        var state = CompensationScopeStatePersister.Load(activity)!;

        Assert.Equal(CompensationScopePhase.Compensating, state.Phase);
        var running = state.Entries[0];
        Assert.Equal(CompensationEntryStatus.Compensating, running.Status);
        Assert.Equal("ae-1", running.OperationKey);
        Assert.Equal(CompensationForwardOutcome.Completed, running.ForwardOutcome);
        Assert.Equal(CompensationAttemptResult.Running, Assert.Single(running.HandlerAttempts).Result);
        var done = state.Entries[1];
        Assert.Equal("ae-2", done.OperationKey);
        var attempt = Assert.Single(done.HandlerAttempts);
        Assert.Equal((CompensationAttemptResult.Completed, "ae-4"), (attempt.Result, attempt.ActivityExecutionId));

        // It is written back as current version, never as version 1.
        var staged = CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, state);
        Assert.Equal(CompensationScopeStatePersister.StateSchemaVersion, staged.StateUpdate!.StateVersion);
        Assert.Equal(5, CompensationScopeStatePersister.StateSchemaVersion);
    }

    [Fact]
    public async Task A_handler_that_M1_scheduled_still_completes_its_entry_after_the_state_is_upgraded()
    {
        // An M1 scope was compensating Reserve inventory when the host was upgraded: its handler's command carries no
        // attempt number, and the upgraded entry has one running attempt.
        var v1 = JsonSerializer.SerializeToElement(new
        {
            phase = "Compensating",
            nextStepIndex = 3,
            nextOrdinal = 3,
            cause = new { kind = "fault", step = "Confirm order", activityExecutionId = "ae-3", incidentId = "inc-3" },
            entries = new object[]
            {
                new { id = "e1", step = "Reserve inventory", ordinal = 1, status = "Compensating", forwardActivityExecutionId = "ae-1", handlerActivityExecutionIds = Array.Empty<string>() },
                new { id = "e2", step = "Reserve delivery", ordinal = 2, status = "Compensated", forwardActivityExecutionId = "ae-2", handlerActivityExecutionIds = new[] { "ae-4" } }
            }
        });
        var privateState = new ActivityPrivateState(ScopeExecutionId, 1,
            ValueEnvelope.Inline(new ValueTypeDescriptor(CompensationScopeStatePersister.StateTypeAlias, schemaVersion: 1), v1, ValueProtectionPolicy.InstanceInline),
            "attempt-1", DateTimeOffset.UnixEpoch);
        var m1HandlerMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CompensationScope.MetadataKeys.Role] = CompensationScope.MetadataKeys.HandlerRole,
            [CompensationScope.MetadataKeys.Scope] = ScopeExecutionId,
            [CompensationScope.MetadataKeys.Step] = "Reserve inventory",
            [CompensationScope.MetadataKeys.Entry] = "e1",
            [CompensationScope.MetadataKeys.Forward] = "ae-1"
        };
        var context = NewContext(privateState, new Dictionary<string, string>());

        var continuation = await new CompensationScope().OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "ae-5", "release-inventory", [ActivityOutcomes.Done], null, m1HandlerMetadata));

        // The completion is not dropped: the entry is compensated and, with nothing left, the scope finishes.
        Assert.False(continuation.IsDeferred);
        var state = CompensationScopeStatePersister.Deserialize(continuation.StateUpdate!.Value.InlineValue!.Value);
        Assert.Equal(CompensationScopePhase.Compensated, state.Phase);
        var entry = state.Entries.Single(candidate => candidate.Id == "e1");
        Assert.Equal(CompensationEntryStatus.Compensated, entry.Status);
        Assert.Equal((CompensationAttemptResult.Completed, "ae-5"), (entry.HandlerAttempts.Single().Result, entry.HandlerAttempts.Single().ActivityExecutionId));
    }

    [Theory]
    [InlineData(1, null, true)]
    [InlineData(2, null, false)]
    [InlineData(2, "2", true)]
    [InlineData(2, "1", false)]
    public void Only_the_running_attempt_owns_a_handler_outcome(int attempts, string? attempt, bool expected)
    {
        var history = Enumerable.Range(1, attempts - 1).Select(index => new CompensationHandlerAttempt(CompensationAttemptResult.Faulted, $"ae-{index}"))
            .Append(new CompensationHandlerAttempt(CompensationAttemptResult.Running)).ToArray();
        var entry = new CompensationEntry("e1", "Reserve inventory", 1, CompensationEntryStatus.Compensating, "ae-f", "key", handlerAttempts: history);

        Assert.Equal(expected, CompensationScope.IsRunningAttempt(entry, attempt));
    }

    // Starts the journey and completes Reserve inventory, so Reserve delivery is the running step.
    private static async Task<(Evaluation AfterInventory, RuntimeChildActivityScheduleRequest DeliverStep)> RunToDeliveryAsync()
    {
        var started = await ExecuteAsync();
        var inventory = await CompletedAsync(started.State, Assert.Single(started.Requests), "actexec-reserve-inventory");
        return (inventory, Assert.Single(inventory.Requests));
    }

    // Runs both reservations, faults Confirm order, and returns the scheduled Cancel delivery.
    private static async Task<(Evaluation Faulted, RuntimeChildActivityScheduleRequest Cancel)> RunToFirstHandlerAsync(int maxAttempts)
    {
        var (afterInventory, deliverStep) = await RunToDeliveryAsync();
        var afterDelivery = await CompletedAsync(afterInventory.State, deliverStep, "actexec-reserve-delivery", maxAttempts);
        var faulted = await FaultedAsync(afterDelivery.State, Assert.Single(afterDelivery.Requests), "actexec-confirm-order", "incident-confirm", category: null, maxAttempts);
        var cancel = Assert.Single(faulted.Requests);
        Assert.Equal("cancel-delivery", cancel.ExecutableNodeId);
        return (faulted, cancel);
    }

    private static async Task<Evaluation> ExecuteAsync()
    {
        var context = NewContext(state: null, commandMetadata: new Dictionary<string, string>());
        var continuation = await ((IRuntimeStructuralActivity)new CompensationScope()).ExecuteStructureAsync(context);
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray());
    }

    private static async Task<Evaluation> CompletedAsync(RuntimeStructuralStateUpdate? state, RuntimeChildActivityScheduleRequest child, string childExecutionId, int maxAttempts = 3)
    {
        var context = NewContext(state, new Dictionary<string, string>());
        var continuation = await new CompensationScope { HandlerMaxAttempts = maxAttempts }.OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, childExecutionId, child.ExecutableNodeId, [ActivityOutcomes.Done], null, child.Metadata));
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray(), state);
    }

    private static async Task<Evaluation> FaultedAsync(
        RuntimeStructuralStateUpdate? state,
        RuntimeChildActivityScheduleRequest child,
        string childExecutionId,
        string incidentId,
        string? category,
        int maxAttempts = 3)
    {
        var context = NewContext(state, new Dictionary<string, string>());
        var continuation = await new CompensationScope { HandlerMaxAttempts = maxAttempts }.OnChildFaultedAsync(
            new ActivityChildFaultedContext(context, childExecutionId, child.ExecutableNodeId, incidentId, null, "participant.fault", category, child.Metadata));
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray(), state);
    }

    private static CompensationScopeState StateOf(Evaluation evaluation)
    {
        var update = evaluation.State ?? throw new InvalidOperationException("The evaluation has no committed state.");
        return CompensationScopeStatePersister.Deserialize(update.Value.InlineValue!.Value);
    }

    private static SimpleActivityExecutionContext NewContext(RuntimeStructuralStateUpdate? state, IReadOnlyDictionary<string, string> commandMetadata) =>
        NewContext(state is null ? null : new ActivityPrivateState(ScopeExecutionId, state.StateVersion, state.Value, "attempt-1", DateTimeOffset.UnixEpoch), commandMetadata);

    private static SimpleActivityExecutionContext NewContext(ActivityPrivateState? privateState, IReadOnlyDictionary<string, string> commandMetadata) =>
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
            Journey,
            ScopeState(privateState));

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
