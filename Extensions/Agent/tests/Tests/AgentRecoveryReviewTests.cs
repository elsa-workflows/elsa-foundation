using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 review of the Phase 3–5 recovery work: a decision never overwrites a newer recorded state, an overdue client
/// request is uncertain without a restart, an invocation of another session is not disclosed, ownership loss is never
/// recorded as the owner's stop, and a graceful shutdown's lease release is never undone by renewal.
/// </summary>
public sealed class AgentRecoveryReviewTests
{
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PastLease = InMemoryAgentSessionRecordStore.LeaseDuration + TimeSpan.FromSeconds(1);

    [Fact]
    public async Task A_decision_on_a_proposal_another_process_settled_meanwhile_is_refused_and_records_nothing()
    {
        GatedRecordStore? store = null;
        using var harness = new AgentRestartHarness(clock => store = new(new InMemoryAgentSessionRecordStore(clock)));
        var host = harness.Host;
        var session = await host.CreateAsync();
        var proposal = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        // Another write of this process holds the session write gate, so the execute below reads Approved and then waits.
        var held = store!.HoldNextWrite(write => write.Messages.Count > 0);
        var posting = Task.Run(() => host.PostAsync(session, "hello"));
        await held.Entered.Task.WaitAsync(HandoffTimeout);
        var execute = host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);

