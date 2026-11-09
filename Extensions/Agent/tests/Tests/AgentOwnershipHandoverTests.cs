using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 Phase 3 review: ownership handing over between processes, and a graceful restart. A process that lost a session
/// never acts on what it read before the loss, a recorded proposal changes only from the version its writer read, and a
/// graceful shutdown hands its sessions back at once.
/// </summary>
public sealed class AgentOwnershipHandoverTests
{
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PastLease = InMemoryAgentSessionRecordStore.LeaseDuration + TimeSpan.FromSeconds(1);

    [Fact]
    public async Task A_reacquiring_owner_cannot_execute_a_proposal_another_process_denied()
    {
        using var harness = new AgentRestartHarness();
        var a = harness.Host;
        var session = await a.CreateAsync();
        var proposal = (await a.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await a.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        using var b = await DenyElsewhereAsync(harness, session, proposal);

        // The earlier owner's next write is refused under its superseded fence, which evicts what it had loaded.
        Assert.Equal(AgentSessionOwnershipException.OwnershipLost, (await Assert.ThrowsAsync<AgentSessionOwnershipException>(
            () => a.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision))).Code);
        harness.Clock.Advance(PastLease);

        var retried = await a.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);

        Assert.Equal("agent.proposal.closed", retried.Error?.Code);
        Assert.Equal(AgentActionProposalStatus.Denied, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
        Assert.Equal(0, harness.Module.Dispatches);
    }

    [Fact]
    public async Task A_reacquiring_owner_cannot_approve_a_proposal_another_process_denied()
    {
        using var harness = new AgentRestartHarness();
        var a = harness.Host;
        var session = await a.CreateAsync();
        var proposal = (await a.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        using var b = await DenyElsewhereAsync(harness, session, proposal);

        Assert.Equal(AgentSessionOwnershipException.OwnershipLost, (await Assert.ThrowsAsync<AgentSessionOwnershipException>(
            () => a.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision))).Code);
        harness.Clock.Advance(PastLease);

        var approved = await a.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        var executed = await a.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);

        Assert.Equal("agent.proposal.invalid_state", approved.Error?.Code);
        Assert.Equal("agent.proposal.closed", executed.Error?.Code);
        Assert.Equal(AgentActionProposalStatus.Denied, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
        Assert.Equal(0, harness.Module.Dispatches);
    }

