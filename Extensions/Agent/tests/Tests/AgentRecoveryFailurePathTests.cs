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

    [Fact]
    public async Task An_outcome_whose_write_committed_but_failed_returns_the_recorded_outcome_and_audits_it()
    {
        using var harness = new AgentRestartHarness(clock => new FaultingRecordStore(new InMemoryAgentSessionRecordStore(clock)));
        var store = (FaultingRecordStore)harness.Store;
        var host = harness.Host;
        var session = await host.CreateAsync();
        var proposal = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        store.CommitThenThrowNextWrite(write => write.Proposals.Any(x => x.Proposal.Status == AgentActionProposalStatus.Executed));

        var result = await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);

        Assert.True(result.Value?.Executed, result.Value?.Status);
        Assert.NotNull(result.Value!.ToolResult?.Receipt);
        Assert.Equal(AgentActionProposalStatus.Executed, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
        Assert.Single(await host.AuditAsync(session), x => x.Kind == AgentAuditEventKind.ProposalExecuted);
        Assert.DoesNotContain(await host.AuditAsync(session), x => x.Metadata.GetValueOrDefault(AgentProposalRecovery.SettledBy) == AgentProposalRecovery.Recovery);
        Assert.True((await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value?.Executed);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);
        Assert.Equal(1, harness.Module.Effects);
    }

    [Fact]
    public async Task An_unrecorded_outcome_that_cannot_be_settled_yet_is_settled_on_its_next_access()
    {
        using var harness = new AgentRestartHarness(clock => new FaultingRecordStore(new InMemoryAgentSessionRecordStore(clock)));
        var store = (FaultingRecordStore)harness.Store;
        var host = harness.Host;
        var session = await host.CreateAsync();
        var proposal = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        // Both the outcome write and the settlement attempted right after it fail.
        store.FailNextWrites(write => write.Proposals.Any(x => x.Proposal.Status is AgentActionProposalStatus.Executed or AgentActionProposalStatus.Uncertain), 2);

        Assert.Equal("uncertain", (await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value?.Status);
        Assert.Equal(AgentActionProposalStatus.Executing, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);

        Assert.Equal(AgentActionProposalStatus.Uncertain, (await host.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);
        Assert.Equal(1, harness.Module.Dispatches);
    }

    [Fact]
    public async Task A_committed_outcome_found_by_a_later_reconcile_is_audited_exactly_once()
    {
        using var harness = new AgentRestartHarness(clock => new FaultingRecordStore(new InMemoryAgentSessionRecordStore(clock)));
        var store = (FaultingRecordStore)harness.Store;
        var host = harness.Host;
        var session = await host.CreateAsync();
        var proposal = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        // The outcome write commits and then reports a failure, and the reload right after it fails too.
        store.CommitThenThrowNextWrite(write => write.Proposals.Any(x => x.Proposal.Status == AgentActionProposalStatus.Executed));
        store.FailNextLoad();
        Assert.Equal("uncertain", (await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value?.Status);

        // The next access finds the real outcome on the record and audits it, attributed to recovery.
        Assert.Equal(AgentActionProposalStatus.Executed, (await host.Proposals.FindAsync(proposal.Id))!.Status);
        await host.Proposals.FindAsync(proposal.Id);
        var executed = Assert.Single(await host.AuditAsync(session), x => x.Kind == AgentAuditEventKind.ProposalExecuted);
        Assert.Null(executed.ActorId);
        Assert.Equal(AgentProposalRecovery.Recovery, executed.Metadata.GetValueOrDefault(AgentProposalRecovery.SettledBy));
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);
        Assert.Equal(1, harness.Module.Effects);
    }

    [Fact]
    public async Task A_committed_outcome_whose_audit_failed_is_still_returned_and_its_audit_retried()
    {
        var sink = new FailOnceAuditSink();
        using var harness = new AgentRestartHarness(clock => new FaultingRecordStore(new InMemoryAgentSessionRecordStore(clock)),
            services => services.Replace(ServiceDescriptor.Singleton<IAgentAuditSink>(sp => sink.Bind(sp.GetRequiredService<InMemoryAgentAuditStore>()))));
        var store = (FaultingRecordStore)harness.Store;
        var host = harness.Host;
        var session = await host.CreateAsync();
        var proposal = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        store.CommitThenThrowNextWrite(write => write.Proposals.Any(x => x.Proposal.Status == AgentActionProposalStatus.Executed));
        sink.FailNext(AgentAuditEventKind.ProposalExecuted);

        Assert.True((await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value?.Executed);
        Assert.DoesNotContain(await host.AuditAsync(session), x => x.Kind == AgentAuditEventKind.ProposalExecuted);

        await host.Proposals.FindAsync(proposal.Id);
        await host.Proposals.FindAsync(proposal.Id);
        Assert.Single(await host.AuditAsync(session), x => x.Kind == AgentAuditEventKind.ProposalExecuted);
    }

    [Theory]
    [InlineData("foreign-actor", AgentSessionPolicyOutcome.NotFound)]
    [InlineData("foreign-tenant", AgentSessionPolicyOutcome.NotFound)]
    [InlineData("undefined-mode", AgentSessionPolicyOutcome.InvalidMode)]
    public async Task A_refused_autonomy_change_settles_nothing(string caller, AgentSessionPolicyOutcome refusal)
    {
        using var harness = new AgentRestartHarness(clock => new FaultingRecordStore(new InMemoryAgentSessionRecordStore(clock)));
        var store = (FaultingRecordStore)harness.Store;
        var host = harness.Host;
        var session = await host.CreateAsync();
        var proposal = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        store.FailNextWrites(write => write.Proposals.Any(x => x.Proposal.Status is AgentActionProposalStatus.Executed or AgentActionProposalStatus.Uncertain), 2);
        Assert.Equal("uncertain", (await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value?.Status);

        var (actor, tenant, mode) = caller switch
        {
            "foreign-actor" => ("actor-2", session.TenantId, AgentAutonomyMode.AutoReadOnly),
            "foreign-tenant" => (session.ActorId, "tenant-2", AgentAutonomyMode.AutoReadOnly),
            _ => (session.ActorId, session.TenantId, (AgentAutonomyMode)99)
        };
        Assert.Equal(refusal, (await host.Policy.ChangeAutonomyAsync(new(session.Id, actor, tenant, mode, 1))).Outcome);

        Assert.Equal(AgentActionProposalStatus.Executing, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
        Assert.True(host.ProposalStore.Unsettled.ContainsKey(proposal.Id));
    }

    [Fact]
    public async Task An_autonomy_change_retries_an_unsettled_outcome_before_it_answers_busy()
    {
        using var harness = new AgentRestartHarness(clock => new FaultingRecordStore(new InMemoryAgentSessionRecordStore(clock)));
        var store = (FaultingRecordStore)harness.Store;
        var host = harness.Host;
        var session = await host.CreateAsync();
        var proposal = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        store.FailNextWrites(write => write.Proposals.Any(x => x.Proposal.Status is AgentActionProposalStatus.Executed or AgentActionProposalStatus.Uncertain), 2);
        Assert.Equal("uncertain", (await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value?.Status);

        // Nobody reads the operation again; the store has recovered, so the policy change settles it and is not busy.
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
        Assert.Equal(1, harness.Module.Dispatches);
    }

    [Fact]
    public async Task An_unrecorded_outcome_settles_only_its_own_operation_and_leaves_other_live_work_running()
    {
        var client = new ProposalClientTool();
        using var harness = new AgentRestartHarness(clock => new FaultingRecordStore(new InMemoryAgentSessionRecordStore(clock)),
            services => services.AddSingleton<IAgentTool>(client));
        var store = (FaultingRecordStore)harness.Store;
        var host = harness.Host;
        var session = await host.CreateAsync();
        var first = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.ClientCall(session, client), session.Policy)).Proposal!;
        var second = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session, "call-2", "turn-2"), session.Policy)).Proposal!;
        Assert.True((await host.Proposals.ApproveAsync(first.Id, session.ActorId, first.BaseRevision)).Succeeded);
        Assert.True((await host.Proposals.ApproveAsync(second.Id, session.ActorId, second.BaseRevision)).Succeeded);
        // A foreground turn runs, and the second operation's executor is in flight, while the first one's outcome cannot be recorded.
        await host.PostAsync(session, "hello");
        var turn = harness.Provider.HoldNext();
        var running = Task.Run(() => host.StreamAsync(session));
        await turn.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var held = harness.Module.HoldNextDispatch();
        var inFlight = Task.Run(() => host.Proposals.ExecuteAsync(second.Id, session.ActorId, second.BaseRevision));
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        store.FailNextWrite(write => write.Proposals.Any(x => x.Proposal.Id == first.Id && x.Proposal.Status == AgentActionProposalStatus.AwaitingClient));

        Assert.Equal("uncertain", (await host.Proposals.ExecuteAsync(first.Id, session.ActorId, first.BaseRevision)).Value?.Status);

        // Only the first operation is settled from its record; the second records its real receipt and the turn completes.
        held.Release.SetResult();
        Assert.True((await inFlight.WaitAsync(TimeSpan.FromSeconds(30))).Value?.Executed);
        turn.Release.SetResult();
        Assert.Equal(AgentStreamEventKind.Completed, (await running.WaitAsync(TimeSpan.FromSeconds(30)))[^1].Kind);
        var recorded = (await harness.Store.LoadAsync(session.Id))!.Proposals.ToDictionary(x => x.Proposal.Id, x => x.Proposal.Status);
        Assert.Equal((AgentActionProposalStatus.Uncertain, AgentActionProposalStatus.Executed), (recorded[first.Id], recorded[second.Id]));
        var settled = Assert.Single(await host.AuditAsync(session), x => x.Metadata.GetValueOrDefault(AgentProposalRecovery.SettledBy) == AgentProposalRecovery.Recovery);
        Assert.Equal(first.Id, settled.Metadata["proposalId"]);
        Assert.Equal((1, 0), (harness.Module.Effects, client.ExecuteCalls));
    }

    [Fact]
    public async Task A_recovered_uncertain_client_operation_keeps_the_session_busy_but_not_its_lease()
    {
        var client = new ProposalClientTool();
        var authority = new ProposalTestAuthorizer();
        using var harness = new AgentRestartHarness(configure: services =>
        {
            services.AddSingleton<IAgentTool>(client);
            services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer>(_ => authority));
        });
        var (session, proposal, release) = await AgentRecoveryApiTests.UncertainClientOperationAsync(harness, client, authority);
        var b = harness.Host;
        // Process B recovered the operation Uncertain: it still holds the session busy, but nothing is in flight here.
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await b.ProbeAsync(session)).Outcome);
        using var c = harness.StartAnotherHost();

        harness.Clock.Advance(PastLease);
        await b.Owner.RenewLeasesAsync();

        // B is idle, so its lease lapsed: process C takes the session over and the owner resolves the operation there.
        var resolved = await c.Proposals.ResolveClientInvocationAsync(session.Id, proposal.PreparedInvocation!.InvocationId, session.ActorId,
            AgentOwnerResolutionClaim.NotApplied);
        Assert.True(resolved.Succeeded, resolved.Error?.Code);
        Assert.Equal(AgentOwnerResolutionClaim.NotApplied, resolved.Value!.OwnerResolution?.Claim);
        Assert.Equal((0, 0), (client.ExecuteCalls, client.CompleteCalls));
        await release();
    }

    [Fact]
    public async Task A_recovery_write_the_store_refuses_gives_back_the_lease_that_hydration_took()
    {
        using var harness = new AgentRestartHarness(clock => new FaultingRecordStore(new InMemoryAgentSessionRecordStore(clock)));
        var store = (FaultingRecordStore)harness.Store;
        var session = await harness.Host.CreateAsync();
        var proposal = (await harness.Host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        harness.Clock.Advance(PastDeadline);
        var b = harness.Restart();
        store.FailNextWrite(write => write.Proposals.Any(x => x.Proposal.Status == AgentActionProposalStatus.Expired));

        await Assert.ThrowsAsync<InvalidOperationException>(() => b.Sessions.FindAsync(session.Id));

        // Nothing was settled, and the lease is free at once: another process takes the session over and settles it.
        Assert.Equal(AgentActionProposalStatus.AwaitingApproval, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
        using var c = harness.StartAnotherHost();
        Assert.NotNull(await c.Sessions.FindAsync(session.Id));
        Assert.Equal(AgentActionProposalStatus.Expired, (await c.Proposals.FindAsync(proposal.Id))!.Status);
    }

    [Fact]
    public async Task A_proposal_inserted_after_its_row_was_reloaded_keeps_a_claim_on_the_reloaded_row()
    {
        using var harness = new AgentRestartHarness(clock => new FaultingRecordStore(new InMemoryAgentSessionRecordStore(clock)));
        var store = (FaultingRecordStore)harness.Store;
        var session = await harness.Host.CreateAsync();
        var first = (await harness.Host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await harness.Host.Proposals.DenyAsync(first.Id, session.ActorId, "not this one")).Succeeded);
        var second = first with
        {
            Id = "proposal-2", Status = AgentActionProposalStatus.AwaitingApproval,
            PreparedInvocation = first.PreparedInvocation! with { InvocationId = "invocation-2", ToolCallId = "call-2" }
        };
        var b = harness.Restart();
        // The provisional claim is acquired on the row its hydration loads; before the insertion's own commit, another
        // operation of the session evicts that row, so the insertion commits on a newly hydrated row.
        var held = store.HoldNextAcquire();
        var insert = Task.Run(() => b.Proposals.AddAsync(second));
        await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        store.RefuseNextWrite(write => write.Context.Count > 0, AgentSessionRecordOutcome.OwnershipLost);
        var evicting = b.Sessions.AddContextAsync(session.Id,
            [new("attachment-1", "test", null, "Label", "test.kind", AgentContextSensitivity.Internal, "scope", "Summary", null, new Dictionary<string, string>())]);
        held.Release.SetResult();
        await Assert.ThrowsAsync<AgentSessionOwnershipException>(() => evicting);
        await insert.WaitAsync(TimeSpan.FromSeconds(30));

        // The recorded, undecided proposal holds the session busy until its deadline, exactly as a hydrated one would.
        Assert.Equal(AgentActionProposalStatus.AwaitingApproval, (await b.Proposals.FindAsync(second.Id))!.Status);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await b.ProbeAsync(session)).Outcome);
    }
}

