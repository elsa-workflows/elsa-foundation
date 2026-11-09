using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit.Sdk;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 story 2 (T009-T011): restarts at each lifecycle boundary, before and after each deadline, on both provider loops
/// (the host-driven loop and a delegated, harness-owned loop as the Copilot adapter runs). The owning module's dispatch
/// count is the authority on how often an effect ran.
/// </summary>
public sealed class AgentRecoveryRestartTests
{
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PastPreparedDeadline = TimeSpan.FromMinutes(6);

    // ---- T009 causal red control: on the pre-T010 code the review was "agent.proposal.not_found" after a restart ----

    [Fact]
    public async Task A_pending_review_survives_a_restart_and_stays_decidable_before_its_deadline()
    {
        using var harness = new AgentRestartHarness();
        var session = await harness.Host.CreateAsync();
        var proposal = await harness.Host.PauseOnReviewAsync(session);

        var host = harness.Restart();

        var approved = await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        Assert.True(approved.Succeeded, approved.Error?.Code);
        Assert.True((await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value!.Executed);
        Assert.Equal(1, harness.Module.Dispatches);
    }

    // ---- T009 negative control: a deliberately skipped awaited write makes a restart scenario fail ----

    [Fact]
    public async Task A_skipped_approval_write_makes_the_approved_restart_scenario_fail()
    {
        DroppingRecordStore? dropping = null;
        using var harness = new AgentRestartHarness(clock => dropping = new(new InMemoryAgentSessionRecordStore(clock)) { Drop = AgentActionProposalStatus.Approved });

        await Assert.ThrowsAnyAsync<XunitException>(() => ApprovedBeforeDispatchAsync(harness, afterDeadline: false));
        Assert.Equal(1, dropping!.Dropped);
    }

    // ---- T009 SC-001 matrix: six boundaries, before and after each deadline ----

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Idle_session(bool afterDeadline, bool delegated)
    {
        using var harness = new AgentRestartHarness(delegated: delegated);
        var session = await harness.Host.CreateAsync();
        await harness.Host.PostAsync(session, "hello");
        Assert.Equal(AgentStreamEventKind.Completed, (await harness.Host.StreamAsync(session))[^1].Kind);
        var recorded = await harness.Host.Sessions.ListMessagesAsync(session.Id);
        if (afterDeadline) harness.Clock.Advance(TimeSpan.FromHours(9));

        var host = harness.Restart();

        var found = (await host.Sessions.FindAsync(session.Id))!;
        if (afterDeadline)
        {
            // The session's own deadline passed while the host was down: it is closed at hydration.
            Assert.Equal(AgentSessionStatus.Expired, found.Status);
            Assert.Equal(AgentSessionPolicyOutcome.SessionClosed, (await host.ProbeAsync(session)).Outcome);
            Assert.Equal("agent.session.unavailable", Assert.Single(await host.StreamAsync(session)).Error?.Code);
        }
        else
        {
            Assert.Equal(AgentSessionStatus.Active, found.Status);
            Assert.Equal(recorded.Select(AgentInvocationIntegrity.Digest), (await host.Sessions.ListMessagesAsync(session.Id)).Select(AgentInvocationIntegrity.Digest));
            var policy = await host.Policy.GetAsync(session.Id, session.ActorId, session.TenantId);
            Assert.Equal((AgentAutonomyMode.AutoReadOnly, 1L), (policy.State!.Policy.AutonomyMode, policy.State.Revision));
            // The recorded history re-verifies and the session continues.
            await host.PostAsync(session, "hello again");
            Assert.Equal(AgentStreamEventKind.Completed, (await host.StreamAsync(session))[^1].Kind);
            Assert.Contains(harness.Provider.Contexts[^1].History, x => x.Role == AgentRole.User && x.Content == "hello");
        }
        Assert.Equal(0, harness.Module.Dispatches);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Pending_review(bool afterDeadline, bool delegated)
    {
        using var harness = new AgentRestartHarness(delegated: delegated);
        var session = await harness.Host.CreateAsync();
        var proposal = await harness.Host.PauseOnReviewAsync(session);
        if (afterDeadline) harness.Clock.Advance(PastPreparedDeadline);

        var host = harness.Restart();

        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await host.ProbeAsync(session)).Outcome);
        if (afterDeadline)
        {
            Assert.Equal(AgentActionProposalStatus.Expired, (await host.Proposals.FindAsync(proposal.Id))!.Status);
            AssertSettledOnce(await host.AuditAsync(session), proposal, nameof(AgentActionProposalStatus.Expired));
            Assert.Equal("agent.proposal.expired", (await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Error?.Code);
            Assert.Equal("agent.proposal.expired", (await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Error?.Code);
            Assert.Equal(AgentStreamEventKind.Completed, (await host.StreamAsync(session))[^1].Kind);
            // A later restart finds the recorded Expired; nothing is settled twice.
            var again = harness.Restart();
            Assert.Equal(AgentActionProposalStatus.Expired, (await again.Proposals.FindAsync(proposal.Id))!.Status);
            Assert.Empty(await again.AuditAsync(session));
            Assert.Equal(0, harness.Module.Dispatches);
        }
        else
        {
            Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
            Assert.True((await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value!.Executed);
            Assert.Equal(AgentStreamEventKind.Completed, (await host.StreamAsync(session))[^1].Kind);
            Assert.Equal("agent.message.no_pending", Assert.Single(await host.StreamAsync(session), x => x.Kind == AgentStreamEventKind.Error).Error?.Code);
            Assert.Equal(1, harness.Module.Dispatches);
            Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Approved_before_dispatch(bool afterDeadline, bool delegated)
    {
        using var harness = new AgentRestartHarness(delegated: delegated);
        await ApprovedBeforeDispatchAsync(harness, afterDeadline);
    }

    private static async Task ApprovedBeforeDispatchAsync(AgentRestartHarness harness, bool afterDeadline)
    {
        var session = await harness.Host.CreateAsync();
        var proposal = await harness.Host.PauseOnReviewAsync(session);
        Assert.True((await harness.Host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        if (afterDeadline) harness.Clock.Advance(PastPreparedDeadline);

        var host = harness.Restart();

        // Recovery never executes on its own initiative.
        var recovered = (await host.Proposals.FindAsync(proposal.Id))!;
        Assert.Equal(0, harness.Module.Dispatches);
        if (afterDeadline)
        {
            Assert.Equal(AgentActionProposalStatus.Expired, recovered.Status);
            AssertSettledOnce(await host.AuditAsync(session), proposal, nameof(AgentActionProposalStatus.Expired));
            Assert.Equal("agent.proposal.expired", (await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Error?.Code);
            Assert.Equal(0, harness.Module.Dispatches);
        }
        else
        {
            Assert.Equal(AgentActionProposalStatus.Approved, recovered.Status);
            Assert.Equal(AgentSessionPolicyOutcome.Busy, (await host.ProbeAsync(session)).Outcome);
            var executed = await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
            Assert.True(executed.Value?.Executed, executed.Error?.Code);
            Assert.True((await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value!.Executed);
            Assert.Equal(1, harness.Module.Dispatches);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Dispatched_before_the_receipt(bool afterDeadline, bool delegated)
    {
        using var harness = new AgentRestartHarness(delegated: delegated);
        var session = await harness.Host.CreateAsync();
        var proposal = await harness.Host.PauseOnReviewAsync(session);
        var old = harness.Host;
        Assert.True((await old.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        var held = harness.Module.HoldNextDispatch();
        var interrupted = Task.Run(() => old.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision));
        await held.Started.Task.WaitAsync(HandoffTimeout);
        if (afterDeadline) harness.Clock.Advance(PastPreparedDeadline);

        var host = harness.Restart();

        Assert.Equal(AgentActionProposalStatus.Uncertain, (await host.Proposals.FindAsync(proposal.Id))!.Status);
        var settled = AssertSettledOnce(await host.AuditAsync(session), proposal, nameof(AgentActionProposalStatus.Uncertain));
        Assert.Equal(("server", "possible"), (settled.Metadata["dispatch"], settled.Metadata["effect"]));
        var retried = await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        Assert.Equal("uncertain", retried.Value?.Status);
        // The paused turn continues once and reports the uncertain outcome, never a replay.
        Assert.Equal(AgentStreamEventKind.Completed, (await host.StreamAsync(session))[^1].Kind);
        Assert.Equal("uncertain", (await host.Proposals.FindResultAsync(proposal.Id))!.Payload!.Data.GetProperty("status").GetString());

        // The crashed host's effect finishes later; its outcome write is refused under the superseded fence.
        held.Release.SetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => interrupted.WaitAsync(HandoffTimeout));
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
        Assert.Equal(1, harness.Module.Dispatches);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Receipt_recorded_before_the_provider_continuation(bool afterDeadline, bool delegated)
    {
        using var harness = new AgentRestartHarness(delegated: delegated);
        var session = await harness.Host.CreateAsync();
        var proposal = await harness.Host.PauseOnReviewAsync(session);
        Assert.True((await harness.Host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        var receipt = (await harness.Host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value!.ToolResult!.Receipt!;
        if (afterDeadline) harness.Clock.Advance(PastPreparedDeadline);

        var host = harness.Restart();

        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await host.ProbeAsync(session)).Outcome);
        var turns = harness.Provider.Turns;
        Assert.Equal(AgentStreamEventKind.Completed, (await host.StreamAsync(session))[^1].Kind);
        Assert.Equal(turns + 1, harness.Provider.Turns);
        Assert.Equal(receipt.InvocationId, Assert.Single(harness.Provider.Contexts[^1].PendingToolResults).Receipt!.InvocationId);
        Assert.Equal("agent.message.no_pending", Assert.Single(await host.StreamAsync(session), x => x.Kind == AgentStreamEventKind.Error).Error?.Code);
        Assert.Equal(1, harness.Module.Dispatches);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Mid_provider_turn(bool afterDeadline, bool delegated)
    {
        using var harness = new AgentRestartHarness(delegated: delegated);
        var session = await harness.Host.CreateAsync();
        var old = harness.Host;
        var prompt = await old.PostAsync(session, "hello");
        var held = harness.Provider.HoldNext();
        var running = Task.Run(() => old.StreamAsync(session));
        await held.Entered.Task.WaitAsync(HandoffTimeout);
        if (afterDeadline) harness.Clock.Advance(PastPreparedDeadline);

        var host = harness.Restart();

        var interrupted = (await host.Sessions.FindMessageAsync(session.Id, prompt.Id))!;
        Assert.Equal((AgentMessageStatus.Cancelled, AgentMessageStatusReasons.Interrupted), (interrupted.Status, interrupted.StatusReason));
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);
        await host.PostAsync(session, "hello again");
        Assert.Equal(AgentStreamEventKind.Completed, (await host.StreamAsync(session))[^1].Kind);

        // The crashed host's turn resumes later; nothing it records is accepted under the superseded fence.
        held.Release.SetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => running.WaitAsync(HandoffTimeout));
        Assert.Equal(AgentMessageStatus.Cancelled, (await harness.Store.LoadAsync(session.Id))!.Messages.Single(x => x.Message.Id == prompt.Id).Message.Status);
        Assert.Equal(0, harness.Module.Dispatches);
    }

    // ---- T010 claim rebuild rows ----

    [Fact]
    public async Task A_pending_prompt_keeps_its_prompt_claim_across_a_restart_and_starts_only_on_request()
    {
        using var harness = new AgentRestartHarness();
        var session = await harness.Host.CreateAsync();
        var prompt = await harness.Host.PostAsync(session, "hello");

        var host = harness.Restart();

        Assert.Equal(AgentMessageStatus.Pending, (await host.Sessions.FindMessageAsync(session.Id, prompt.Id))!.Status);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await host.ProbeAsync(session)).Outcome);
        Assert.Equal(0, harness.Provider.Turns);
        Assert.Equal(AgentStreamEventKind.Completed, (await host.StreamAsync(session))[^1].Kind);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);
    }

    [Fact]
    public async Task A_paused_turn_rebuilds_its_pause_claim()
    {
        using var harness = new AgentRestartHarness();
        var session = await harness.Host.CreateAsync();
        var proposal = await harness.Host.PauseOnReviewAsync(session);
        Assert.True((await harness.Host.Proposals.DenyAsync(proposal.Id, session.ActorId, "No")).Succeeded);

        var host = harness.Restart();

        // The denied proposal settled; only the paused turn still owns the policy, until its continuation runs.
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await host.ProbeAsync(session)).Outcome);
        Assert.NotNull(host.Owner.FindPause(session.Id));
        Assert.Equal(AgentStreamEventKind.Completed, (await host.StreamAsync(session))[^1].Kind);
        Assert.Null(host.Owner.FindPause(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);
    }

    [Fact]
    public async Task An_unsettled_proposal_rebuilds_its_claim_with_the_prepared_deadline()
    {
        using var harness = new AgentRestartHarness();
        var session = await harness.Host.CreateAsync();
        var proposal = (await harness.Host.Invoker.DispatchAsync(Call(session), session.Policy)).Proposal!;

        var host = harness.Restart();

        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await host.ProbeAsync(session)).Outcome);
        // A pre-dispatch claim past its prepared deadline no longer owns the policy, exactly as before the restart.
        harness.Clock.Advance(PastPreparedDeadline);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);
        Assert.Equal("agent.proposal.expired", (await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Error?.Code);
    }

    // ---- T011 client settlement rows ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_awaiting_client_request_stays_pending_before_its_deadline_and_becomes_uncertain_after_it(bool afterDeadline)
    {
        var client = new ProposalClientTool();
        using var harness = new AgentRestartHarness(configure: services => services.AddSingleton<IAgentTool>(client));
        var session = await harness.Host.CreateAsync();
        var proposal = (await harness.Host.Invoker.DispatchAsync(ClientCall(session, client), session.Policy)).Proposal!;
        Assert.True((await harness.Host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.Equal("awaiting-client", (await harness.Host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value?.Status);
        if (afterDeadline) harness.Clock.Advance(PastPreparedDeadline);

        var host = harness.Restart();

        var recovered = (await host.Proposals.FindAsync(proposal.Id))!;
        // A dispatched client request keeps the session busy either way.
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await host.ProbeAsync(session)).Outcome);
        if (afterDeadline)
        {
            Assert.Equal(AgentActionProposalStatus.Uncertain, recovered.Status);
            var settled = AssertSettledOnce(await host.AuditAsync(session), proposal, nameof(AgentActionProposalStatus.Uncertain));
            Assert.Equal("pending-client", settled.Metadata["deadline"]);
            Assert.DoesNotContain(await host.AuditAsync(session), x => x.Metadata.GetValueOrDefault("status") == nameof(AgentActionProposalStatus.Expired));
        }
        else
        {
            Assert.Equal(AgentActionProposalStatus.AwaitingClient, recovered.Status);
            Assert.True((await host.Proposals.AcknowledgeAsync(session.Id, proposal.PreparedInvocation!.InvocationId, session.ActorId, Acknowledgement(proposal))).Value!.Executed);
            Assert.Equal(1, client.CompleteCalls);
        }
        Assert.Equal(0, client.ExecuteCalls);
    }

    [Fact]
    public async Task A_late_truthful_receipt_after_a_restart_uncertainty_reconciles_once_and_a_conflicting_one_is_refused()
    {
        var client = new ProposalClientTool();
        var authority = new ProposalTestAuthorizer();
        using var harness = new AgentRestartHarness(configure: services =>
        {
            services.AddSingleton<IAgentTool>(client);
            services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer>(_ => authority));
        });
        var session = await harness.Host.CreateAsync();
        var old = harness.Host;
        var proposal = (await old.Invoker.DispatchAsync(ClientCall(session, client), session.Policy)).Proposal!;
        Assert.True((await old.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        // Executing is recorded; the process stops inside the executor's authority recheck, before the envelope.
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        authority.HoldNextDecision(async _ => { entered.TrySetResult(); await release.Task.WaitAsync(HandoffTimeout); });
        var interrupted = Task.Run(() => old.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision));
        await entered.Task.WaitAsync(HandoffTimeout);

        var host = harness.Restart();

        Assert.Equal(AgentActionProposalStatus.Uncertain, (await host.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await host.ProbeAsync(session)).Outcome);
        var invocationId = proposal.PreparedInvocation!.InvocationId;
        var reconciled = await host.Proposals.AcknowledgeAsync(session.Id, invocationId, session.ActorId, Acknowledgement(proposal));
        Assert.True(reconciled.Value!.Executed);
        Assert.Equal(AgentActionProposalStatus.Executed, (await host.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.True((await host.Proposals.AcknowledgeAsync(session.Id, invocationId, session.ActorId, Acknowledgement(proposal))).Value!.Executed);
        var conflicting = Acknowledgement(proposal) with { Summary = "A different observation." };
        Assert.Equal("agent.invocation.acknowledgement_conflict",
            (await host.Proposals.AcknowledgeAsync(session.Id, invocationId, session.ActorId, conflicting)).Error?.Code);
        Assert.Equal(1, client.CompleteCalls);
        Assert.Equal(0, client.ExecuteCalls);

        release.SetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => interrupted.WaitAsync(HandoffTimeout));
        Assert.Equal(AgentActionProposalStatus.Executed, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
    }

    internal static AgentToolInvocation Call(AgentSession session, string callId = "call-1", string turnId = "turn-1")
        => new(callId, DeterministicAgentProvider.MutatingToolName, session.Id, session.ActorId, new Dictionary<string, object?> { ["change"] = "add-activity" }, [])
        { TenantId = session.TenantId, TurnId = turnId };

    internal static AgentToolInvocation ClientCall(AgentSession session, ProposalClientTool tool)
    {
        var binding = new AgentClientBinding("client-1", "generation-1");
        AgentContextAttachment snapshot = new("snapshot-1", "test", "editor-1", "Editor", "test.editor", AgentContextSensitivity.Internal, "selection", "Bound editor", null,
            new Dictionary<string, string>())
        { ModuleId = "test.module", Origin = AgentContextOrigin.ClientSnapshot, Client = binding, Target = new("test.module", "test.editor", "editor-1", "revision-1") };
        return new("call-1", tool.Descriptor.Name, session.Id, session.ActorId, new Dictionary<string, object?> { ["value"] = "new" }, [snapshot])
        { TenantId = session.TenantId, TurnId = "turn-1", Client = binding };
    }

    internal static AgentClientAcknowledgement Acknowledgement(AgentActionProposal proposal) => new(proposal.PreparedInvocation!.Client!,
        proposal.PreparedInvocation.ApprovalDigest, AgentClientOutcome.Applied, "The client observed the edit.", proposal.ResourceTarget! with { Revision = "revision-2" },
        new("test.client-change", "1", System.Text.Json.JsonSerializer.SerializeToElement(new { observed = true, value = "new", revision = "revision-2" })));

    /// <summary>Exactly one recovery-attributed lifecycle record, with no actor and the original correlation.</summary>
    private static AgentAuditEvent AssertSettledOnce(IReadOnlyList<AgentAuditEvent> events, AgentActionProposal proposal, string status)
    {
        var settled = Assert.Single(events, x => x.Metadata.GetValueOrDefault(AgentProposalRecovery.SettledBy) == AgentProposalRecovery.Recovery);
        Assert.Null(settled.ActorId);
        Assert.Equal(AgentAuditEventKind.ProposalFailed, settled.Kind);
        Assert.Equal(status, settled.Metadata["status"]);
        Assert.Equal(proposal.Id, settled.Metadata["proposalId"]);
        Assert.Equal(proposal.PreparedInvocation!.InvocationId, settled.Metadata["invocationId"]);
        Assert.Equal(proposal.PreparedInvocation.ToolCallId, settled.Metadata["toolCallId"]);
        return settled;
    }

    /// <summary>A store that silently skips the next write recording a proposal with <see cref="Drop"/> status.</summary>
    private sealed class DroppingRecordStore(IAgentSessionRecordStore inner) : IAgentSessionRecordStore
    {
        public AgentActionProposalStatus? Drop { get; set; }
        public int Dropped { get; private set; }
        public bool SupportsFencing => inner.SupportsFencing;
        public Task<AgentSessionRecordResult> CreateAsync(AgentSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => inner.CreateAsync(record, ownerToken, cancellationToken);
        public Task<AgentSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default) => inner.LoadAsync(sessionId, cancellationToken);
        public Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
            => inner.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
        public Task ReleaseOwnershipAsync(string sessionId, AgentSessionFence fence, CancellationToken cancellationToken = default)
            => inner.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);
        public Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default)
        {
            if (Drop is { } status && write.Proposals.Any(x => x.Proposal.Status == status))
            {
                Drop = null;
                Dropped++;
                return Task.FromResult(new AgentSessionRecordResult(AgentSessionRecordOutcome.Succeeded) { Fence = write.Fence });
            }
            return inner.WriteAsync(write, cancellationToken);
        }
        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => inner.FindProposalSessionAsync(proposalId, cancellationToken);
        public Task<AgentSessionSummaryPage> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => inner.ListOwnedAsync(query, cancellationToken);
    }
}