    [Fact]
    public async Task A_proposal_without_a_bound_claim_is_never_marked_dispatched()
    {
        using var harness = new AgentRestartHarness();
        var session = await harness.Host.CreateAsync();
        var proposal = (await harness.Host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        harness.Host.Owner.ReleaseProposal(session.Id, proposal.Id);

        Assert.False(await harness.Host.Owner.MarkProposalDispatchedAsync(session.Id, proposal.Id));
    }

    [Fact]
    public async Task A_recorded_proposal_changes_only_from_the_version_its_writer_read()
    {
        var store = new InMemoryAgentSessionRecordStore();
        var session = new AgentSession("session-1", "Title", "tenant-1", "actor-1", "conversation-1", "provider", "explain", AgentPolicy.Default,
            AgentSessionStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, new Dictionary<string, string>());
        var proposal = new AgentActionProposal("proposal-1", session.Id, null, "capability", "kind", "Title", "Summary", AgentRisk.ReviewRequired, null,
            [], [], [], null, [], null, null, true, AgentActionProposalStatus.AwaitingApproval, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var fence = (await store.CreateAsync(new(session) { Proposals = [new(proposal)] }, "owner-1")).Fence!;
        var recorded = (await store.LoadAsync(session.Id))!.Proposals.Single();
        Assert.Equal(1, recorded.Version);

        var denied = recorded with { Proposal = proposal with { Status = AgentActionProposalStatus.Denied } };
        Assert.True((await store.WriteAsync(new(session.Id, fence) { Proposals = [denied] })).Succeeded);
        var stale = recorded with { Proposal = proposal with { Status = AgentActionProposalStatus.Approved } };
        Assert.Equal(AgentSessionRecordOutcome.VersionConflict, (await store.WriteAsync(new(session.Id, fence) { Proposals = [stale] })).Outcome);
        var duplicate = new AgentProposalRecord(proposal with { Status = AgentActionProposalStatus.Approved });
        Assert.Equal(AgentSessionRecordOutcome.VersionConflict, (await store.WriteAsync(new(session.Id, fence) { Proposals = [duplicate] })).Outcome);

        var current = (await store.LoadAsync(session.Id))!.Proposals.Single();
        Assert.Equal((AgentActionProposalStatus.Denied, 2L), (current.Proposal.Status, current.Version));
    }

    [Fact]
    public async Task A_conflicting_outcome_write_after_dispatch_is_uncertain_never_a_conflict_to_decide_again()
    {
        using var harness = new AgentRestartHarness(clock => new ScriptedRecordStore(new InMemoryAgentSessionRecordStore(clock)));
        var store = (ScriptedRecordStore)harness.Store;
        var host = harness.Host;
        var session = await host.CreateAsync();
        var proposal = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        // Executing is recorded; the outcome write after the executor ran finds the operation changed meanwhile.
        store.ConflictNextWrite(write => write.Proposals.Any(x => x.Proposal.Id == proposal.Id && x.Proposal.Status != AgentActionProposalStatus.Executing));

        var executed = await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);

        Assert.Equal(1, harness.Module.Dispatches);
        Assert.True(executed.Succeeded, executed.Error?.Message);
        Assert.Equal(("uncertain", false), (executed.Value!.Status, executed.Value.Executed));
        Assert.DoesNotContain("decide again", executed.Value.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nothing was recorded", executed.Value.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AgentActionProposalStatus.Executing, (await store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
    }

    [Fact]
    public async Task A_graceful_restart_hands_recent_sessions_back_at_once()
    {
        using var harness = new AgentRestartHarness();
        var session = await harness.Host.CreateAsync();
        await harness.Host.PostAsync(session, "hello");

        var host = harness.Restart(afterLease: false);

        Assert.Equal(session.Id, (await host.Sessions.FindAsync(session.Id))!.Id);
        Assert.Single(await host.Sessions.ListMessagesAsync(session.Id));
    }

    [Fact]
    public async Task One_failing_renewal_does_not_stop_the_renewal_of_other_sessions()
    {
        using var harness = new AgentRestartHarness(clock => new ScriptedRecordStore(new InMemoryAgentSessionRecordStore(clock)));
        var store = (ScriptedRecordStore)harness.Store;
        var owner = harness.Host.Owner;
        var first = await harness.Host.CreateAsync();
        var second = await harness.Host.CreateAsync();
        Assert.NotNull(await owner.AcquireAsync(first.Id, AgentSessionClaimKind.Foreground));
        Assert.NotNull(await owner.AcquireAsync(second.Id, AgentSessionClaimKind.Foreground));
        harness.Clock.Advance(TimeSpan.FromSeconds(20));
        store.FailNextAcquire();

        await owner.RenewLeasesAsync();

        // Whichever session failed, the other one's lease was extended in the same pass.
        var leases = new[] { (await store.LoadAsync(first.Id))!.LeaseExpiresAt, (await store.LoadAsync(second.Id))!.LeaseExpiresAt };
        Assert.Contains(harness.Clock.GetUtcNow() + InMemoryAgentSessionRecordStore.LeaseDuration, leases);
    }

    [Fact]
    public async Task A_stale_row_losing_ownership_never_cancels_a_newer_hydrations_turn()
    {
        using var harness = new AgentRestartHarness(clock => new ScriptedRecordStore(new InMemoryAgentSessionRecordStore(clock)));
        var store = (ScriptedRecordStore)harness.Store;
        var a = harness.Host;
        var session = await a.CreateAsync();
        Assert.NotNull(await a.Owner.AcquireAsync(session.Id, AgentSessionClaimKind.Foreground));
        // A renewal pass reads the loaded row and is held at its ownership read.
        var held = store.HoldNextAcquire();
        var renewal = Task.Run(() => a.Owner.RenewLeasesAsync());
        await held.Entered.Task.WaitAsync(HandoffTimeout);

        // Meanwhile that row is lost to another process and evicted, and this process later hydrates the session again.
        harness.Clock.Advance(PastLease);
        using (var b = harness.StartAnotherHost())
            Assert.NotNull(await b.Sessions.FindAsync(session.Id));
        await Assert.ThrowsAsync<AgentSessionOwnershipException>(() => a.PostAsync(session, "lost"));
        harness.Clock.Advance(PastLease);
        await a.PostAsync(session, "hello");
        var turn = harness.Provider.HoldNext();
        var running = Task.Run(() => a.StreamAsync(session));
        await turn.Entered.Task.WaitAsync(HandoffTimeout);

        // The held pass now finds the session under a newer fence: the stale row it read is not the current one.
        held.Release.SetResult();
        await renewal.WaitAsync(HandoffTimeout);
        turn.Release.SetResult();

        Assert.Equal(AgentStreamEventKind.Completed, (await running.WaitAsync(HandoffTimeout))[^1].Kind);
    }

    [Fact]
    public async Task A_policy_stale_refusal_records_why_it_was_not_dispatched()
    {
        using var harness = new AgentRestartHarness();
        var host = harness.Host;
        var session = await host.CreateAsync();
        var proposal = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        host.Owner.ReleaseProposal(session.Id, proposal.Id);
        Assert.True((await host.Policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, AgentAutonomyMode.FullAuto, 1))).Succeeded);

        Assert.Equal("agent.proposal.policy_stale", (await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Error?.Code);

        var result = (await host.Proposals.FindResultAsync(proposal.Id))!;
        Assert.Contains("policy", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("executor", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("refused", result.Payload!.Data.GetProperty("status").GetString());
    }

    private static async Task<RecoveryHost> DenyElsewhereAsync(AgentRestartHarness harness, AgentSession session, AgentActionProposal proposal)
    {
        harness.Clock.Advance(PastLease);
        var b = harness.StartAnotherHost();
        Assert.True((await b.Proposals.DenyAsync(proposal.Id, session.ActorId, "denied elsewhere")).Succeeded);
        return b;
    }

    private sealed class Hold
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>A forwarding record store whose next ownership read can be held at a barrier or made to fail.</summary>
    private sealed class ScriptedRecordStore(IAgentSessionRecordStore inner) : IAgentSessionRecordStore
    {
        private Hold? _hold;
        private int _fail;

        public bool SupportsFencing => inner.SupportsFencing;

        public Hold HoldNextAcquire()
        {
            var hold = new Hold();
            Volatile.Write(ref _hold, hold);
            return hold;
        }

        public void FailNextAcquire() => Volatile.Write(ref _fail, 1);

        private Func<AgentSessionRecordWrite, bool>? _conflict;

        /// <summary>The next matching write answers <see cref="AgentSessionRecordOutcome.VersionConflict"/> and records nothing.</summary>
        public void ConflictNextWrite(Func<AgentSessionRecordWrite, bool> condition) => Volatile.Write(ref _conflict, condition);

        public async Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _fail, 0) == 1) throw new InvalidOperationException("The store is unavailable for this read.");
            if (Interlocked.Exchange(ref _hold, null) is { } held)
            {
                held.Entered.TrySetResult();
                await held.Release.Task.WaitAsync(HandoffTimeout, cancellationToken);
            }
            return await inner.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
        }

        public Task<AgentSessionRecordResult> CreateAsync(AgentSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => inner.CreateAsync(record, ownerToken, cancellationToken);
        public Task<AgentSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default) => inner.LoadAsync(sessionId, cancellationToken);
        public Task ReleaseOwnershipAsync(string sessionId, AgentSessionFence fence, CancellationToken cancellationToken = default)
            => inner.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);
        public Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _conflict) is { } condition && condition(write) && Interlocked.CompareExchange(ref _conflict, null, condition) == condition)
                return Task.FromResult(new AgentSessionRecordResult(AgentSessionRecordOutcome.VersionConflict));
            return inner.WriteAsync(write, cancellationToken);
        }
        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => inner.FindProposalSessionAsync(proposalId, cancellationToken);
        public Task<AgentSessionSummaryPage> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => inner.ListOwnedAsync(query, cancellationToken);
        public Task<AgentSessionOwner?> FindOwnerAsync(string sessionId, CancellationToken cancellationToken = default) => inner.FindOwnerAsync(sessionId, cancellationToken);
    }
}
