using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

/// <summary>#3441 T007: a prepared call's keys are one recorded insert, and every durable proposal transition is an awaited write.</summary>
public sealed class WeaverProposalRecordTests
{
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(30);

    // ---- Causal control: the call key, turn mutation and call input were three separate steps ----

    [Fact]
    public async Task A_partially_registered_call_neither_reprepares_an_identical_retry_nor_throws_on_a_conflicting_one()
    {
        var sink = new HookedAuditSink();
        var tool = new ProposalMutationTool();
        using var fixture = await ProposalFixture.CreateAsync(tool, null, services =>
            services.Replace(ServiceDescriptor.Singleton<IWeaverAuditSink>(sp => sink.Bind(sp.GetRequiredService<InMemoryWeaverAuditStore>()))));
        // The proposal is inserted, then its creation audit fails; the call input used to be registered only after that.
        sink.OnNext(WeaverAuditEventKind.ProposalCreated, (_, _) => throw new InvalidOperationException("The audit sink failed after insertion."));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy));
        var original = Assert.Single(fixture.Services.GetRequiredService<InMemoryWeaverProposalStore>().Proposals.Values);

        var conflicting = await fixture.Invoker.DispatchAsync(fixture.Invocation(arguments: new Dictionary<string, object?> { ["value"] = "other", ["text"] = "hi" }),
            fixture.Session.Policy);
        Assert.Equal("weaver.tool.duplicate_conflict", conflicting.Error?.Code);

        var repeated = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(WeaverToolDispatchOutcome.ProposalCreated, repeated.Outcome);
        Assert.Equal(original.Id, repeated.Proposal!.Id);
        Assert.Equal(1, tool.PrepareCalls);
        Assert.Single(fixture.Services.GetRequiredService<InMemoryWeaverProposalStore>().Proposals.Values);
    }

    [Fact]
    public async Task A_prepared_call_records_its_proposal_and_all_four_keys_in_one_write()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var proposal = await fixture.ProposeAsync();

        var recorded = (await RecordStore(fixture).LoadAsync(fixture.Session.Id))!;

        Assert.Equal(proposal.Id, Assert.Single(recorded.Proposals).Proposal.Id);
        Assert.Equal(WeaverActionProposalStatus.AwaitingApproval, recorded.Proposals[0].Proposal.Status);
        Assert.Equal(new[] { WeaverSessionKeyKinds.Call, WeaverSessionKeyKinds.Invocation, WeaverSessionKeyKinds.TurnMutation, WeaverSessionKeyKinds.CallInput },
            recorded.Keys.Select(x => x.Kind));
        Assert.Equal(proposal.Id, recorded.Keys.Single(x => x.Kind == WeaverSessionKeyKinds.Invocation && x.Key == proposal.PreparedInvocation!.InvocationId).Value);
        Assert.Equal(fixture.Session.Id, await RecordStore(fixture).FindProposalSessionAsync(proposal.Id));
        // Another call in the same turn is refused before anything is recorded.
        Assert.Equal("weaver.tool.turn_mutation_limit", (await fixture.Invoker.DispatchAsync(fixture.Invocation("call-2"), fixture.Session.Policy)).Error?.Code);
        Assert.Equal(4, (await RecordStore(fixture).LoadAsync(fixture.Session.Id))!.Keys.Count);
    }

    [Fact]
    public async Task Executing_is_recorded_before_the_executor_runs_and_the_outcome_is_one_aggregate_write()
    {
        var tool = new ProposalMutationTool { Started = Barrier(), Release = Barrier() };
        using var fixture = await ProposalFixture.CreateAsync(tool);
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        Assert.Equal(WeaverActionProposalStatus.Approved, (await RecordedAsync(fixture, proposal)).Proposal.Status);

        var execution = Task.Run(() => fixture.ExecuteAsync(proposal));
        await tool.Started!.Task.WaitAsync(HandoffTimeout);
        var dispatched = await RecordedAsync(fixture, proposal);
        Assert.Equal(WeaverActionProposalStatus.Executing, dispatched.Proposal.Status);
        Assert.Null(dispatched.Execution);
        tool.Release!.TrySetResult();
        Assert.True((await execution.WaitAsync(HandoffTimeout)).Value!.Executed);

        var settled = await RecordedAsync(fixture, proposal);
        Assert.Equal(WeaverActionProposalStatus.Executed, settled.Proposal.Status);
        Assert.True(settled.Execution!.Value!.Executed);
        Assert.Equal(settled.Execution.Value.ToolResult!.Receipt!.InvocationId, settled.Result!.Receipt!.InvocationId);
    }

    [Fact]
    public async Task Awaiting_client_is_recorded_before_the_envelope_returns_and_the_accepted_receipt_with_its_outcome()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);

        var pending = await fixture.ExecuteAsync(proposal);

        Assert.NotNull(pending.Value!.ClientInvocation);
        var awaiting = await RecordedAsync(fixture, proposal);
        Assert.Equal(WeaverActionProposalStatus.AwaitingClient, awaiting.Proposal.Status);
        Assert.Equal("awaiting-client", awaiting.Execution!.Value!.Status);
        Assert.Null(awaiting.AcknowledgementDigest);

        Assert.True((await fixture.AcknowledgeAsync(proposal)).Value!.Executed);
        var acknowledged = await RecordedAsync(fixture, proposal);
        Assert.Equal(WeaverActionProposalStatus.Executed, acknowledged.Proposal.Status);
        Assert.Equal(WeaverInvocationIntegrity.Digest(fixture.Acknowledgement(proposal)), acknowledged.AcknowledgementDigest);
        Assert.NotNull(acknowledged.Result!.Receipt);
    }

    [Fact]
    public async Task Denial_and_pre_dispatch_expiry_are_recorded_with_their_lifecycle_results()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var denied = await fixture.ProposeAsync();
        Assert.True((await fixture.Proposals.DenyAsync(denied.Id, ProposalFixture.Actor, "No")).Succeeded);
        var recordedDenial = await RecordedAsync(fixture, denied);
        Assert.Equal(WeaverActionProposalStatus.Denied, recordedDenial.Proposal.Status);
        Assert.NotNull(recordedDenial.Result);

        var expiring = (await fixture.Invoker.DispatchAsync(fixture.Invocation("call-1") with { TurnId = "turn-2" }, fixture.Session.Policy)).Proposal!;
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal("weaver.proposal.expired", (await fixture.ApproveAsync(expiring)).Error?.Code);
        Assert.Equal(WeaverActionProposalStatus.Expired, (await RecordedAsync(fixture, expiring)).Proposal.Status);
    }

    // ---- Review fix: without the default owner, insertion stays one atomic step ----

    [Fact]
    public async Task Racing_calls_in_one_turn_insert_one_proposal_when_the_session_service_is_replaced()
    {
        var tool = new RacingPreparationTool();
        using var fixture = await ProposalFixture.CreateAsync(tool, null, services =>
            services.Replace(ServiceDescriptor.Singleton<IWeaverSessionService>(sp => new WeaverSessionPolicyTests.ForwardingSessions(
                new InMemoryWeaverSessionService(sp.GetRequiredService<IWeaverAuditSink>(), sp.GetRequiredService<TimeProvider>())))));
        var store = fixture.Services.GetRequiredService<InMemoryWeaverProposalStore>();
        for (var turn = 0; turn < 200; turn++)
        {
            tool.Arm();
            var first = Task.Run(() => fixture.Invoker.DispatchAsync(fixture.Invocation("call-a") with { TurnId = $"turn-{turn}" }, fixture.Session.Policy));
            var second = Task.Run(() => fixture.Invoker.DispatchAsync(fixture.Invocation("call-b") with { TurnId = $"turn-{turn}" }, fixture.Session.Policy));
            var outcomes = await Task.WhenAll(first, second).WaitAsync(HandoffTimeout);

            Assert.Single(outcomes, x => x.Outcome == WeaverToolDispatchOutcome.ProposalCreated);
            Assert.Single(outcomes, x => x.Error?.Code == "weaver.tool.turn_mutation_limit");
            Assert.Single(store.Proposals.Values, x => x.PreparedInvocation!.TurnId == $"turn-{turn}");
        }
    }

    /// <summary>A mutation whose two preparations in one round are released together, so both insert at once.</summary>
    private sealed class RacingPreparationTool : IWeaverTool
    {
        private readonly ProposalMutationTool _inner = new();
        private Barrier? _round;
        public WeaverToolDescriptor Descriptor => _inner.Descriptor;
        public void Arm() => _round = new Barrier(2);
        public async Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            var round = _round!;
            await Task.Run(() => round.SignalAndWait(HandoffTimeout), cancellationToken);
            return await _inner.PrepareAsync(invocation, cancellationToken);
        }
        public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
            => _inner.ExecuteAsync(invocation, cancellationToken);
    }

    private static IWeaverSessionRecordStore RecordStore(ProposalFixture fixture) => fixture.Services.GetRequiredService<IWeaverSessionRecordStore>();

    private static async Task<WeaverProposalRecord> RecordedAsync(ProposalFixture fixture, WeaverActionProposal proposal)
        => (await RecordStore(fixture).LoadAsync(proposal.SessionId))!.Proposals.Single(x => x.Proposal.Id == proposal.Id);

    private static TaskCompletionSource Barrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