/// <summary>A forwarding record store whose next matching write or acquisition fails as an unavailable store would.</summary>
internal sealed class FaultingRecordStore(IAgentSessionRecordStore inner) : IAgentSessionRecordStore
{
    private Func<AgentSessionRecordWrite, bool>? _failWrite;
    private int _failAcquire;

    public bool SupportsFencing => inner.SupportsFencing;

    /// <summary>The next write matching <paramref name="condition"/> throws and records nothing.</summary>
    public void FailNextWrite(Func<AgentSessionRecordWrite, bool> condition) => FailNextWrites(condition, 1);

    private int _failures;
    private Func<AgentSessionRecordWrite, bool>? _commitThenThrow;

    /// <summary>The next write matching <paramref name="condition"/> is recorded, and then reported as failed (a timeout after commit).</summary>
    public void CommitThenThrowNextWrite(Func<AgentSessionRecordWrite, bool> condition) => Volatile.Write(ref _commitThenThrow, condition);

    /// <summary>The next <paramref name="count"/> writes matching <paramref name="condition"/> throw and record nothing.</summary>
    public void FailNextWrites(Func<AgentSessionRecordWrite, bool> condition, int count)
    {
        Volatile.Write(ref _failures, count);
        Volatile.Write(ref _failWrite, condition);
    }

