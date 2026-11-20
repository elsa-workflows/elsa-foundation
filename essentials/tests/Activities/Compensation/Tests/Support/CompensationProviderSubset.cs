using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests.Support;

/// <summary>
/// The spec 261 provider subset S1–S7 (contracts/qualification-matrix.md#provider-subset): one journey per family whose
/// behaviour depends on the store — checkpoint ordering, the incident outcome write-once rule and alteration
/// idempotency, ancestor notification ordering, decision delivery, fault propagation, private-state size and the
/// outbox's <c>AvailableAt</c> — each with its restart point. <c>CompensationProviderSubsetTests</c> runs it on SQLite on
/// every pull request; the nightly <c>compensation</c> EF suite runs it on PostgreSQL, SQL Server and MySQL. The
/// assertions are the source journeys', on the participant and the scope state.
/// </summary>
public static class CompensationProviderSubset
{
    private const string RunId = CompensationHost.WorkflowExecutionId;
    private static readonly WorkflowExecutionPartition Partition = CompensationHost.DefaultPartition;
    private static readonly string[] ReservationEffects = ["reserve-inventory", "reserve-delivery", "cancel-delivery", "release-inventory"];
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>The subset's journey ids, S1 to S7.</summary>
    public static IReadOnlyList<string> Journeys { get; } = ["S1", "S2", "S3", "S4", "S5", "S6", "S7"];

    /// <summary>Every journey in every persistence mode, for a theory.</summary>
    public static TheoryData<string, PersistenceMode> Cases()
    {
        var data = new TheoryData<string, PersistenceMode>();
        foreach (var journey in Journeys)
        foreach (var mode in Enum.GetValues<PersistenceMode>())
            data.Add(journey, mode);
        return data;
    }

    /// <summary>Runs one journey on a fresh store from <paramref name="store"/>.</summary>
    public static async Task RunAsync(string journey, PersistenceMode mode, Func<Task<CompensationStore>> store)
    {
        using var host = new CompensationHost(mode, await store());
        Assert.True(host.Store.SurvivesRestart, $"The provider subset restarts the host; {host.Store.Name} does not survive a restart.");
        await (journey switch
        {
            "S1" => S1_a_handler_killed_after_its_effect_runs_again_with_the_same_invocation(host),
            "S2" => S2_a_wait_survives_a_restart_and_two_retries_use_one_key(host),
            "S3" => S3_a_stop_before_the_hand_over_is_journaled_delivers_it_once(host),
            "S4" => S4_a_resolve_stopped_before_delivery_is_delivered_once_after_restart(host),
            "S5" => S5_abandon_faults_the_scope_and_the_run(host),
            "S6" => S6_fifty_iterations_round_trip_the_outer_scope_s_state(host),
            "S7" => S7_a_retry_waiting_through_a_kill_runs_once_after_its_due_time(host),
            _ => throw new ArgumentOutOfRangeException(nameof(journey), journey, "Unknown provider subset journey.")
        });
    }

    // S1 (spec 250 P2): the host stops after Cancel delivery applied its effect and before its completion commits.
    private static async Task S1_a_handler_killed_after_its_effect_runs_again_with_the_same_invocation(CompensationHost host)
    {
        var participant = new Participant().Rejecting("confirm-order").KillingHostAfterEffectOf("cancel-delivery");
        await using (var first = await host.StartHostAsync(participant))
            await CompensationHost.StartAsync(first, ReservationJourney());

        await using var second = await host.StartHostAsync(participant);
        var run = await host.RecoverAsync(second);

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(ReservationEffects, participant.EffectOrder);
        var calls = participant.Calls.Where(call => call.NodeId == "cancel-delivery").ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Equal(calls[0].InvocationId, calls[1].InvocationId);
        Assert.Single(run.States("cancel-delivery"), state => state.Status == ActivityExecutionStatus.Completed);
    }

