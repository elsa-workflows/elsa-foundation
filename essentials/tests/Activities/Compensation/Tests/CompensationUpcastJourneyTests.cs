using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// Spec 261 FR-518 (U1–U4): a scope whose committed state an older build wrote resumes under the current code. Each
/// journey stops a real run at a point that older build could have left it, replaces the scope's committed private state
/// with the equivalent payload of that version (as the older build would have written it), restarts the host and
/// finishes the recovery. The next commit writes the state back as the current version. The payload upcasts themselves
/// are unit-tested in <c>CompensationScopeUncertaintyTests</c> and <c>CompensationScopeDecisionTests</c>.
/// </summary>
public sealed class CompensationUpcastJourneyTests
{
    private const string RunId = CompensationHost.WorkflowExecutionId;
    private static readonly WorkflowExecutionPartition Partition = CompensationHost.DefaultPartition;

    /// <summary>
    /// U1: version 1 (M1) knew no retries and no waiting, so its run stops mid-recovery: Cancel delivery is scheduled and
    /// the host stops before its effect, which is the spec 250 data model's own example. After the restart the handler
    /// runs, Release inventory is scheduled with the key an M1 participant saw (the forward invocation id) and waits after
    /// its attempts, with the state written back as the current version; an operator retry finishes the recovery.
    /// </summary>
    [Fact]
    public async Task U1_a_version_1_scope_mid_recovery_finishes_and_is_written_back_as_the_current_version()
    {
        using var host = new CompensationHost(PersistenceMode.Durable);
        var participant = new Participant().Rejecting("confirm-order")
            .KillingHostBeforeEffectOf("cancel-delivery").FailingTimes("release-inventory", 3);
        await using (var first = await host.StartHostAsync(participant))
            await CompensationHost.StartAsync(first, ReservationJourney());

        await RewriteScopeStateAsync(host, version: 1, expectedPhase: CompensationScopePhase.Compensating);

        await using var second = await host.StartHostAsync(participant);
        await host.RecoverAsync(second);

        var waiting = await WaitingScopeAsync(second);
        Assert.Equal(CompensationScopeStatePersister.StateSchemaVersion, waiting.Scope.PrivateState!.StateVersion);
        var inventory = waiting.State.Entries.Single(entry => entry.Step == "Reserve inventory");
        Assert.Equal(CompensationEntryStatus.Failed, inventory.Status);
        Assert.Equal(inventory.ForwardActivityExecutionId, inventory.OperationKey);
        Assert.Equal(CompensationEntryStatus.Compensated, waiting.State.Entries.Single(entry => entry.Step == "Reserve delivery").Status);

        await RetryWaitingAsync(second, "u1-retry");

        AssertCompensated(await second.ReadRunAsync(RunId));
        Assert.Equal(["reserve-inventory", "reserve-delivery", "cancel-delivery", "release-inventory"], participant.EffectOrder);
        Assert.Single(participant.Calls, call => call.NodeId == "cancel-delivery");
        // The handler scheduled after the upgrade targets the key an M1 participant saw: the forward invocation id.
        Assert.Equal(inventory.ForwardActivityExecutionId, participant.Effects.Single(effect => effect.NodeId == "release-inventory").TargetOperationKey);
    }

    /// <summary>U2–U4: a scope waiting for intervention under version 2, 3 or 4 resumes, and an operator retry finishes it.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task U2_to_U4_a_waiting_scope_of_an_older_version_resumes_and_finishes(int version)
    {
        using var host = new CompensationHost(PersistenceMode.Durable);
        var participant = new Participant().Rejecting("confirm-order")
            .FailingTimes("cancel-delivery", 3).FailingTimes("release-inventory", 3);
        await using (var first = await host.StartHostAsync(participant))
        {
            await CompensationHost.StartAsync(first, ReservationJourney());
            await first.SweepUntilQuietAsync();
        }

        await RewriteScopeStateAsync(host, version, expectedPhase: CompensationScopePhase.AwaitingIntervention);

        await using var second = await host.StartHostAsync(participant);
        await host.RecoverAsync(second);
        var before = await WaitingScopeAsync(second);
        // Still the older payload: nothing ran, so nothing was written.
        Assert.Equal(version, before.Scope.PrivateState!.StateVersion);
        Assert.Equal(3, participant.Calls.Count(call => call.NodeId == "cancel-delivery"));

        await RetryWaitingAsync(second, $"u{version}-retry-delivery");

        // The retried handler finished its entry, Release inventory then waited after its attempts, and the scope's
        // next commits wrote the state as the current version.
        var after = await WaitingScopeAsync(second);
        Assert.Equal(CompensationScopeStatePersister.StateSchemaVersion, after.Scope.PrivateState!.StateVersion);
        Assert.Equal(CompensationEntryStatus.Compensated, after.State.Entries.Single(entry => entry.Step == "Reserve delivery").Status);
        Assert.Equal(CompensationEntryStatus.Failed, after.State.Entries.Single(entry => entry.Step == "Reserve inventory").Status);

        await RetryWaitingAsync(second, $"u{version}-retry-inventory");

        AssertCompensated(await second.ReadRunAsync(RunId));
        Assert.Empty(participant.Present);
        Assert.Single(participant.Calls.Where(call => call.NodeId == "cancel-delivery").Select(call => call.OperationKey).Distinct());
    }