    /// <summary>The next ownership acquisition throws.</summary>
    public void FailNextAcquire() => Volatile.Write(ref _failAcquire, 1);

    private int _failLoad;

    /// <summary>The next load throws, as an unavailable store would.</summary>
    public void FailNextLoad() => Volatile.Write(ref _failLoad, 1);

    private (Func<AgentSessionRecordWrite, bool> Condition, AgentSessionRecordOutcome Outcome)? _refuseWrite;
    private (TaskCompletionSource Entered, TaskCompletionSource Release)? _holdAcquire;

    /// <summary>The next write matching <paramref name="condition"/> answers <paramref name="outcome"/> and records nothing.</summary>
    public void RefuseNextWrite(Func<AgentSessionRecordWrite, bool> condition, AgentSessionRecordOutcome outcome)
    {
        lock (this) _refuseWrite = (condition, outcome);
    }

    /// <summary>Holds the next ownership acquisition at a barrier until released.</summary>
    public (TaskCompletionSource Entered, TaskCompletionSource Release) HoldNextAcquire()
    {
        var hold = (new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        lock (this) _holdAcquire = hold;
        return hold;
    }

    public async Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _commitThenThrow) is { } committing && committing(write) && Interlocked.CompareExchange(ref _commitThenThrow, null, committing) == committing)
        {
            await inner.WriteAsync(write, cancellationToken);
            throw new TimeoutException("The record store timed out after committing this write.");
        }
        lock (this)
            if (_refuseWrite is { } refuse && refuse.Condition(write))
            {
                _refuseWrite = null;
                return new AgentSessionRecordResult(refuse.Outcome);
            }
        if (Volatile.Read(ref _failWrite) is { } condition && condition(write) && Interlocked.Decrement(ref _failures) >= 0)
        {
            if (Volatile.Read(ref _failures) <= 0) Interlocked.CompareExchange(ref _failWrite, null, condition);
            throw new InvalidOperationException("The record store is unavailable for this write.");
        }
        return await inner.WriteAsync(write, cancellationToken);
    }

    public async Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _failAcquire, 0) == 1) throw new InvalidOperationException("The record store is unavailable for this read.");
        (TaskCompletionSource Entered, TaskCompletionSource Release)? hold;
        lock (this)
        {
            hold = _holdAcquire;
            _holdAcquire = null;
        }
        if (hold is { } held)
        {
            held.Entered.TrySetResult();
            await held.Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        return await inner.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
    }

    public Task<AgentSessionRecordResult> CreateAsync(AgentSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
        => inner.CreateAsync(record, ownerToken, cancellationToken);
    public Task<AgentSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default)
        => Interlocked.Exchange(ref _failLoad, 0) == 1 ? throw new InvalidOperationException("The record store is unavailable for this read.")
            : inner.LoadAsync(sessionId, cancellationToken);
    public Task ReleaseOwnershipAsync(string sessionId, AgentSessionFence fence, CancellationToken cancellationToken = default)
        => inner.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);
    public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
        => inner.FindProposalSessionAsync(proposalId, cancellationToken);
    public Task<AgentSessionSummaryPage> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
        => inner.ListOwnedAsync(query, cancellationToken);
    public Task<AgentSessionOwner?> FindOwnerAsync(string sessionId, CancellationToken cancellationToken = default) => inner.FindOwnerAsync(sessionId, cancellationToken);
}

/// <summary>Forwards to the host's audit store; the next record of one kind can be made to fail before it is recorded.</summary>
internal sealed class FailOnceAuditSink : IAgentAuditSink
{
    private IAgentAuditSink _inner = null!;
    private AgentAuditEventKind? _fail;

    public FailOnceAuditSink Bind(IAgentAuditSink inner)
    {
        _inner = inner;
        return this;
    }

    public void FailNext(AgentAuditEventKind kind)
    {
        lock (this) _fail = kind;
    }

    public Task EmitAsync(AgentAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        lock (this)
            if (_fail == auditEvent.Kind)
            {
                _fail = null;
                throw new InvalidOperationException("The audit sink is unavailable.");
            }
        return _inner.EmitAsync(auditEvent, cancellationToken);
    }
}