    // S2 (spec 251 J4 → J5): the scope waits after three failed attempts, the host restarts while it waits, then two
    // operator retries run the handler with one operation key. The first incident's outcome is written once; the first
    // retry's submission, sent again with its key, is replayed and applies nothing.
    private static async Task S2_a_wait_survives_a_restart_and_two_retries_use_one_key(CompensationHost host)
    {
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 4);
        await using (var first = await host.StartHostAsync(participant))
        {
            await CompensationHost.StartAsync(first, ReservationJourney());
            await first.SweepUntilQuietAsync();
        }

        await using var second = await host.StartHostAsync(participant);
        var waiting = await host.RecoverAsync(second);
        Assert.Equal(WorkflowExecutionStatus.Running, waiting.WorkflowState!.Status);
        Assert.Equal(3, CallsTo(participant, "cancel-delivery"));
        var firstWait = await WaitingIncidentAsync(second);

        var firstRetry = await OperatorActions.RetryAsync(second, RunId, firstWait.ActivityExecutionId!, "s2-retry-1", Partition);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, firstRetry.Status);
        Assert.Equal(4, CallsTo(participant, "cancel-delivery"));
        var secondWait = await WaitingIncidentAsync(second);
        Assert.NotEqual(firstWait.IncidentId, secondWait.IncidentId);
        var resolved = await IncidentAsync(second, firstWait.IncidentId);
        Assert.Equal(IncidentResolutionActionKinds.OperatorRetry, resolved.ResolutionOutcome!.ActionKind);

        var replay = await OperatorActions.SubmitAsync(second, RunId, RetryEnvelope(firstWait.ActivityExecutionId!), "s2-retry-1", Partition);
        Assert.True(replay.IsReplay);
        Assert.Equal(firstRetry.PlanId, replay.Plan.PlanId);
        Assert.Equal(4, CallsTo(participant, "cancel-delivery"));

        await OperatorActions.RetryAsync(second, RunId, secondWait.ActivityExecutionId!, "s2-retry-2", Partition);

        var run = await second.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(ReservationEffects, participant.EffectOrder);
        var cancels = participant.Calls.Where(call => call.NodeId == "cancel-delivery").ToArray();
        Assert.Equal(5, cancels.Length);
        Assert.Single(cancels.Select(call => call.OperationKey).Distinct());
        // Written once: the second retry did not overwrite the first incident's outcome.
        Assert.Equal(JsonSerializer.Serialize(resolved.ResolutionOutcome),
            JsonSerializer.Serialize((await IncidentAsync(second, firstWait.IncidentId)).ResolutionOutcome));
        Assert.Empty(participant.Present);
    }

    // S3 (spec 258 N1, injection point 1): the host stops after the inner scope's hand-over commits and before the outer
    // scope journals it; after the restart the outer scope undoes B then A, once each.
    private static async Task S3_a_stop_before_the_hand_over_is_journaled_delivers_it_once(CompensationHost host)
    {
        var participant = new Participant().Rejecting("charge");
        await using (var first = await host.StartHostAsync(participant, HostStop.BeforeNotification(CompensationScope.Notifications.Register)))
        {
            await CompensationHost.StartAsync(first, Nested());
            Assert.Equal(["reserve-a", "reserve-b"], participant.EffectOrder);
        }

        await using var second = await host.StartHostAsync(participant);
        var run = await host.RecoverAsync(second);

        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-inner", ActivityOutcomes.Done);
        run.AssertOutcomes("node-outer", CompensationScope.CompensatedOutcome);
        Assert.Equal(["reserve-a", "reserve-b", "release-b", "release-a"], participant.EffectOrder);
        Assert.Single(participant.Calls, call => call.NodeId == "release-a");
        Assert.Single(participant.Calls, call => call.NodeId == "release-b");
        Assert.Empty(participant.Present);
    }

    // S4 (spec 260 O2h, O8 point 2): a resolve "effect absent" commits its alteration checkpoint and the host stops before
    // the decision reaches the scope; after the restart it is delivered once and recovery continues.
    private static async Task S4_a_resolve_stopped_before_delivery_is_delivered_once_after_restart(CompensationHost host)
    {
        var participant = WaitingParticipant();
        await using (var first = await host.StartHostAsync(participant, HostStop.BeforeDecisionDelivery()))
        {
            await CompensationHost.StartAsync(first, ReservationJourney());
            await first.SweepUntilQuietAsync();
            var waiting = await WaitingIncidentAsync(first);
            var job = await OperatorActions.DecideAsync(first, RunId, waiting.IncidentId, CompensationScope.Decisions.ResolveEffectAbsent,
                "s4-resolve", Partition, hostMayStop: true);
            Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
            Assert.Equal(IncidentStatus.Resolved, (await IncidentAsync(first, waiting.IncidentId)).Status);
            Assert.DoesNotContain(participant.Calls, call => call.NodeId == "release-inventory");
        }

        await using var second = await host.StartHostAsync(participant);
        var run = await host.RecoverAsync(second);

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Single(participant.Calls, call => call.NodeId == "release-inventory");
        Assert.Equal(3, CallsTo(participant, "cancel-delivery"));
        // The operator said the delivery was undone elsewhere: it is still reserved here.
        Assert.Single(participant.Present);
        var decided = (await second.Services.GetRequiredService<IIncidentStateStore>().ListAsync(RunId))
            .Where(incident => incident.ResolutionOutcome?.ActionKind == IncidentResolutionActionKinds.OperatorDecision);
        Assert.Single(decided);
    }

    // S5 (spec 260 O4): abandon after a restart while waiting: nothing more is undone, the scope faults with the coded
    // fault, the run faults, and the journal keeps the abandonment.
    private static async Task S5_abandon_faults_the_scope_and_the_run(CompensationHost host)
    {
        var participant = WaitingParticipant();
        await using (var first = await host.StartHostAsync(participant))
        {
            await CompensationHost.StartAsync(first, ReservationJourney());
            await first.SweepUntilQuietAsync();
        }

        await using var second = await host.StartHostAsync(participant);
        await host.RecoverAsync(second);
        var waiting = await WaitingIncidentAsync(second);

        var job = await OperatorActions.DecideAsync(second, RunId, waiting.IncidentId, CompensationScope.Decisions.Abandon, "s5-abandon", Partition,
            "Order written off by finance");

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await second.ReadRunAsync(RunId);
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

    // S6 (spec 258 L1 with 50 iterations): each iteration's scope hands one entry over, so the outer scope's private
    // state carries 50 entries through every commit of its recovery; it undoes the newest iteration first.
    private static async Task S6_fifty_iterations_round_trip_the_outer_scope_s_state(CompensationHost host)
    {
        var participant = new Participant().Rejecting("ship");
        var items = Enumerable.Range(1, 50).Select(index => $"item-{index}").ToArray();
        var root = Scope("node-outer", faultAfterCompensation: false,
            new Step("Reserve", ForEachNode("node-items",
                Scope("node-item", faultAfterCompensation: false, new Step("Reserve item", Participant("reserve-item"), Participant("release-item"))),
                items)),
            new Step("Ship", Participant("ship")));

        var run = await host.RunAsync(participant, root);

        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-outer", CompensationScope.CompensatedOutcome);
        Assert.Equal(50, run.States("node-item").Count);
        var forwardKeys = participant.Effects.Where(effect => effect.NodeId == "reserve-item").Select(effect => effect.OperationKey).ToArray();
        var undoneKeys = participant.Effects.Where(effect => effect.NodeId == "release-item").Select(effect => effect.TargetOperationKey).ToArray();
        Assert.Equal(50, forwardKeys.Distinct().Count());
        Assert.Equal(forwardKeys.Reverse(), undoneKeys);
        Assert.Empty(participant.Present);
    }

    // S7 (spec 261 BK2): Cancel delivery fails twice; the host is killed while attempt 3 waits and restarts after its due
    // time, which passed while it was down. The retry runs once, at the first sweep, with the same operation key.
    private static async Task S7_a_retry_waiting_through_a_kill_runs_once_after_its_due_time(CompensationHost host)
    {
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 2);
        var root = Scope(ScopeNodeId, faultAfterCompensation: false, handlerMaxAttempts: 4, confirmOnComplete: false,
            new Backoff(RetryDelay, 2, TimeSpan.FromSeconds(90)),
            new Step("Reserve inventory", Participant("reserve-inventory"), Participant("release-inventory")),
            new Step("Reserve delivery", Participant("reserve-delivery"), Participant("cancel-delivery")),
            new Step("Confirm order", Participant("confirm-order")));
        await using (var first = await host.StartHostAsync(participant))
        {
            await CompensationHost.StartAsync(first, root);
            await first.SweepUntilQuietAsync();
            Assert.Equal(1, CallsTo(participant, "cancel-delivery"));

            // Not yet due: a sweep one second early runs nothing.
            host.Advance(RetryDelay - TimeSpan.FromSeconds(1));
            await first.SweepUntilQuietAsync();
            Assert.Equal(1, CallsTo(participant, "cancel-delivery"));

            host.Advance(TimeSpan.FromSeconds(1));
            await first.SweepUntilQuietAsync();
            Assert.Equal(2, CallsTo(participant, "cancel-delivery"));
            var waiting = CompensationScopeStatePersister.Load((await first.ReadRunAsync(RunId)).State(ScopeNodeId))!
                .Entries.Single(entry => entry.Step == "Reserve delivery");
            Assert.Equal(host.Now + RetryDelay * 2, waiting.HandlerAttempts[^1].NotBefore);
        }

        // Down past the due time.
        host.Advance(TimeSpan.FromSeconds(170));
        await using var second = await host.StartHostAsync(participant);
        await second.SweepUntilQuietAsync();

        var run = await second.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(3, CallsTo(participant, "cancel-delivery"));
        Assert.Equal(3, run.States("cancel-delivery").Count);
        Assert.Single(participant.Calls.Where(call => call.NodeId == "cancel-delivery").Select(call => call.OperationKey).Distinct());
        Assert.Empty(participant.Present);
    }

    // Outer [Reserve: inner [A, B], Charge].
    private static ExecutableNode Nested() =>
        Scope("node-outer", faultAfterCompensation: false,
            new Step("Reserve", Scope("node-inner", faultAfterCompensation: false,
                new Step("Reserve A", Participant("reserve-a"), Participant("release-a")),
                new Step("Reserve B", Participant("reserve-b"), Participant("release-b")))),
            new Step("Charge", Participant("charge")));

    // Cancel delivery fails three times (handlerMaxAttempts 3), so the scope waits on its last attempt's incident.
    private static Participant WaitingParticipant() => new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 3);

    private static WorkflowAlterationEnvelope RetryEnvelope(string sourceActivityExecutionId) =>
        new(Elsa.Workflows.Runtime.Services.Alterations.Handlers.RetryFaultedActivityAlterationHandler.Kind,
            Elsa.Workflows.Runtime.Services.Alterations.Handlers.RetryFaultedActivityAlterationHandler.SchemaVersion,
            JsonSerializer.SerializeToElement(new { sourceActivityExecutionId, reason = "Participant restored" }));

    private static async Task<IncidentState> WaitingIncidentAsync(WorkflowExecutionHarness harness)
    {
        var waiting = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(RunId));
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, waiting.ResolutionOutcome?.ActionKind);
        return waiting;
    }

    private static async Task<IncidentState> IncidentAsync(WorkflowExecutionHarness harness, string incidentId) =>
        (await harness.Services.GetRequiredService<IIncidentStateStore>().FindAsync(RunId, incidentId))!;

    private static int CallsTo(Participant participant, string nodeId) => participant.Calls.Count(call => call.NodeId == nodeId);
}
