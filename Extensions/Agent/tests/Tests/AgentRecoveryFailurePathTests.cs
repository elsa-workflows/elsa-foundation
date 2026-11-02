using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 self-review round 1: store failures around dispatch, settlement and hydration leave no state the record does
/// not hold. A dispatch that was never recorded keeps neither the session busy nor its lease renewed, and an outcome that
/// could not be recorded is settled from the record, never left in memory.
/// </summary>
public sealed class AgentRecoveryFailurePathTests
{
    private static readonly TimeSpan PastDeadline = TimeSpan.FromMinutes(6);
    private static readonly TimeSpan PastLease = InMemoryAgentSessionRecordStore.LeaseDuration + TimeSpan.FromSeconds(1);

    [Fact]
    public async Task A_failed_executing_write_leaves_the_claim_undispatched_so_its_deadline_still_ends_the_busy_state()
    {
        using var harness = new AgentRestartHarness(clock => new FaultingRecordStore(new InMemoryAgentSessionRecordStore(clock)));
        var store = (FaultingRecordStore)harness.Store;
        var host = harness.Host;
        var session = await host.CreateAsync();
        var proposal = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        store.FailNextWrite(write => write.Proposals.Any(x => x.Proposal.Status == AgentActionProposalStatus.Executing));

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision));

        Assert.Equal(0, harness.Module.Dispatches);
        Assert.Equal(AgentActionProposalStatus.Approved, (await host.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.False(host.Owner.RenewalActive);
        // Before its deadline the approved operation keeps the session busy; after it, the claim can never dispatch.
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await host.ProbeAsync(session)).Outcome);
        harness.Clock.Advance(PastDeadline);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);
    }

    [Fact]
    public async Task An_outcome_the_store_could_not_record_is_settled_uncertain_from_the_record_and_never_replayed()
    {
        using var harness = new AgentRestartHarness(clock => new FaultingRecordStore(new InMemoryAgentSessionRecordStore(clock)));
        var store = (FaultingRecordStore)harness.Store;
        var host = harness.Host;
        var session = await host.CreateAsync();
        var proposal = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        store.FailNextWrite(write => write.Proposals.Any(x => x.Proposal.Status == AgentActionProposalStatus.Executed));

        var result = await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);

        Assert.Equal("uncertain", result.Value?.Status);
        Assert.Equal(1, harness.Module.Effects);
        // The next access hydrates the recorded Executing status and recovery settles it once, attributed to recovery.
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await host.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
        Assert.Single(await host.AuditAsync(session), x => x.Metadata.GetValueOrDefault(AgentProposalRecovery.SettledBy) == AgentProposalRecovery.Recovery);
        // A settled server operation no longer holds the session, and a repeated execute returns the retained uncertainty.
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);
        Assert.Equal("uncertain", (await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value?.Status);
        Assert.Equal((1, 1), (harness.Module.Dispatches, harness.Module.Effects));
    }
}

/// <summary>A forwarding record store whose next matching write or acquisition fails as an unavailable store would.</summary>
internal sealed class FaultingRecordStore(IAgentSessionRecordStore inner) : IAgentSessionRecordStore
{
    private Func<AgentSessionRecordWrite, bool>? _failWrite;
    private int _failAcquire;

    public bool SupportsFencing => inner.SupportsFencing;

    /// <summary>The next write matching <paramref name="condition"/> throws and records nothing.</summary>
    public void FailNextWrite(Func<AgentSessionRecordWrite, bool> condition) => Volatile.Write(ref _failWrite, condition);

    /// <summary>The next ownership acquisition throws.</summary>
    public void FailNextAcquire() => Volatile.Write(ref _failAcquire, 1);

    public Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _failWrite) is { } condition && condition(write) && Interlocked.CompareExchange(ref _failWrite, null, condition) == condition)
            throw new InvalidOperationException("The record store is unavailable for this write.");
        return inner.WriteAsync(write, cancellationToken);
    }

    public Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _failAcquire, 0) == 1) throw new InvalidOperationException("The record store is unavailable for this read.");
        return inner.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
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
