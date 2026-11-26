using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// The spec 261 backoff journeys BK0–BK6 through the real workflow agent, in every persistence mode, on a SQLite store and
/// the shared <see cref="Participant"/>, with the three backoff restart points: a kill after the failed attempt's commit,
/// before its due time (BK2); a kill after the sweep enqueued the delayed retry, before the handler starts (BK1 variant);
/// and a due time that passed while the host was down (BK2).
/// </summary>
public sealed class CompensationBackoffJourneyTests
{
    private const string RunId = CompensationHost.WorkflowExecutionId;
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Cap = TimeSpan.FromSeconds(90);

    public static TheoryData<PersistenceMode> Modes() => new(Enum.GetValues<PersistenceMode>());

    /// <summary>BK1: one failure; the retry waits 30 s, then runs once with the same operation key.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task BK1_a_failed_handler_runs_again_once_after_its_wait(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 1);
        await using var harness = await StartAsync(host, participant);
        var faultedAt = host.Now;

        var waiting = await DeliveryAsync(harness);
        Assert.Equal(CompensationEntryStatus.Compensating, waiting.Status);
        Assert.Equal(faultedAt + Delay, waiting.HandlerAttempts[^1].NotBefore);
        Assert.Equal(1, CancelCalls(participant));

        host.Advance(Delay - TimeSpan.FromSeconds(1));
        await harness.SweepUntilQuietAsync();
        Assert.Equal(1, CancelCalls(participant));

        host.Advance(TimeSpan.FromSeconds(1));
        await harness.SweepUntilQuietAsync();

