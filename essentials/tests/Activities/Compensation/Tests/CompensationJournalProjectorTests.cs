using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// The compensation journal (spec 260 FR-414, view <c>compensation.journal/1</c>) projected from the scope's private
/// state in each phase, with and without the sensitive-values grant (J1).
/// </summary>
public sealed class CompensationJournalProjectorTests
{
    private const string ScopeExecutionId = "actexec-scope";
    private static readonly ExecutableNode Journey = ReservationJourney();
    private static readonly string[] Offer = CompensationScope.Decisions.Offer.ToArray();

    [Fact]
    public void The_feature_registers_the_projector_for_the_scope_state()
    {
        var services = new ServiceCollection();
        new ActivitiesCompensationFeature().ConfigureServices(services);

        var projector = Assert.Single(services.BuildServiceProvider().GetServices<IActivityStateViewProjector>());
        Assert.Equal(CompensationScopeStatePersister.StateTypeAlias, projector.StateTypeAlias);
        Assert.Equal("compensation.journal", projector.ViewKind);
        Assert.Equal(1, projector.ViewVersion);
    }

    [Fact]
    public async Task A_running_scope_lists_its_entries_and_what_recovery_would_undo()
    {
        var state = CompensationScopeState.Initial with
        {
            NextStepIndex = 2,
            NextOrdinal = 3,
            Entries =
            [
                Entry("e1", "Reserve inventory", 1, CompensationEntryStatus.Registered),
                Entry("e2", "Reserve delivery", 2, CompensationEntryStatus.Registered)
            ]
        };

        var view = await ProjectAsync(state, []);

        Assert.Equal("Running", view.GetProperty("phase").GetString());
        Assert.Equal(5, view.GetProperty("stateVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("cause").ValueKind);
        Assert.Equal(["e2", "e1"], Strings(view.GetProperty("remaining")));
        var first = view.GetProperty("entries")[0];
        Assert.Equal("e1", first.GetProperty("id").GetString());
        Assert.Equal("Registered", first.GetProperty("status").GetString());
        Assert.Equal("Completed", first.GetProperty("forwardOutcome").GetString());
        Assert.Equal("key-e1", first.GetProperty("operationKey").GetString());
        Assert.Equal("ae-e1", first.GetProperty("forward").GetProperty("activityExecutionId").GetString());
        Assert.Equal("release-inventory", first.GetProperty("handler").GetProperty("executableNodeId").GetString());
        Assert.True(first.GetProperty("handler").GetProperty("remaining").GetBoolean());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("waiting").ValueKind);
        Assert.Equal(JsonValueKind.Null, view.GetProperty("abandonment").ValueKind);
    }

    [Fact]
    public async Task A_waiting_scope_shows_the_attempts_the_wait_and_the_offered_decisions()
    {
        var state = WaitingState();
        var incidents = new[] { Cause(), WaitingIncident(Offer) };

        var view = await ProjectAsync(state, incidents);

        Assert.Equal("AwaitingIntervention", view.GetProperty("phase").GetString());
        var delivery = view.GetProperty("entries")[1];
        Assert.Equal("Failed", delivery.GetProperty("status").GetString());
        var attempts = delivery.GetProperty("attempts").EnumerateArray().ToArray();
        Assert.Equal([1, 2], attempts.Select(attempt => attempt.GetProperty("number").GetInt32()));
        Assert.Equal(["ae-9", "ae-10"], attempts.Select(attempt => attempt.GetProperty("activityExecutionId").GetString()));
        Assert.Equal(["inc-4", "inc-5"], attempts.Select(attempt => attempt.GetProperty("incidentId").GetString()));
        Assert.All(attempts, attempt => Assert.Equal("Faulted", attempt.GetProperty("result").GetString()));
        Assert.Equal("inc-5", delivery.GetProperty("waiting").GetProperty("incidentId").GetString());
        Assert.Equal(Offer, Strings(delivery.GetProperty("waiting").GetProperty("decisions")));
        Assert.Equal(["e2", "e1"], Strings(view.GetProperty("remaining")));
        Assert.Equal("Confirm order", view.GetProperty("cause").GetProperty("step").GetString());
        Assert.Equal("ActivityReturnedFault", view.GetProperty("cause").GetProperty("failureType").GetString());
    }

    // Spec 261 T510: a retry the scope scheduled after a wait carries its due time (and the clamp, when one applied); every
    // other attempt carries neither field, so the rest of the journal is unchanged.
    [Fact]
    public async Task A_scheduled_retry_shows_its_due_time_and_other_attempts_do_not()
    {
        var dueAt = new DateTimeOffset(2026, 10, 20, 10, 42, 30, TimeSpan.Zero);
        var state = CompensationScopeState.Initial with
        {
            Phase = CompensationScopePhase.Compensating,
            NextStepIndex = 3,
            NextOrdinal = 3,
            Cause = new CompensationRecoveryCause(CompensationRecoveryCause.FaultKind, "Confirm order", "ae-7", "inc-1"),
            Entries =
            [
                Entry("e1", "Reserve inventory", 1, CompensationEntryStatus.Registered),
                Entry("e2", "Reserve delivery", 2, CompensationEntryStatus.Compensating) with
                {
                    HandlerAttempts =
                    [
                        new CompensationHandlerAttempt(CompensationAttemptResult.Faulted, "ae-9", "inc-4"),
                        new CompensationHandlerAttempt(CompensationAttemptResult.Running, notBefore: dueAt, delayClamped: true)
                    ]
                }
            ]
        };

        var view = await ProjectAsync(state, [Cause()]);

        var attempts = view.GetProperty("entries")[1].GetProperty("attempts").EnumerateArray().ToArray();
        Assert.False(attempts[0].TryGetProperty("notBefore", out _));
        Assert.False(attempts[0].TryGetProperty("delayClamped", out _));
        Assert.Equal("Running", attempts[1].GetProperty("result").GetString());
        Assert.Equal(dueAt, attempts[1].GetProperty("notBefore").GetDateTimeOffset());
        Assert.True(attempts[1].GetProperty("delayClamped").GetBoolean());
        Assert.Equal(JsonValueKind.Null, attempts[1].GetProperty("activityExecutionId").ValueKind);
    }

    [Fact]
    public async Task An_attempt_without_a_wait_has_neither_backoff_field()
    {
        var view = await ProjectAsync(WaitingState(), [Cause(), WaitingIncident(Offer)]);

        Assert.DoesNotContain("notBefore", view.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("delayClamped", view.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wait_from_before_spec_260_offers_no_decisions()
    {
        var view = await ProjectAsync(WaitingState(), [Cause(), WaitingIncident(offer: null)]);

        Assert.Empty(view.GetProperty("entries")[1].GetProperty("waiting").GetProperty("decisions").EnumerateArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_cause_message_is_shown_only_with_the_values_grant(bool canInspectValues)
    {
        var view = await ProjectAsync(WaitingState(), [Cause(), WaitingIncident(Offer)], canInspectValues);

        var cause = view.GetProperty("cause");
        Assert.Equal("fault", cause.GetProperty("kind").GetString());
        Assert.Equal("inc-1", cause.GetProperty("incidentId").GetString());
        if (canInspectValues)
        {
            Assert.Equal("Order 42 was rejected for card 4111.", cause.GetProperty("message").GetString());
            Assert.False(cause.GetProperty("messageWithheld").GetBoolean());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, cause.GetProperty("message").ValueKind);
            Assert.True(cause.GetProperty("messageWithheld").GetBoolean());
            Assert.DoesNotContain("4111", view.GetRawText(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_compensating_scope_lists_only_the_entries_still_to_undo_in_recovery_order()
    {
        var state = WaitingState() with
        {
            Phase = CompensationScopePhase.Compensating,
            Entries =
            [
                Entry("e1", "Reserve inventory", 1, CompensationEntryStatus.Registered),
                Entry("e2", "Reserve delivery", 2, CompensationEntryStatus.Compensated),
                Entry("e3", "Confirm order", 3, CompensationEntryStatus.Uncertain, CompensationForwardOutcome.Uncertain)
            ]
        };

        var view = await ProjectAsync(state, [Cause()]);

        Assert.Equal(["e1"], Strings(view.GetProperty("remaining")));
        var confirm = view.GetProperty("entries")[2];
        Assert.Equal(JsonValueKind.Null, confirm.GetProperty("handler").ValueKind);
        Assert.Equal("Uncertain", confirm.GetProperty("forwardOutcome").GetString());
        Assert.False(view.GetProperty("entries")[1].GetProperty("handler").GetProperty("remaining").GetBoolean());
    }

    [Fact]
    public async Task A_resolved_entry_shows_its_resolution_and_is_not_to_undo()
    {
        var state = WaitingState() with
        {
            Phase = CompensationScopePhase.Compensating,
            Entries =
            [
                Entry("e1", "Reserve inventory", 1, CompensationEntryStatus.Registered),
                Entry("e2", "Reserve delivery", 2, CompensationEntryStatus.ResolvedManually) with
                {
                    Resolution = new CompensationEntryResolution(CompensationResolutionOutcome.EffectAbsent, "inc-5")
                }
            ]
        };

        var view = await ProjectAsync(state, [Cause()]);

        var resolved = view.GetProperty("entries")[1];
        Assert.Equal("ResolvedManually", resolved.GetProperty("status").GetString());
        Assert.Equal("EffectAbsent", resolved.GetProperty("resolution").GetProperty("outcome").GetString());
        Assert.Equal("inc-5", resolved.GetProperty("resolution").GetProperty("incidentId").GetString());
        Assert.Equal(["e1"], Strings(view.GetProperty("remaining")));
    }

    [Fact]
    public async Task An_abandoned_scope_keeps_its_journal_and_has_nothing_left_to_undo()
    {
        var state = WaitingState() with
        {
            Phase = CompensationScopePhase.Abandoned,
            Abandonment = new CompensationAbandonment("inc-5", "e2", CompensationScopePhase.Compensating)
        };

        var view = await ProjectAsync(state, [Cause()]);

        Assert.Equal("Abandoned", view.GetProperty("phase").GetString());
        Assert.Empty(view.GetProperty("remaining").EnumerateArray());
        var abandonment = view.GetProperty("abandonment");
        Assert.Equal(("inc-5", "e2", "Compensating"),
            (abandonment.GetProperty("incidentId").GetString(), abandonment.GetProperty("entryId").GetString(), abandonment.GetProperty("fromPhase").GetString()));
        Assert.Equal(2, view.GetProperty("entries").GetArrayLength());
        Assert.All(view.GetProperty("entries").EnumerateArray(), entry => Assert.False(entry.GetProperty("handler").GetProperty("remaining").GetBoolean()));
    }

    [Fact]
    public async Task A_cancelled_scope_projects_the_journal_it_kept()
    {
        var view = await ProjectAsync(WaitingState(), [Cause()], activityStatus: ActivityExecutionStatus.Cancelled);

        Assert.Equal("AwaitingIntervention", view.GetProperty("phase").GetString());
        Assert.Equal(2, view.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task Handed_over_entries_show_their_origin_confirmations_and_hand_over_progress()
    {
        var state = CompensationScopeState.Initial with
        {
            NextStepIndex = 1,
            NextOrdinal = 3,
            Entries =
            [
                Entry("e1", "Reserve inventory", 1, CompensationEntryStatus.Confirmed),
                Entry("e2", "Reserve item", 2, CompensationEntryStatus.Registered) with
                {
                    HandlerExecutableNodeId = "release-item",
                    Origin = new CompensationEntryOrigin("ae-inner", "e7")
                }
            ],
            Confirmations = [new CompensationConfirmation("ae-confirm", ["e1"])],
            ReceivedHandOvers = ["ae-inner"],
            HandOversInProgress = [new CompensationHandOverProgress("ae-inner-2", [0])]
        };

        var view = await ProjectAsync(state, []);

        var confirmed = view.GetProperty("entries")[0];
        Assert.Equal("ae-confirm", confirmed.GetProperty("confirmedBy").GetString());
        var handed = view.GetProperty("entries")[1];
        Assert.Equal("ae-inner", handed.GetProperty("origin").GetProperty("scopeActivityExecutionId").GetString());
        Assert.Equal("e7", handed.GetProperty("origin").GetProperty("entryId").GetString());
        Assert.Equal("release-item", handed.GetProperty("handler").GetProperty("executableNodeId").GetString());
        Assert.Equal(["e2"], Strings(view.GetProperty("remaining")));
        Assert.Equal(["ae-inner"], Strings(view.GetProperty("handOvers").GetProperty("received")));
        var progress = view.GetProperty("handOvers").GetProperty("inProgress")[0];
        Assert.Equal("ae-inner-2", progress.GetProperty("originScope").GetString());
        Assert.Equal([0], progress.GetProperty("batches").EnumerateArray().Select(batch => batch.GetInt32()));
        Assert.Equal(["e1"], Strings(view.GetProperty("confirmations")[0].GetProperty("entryIds")));
    }

    private static CompensationScopeState WaitingState() => CompensationScopeState.Initial with
    {
        Phase = CompensationScopePhase.AwaitingIntervention,
        NextStepIndex = 3,
        NextOrdinal = 3,
        Cause = new CompensationRecoveryCause(CompensationRecoveryCause.FaultKind, "Confirm order", "ae-7", "inc-1"),
        Entries =
        [
            Entry("e1", "Reserve inventory", 1, CompensationEntryStatus.Registered),
            Entry("e2", "Reserve delivery", 2, CompensationEntryStatus.Failed) with
            {
                HandlerAttempts =
                [
                    new CompensationHandlerAttempt(CompensationAttemptResult.Faulted, "ae-9", "inc-4"),
                    new CompensationHandlerAttempt(CompensationAttemptResult.Faulted, "ae-10", "inc-5")
                ],
                InterventionIncidentId = "inc-5"
            }
        ]
    };

    private static CompensationEntry Entry(string id, string step, int ordinal, CompensationEntryStatus status, CompensationForwardOutcome outcome = CompensationForwardOutcome.Completed) =>
        new(id, step, ordinal, status, $"ae-{id}", $"key-{id}", outcome);

    private static IncidentState Cause() => new(
        "inc-1", "wfexec-1", "ae-7", "confirm-order", IncidentSeverity.Error, IncidentStatus.Resolved,
        new IncidentResolutionOutcome(IncidentResolutionActionKinds.AbsorbFault, DateTimeOffset.UnixEpoch, null, IncidentResolutionSystemSources.StructuralFaultAbsorption),
        "ActivityReturnedFault", "Order 42 was rejected for card 4111.", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static IncidentState WaitingIncident(string[]? offer)
    {
        var metadata = new Dictionary<string, string> { [CompensationScope.MetadataKeys.Entry] = "e2" };
        if (offer is not null)
            metadata[RuntimeMetadataKeys.InterventionDecisions] = JsonSerializer.Serialize(offer);
        return new IncidentState(
            "inc-5", "wfexec-1", "ae-10", "cancel-delivery", IncidentSeverity.Error, IncidentStatus.Blocking,
            new IncidentResolutionOutcome(IncidentResolutionActionKinds.WaitForIntervention, DateTimeOffset.UnixEpoch, null,
                IncidentResolutionSystemSources.StructuralIntervention, metadata),
            "ActivityReturnedFault", "Cancel failed.", DateTimeOffset.UnixEpoch, null);
    }

    private static async Task<JsonElement> ProjectAsync(
        CompensationScopeState state,
        IReadOnlyCollection<IncidentState> incidents,
        bool canInspectValues = false,
        ActivityExecutionStatus activityStatus = ActivityExecutionStatus.Running)
    {
        var staged = CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, state).StateUpdate!;
        var activity = new ActivityExecutionState(
            Execution: new ActivityExecution(ScopeExecutionId, "wfexec-1", ScopeNodeId, $"authored-{ScopeNodeId}", typeof(CompensationScope).FullName!, "1.0.0"),
            Status: activityStatus,
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
            PrivateState = new ActivityPrivateState(ScopeExecutionId, staged.StateVersion, staged.Value, "attempt-1", DateTimeOffset.UnixEpoch)
        };
        var workflow = new WorkflowExecutionState("wfexec-1", new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            WorkflowExecutionStatus.Running, null, DateTimeOffset.UnixEpoch, null, null, null, null, null, "tenant", new Dictionary<string, string>());

        var view = await new CompensationJournalProjector().ProjectAsync(new ActivityStateViewContext(workflow, activity, Journey, incidents, canInspectValues));
        return view;
    }

    private static string?[] Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()).ToArray();
}
