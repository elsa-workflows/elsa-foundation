using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Extensions;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

/// <summary>
/// Deterministic ownership tests for the experimental session policy owner (#3440, T008). These run the real
/// default services with TaskCompletionSource barriers; they are behavior tests, not native/provider evidence.
/// </summary>
public sealed class AgentSessionPolicyTests
{
    private static readonly AgentPolicy FullAuto = AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto };

    // ---- Conditional update order and dispositions ----

    [Fact]
    public async Task Update_refuses_an_undefined_mode_then_a_closed_session_then_a_stale_revision_then_busy()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        await fixture.PostAsync(session, "hello");

        Assert.Equal(AgentSessionPolicyOutcome.InvalidMode, (await fixture.ChangeAsync(session, (AgentAutonomyMode)42, 99)).Outcome);
        var stale = await fixture.ChangeAsync(session, AgentAutonomyMode.FullAuto, 99);
        Assert.Equal(AgentSessionPolicyOutcome.StaleRevision, stale.Outcome);
        Assert.Equal(1, stale.State!.Revision);
        var busy = await fixture.ChangeAsync(session, AgentAutonomyMode.FullAuto, 1);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, busy.Outcome);
        Assert.Equal(AgentAutonomyMode.AutoReadOnly, busy.State!.Policy.AutonomyMode);

        fixture.Clock.Advance(TimeSpan.FromHours(9));
        Assert.Equal(AgentSessionPolicyOutcome.InvalidMode, (await fixture.ChangeAsync(session, (AgentAutonomyMode)42, 99)).Outcome);
        Assert.Equal(AgentSessionPolicyOutcome.SessionClosed, (await fixture.ChangeAsync(session, AgentAutonomyMode.FullAuto, 99)).Outcome);
        Assert.Equal(AgentSessionPolicyOutcome.SessionClosed, (await fixture.ChangeAsync(session, AgentAutonomyMode.FullAuto, 1)).Outcome);
        var stored = (await fixture.Sessions.FindAsync(session.Id))!;
        Assert.Equal(AgentAutonomyMode.AutoReadOnly, stored.Policy.AutonomyMode);
        Assert.Equal(1, stored.PolicyRevision);
    }

    [Fact]
    public async Task A_stale_revision_conflicts_after_newer_updates_without_overwriting_them()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        Assert.Equal(2, (await fixture.ChangeAsync(session, AgentAutonomyMode.Manual, 1)).State!.Revision);
        Assert.Equal(3, (await fixture.ChangeAsync(session, AgentAutonomyMode.FullAuto, 2)).State!.Revision);

        foreach (var old in new long[] { 1, 2 })
        {
            var conflict = await fixture.ChangeAsync(session, AgentAutonomyMode.AutoReadOnly, old);
            Assert.Equal(AgentSessionPolicyOutcome.StaleRevision, conflict.Outcome);
            Assert.Equal(new AgentSessionPolicyState(conflict.State!.Policy, 3), conflict.State);
            Assert.Equal(AgentAutonomyMode.FullAuto, conflict.State.Policy.AutonomyMode);
        }
        var stored = (await fixture.Sessions.FindAsync(session.Id))!;
        Assert.Equal(AgentAutonomyMode.FullAuto, stored.Policy.AutonomyMode);
        Assert.Equal(3, stored.PolicyRevision);
    }

    [Fact]
    public async Task Update_replaces_only_the_autonomy_mode_and_an_unchanged_idle_request_acknowledges_without_increment()
    {
        using var fixture = new Fixture();
        var policy = new AgentPolicy("restricted", true, false, AgentContextSensitivity.Public, ["kind.a", "kind.b"],
            AgentAutonomyMode.FullAuto, AgentAutonomyMode.FullAuto, ["denied.tool"], "Retained by test");
        var session = await fixture.CreateAsync(policy);

        var changed = await fixture.ChangeAsync(session, AgentAutonomyMode.Manual, 1);

        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, changed.Outcome);
        Assert.Equal(2, changed.State!.Revision);
        var stored = (await fixture.Sessions.FindAsync(session.Id))!.Policy;
        foreach (var current in new[] { changed.State.Policy, stored })
        {
            Assert.Equal(AgentAutonomyMode.Manual, current.AutonomyMode);
            Assert.Equal(policy.Id, current.Id);
            Assert.Equal(policy.Enabled, current.Enabled);
            Assert.Equal(policy.ContextVisibility, current.ContextVisibility);
            Assert.Equal(policy.MaxContextSensitivity, current.MaxContextSensitivity);
            Assert.Equal(policy.AllowedContextKinds, current.AllowedContextKinds);
            Assert.Equal(policy.MaxAutonomyMode, current.MaxAutonomyMode);
            Assert.Equal(policy.DeniedCapabilityIds, current.DeniedCapabilityIds);
            Assert.Equal(policy.RetentionLabel, current.RetentionLabel);
        }

        var unchanged = await fixture.ChangeAsync(session, AgentAutonomyMode.Manual, 2);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, unchanged.Outcome);
        Assert.Equal(2, unchanged.State!.Revision);
        Assert.Equal(2, (await fixture.Sessions.FindAsync(session.Id))!.PolicyRevision);
    }

    [Fact]
    public async Task A_requested_mode_clamps_to_the_current_ceiling()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync(AgentPolicy.Default with
        { AutonomyMode = AgentAutonomyMode.Manual, MaxAutonomyMode = AgentAutonomyMode.AutoReadOnly });

        var clamped = await fixture.ChangeAsync(session, AgentAutonomyMode.FullAuto, 1);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, clamped.Outcome);
        Assert.Equal(AgentAutonomyMode.AutoReadOnly, clamped.State!.Policy.AutonomyMode);
        Assert.Equal(2, clamped.State.Revision);

        var again = await fixture.ChangeAsync(session, AgentAutonomyMode.FullAuto, 2);
        Assert.Equal(AgentAutonomyMode.AutoReadOnly, again.State!.Policy.AutonomyMode);
        Assert.Equal(2, again.State.Revision);
    }

    [Fact]
    public async Task A_missing_or_foreign_session_is_not_found_for_readback_and_update()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        foreach (var (id, actor, tenant) in new[] { ("missing", session.ActorId, session.TenantId), (session.Id, "other", session.TenantId), (session.Id, session.ActorId, "other") })
        {
            Assert.Equal(AgentSessionPolicyOutcome.NotFound, (await fixture.Policy.GetAsync(id, actor, tenant)).Outcome);
            var refused = await fixture.Policy.ChangeAutonomyAsync(new(id, actor, tenant, AgentAutonomyMode.Manual, 1));
            Assert.Equal(AgentSessionPolicyOutcome.NotFound, refused.Outcome);
            Assert.Null(refused.State);
        }
        Assert.Equal(1, (await fixture.Sessions.FindAsync(session.Id))!.PolicyRevision);
    }

    [Fact]
    public async Task Retained_policy_snapshots_do_not_alias_caller_owned_collections()
    {
        using var fixture = new Fixture();
        var denied = new List<string> { "first" };
        var session = await fixture.CreateAsync(AgentPolicy.Default with { DeniedCapabilityIds = denied });
        var message = await fixture.PostAsync(session, "hello");
        var readback = (await fixture.Policy.GetAsync(session.Id, session.ActorId, session.TenantId)).State!;

        denied.Add("later");

        Assert.Equal(["first"], message.AcceptedPolicy!.Policy.DeniedCapabilityIds);
        Assert.Equal(["first"], readback.Policy.DeniedCapabilityIds);
    }

    // ---- Prompt admission and attribution ----

    [Fact]
    public async Task Prompt_first_holds_the_policy_until_its_turn_completes_and_keeps_its_original_attribution()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var prompt = await fixture.PostAsync(session, "hello");
        Assert.Equal(1, prompt.AcceptedPolicy!.Revision);
        Assert.Equal(AgentAutonomyMode.AutoReadOnly, prompt.AcceptedPolicy.Policy.AutonomyMode);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await fixture.ChangeAsync(session, AgentAutonomyMode.FullAuto, 1)).Outcome);

        var events = await Collect(fixture.Streaming.StreamAsync(session.Id));
        Assert.Equal(AgentStreamEventKind.Completed, events[^1].Kind);

        Assert.Equal(2, (await fixture.ChangeAsync(session, AgentAutonomyMode.FullAuto, 1)).State!.Revision);
        var retained = Assert.Single(await fixture.Sessions.ListMessagesAsync(session.Id), x => x.Id == prompt.Id);
        Assert.Equal(1, retained.AcceptedPolicy!.Revision);
        Assert.Equal(AgentAutonomyMode.AutoReadOnly, retained.AcceptedPolicy.Policy.AutonomyMode);
    }

    [Fact]
    public async Task Policy_first_attributes_the_next_prompt_to_the_new_revision()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        Assert.True((await fixture.ChangeAsync(session, AgentAutonomyMode.FullAuto, 1)).Succeeded);

        var prompt = await fixture.PostAsync(session, "hello", expectedRevision: 2);

        Assert.Equal(2, prompt.AcceptedPolicy!.Revision);
        Assert.Equal(AgentAutonomyMode.FullAuto, prompt.AcceptedPolicy.Policy.AutonomyMode);
    }

    [Fact]
    public async Task Insertion_refuses_a_revision_that_moved_after_authorization_without_inserting_or_claiming()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        Assert.True((await fixture.ChangeAsync(session, AgentAutonomyMode.Manual, 1)).Succeeded);

        var refused = await Assert.ThrowsAsync<AgentSessionPolicyChangedException>(() => fixture.PostAsync(session, "hello", expectedRevision: 1));

        Assert.Equal(1, refused.ExpectedRevision);
        Assert.Equal(2, refused.CurrentRevision);
        Assert.Equal("agent.message.policy_changed", AgentSessionPolicyChangedException.Code);
        Assert.Empty(await fixture.Sessions.ListMessagesAsync(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
        Assert.Equal(2, (await fixture.PostAsync(session, "hello", expectedRevision: 2)).AcceptedPolicy!.Revision);
    }

    [Fact]
    public async Task Terminalizing_a_never_started_prompt_releases_only_its_own_claim()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var prompt = await fixture.PostAsync(session, "hello");
        var foreground = fixture.Owner.Acquire(session.Id, AgentSessionClaimKind.Foreground)!;

        await fixture.Sessions.SetMessageStatusAsync(session.Id, prompt.Id, AgentMessageStatus.Cancelled);

        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        fixture.Owner.Release(foreground);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
    }

    [Fact]
    public async Task Releasing_an_old_token_never_releases_a_successor()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var owner = fixture.Owner;
        var old = owner.Acquire(session.Id, AgentSessionClaimKind.Foreground)!;
        owner.Release(old);
        var successor = owner.Acquire(session.Id, AgentSessionClaimKind.Foreground)!;
        owner.Release(old);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        owner.Release(successor);

        var oldPause = owner.EnsurePause(session.Id)!;
        Assert.Same(oldPause, owner.EnsurePause(session.Id));
        owner.Release(oldPause);
        var nextPause = owner.EnsurePause(session.Id)!;
        Assert.NotSame(oldPause, nextPause);
        owner.Release(oldPause);
        Assert.Same(nextPause, owner.FindPause(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        owner.Release(nextPause);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
    }

    // ---- Foreground registration handoffs ----

    [Fact]
    public async Task Foreground_claim_is_held_across_a_held_registration_and_a_held_unregistration()
    {
        var registry = new GatedTurnRegistry(new AgentTurnRegistry()) { HoldRegister = true, HoldUnregister = true };
        using var fixture = new Fixture(services => services.Replace(ServiceDescriptor.Singleton<IAgentTurnRegistry>(registry)));
        var session = await fixture.CreateAsync();

        var stream = Task.Run(() => Collect(fixture.Streaming.StreamAsync(session.Id)));
        await registry.RegisterEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        registry.RegisterRelease.SetResult();

        await registry.UnregisterEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        registry.UnregisterRelease.SetResult();

        var events = await stream.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal("agent.message.no_pending", events[^1].Error?.Code);
        // A forwarding registry replacement keeps the update supported: the orchestrator holds claims around it.
        var changed = await fixture.ChangeAsync(session, AgentAutonomyMode.Manual, 1);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, changed.Outcome);
        Assert.Equal(2, changed.State!.Revision);
    }

    [Fact]
    public async Task The_default_registry_busy_refusal_releases_the_new_foreground_claim()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var turns = fixture.Scope.ServiceProvider.GetRequiredService<IAgentTurnRegistry>();
        turns.Register(session.Id, "external-turn");
        try
        {
            var refused = Assert.Single(await Collect(fixture.Streaming.StreamAsync(session.Id)));
            Assert.Equal("agent.turn.busy", refused.Error?.Code);
            Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
        }
        finally { turns.Unregister(session.Id, "external-turn"); }
    }

    [Theory]
    [InlineData("write-then-throw")]
    [InlineData("busy-shaped-replacement")]
    public async Task An_unknown_registration_failure_leaves_the_foreground_claim_unresolved(string failure)
    {
        var registry = new GatedTurnRegistry(new AgentTurnRegistry()) { Failure = failure };
        using var fixture = new Fixture(services => services.Replace(ServiceDescriptor.Singleton<IAgentTurnRegistry>(registry)));
        var session = await fixture.CreateAsync();

        if (failure == "write-then-throw")
            await Assert.ThrowsAsync<AgentContributionException>(() => Collect(fixture.Streaming.StreamAsync(session.Id)));
        else
            Assert.Equal("agent.turn.busy", Assert.Single(await Collect(fixture.Streaming.StreamAsync(session.Id))).Error?.Code);

        // The owner cannot rule out a write, so it does not fabricate an idle state.
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        Assert.Equal(1, (await fixture.Sessions.FindAsync(session.Id))!.PolicyRevision);
    }

    // ---- Direct invoker preparation ----

    [Fact]
    public async Task A_direct_invoker_mutation_holds_its_preparation_claim_and_releases_it_on_every_exit()
    {
        var tool = new GatedApplyChangeTool();
        using var fixture = new Fixture(null, tool);
        var session = await fixture.CreateAsync(FullAuto);

        tool.Arm();
        var dispatch = Task.Run(() => fixture.Invoker.DispatchAsync(Invocation(session, "turn-1"), session.Policy));
        await tool.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        tool.Release.SetResult();
        var executed = await dispatch.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(AgentToolDispatchOutcome.Executed, executed.Outcome);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));

        // Duplicate replay returns the retained receipt and releases its preparation claim.
        Assert.Equal(AgentToolDispatchOutcome.Executed, (await fixture.Invoker.DispatchAsync(Invocation(session, "turn-1"), session.Policy)).Outcome);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));

        // Cancellation during preparation releases it as well.
        tool.Arm();
        using var cancellation = new CancellationTokenSource();
        var cancelled = Task.Run(() => fixture.Invoker.DispatchAsync(Invocation(session, "turn-2"), session.Policy, cancellation.Token));
        await tool.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
        Assert.Equal(1, tool.Inner.Invocations);
    }

    [Fact]
    public async Task Preparation_uses_the_claim_snapshot_as_its_approval_basis_and_stamps_its_revision()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), FullAuto);
        var policy = fixture.Services.GetRequiredService<IAgentSessionPolicyService>();
        var session = fixture.Session;
        var changed = await policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, AgentAutonomyMode.Manual, 1));
        Assert.Equal(2, changed.State!.Revision);

        // The caller still passes the earlier FullAuto snapshot; it cannot loosen the claimed Manual basis.
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), session.Policy);

        Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
        Assert.True(dispatch.Proposal!.RequiresApproval);
        Assert.Equal(2, dispatch.Proposal.PolicyRevision);
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
    }

    // ---- Proposal settlement ----

    [Theory]
    [InlineData("deny")]
    [InlineData("execute")]
    public async Task A_reviewed_proposal_holds_the_policy_until_it_settles(string settle)
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var proposal = await fixture.ProposeAsync();
        Assert.Equal(AgentActionProposalStatus.AwaitingApproval, proposal.Status);
        Assert.Equal(1, proposal.PolicyRevision);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await ProbeAsync(fixture));

        if (settle == "deny")
            Assert.Equal(AgentActionProposalStatus.Denied, (await fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "No")).Value?.Status);
        else
        {
            await fixture.ApproveAsync(proposal);
            Assert.Equal(AgentSessionPolicyOutcome.Busy, await ProbeAsync(fixture));
            Assert.True((await fixture.ExecuteAsync(proposal)).Value!.Executed);
        }

        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    [Fact]
    public async Task A_server_scope_uncertain_outcome_releases_its_proposal_claim()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool { NoopWithoutReceipt = true });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);

        Assert.Equal("uncertain", (await fixture.ExecuteAsync(proposal)).Value?.Status);

        Assert.Equal(AgentActionProposalStatus.Uncertain, (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    [Fact]
    public async Task A_client_proposal_retains_its_claim_while_awaiting_the_client_and_while_client_scope_uncertain()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        Assert.Equal("awaiting-client", (await fixture.ExecuteAsync(proposal)).Value?.Status);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await ProbeAsync(fixture));

        Assert.Equal(AgentActionProposalStatus.Uncertain, (await fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "Cancel")).Value?.Status);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await ProbeAsync(fixture));

        // A late admitted receipt settles it; the acknowledgement does not depend on the current mode.
        Assert.True((await fixture.AcknowledgeAsync(proposal)).Value!.Executed);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    // ---- Pause and continuation ----

    [Fact]
    public async Task The_pause_holds_the_policy_from_the_paused_turn_until_its_continuation_removes_it()
    {
        var tool = new ApplyChangeTool();
        using var fixture = new Fixture(null, tool);
        var session = await fixture.CreateAsync();
        await fixture.PostAsync(session, "mutate the resource");
        var first = await Collect(fixture.Streaming.StreamAsync(session.Id));
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        Assert.NotNull(fixture.Owner.FindPause(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));

        Assert.True((await fixture.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.True((await fixture.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value!.Executed);
        // The proposal settled; the pause still covers the originating turn until its continuation.
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));

        var resumed = await Collect(fixture.Streaming.StreamAsync(session.Id));

        Assert.Equal(AgentStreamEventKind.Completed, resumed[^1].Kind);
        Assert.Null(fixture.Owner.FindPause(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
        Assert.Equal(1, tool.Invocations);
    }

    [Fact]
    public async Task An_orphaned_pre_dispatch_proposal_stops_counting_as_busy_after_its_deadline()
    {
        var sink = new CancellingAuditSink();
        var tool = new ApplyChangeTool();
        using var fixture = new Fixture(services => services.Replace(ServiceDescriptor.Singleton<IAgentAuditSink>(sp =>
        {
            sink.Inner = sp.GetRequiredService<InMemoryAgentAuditStore>();
            return sink;
        })), tool);
        var session = await fixture.CreateAsync();
        await fixture.PostAsync(session, "mutate the resource");
        using var cancellation = new CancellationTokenSource();
        sink.CancelAfterProposal = cancellation;

        // The turn is cancelled after proposal insertion and before the pause is saved.
        var events = await Collect(fixture.Streaming.StreamAsync(session.Id, cancellation.Token));

        Assert.Equal(AgentStreamEventKind.TurnCancelled, events[^1].Kind);
        Assert.Null(fixture.Scope.ServiceProvider.GetRequiredService<IAgentTurnStateStore>().Find(session.Id));
        Assert.Null(fixture.Owner.FindPause(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));

        fixture.Clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
        Assert.Equal(0, tool.Invocations);
    }

    // ---- Participation ----

    [Theory]
    [InlineData("session")]
    [InlineData("orchestrator")]
    [InlineData("invoker")]
    [InlineData("proposals")]
    public async Task A_forwarding_lifecycle_replacement_makes_the_update_unsupported_without_state_change(string replaced)
    {
        using var fixture = new Fixture(services =>
        {
            switch (replaced)
            {
                case "session":
                    services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => new ForwardingSessions(
                        new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>(), sp.GetRequiredService<TimeProvider>()))));
                    break;
                case "orchestrator":
                    services.Replace(ServiceDescriptor.Scoped<IAgentStreamingService>(sp => new ForwardingStreaming(
                        ActivatorUtilities.CreateInstance<DefaultAgentTurnOrchestrator>(sp))));
                    break;
                case "invoker":
                    services.Replace(ServiceDescriptor.Scoped<IAgentToolInvoker>(sp => new ForwardingInvoker(
                        ActivatorUtilities.CreateInstance<DefaultAgentToolInvoker>(sp))));
                    break;
                case "proposals":
                    services.Replace(ServiceDescriptor.Scoped<IAgentProposalService>(sp => new ForwardingProposals(
                        ActivatorUtilities.CreateInstance<InMemoryAgentProposalService>(sp))));
                    break;
            }
        });
        var session = await fixture.CreateAsync();

        Assert.Equal(AgentSessionPolicyOutcome.Unsupported, (await fixture.Policy.GetAsync(session.Id, session.ActorId, session.TenantId)).Outcome);
        var refused = await fixture.ChangeAsync(session, AgentAutonomyMode.FullAuto, 1);

        Assert.Equal(AgentSessionPolicyOutcome.Unsupported, refused.Outcome);
        Assert.Null(refused.State);
        var stored = (await fixture.Sessions.FindAsync(session.Id))!;
        Assert.Equal(AgentAutonomyMode.AutoReadOnly, stored.Policy.AutonomyMode);
        Assert.Equal(1, stored.PolicyRevision);
    }

    [Fact]
    public void The_policy_service_is_registered_once_as_a_scoped_default()
    {
        var services = new ServiceCollection();
        services.AddFoundationAgentAbstractions();
        services.AddFoundationAgentAbstractions();

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IAgentSessionPolicyService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public async Task The_default_composition_resolves_a_supported_policy_service_per_scope()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        using var other = fixture.Root.CreateScope();

        Assert.IsType<DefaultAgentSessionPolicyService>(fixture.Policy);
        Assert.NotSame(fixture.Policy, other.ServiceProvider.GetRequiredService<IAgentSessionPolicyService>());
        var readback = await other.ServiceProvider.GetRequiredService<IAgentSessionPolicyService>().GetAsync(session.Id, session.ActorId, session.TenantId);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, readback.Outcome);
        Assert.Equal(1, readback.State!.Revision);
        Assert.Equal(1, session.PolicyRevision);
    }

    // ---- Helpers ----

    private static AgentToolInvocation Invocation(AgentSession session, string turnId)
        => AgentTestToolHelpers.Invocation(DeterministicAgentProvider.MutatingToolName, session.Id) with { TurnId = turnId };

    private static async Task<AgentSessionPolicyOutcome> ProbeAsync(ProposalFixture fixture)
        => await Fixture.ProbeAsync(fixture.Services.GetRequiredService<IAgentSessionPolicyService>(), fixture.Session);

    private static async Task<List<AgentStreamEvent>> Collect(IAsyncEnumerable<AgentStreamEvent> events)
    {
        var result = new List<AgentStreamEvent>();
        await foreach (var item in events)
            result.Add(item);
        return result;
    }

    private static TaskCompletionSource Barrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Fixture : IDisposable
    {
        public Fixture(Action<IServiceCollection>? configure = null, params IAgentTool[] tools)
        {
            Root = AgentTestServices.Create(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
                services.AddSingleton<IAgentProvider, DeterministicAgentProvider>();
                foreach (var tool in tools) services.AddSingleton(tool);
                configure?.Invoke(services);
            });
            Scope = Root.CreateScope();
        }

        public ProposalTestClock Clock { get; } = new();
        public ServiceProvider Root { get; }
        public IServiceScope Scope { get; }
        public IAgentSessionService Sessions => Scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        public IAgentStreamingService Streaming => Scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        public IAgentSessionPolicyService Policy => Scope.ServiceProvider.GetRequiredService<IAgentSessionPolicyService>();
        public IAgentProposalService Proposals => Scope.ServiceProvider.GetRequiredService<IAgentProposalService>();
        public IAgentToolInvoker Invoker => Scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>();
        public IAgentSessionPolicyOwner Owner => (IAgentSessionPolicyOwner)Sessions;

        public Task<AgentSession> CreateAsync(AgentPolicy? policy = null) => Sessions.CreateAsync(new(
            "tenant-1", "actor-1", "conversation-1", DeterministicAgentProvider.Id, "explain", "Policy session", policy ?? AgentPolicy.Default,
            new Dictionary<string, string>()));

        public Task<AgentMessage> PostAsync(AgentSession session, string content, long? expectedRevision = null)
            => Sessions.AddMessageAsync(session.Id, new(AgentRole.User, content, AgentMessageStatus.Pending, null, [], [])
            { ExpectedPolicyRevision = expectedRevision });

        public Task<AgentSessionPolicyResult> ChangeAsync(AgentSession session, AgentAutonomyMode mode, long expectedRevision)
            => Policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, mode, expectedRevision));

        public Task<AgentSessionPolicyOutcome> ProbeAsync(AgentSession session) => ProbeAsync(Policy, session);

        /// <summary>Requests the current mode at the current revision: busy reports Busy, idle acknowledges unchanged.</summary>
        public static async Task<AgentSessionPolicyOutcome> ProbeAsync(IAgentSessionPolicyService policy, AgentSession session)
        {
            var current = await policy.GetAsync(session.Id, session.ActorId, session.TenantId);
            Assert.Equal(AgentSessionPolicyOutcome.Succeeded, current.Outcome);
            var probe = await policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId,
                current.State!.Policy.AutonomyMode, current.State.Revision));
            Assert.Equal(current.State.Revision, probe.State!.Revision);
            return probe.Outcome;
        }

        public void Dispose()
        {
            Scope.Dispose();
            Root.Dispose();
        }
    }

    /// <summary>A forwarding registry replacement with barriers and injected registration failures.</summary>
    private sealed class GatedTurnRegistry(AgentTurnRegistry inner) : IAgentTurnRegistry
    {
        public bool HoldRegister { get; init; }
        public bool HoldUnregister { get; init; }
        public string? Failure { get; init; }
        public TaskCompletionSource RegisterEntered { get; } = Barrier();
        public TaskCompletionSource RegisterRelease { get; } = Barrier();
        public TaskCompletionSource UnregisterEntered { get; } = Barrier();
        public TaskCompletionSource UnregisterRelease { get; } = Barrier();

        public CancellationToken Register(string sessionId, string turnId)
        {
            if (HoldRegister)
            {
                RegisterEntered.TrySetResult();
                RegisterRelease.Task.Wait(TimeSpan.FromSeconds(30));
            }
            if (Failure == "busy-shaped-replacement")
                throw new AgentContributionException("A replacement registry refused registration.");
            var token = inner.Register(sessionId, turnId);
            if (Failure == "write-then-throw")
                throw new InvalidOperationException("The registry failed after writing its lease.");
            return token;
        }

        public bool Cancel(string sessionId, string turnId) => inner.Cancel(sessionId, turnId);

        public void Unregister(string sessionId, string turnId)
        {
            if (HoldUnregister)
            {
                UnregisterEntered.TrySetResult();
                UnregisterRelease.Task.Wait(TimeSpan.FromSeconds(30));
            }
            inner.Unregister(sessionId, turnId);
        }

        public bool IsActive(string sessionId) => inner.IsActive(sessionId);
    }

    /// <summary>The real apply-change module with a preparation barrier.</summary>
    private sealed class GatedApplyChangeTool : IAgentTool
    {
        public ApplyChangeTool Inner { get; } = new();
        public TaskCompletionSource Entered { get; private set; } = Barrier();
        public TaskCompletionSource Release { get; private set; } = Barrier();
        public AgentToolDescriptor Descriptor => Inner.Descriptor;

        public void Arm()
        {
            Entered = Barrier();
            Release = Barrier();
        }

        public async Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await Inner.PrepareAsync(invocation, cancellationToken);
        }

        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
            => Inner.ExecuteAsync(invocation, cancellationToken);
    }

    /// <summary>Cancels the foreground turn right after its proposal is inserted, before the pause can be saved.</summary>
    private sealed class CancellingAuditSink : IAgentAuditSink
    {
        public IAgentAuditSink Inner { get; set; } = null!;
        public CancellationTokenSource? CancelAfterProposal { get; set; }

        public async Task EmitAsync(AgentAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await Inner.EmitAsync(auditEvent, cancellationToken);
            if (CancelAfterProposal is { } cancellation && auditEvent.Kind == AgentAuditEventKind.ToolInvoked
                && auditEvent.Metadata.GetValueOrDefault("outcome") == "proposal-created")
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
        }
    }

    private sealed class ForwardingSessions(IAgentSessionService inner) : IAgentSessionService
    {
        public Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default) => inner.CreateAsync(request, cancellationToken);
        public Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default) => inner.FindAsync(sessionId, cancellationToken);
        public Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default) => inner.AddMessageAsync(sessionId, request, cancellationToken);
        public Task<IReadOnlyCollection<AgentMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default) => inner.ListMessagesAsync(sessionId, cancellationToken);
        public Task<AgentMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default) => inner.FindMessageAsync(sessionId, messageId, cancellationToken);
        public Task<AgentMessage?> FindLatestMessageAsync(string sessionId, AgentRole? role = null, CancellationToken cancellationToken = default) => inner.FindLatestMessageAsync(sessionId, role, cancellationToken);
        public Task SetMessageStatusAsync(string sessionId, string messageId, AgentMessageStatus status, CancellationToken cancellationToken = default) => inner.SetMessageStatusAsync(sessionId, messageId, status, cancellationToken);
        public Task AddContextAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default) => inner.AddContextAsync(sessionId, attachments, cancellationToken);
        public Task<IReadOnlyCollection<AgentContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default) => inner.ListContextAsync(sessionId, cancellationToken);
    }

    private sealed class ForwardingStreaming(IAgentStreamingService inner) : IAgentStreamingService
    {
        public IAsyncEnumerable<AgentStreamEvent> StreamAsync(string sessionId, CancellationToken cancellationToken = default) => inner.StreamAsync(sessionId, cancellationToken);
    }

    private sealed class ForwardingInvoker(IAgentToolInvoker inner) : IAgentToolInvoker
    {
        public Task<AgentToolDispatch> DispatchAsync(AgentToolInvocation invocation, AgentPolicy policy, CancellationToken cancellationToken = default)
            => inner.DispatchAsync(invocation, policy, cancellationToken);
    }

    private sealed class ForwardingProposals(IAgentProposalService inner) : IAgentProposalService
    {
        public Task<AgentActionProposal> AddAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default) => inner.AddAsync(proposal, cancellationToken);
        public Task<AgentActionProposal?> FindAsync(string proposalId, CancellationToken cancellationToken = default) => inner.FindAsync(proposalId, cancellationToken);
        public Task<AgentResult<AgentActionProposal>> ApproveAsync(string proposalId, string actorId, string? expectedRevision = null, string? comment = null, CancellationToken cancellationToken = default)
            => inner.ApproveAsync(proposalId, actorId, expectedRevision, comment, cancellationToken);
        public Task<AgentResult<AgentActionProposal>> DenyAsync(string proposalId, string actorId, string? reason, CancellationToken cancellationToken = default)
            => inner.DenyAsync(proposalId, actorId, reason, cancellationToken);
        public Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(string proposalId, string actorId, string? expectedRevision = null, CancellationToken cancellationToken = default)
            => inner.ExecuteAsync(proposalId, actorId, expectedRevision, cancellationToken);
        public Task<AgentResult<AgentProposalExecutionResult>> AcknowledgeAsync(string sessionId, string invocationId, string actorId, AgentClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
            => inner.AcknowledgeAsync(sessionId, invocationId, actorId, acknowledgement, cancellationToken);
        public Task<AgentToolResult?> FindResultAsync(string proposalId, CancellationToken cancellationToken = default) => inner.FindResultAsync(proposalId, cancellationToken);
    }
}