        // A completed scope's private state is cleared; the handler's two occurrences remain.
        await AssertCompensatedAsync(harness, participant, cancelCalls: 2);
        Assert.Equal(2, (await harness.ReadRunAsync(RunId)).States("cancel-delivery").Count);
        Assert.Single(participant.Calls.Where(call => call.NodeId == "cancel-delivery").Select(call => call.OperationKey).Distinct());
    }

    /// <summary>BK1, restart point 2: the host stops after the sweep queued the retry, before the handler starts.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task BK1_a_stop_after_the_retry_was_queued_runs_it_once_after_restart(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 1);
        await using (var first = await StartAsync(host, participant))
        {
            host.Advance(Delay);
            // Only the outbox step of a sweep: the delayed retry is queued, and nothing drains it before the stop.
            await using var scope = first.Services.CreateAsyncScope();
            var delivered = await scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
                .ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));
            Assert.Equal(1, delivered.DeliveredCount);
            Assert.Equal(1, CancelCalls(participant));
        }

        await using var second = await host.StartHostAsync(participant);
        await host.RecoverAsync(second);

        await AssertCompensatedAsync(second, participant, cancelCalls: 2);
    }

    /// <summary>BK2: two failures; the host stops while attempt 3 waits and restarts after its due time.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task BK2_a_retry_that_was_due_while_the_host_was_down_runs_once_after_restart(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 2);
        await using (var first = await StartAsync(host, participant))
        {
            host.Advance(Delay);
            await first.SweepUntilQuietAsync();
            Assert.Equal(2, CancelCalls(participant));
            var waiting = await DeliveryAsync(first);
            Assert.Equal(host.Now + Delay * 2, waiting.HandlerAttempts[^1].NotBefore);
        }

        // Down past the due time: 200 s after the first fault.
        host.Advance(TimeSpan.FromSeconds(170));
        await using var second = await host.StartHostAsync(participant);
        await second.SweepUntilQuietAsync();

        await AssertCompensatedAsync(second, participant, cancelCalls: 3);
        Assert.Equal(3, (await second.ReadRunAsync(RunId)).States("cancel-delivery").Count);
    }

    /// <summary>BK3: every attempt fails; the waits are 30, 60 and 90 s (capped), then an operator retry runs at once.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task BK3_the_waits_grow_to_the_cap_and_an_operator_retry_never_waits(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 99);
        await using var harness = await StartAsync(host, participant);
        var waits = new List<TimeSpan>();

        for (var attempt = 2; attempt <= 4; attempt++)
        {
            var waiting = await DeliveryAsync(harness);
            var wait = waiting.HandlerAttempts[^1].NotBefore!.Value - host.Now;
            waits.Add(wait);
            host.Advance(wait);
            await harness.SweepUntilQuietAsync();
        }

        Assert.Equal([TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), Cap], waits);
        var failed = await DeliveryAsync(harness);
        Assert.Equal(CompensationEntryStatus.Failed, failed.Status);
        Assert.Equal(4, failed.HandlerAttempts.Count);
        Assert.Equal(4, CancelCalls(participant));
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(RunId));

        await OperatorRetry.RetryAsync(harness, RunId, incident.ActivityExecutionId!, "bk3-retry", CompensationHost.DefaultPartition);

        // The operator's attempt ran at once, without the clock moving, and recorded no due time.
        Assert.Equal(5, CancelCalls(participant));
        var retried = await DeliveryAsync(harness);
        Assert.Equal(5, retried.HandlerAttempts.Count);
        Assert.Null(retried.HandlerAttempts[^1].NotBefore);
        Assert.Contains("reserve-delivery", participant.EffectOrder);
        Assert.Equal(2, participant.Present.Count);
    }

    /// <summary>BK4: the run is cancelled while attempt 2 waits; the handler never starts.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task BK4_cancelling_the_run_while_a_retry_waits_stops_it(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 99);
        await using var harness = await StartAsync(host, participant);

        await harness.CancelAsync(RunId, CompensationHost.DefaultPartition);
        host.Advance(Delay + TimeSpan.FromSeconds(1));
        await harness.SweepUntilQuietAsync();

        var run = await harness.ReadRunAsync(RunId);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, run.WorkflowState!.Status);
        Assert.Equal(1, CancelCalls(participant));
        Assert.Single(run.States("cancel-delivery"));
        Assert.Equal(2, participant.Present.Count);
    }

    /// <summary>BK5: a republish while the retry waits; the retry runs the handler of the pinned executable.</summary>
    [Fact]
    public async Task BK5_a_retry_after_a_republish_runs_the_pinned_handler()
    {
        using var host = new CompensationHost(PersistenceMode.Durable);
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 1);
        await using var harness = await StartAsync(host, participant);

        var republished = Shape(cancelDeliveryNodeId: "cancel-delivery-v2");
        var nextVersion = new WorkflowExecutableIdentity("artifact-2", WorkflowExecutionHarness.Identity.DefinitionId, "version-2", "2.0.0", "sha256:test-2");
        await harness.PublishAsync(WorkflowExecutionHarness.NewExecutable(republished, nextVersion), "source-compensation-republished");
        host.Advance(Delay);
        await harness.SweepUntilQuietAsync();

        await AssertCompensatedAsync(harness, participant, cancelCalls: 2);
        Assert.DoesNotContain(participant.Calls, call => call.NodeId == "cancel-delivery-v2");
    }

    /// <summary>
    /// BK6: an inner scope's retry waits while a sibling inner scope, in a Parallel, completes and hands its entry over to
    /// the outer scope: the outer scope journals it at once. Then the outer scope's last step fails and every effect is
    /// undone once.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task BK6_a_hand_over_is_journaled_while_an_inner_retry_waits(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("confirm-order", "ship").FailingTimes("cancel-delivery", 1);
        var root = Scope("node-outer", faultAfterCompensation: false, handlerMaxAttempts: 3, confirmOnComplete: false, Backoff.None,
            new Step("Charge", Participant("charge"), Participant("refund")),
            new Step("Fulfil", ParallelNode("node-parallel",
                Shape(scopeNodeId: "node-inner"),
                Scope("node-sibling", faultAfterCompensation: false, new Step("Pack", Participant("pack"), Participant("unpack"))))),
            new Step("Ship", Participant("ship")));
        var harness = await host.StartHostAsync(participant);
        await using var _ = harness;
        await CompensationHost.StartAsync(harness, root);
        await harness.SweepUntilQuietAsync();

        // The inner scope waits for its retry; the sibling finished and its entry is already the outer scope's.
        var run = await harness.ReadRunAsync(RunId);
        var inner = CompensationScopeStatePersister.Load(run.State("node-inner"))!;
        Assert.Equal(CompensationScopePhase.Compensating, inner.Phase);
        Assert.NotNull(inner.Entries.Single(entry => entry.Step == "Reserve delivery").HandlerAttempts[^1].NotBefore);
        var outer = CompensationScopeStatePersister.Load(run.State("node-outer"))!;
        Assert.Contains(run.State("node-sibling").Execution.ActivityExecutionId, outer.ReceivedHandOvers);
        Assert.Contains(outer.Entries, entry => entry.Step == "Pack");

        host.Advance(Delay);
        await harness.SweepUntilQuietAsync();
        await harness.SweepUntilQuietAsync();

        run = await harness.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-outer", CompensationScope.CompensatedOutcome);
        Assert.Empty(participant.Present);
        Assert.Single(participant.Effects, effect => effect.NodeId == "cancel-delivery");
        Assert.Equal(2, participant.Calls.Count(call => call.NodeId == "cancel-delivery"));
        Assert.Contains("unpack", participant.EffectOrder);
        Assert.Contains("refund", participant.EffectOrder);
    }

    /// <summary>BK0: the default inputs retry at once, as in M2–M4 (spec 251 J3).</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task BK0_the_default_retries_at_once(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant().Rejecting("confirm-order").FailingTimes("cancel-delivery", 2);
        await using var harness = await host.StartHostAsync(participant);
        await CompensationHost.StartAsync(harness, Shape(backoff: Backoff.None));
        await harness.SweepUntilQuietAsync();

        // Three attempts without the clock moving: none of them waited.
        await AssertCompensatedAsync(harness, participant, cancelCalls: 3);
    }

    // The quickstart shape: handlerMaxAttempts 4, delay 30 s, ×2, cap 90 s.
    private static ExecutableNode Shape(string scopeNodeId = ScopeNodeId, string cancelDeliveryNodeId = "cancel-delivery", Backoff? backoff = null) =>
        Scope(scopeNodeId, faultAfterCompensation: false, handlerMaxAttempts: 4, confirmOnComplete: false, backoff ?? new Backoff(Delay, 2, Cap),
            new Step("Reserve inventory", Participant("reserve-inventory"), Participant("release-inventory")),
            new Step("Reserve delivery", Participant("reserve-delivery"), Participant(cancelDeliveryNodeId)),
            new Step("Confirm order", Participant("confirm-order")));

    private static async Task<WorkflowExecutionHarness> StartAsync(CompensationHost host, Participant participant)
    {
        var harness = await host.StartHostAsync(participant);
        await CompensationHost.StartAsync(harness, Shape());
        await harness.SweepUntilQuietAsync();
        return harness;
    }

    // The Reserve delivery entry of a scope that has not completed: a completed scope's private state is cleared.
    private static async Task<CompensationEntry> DeliveryAsync(WorkflowExecutionHarness harness)
    {
        var run = await harness.ReadRunAsync(RunId);
        var state = CompensationScopeStatePersister.Load(run.State(ScopeNodeId))
                    ?? throw new Xunit.Sdk.XunitException("The scope has no private state.");
        return state.Entries.Single(entry => entry.Step == "Reserve delivery");
    }

    private static async Task AssertCompensatedAsync(WorkflowExecutionHarness harness, Participant participant, int cancelCalls)
    {
        var run = await harness.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Empty(participant.Present);
        Assert.Single(participant.Effects, effect => effect.NodeId == "cancel-delivery");
        Assert.Equal(cancelCalls, CancelCalls(participant));
    }

    private static int CancelCalls(Participant participant) => participant.Calls.Count(call => call.NodeId == "cancel-delivery");
}
