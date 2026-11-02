using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 T008: the session write gate orders claims after an in-flight policy write, hydration is single-flight, and
/// the lock order is proposal gate before session gate, never the reverse. Barriers only; no sleeps.
/// </summary>
public sealed class AgentSessionGateOrderingTests
{
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_claim_acquired_during_an_in_flight_policy_write_waits_and_reads_the_new_revision()
    {
        var store = new HeldRecordStore(new InMemoryAgentSessionRecordStore());
        using var root = AgentTestServices.Create(services => services.Replace(ServiceDescriptor.Singleton<IAgentSessionRecordStore>(store)));
        using var scope = root.CreateScope();
        var owner = (InMemoryAgentSessionService)scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var policy = scope.ServiceProvider.GetRequiredService<IAgentSessionPolicyService>();
        var session = await AgentSessionOwnerWriteThroughTests.CreateAsync(owner);
        var held = store.HoldNextWrite();

        var change = policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, AgentAutonomyMode.FullAuto, 1));
        await held.Entered.Task.WaitAsync(HandoffTimeout);
        // The idle check passed and the revision write is in flight; the claim reaches the gate and waits.
        var claim = owner.AcquireAsync(session.Id, AgentSessionClaimKind.Foreground);
        Assert.False(claim.IsCompleted);
        Assert.Equal(1, (await owner.GetPolicyAsync(session.Id, session.ActorId, session.TenantId)).State!.Revision);
        held.Release.SetResult();

        var changed = await change.WaitAsync(HandoffTimeout);
        var admitted = (await claim.WaitAsync(HandoffTimeout))!;
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, changed.Outcome);
        Assert.Equal(2, changed.State!.Revision);
        Assert.Equal(2, admitted.State.Revision);
        Assert.Equal(AgentAutonomyMode.FullAuto, admitted.State.Policy.AutonomyMode);
        // The admitted work now owns the new policy.
        Assert.Equal(AgentSessionPolicyOutcome.Busy,
            (await policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, AgentAutonomyMode.Manual, 2))).Outcome);
    }

    [Fact]
    public async Task Concurrent_first_hydrations_settle_once_on_one_row()
    {
        var store = new HeldRecordStore(new InMemoryAgentSessionRecordStore());
        var first = new InMemoryAgentSessionService(new InMemoryAgentAuditStore(), recordStore: store);
        var session = await AgentSessionOwnerWriteThroughTests.CreateAsync(first);
        await store.ReleaseOwnershipAsync(session.Id, (await store.LoadAsync(session.Id))!.Owner!);
        var restarted = new InMemoryAgentSessionService(new InMemoryAgentAuditStore(), recordStore: store);
        var loads = store.Loads;
        var held = store.HoldNextLoad();

        var hydrating = restarted.FindAsync(session.Id);
        await held.Entered.Task.WaitAsync(HandoffTimeout);
        var waiting = restarted.FindAsync(session.Id);
        var policyRead = restarted.GetPolicyAsync(session.Id, session.ActorId, session.TenantId);
        Assert.False(waiting.IsCompleted);
        Assert.False(policyRead.IsCompleted);
        held.Release.SetResult();

        var hydrated = await hydrating.WaitAsync(HandoffTimeout);
        Assert.Same(hydrated, await waiting.WaitAsync(HandoffTimeout));
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await policyRead.WaitAsync(HandoffTimeout)).Outcome);
        Assert.Equal(loads + 1, store.Loads);
    }

    [Fact]
    public async Task A_cancelled_first_hydration_keeps_one_gate_so_later_callers_still_serialize()
    {
        var store = new HeldRecordStore(new InMemoryAgentSessionRecordStore());
        var first = new InMemoryAgentSessionService(new InMemoryAgentAuditStore(), recordStore: store);
        var session = await AgentSessionOwnerWriteThroughTests.CreateAsync(first);
        await store.ReleaseOwnershipAsync(session.Id, (await store.LoadAsync(session.Id))!.Owner!);
        var restarted = new InMemoryAgentSessionService(new InMemoryAgentAuditStore(), recordStore: store);
        using var cancellation = new CancellationTokenSource();
        var heldFirst = store.HoldNextLoad();

        var cancelled = restarted.FindAsync(session.Id, cancellation.Token);
        await heldFirst.Entered.Task.WaitAsync(HandoffTimeout);
        var queued = restarted.FindAsync(session.Id);
        Assert.False(queued.IsCompleted);
        var heldSecond = store.HoldNextLoad();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(HandoffTimeout));
        await heldSecond.Entered.Task.WaitAsync(HandoffTimeout);
        var loads = store.Loads;

        // The queued caller now hydrates under the gate; a third caller waits behind it instead of hydrating beside it.
        var third = restarted.FindAsync(session.Id);
        Assert.False(third.IsCompleted);
        Assert.Equal(loads, store.Loads);
        heldSecond.Release.SetResult();

        var hydrated = await queued.WaitAsync(HandoffTimeout);
        Assert.Same(hydrated, await third.WaitAsync(HandoffTimeout));
        Assert.Equal(loads, store.Loads);
    }

    [Fact]
    public async Task A_held_proposal_gate_never_blocks_session_gated_owner_operations()
    {
        var sink = new HookedAuditSink();
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), null, services =>
            services.Replace(ServiceDescriptor.Singleton<IAgentAuditSink>(sp => sink.Bind(sp.GetRequiredService<InMemoryAgentAuditStore>()))));
        var owner = (InMemoryAgentSessionService)fixture.Services.GetRequiredService<IAgentSessionService>();
        var policy = fixture.Services.GetRequiredService<IAgentSessionPolicyService>();
        var proposal = await fixture.ProposeAsync();
        var held = sink.HoldNext(AgentAuditEventKind.ProposalApproved);

        // The approval holds its proposal gate after its recorded write, inside the gate.
        var approval = Task.Run(() => fixture.ApproveAsync(proposal));
        await held.Entered.Task.WaitAsync(HandoffTimeout);

        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await policy.ChangeAutonomyAsync(new(fixture.Session.Id, fixture.Session.ActorId,
            fixture.Session.TenantId, AgentAutonomyMode.Manual, 1)).WaitAsync(HandoffTimeout)).Outcome);
        owner.Release((await owner.AcquireAsync(fixture.Session.Id, AgentSessionClaimKind.Foreground).WaitAsync(HandoffTimeout))!);
        owner.Release((await owner.EnsurePauseAsync(fixture.Session.Id).WaitAsync(HandoffTimeout))!);
        // Completes under the session gate; an unclaimed proposal is refused (#3441 Phase 3 review).
        Assert.False(await owner.MarkProposalDispatchedAsync(fixture.Session.Id, "another-proposal").WaitAsync(HandoffTimeout));
        Assert.False(approval.IsCompleted);
        held.Release.SetResult();

        Assert.True((await approval.WaitAsync(HandoffTimeout)).Succeeded);
    }

    [Fact]
    public async Task A_proposal_transition_holding_its_gate_waits_for_the_session_gate_and_the_session_holder_never_needs_it()
    {
        var store = new HeldRecordStore(new InMemoryAgentSessionRecordStore());
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), null, services =>
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionRecordStore>(store)));
        var owner = (InMemoryAgentSessionService)fixture.Services.GetRequiredService<IAgentSessionService>();
        var proposal = await fixture.ProposeAsync();
        var held = store.HoldNextWrite();

        // A session write holds the session gate across its awaited store write.
        var context = owner.AddContextAsync(fixture.Session.Id, [new("attachment-1", "test", null, "Label", "test.kind", AgentContextSensitivity.Internal,
            "scope", "Summary", null, new Dictionary<string, string>())]);
        await held.Entered.Task.WaitAsync(HandoffTimeout);
        // The approval takes its proposal gate, then waits for the session gate; nothing is applied meanwhile.
        var approval = fixture.ApproveAsync(proposal);
        var repeated = fixture.ApproveAsync(proposal);
        Assert.False(approval.IsCompleted);
        Assert.False(repeated.IsCompleted);
        Assert.Equal(AgentActionProposalStatus.AwaitingApproval, (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
        held.Release.SetResult();

        await context.WaitAsync(HandoffTimeout);
        Assert.Equal(AgentActionProposalStatus.Approved, (await approval.WaitAsync(HandoffTimeout)).Value!.Status);
        Assert.Equal(AgentActionProposalStatus.Approved, (await repeated.WaitAsync(HandoffTimeout)).Value!.Status);
        Assert.Equal(AgentActionProposalStatus.Approved, (await store.LoadAsync(fixture.Session.Id))!.Proposals.Single().Proposal.Status);
    }

    /// <summary>A forwarding record store whose next write or load can be held at a barrier.</summary>
    private sealed class HeldRecordStore(IAgentSessionRecordStore inner) : IAgentSessionRecordStore
    {
        private Hold? _nextWrite;
        private Hold? _nextLoad;
        private int _loads;

        public int Loads => Volatile.Read(ref _loads);
        public bool SupportsFencing => inner.SupportsFencing;

        public Hold HoldNextWrite()
        {
            var hold = new Hold();
            Volatile.Write(ref _nextWrite, hold);
            return hold;
        }

        public Hold HoldNextLoad()
        {
            var hold = new Hold();
            Volatile.Write(ref _nextLoad, hold);
            return hold;
        }

        public Task<AgentSessionRecordResult> CreateAsync(AgentSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => inner.CreateAsync(record, ownerToken, cancellationToken);

        public async Task<AgentSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _loads);
            if (Interlocked.Exchange(ref _nextLoad, null) is { } held)
            {
                held.Entered.TrySetResult();
                await held.Release.Task.WaitAsync(HandoffTimeout, cancellationToken);
            }
            return await inner.LoadAsync(sessionId, cancellationToken);
        }

        public Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
            => inner.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);

        public Task ReleaseOwnershipAsync(string sessionId, AgentSessionFence fence, CancellationToken cancellationToken = default)
            => inner.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);

        public async Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _nextWrite, null) is { } held)
            {
                held.Entered.TrySetResult();
                await held.Release.Task.WaitAsync(HandoffTimeout, cancellationToken);
            }
            return await inner.WriteAsync(write, cancellationToken);
        }

        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => inner.FindProposalSessionAsync(proposalId, cancellationToken);

        public Task<AgentSessionSummaryPage> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => inner.ListOwnedAsync(query, cancellationToken);

    }

    /// <summary>One barrier: the held operation signals <see cref="Entered"/> and waits for <see cref="Release"/>.</summary>
    private sealed class Hold
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