        // Meanwhile another process takes the session over, executes the proposal and shuts down gracefully.
        harness.Clock.Advance(PastLease);
        using (var other = harness.StartAnotherHost())
            Assert.True((await other.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value!.Executed);
        held.Release.SetResult();
        await Assert.ThrowsAsync<AgentSessionOwnershipException>(() => posting.WaitAsync(HandoffTimeout));

        // This process hydrates the newer record, but its decision was made on Approved: it is refused, not recorded.
        var refused = await execute.WaitAsync(HandoffTimeout);
        Assert.Equal("agent.proposal.conflict", refused.Error?.Code);
        Assert.Equal(409, refused.Error?.StatusCode);
        Assert.Equal(AgentActionProposalStatus.Executed, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
        Assert.Equal(AgentActionProposalStatus.Executed, (await host.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.Equal((1, 1), (harness.Module.Dispatches, harness.Module.Effects));
        Assert.DoesNotContain(await host.AuditAsync(session), x => x.Metadata.GetValueOrDefault("status") == nameof(AgentActionProposalStatus.Expired));
    }

    [Fact]
    public async Task An_overdue_client_request_is_listed_uncertain_without_a_restart()
    {
        var client = new ProposalClientTool();
        using var harness = new AgentRestartHarness(configure: services => services.AddSingleton<IAgentTool>(client));
        var (session, proposal) = await AwaitingClientAsync(harness, client);
        harness.Clock.Advance(TimeSpan.FromMinutes(6));

        var listed = Assert.Single((await harness.Host.Proposals.ListAsync(session.Id)).Value!);

        Assert.Equal((proposal.Id, AgentActionProposalStatus.Uncertain), (listed.Id, listed.Status));
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
        Assert.Equal(0, client.CompleteCalls);
    }

    [Fact]
    public async Task An_overdue_client_request_can_be_resolved_by_its_owner_without_a_restart()
    {
        var client = new ProposalClientTool();
        using var harness = new AgentRestartHarness(configure: services => services.AddSingleton<IAgentTool>(client));
        var (session, proposal) = await AwaitingClientAsync(harness, client);
        harness.Clock.Advance(TimeSpan.FromMinutes(6));

        var resolved = await harness.Host.Proposals.ResolveClientInvocationAsync(session.Id, proposal.PreparedInvocation!.InvocationId,
            session.ActorId, AgentOwnerResolutionClaim.NotApplied);

        Assert.True(resolved.Succeeded, resolved.Error?.Code);
        Assert.Equal((AgentActionProposalStatus.Uncertain, AgentOwnerResolutionClaim.NotApplied), (resolved.Value!.Status, resolved.Value.OwnerResolution?.Claim));
        // The deadline settlement and the statement are each recorded once.
        var audit = await harness.Host.AuditAsync(session);
        Assert.Single(audit, x => x.Metadata.GetValueOrDefault("deadline") == "pending-client");
    }

    [Fact]
    public async Task The_owners_resolution_is_audited_once_with_the_operations_correlation_and_the_claim()
    {
        var client = new ProposalClientTool();
        using var harness = new AgentRestartHarness(configure: services => services.AddSingleton<IAgentTool>(client));
        var (session, proposal) = await AwaitingClientAsync(harness, client);
        harness.Clock.Advance(TimeSpan.FromMinutes(6));
        var prepared = proposal.PreparedInvocation!;

        Assert.True((await harness.Host.Proposals.ResolveClientInvocationAsync(session.Id, prepared.InvocationId, session.ActorId,
            AgentOwnerResolutionClaim.NotApplied)).Succeeded);
        // An idempotent retry records nothing more.
        Assert.True((await harness.Host.Proposals.ResolveClientInvocationAsync(session.Id, prepared.InvocationId, session.ActorId,
            AgentOwnerResolutionClaim.NotApplied)).Succeeded);

        var resolved = Assert.Single(await harness.Host.AuditAsync(session), x => x.Kind == AgentAuditEventKind.ProposalResolved);
        Assert.Equal((session.Id, session.ActorId), (resolved.SessionId, resolved.ActorId));
        Assert.Equal(proposal.Id, resolved.Metadata["proposalId"]);
        Assert.Equal(prepared.InvocationId, resolved.Metadata["invocationId"]);
        Assert.Equal(prepared.TurnId, resolved.Metadata["turnId"]);
        Assert.Equal(prepared.ToolCallId, resolved.Metadata["toolCallId"]);
        Assert.Equal("notApplied", resolved.Metadata["claim"]);
        Assert.Equal(nameof(AgentActionProposalStatus.Uncertain), resolved.Metadata["status"]);
    }

    [Fact]
    public async Task An_invocation_of_another_owners_session_is_not_found_rather_than_forbidden()
    {
        var client = new ProposalClientTool();
        using var harness = new AgentRestartHarness(configure: services => services.AddSingleton<IAgentTool>(client));
        var (_, proposal) = await AwaitingClientAsync(harness, client);
        var foreign = await harness.Host.Sessions.CreateAsync(new("tenant-1", "actor-2", "conversation-2", DeterministicAgentProvider.Id, "explain",
            "Foreign session", AgentPolicy.Default, new Dictionary<string, string>()));

        var guessed = await harness.Host.Proposals.ResolveClientInvocationAsync(foreign.Id, proposal.PreparedInvocation!.InvocationId,
            foreign.ActorId, AgentOwnerResolutionClaim.Applied);
        var missing = await harness.Host.Proposals.ResolveClientInvocationAsync(foreign.Id, "missing-invocation",
            foreign.ActorId, AgentOwnerResolutionClaim.Applied);

        Assert.Equal(("agent.invocation.not_found", 404), (guessed.Error?.Code, guessed.Error?.StatusCode));
        Assert.Equal((missing.Error?.Code, missing.Error?.StatusCode), (guessed.Error?.Code, guessed.Error?.StatusCode));
        Assert.Null((await harness.Host.Proposals.FindAsync(proposal.Id))!.OwnerResolution);
    }

    [Fact]
    public async Task A_turn_cancelled_by_ownership_loss_keeps_the_interrupted_outcome_another_process_recorded()
    {
        using var harness = new AgentRestartHarness();
        var host = harness.Host;
        var session = await host.CreateAsync();
        var prompt = await host.PostAsync(session, "hello");
        var held = harness.Provider.HoldNext();
        var running = Task.Run(() => host.StreamAsync(session));
        await held.Entered.Task.WaitAsync(HandoffTimeout);

        // Another process takes over, records the running prompt as interrupted, and shuts down gracefully.
        harness.Clock.Advance(PastLease);
        using (var other = harness.StartAnotherHost())
            Assert.Equal(AgentMessageStatusReasons.Interrupted, (await other.Sessions.FindMessageAsync(session.Id, prompt.Id))!.StatusReason);
        // This process then detects the loss; that cancels its turn, which is not the owner's stop.
        await host.Owner.RenewLeasesAsync();
        try { await running.WaitAsync(HandoffTimeout); }
        catch (AgentContributionException) { }

        var recorded = (await harness.Store.LoadAsync(session.Id))!.Messages.Single(x => x.Message.Id == prompt.Id).Message;
        Assert.Equal((AgentMessageStatus.Cancelled, AgentMessageStatusReasons.Interrupted), (recorded.Status, recorded.StatusReason));
    }

    [Fact]
    public async Task A_renewal_in_flight_at_a_graceful_shutdown_never_undoes_its_lease_release()
    {
        GatedRecordStore? store = null;
        using var harness = new AgentRestartHarness(clock => store = new(new InMemoryAgentSessionRecordStore(clock)));
        var host = harness.Host;
        var session = await host.CreateAsync();
        await host.PostAsync(session, "hello");
        var turn = harness.Provider.HoldNext();
        var running = Task.Run(() => host.StreamAsync(session));
        await turn.Entered.Task.WaitAsync(HandoffTimeout);
        var acquire = store!.HoldNextAcquire();
        var renewal = Task.Run(() => host.Owner.RenewLeasesAsync());
        await acquire.Entered.Task.WaitAsync(HandoffTimeout);

        // The shutdown releases the lease while the renewal is still on its way to the store.
        host.Owner.Dispose();
        Assert.Null((await harness.Store.LoadAsync(session.Id))!.LeaseExpiresAt);
        acquire.Release.SetResult();
        await renewal.WaitAsync(HandoffTimeout);

        Assert.Null((await harness.Store.LoadAsync(session.Id))!.LeaseExpiresAt);
        // A timer callback after the shutdown renews nothing either.
        await host.Owner.RenewLeasesAsync();
        Assert.Null((await harness.Store.LoadAsync(session.Id))!.LeaseExpiresAt);
        turn.Release.SetResult();
        try { await running.WaitAsync(HandoffTimeout); }
        catch (Exception) { }
    }

    [Fact]
    public async Task A_shut_down_process_takes_no_new_ownership()
    {
        using var harness = new AgentRestartHarness();
        var session = await harness.Host.CreateAsync();
        await harness.Host.PostAsync(session, "hello");
        var host = harness.Restart(afterLease: false);
        Assert.Null((await harness.Store.LoadAsync(session.Id))!.LeaseExpiresAt);

        host.Owner.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => host.Sessions.ListMessagesAsync(session.Id));
        Assert.Null((await harness.Store.LoadAsync(session.Id))!.LeaseExpiresAt);
        // The next process takes the session over at once.
        using var next = harness.StartAnotherHost();
        Assert.Single(await next.Sessions.ListMessagesAsync(session.Id));
    }

    [Fact]
    public async Task A_write_completing_after_a_graceful_shutdown_gives_its_lease_back()
    {
        GatedRecordStore? store = null;
        using var harness = new AgentRestartHarness(clock => store = new(new InMemoryAgentSessionRecordStore(clock)));
        var host = harness.Host;
        var session = await host.CreateAsync();
        var write = store!.HoldNextWrite(_ => true);
        var posting = Task.Run(() => host.PostAsync(session, "hello"));
        await write.Entered.Task.WaitAsync(HandoffTimeout);

        // The shutdown releases the lease while the write is still on its way to the store.
        host.Owner.Dispose();
        Assert.Null((await harness.Store.LoadAsync(session.Id))!.LeaseExpiresAt);
        write.Release.SetResult();
        await posting.WaitAsync(HandoffTimeout);

        var recorded = (await harness.Store.LoadAsync(session.Id))!;
        Assert.Single(recorded.Messages);
        Assert.Null(recorded.LeaseExpiresAt);
    }

    private static async Task<(AgentSession Session, AgentActionProposal Proposal)> AwaitingClientAsync(AgentRestartHarness harness, ProposalClientTool client)
    {
        var host = harness.Host;
        var session = await host.CreateAsync();
        var proposal = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.ClientCall(session, client), session.Policy)).Proposal!;
        Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.Equal("awaiting-client", (await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value?.Status);
        return (session, proposal);
    }

    /// <summary>A forwarding record store whose next matching write, or next ownership acquisition, waits at a barrier.</summary>
    private sealed class GatedRecordStore(IAgentSessionRecordStore inner) : IAgentSessionRecordStore
    {
        private (Func<AgentSessionRecordWrite, bool> Condition, Barrier Barrier)? _write;
        private Barrier? _acquire;

        public bool SupportsFencing => inner.SupportsFencing;

        public Barrier HoldNextWrite(Func<AgentSessionRecordWrite, bool> condition)
        {
            var barrier = new Barrier();
            lock (this) _write = (condition, barrier);
            return barrier;
        }

        public Barrier HoldNextAcquire() => Volatile.Read(ref _acquire) ?? (_acquire = new Barrier());

        public async Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default)
        {
            Barrier? barrier = null;
            lock (this)
                if (_write is { } armed && armed.Condition(write))
                {
                    barrier = armed.Barrier;
                    _write = null;
                }
            if (barrier is not null) await barrier.PassAsync();
            return await inner.WriteAsync(write, cancellationToken);
        }

        public async Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _acquire, null) is { } barrier) await barrier.PassAsync();
            return await inner.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
        }

        public Task<AgentSessionRecordResult> CreateAsync(AgentSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => inner.CreateAsync(record, ownerToken, cancellationToken);
        public Task<AgentSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default) => inner.LoadAsync(sessionId, cancellationToken);
        public Task ReleaseOwnershipAsync(string sessionId, AgentSessionFence fence, CancellationToken cancellationToken = default)
            => inner.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);
        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => inner.FindProposalSessionAsync(proposalId, cancellationToken);
        public Task<AgentSessionSummaryPage> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => inner.ListOwnedAsync(query, cancellationToken);
        public Task<AgentSessionOwner?> FindOwnerAsync(string sessionId, CancellationToken cancellationToken = default) => inner.FindOwnerAsync(sessionId, cancellationToken);
    }

    private sealed class Barrier
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task PassAsync()
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(HandoffTimeout);
        }
    }
}