    // Replaces the scope's committed private state with the equivalent payload of an older version, between host
    // generations, as the older build would have left it in the store.
    private static async Task RewriteScopeStateAsync(CompensationHost host, int version, CompensationScopePhase expectedPhase)
    {
        await using var harness = await host.StartHostAsync(new Participant());
        var store = harness.Services.GetRequiredService<IActivityExecutionStateStore>();
        var scope = (await harness.ReadRunAsync(RunId)).State(ScopeNodeId);
        var privateState = scope.PrivateState ?? throw new Xunit.Sdk.XunitException("The scope has no committed state.");
        Assert.Equal(CompensationScopeStatePersister.StateSchemaVersion, privateState.StateVersion);
        Assert.Equal(expectedPhase, CompensationScopeStatePersister.Load(scope)!.Phase);

        var payload = Downgrade(JsonNode.Parse(privateState.Value.InlineValue!.Value.GetRawText())!.AsObject(), version);
        var envelope = ValueEnvelope.Inline(
            new ValueTypeDescriptor(CompensationScopeStatePersister.StateTypeAlias, schemaVersion: version),
            JsonSerializer.SerializeToElement(payload),
            ValueProtectionPolicy.InstanceInline);
        await store.SaveAsync(scope with
        {
            PrivateState = new ActivityPrivateState(privateState.InvocationId, version, envelope, privateState.ProducedByAttemptId, privateState.CommittedAt)
        });

        var rewritten = (await harness.ReadRunAsync(RunId)).State(ScopeNodeId);
        Assert.Equal(version, rewritten.PrivateState!.StateVersion);
    }

    // The payload an older build would have written for the same state: each version drops what the next one added.
    private static JsonObject Downgrade(JsonObject state, int version)
    {
        var entries = state["entries"]!.AsArray().OfType<JsonObject>().ToArray();

        // Version 5 → 4: the attempt's due time and clamp (spec 261).
        foreach (var attempt in entries.SelectMany(entry => entry["handlerAttempts"]!.AsArray().OfType<JsonObject>()))
        {
            attempt.Remove("notBefore");
            attempt.Remove("delayClamped");
        }
        if (version >= 4)
            return state;

        // Version 4 → 3: an entry's resolution and the scope's abandonment (spec 260).
        foreach (var entry in entries)
            entry.Remove("resolution");
        state.Remove("abandonment");
        if (version >= 3)
            return state;

        // Version 3 → 2: confirmations, hand-overs and a handed-over entry's origin and handler node (spec 258).
        state.Remove("confirmations");
        state.Remove("receivedHandOvers");
        state.Remove("handOversInProgress");
        foreach (var entry in entries)
        {
            entry.Remove("origin");
            entry.Remove("handlerExecutableNodeId");
        }
        if (version >= 2)
            return state;

        // Version 2 → 1: operation keys, forward outcomes, attempts and interventions (spec 251). M1 listed the handler
        // executions that completed; a compensating entry's running handler was recorded only when it completed.
        foreach (var entry in entries)
        {
            var completed = entry["handlerAttempts"]!.AsArray().OfType<JsonObject>()
                .Where(attempt => attempt["result"]?.GetValue<string>() == nameof(CompensationAttemptResult.Completed))
                .Select(attempt => (JsonNode?)JsonValue.Create(attempt["activityExecutionId"]!.GetValue<string>()))
                .ToArray();
            foreach (var field in new[] { "operationKey", "forwardOutcome", "handlerAttempts", "interventionIncidentId" })
                entry.Remove(field);
            entry["handlerActivityExecutionIds"] = new JsonArray(completed);
        }
        return state;
    }

    private static async Task<(ActivityExecutionState Scope, CompensationScopeState State)> WaitingScopeAsync(WorkflowExecutionHarness harness)
    {
        var run = await harness.ReadRunAsync(RunId);
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState!.Status);
        var scope = run.State(ScopeNodeId);
        var state = CompensationScopeStatePersister.Load(scope)!;
        Assert.Equal(CompensationScopePhase.AwaitingIntervention, state.Phase);
        return (scope, state);
    }

    private static async Task RetryWaitingAsync(WorkflowExecutionHarness harness, string idempotencyKey)
    {
        var waiting = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(RunId));
        await OperatorActions.RetryAsync(harness, RunId, waiting.ActivityExecutionId!, idempotencyKey, Partition);
    }

    private static void AssertCompensated(WorkflowExecutionRun run)
    {
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Null(run.State(ScopeNodeId).PrivateState);
    }
}
