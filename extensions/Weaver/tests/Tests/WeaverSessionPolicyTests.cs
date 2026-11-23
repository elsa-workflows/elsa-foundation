using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Extensions;
using Elsa.Weaver.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

/// <summary>
/// Deterministic ownership tests for the experimental session policy owner (#3440, T008). These run the real
/// default services with TaskCompletionSource barriers; they are behavior tests, not native/provider evidence.
/// </summary>
public sealed class WeaverSessionPolicyTests
{
    private static readonly WeaverPolicy FullAuto = WeaverPolicy.Default with { AutonomyMode = WeaverAutonomyMode.FullAuto };

    // ---- Conditional update order and dispositions ----

    [Fact]
    public async Task Update_refuses_an_undefined_mode_then_a_closed_session_then_a_stale_revision_then_busy()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        await fixture.PostAsync(session, "hello");

        Assert.Equal(WeaverSessionPolicyOutcome.InvalidMode, (await fixture.ChangeAsync(session, (WeaverAutonomyMode)42, 99)).Outcome);
        var stale = await fixture.ChangeAsync(session, WeaverAutonomyMode.FullAuto, 99);
        Assert.Equal(WeaverSessionPolicyOutcome.StaleRevision, stale.Outcome);
        Assert.Equal(1, stale.State!.Revision);
        var busy = await fixture.ChangeAsync(session, WeaverAutonomyMode.FullAuto, 1);
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, busy.Outcome);
        Assert.Equal(WeaverAutonomyMode.AutoReadOnly, busy.State!.Policy.AutonomyMode);

        fixture.Clock.Advance(TimeSpan.FromHours(9));
        Assert.Equal(WeaverSessionPolicyOutcome.InvalidMode, (await fixture.ChangeAsync(session, (WeaverAutonomyMode)42, 99)).Outcome);
        Assert.Equal(WeaverSessionPolicyOutcome.SessionClosed, (await fixture.ChangeAsync(session, WeaverAutonomyMode.FullAuto, 99)).Outcome);
        Assert.Equal(WeaverSessionPolicyOutcome.SessionClosed, (await fixture.ChangeAsync(session, WeaverAutonomyMode.FullAuto, 1)).Outcome);
        var stored = (await fixture.Sessions.FindAsync(session.Id))!;
        Assert.Equal(WeaverAutonomyMode.AutoReadOnly, stored.Policy.AutonomyMode);
        Assert.Equal(1, stored.PolicyRevision);
    }

    [Fact]
    public async Task A_stale_revision_conflicts_after_newer_updates_without_overwriting_them()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        Assert.Equal(2, (await fixture.ChangeAsync(session, WeaverAutonomyMode.Manual, 1)).State!.Revision);
        Assert.Equal(3, (await fixture.ChangeAsync(session, WeaverAutonomyMode.FullAuto, 2)).State!.Revision);

        foreach (var old in new long[] { 1, 2 })
        {
            var conflict = await fixture.ChangeAsync(session, WeaverAutonomyMode.AutoReadOnly, old);
            Assert.Equal(WeaverSessionPolicyOutcome.StaleRevision, conflict.Outcome);
            Assert.Equal(new WeaverSessionPolicyState(conflict.State!.Policy, 3), conflict.State);
            Assert.Equal(WeaverAutonomyMode.FullAuto, conflict.State.Policy.AutonomyMode);
        }
        var stored = (await fixture.Sessions.FindAsync(session.Id))!;
        Assert.Equal(WeaverAutonomyMode.FullAuto, stored.Policy.AutonomyMode);
        Assert.Equal(3, stored.PolicyRevision);
    }

    [Fact]
    public async Task Update_replaces_only_the_autonomy_mode_and_an_unchanged_idle_request_acknowledges_without_increment()
    {
        using var fixture = new Fixture();
        var policy = new WeaverPolicy("restricted", true, false, WeaverContextSensitivity.Public, ["kind.a", "kind.b"],
            WeaverAutonomyMode.FullAuto, WeaverAutonomyMode.FullAuto, ["denied.tool"], "Retained by test");
        var session = await fixture.CreateAsync(policy);

        var changed = await fixture.ChangeAsync(session, WeaverAutonomyMode.Manual, 1);

        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, changed.Outcome);
        Assert.Equal(2, changed.State!.Revision);
        var stored = (await fixture.Sessions.FindAsync(session.Id))!.Policy;
        foreach (var current in new[] { changed.State.Policy, stored })
        {
            Assert.Equal(WeaverAutonomyMode.Manual, current.AutonomyMode);
            Assert.Equal(policy.Id, current.Id);
            Assert.Equal(policy.Enabled, current.Enabled);
            Assert.Equal(policy.ContextVisibility, current.ContextVisibility);
            Assert.Equal(policy.MaxContextSensitivity, current.MaxContextSensitivity);
            Assert.Equal(policy.AllowedContextKinds, current.AllowedContextKinds);
            Assert.Equal(policy.MaxAutonomyMode, current.MaxAutonomyMode);
            Assert.Equal(policy.DeniedCapabilityIds, current.DeniedCapabilityIds);
            Assert.Equal(policy.RetentionLabel, current.RetentionLabel);
        }

        var unchanged = await fixture.ChangeAsync(session, WeaverAutonomyMode.Manual, 2);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, unchanged.Outcome);
        Assert.Equal(2, unchanged.State!.Revision);
        Assert.Equal(2, (await fixture.Sessions.FindAsync(session.Id))!.PolicyRevision);
    }

    [Fact]
    public async Task A_requested_mode_clamps_to_the_current_ceiling()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync(WeaverPolicy.Default with
        { AutonomyMode = WeaverAutonomyMode.Manual, MaxAutonomyMode = WeaverAutonomyMode.AutoReadOnly });

        var clamped = await fixture.ChangeAsync(session, WeaverAutonomyMode.FullAuto, 1);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, clamped.Outcome);
        Assert.Equal(WeaverAutonomyMode.AutoReadOnly, clamped.State!.Policy.AutonomyMode);
        Assert.Equal(2, clamped.State.Revision);

        var again = await fixture.ChangeAsync(session, WeaverAutonomyMode.FullAuto, 2);
        Assert.Equal(WeaverAutonomyMode.AutoReadOnly, again.State!.Policy.AutonomyMode);
        Assert.Equal(2, again.State.Revision);
    }

    [Fact]
    public async Task A_missing_or_foreign_session_is_not_found_for_readback_and_update()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        foreach (var (id, actor, tenant) in new[] { ("missing", session.ActorId, session.TenantId), (session.Id, "other", session.TenantId), (session.Id, session.ActorId, "other") })
        {
            Assert.Equal(WeaverSessionPolicyOutcome.NotFound, (await fixture.Policy.GetAsync(id, actor, tenant)).Outcome);
            var refused = await fixture.Policy.ChangeAutonomyAsync(new(id, actor, tenant, WeaverAutonomyMode.Manual, 1));
            Assert.Equal(WeaverSessionPolicyOutcome.NotFound, refused.Outcome);
            Assert.Null(refused.State);
        }
        Assert.Equal(1, (await fixture.Sessions.FindAsync(session.Id))!.PolicyRevision);
    }

    [Fact]
    public async Task Retained_policy_snapshots_do_not_alias_caller_owned_collections()
    {
        using var fixture = new Fixture();
        var denied = new List<string> { "first" };
        var session = await fixture.CreateAsync(WeaverPolicy.Default with { DeniedCapabilityIds = denied });
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
        Assert.Equal(WeaverAutonomyMode.AutoReadOnly, prompt.AcceptedPolicy.Policy.AutonomyMode);
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, (await fixture.ChangeAsync(session, WeaverAutonomyMode.FullAuto, 1)).Outcome);

        var events = await Collect(fixture.Streaming.StreamAsync(session.Id));
        Assert.Equal(WeaverStreamEventKind.Completed, events[^1].Kind);

        Assert.Equal(2, (await fixture.ChangeAsync(session, WeaverAutonomyMode.FullAuto, 1)).State!.Revision);
        var retained = Assert.Single(await fixture.Sessions.ListMessagesAsync(session.Id), x => x.Id == prompt.Id);
        Assert.Equal(1, retained.AcceptedPolicy!.Revision);
        Assert.Equal(WeaverAutonomyMode.AutoReadOnly, retained.AcceptedPolicy.Policy.AutonomyMode);
    }

    [Fact]
    public async Task Policy_first_attributes_the_next_prompt_to_the_new_revision()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        Assert.True((await fixture.ChangeAsync(session, WeaverAutonomyMode.FullAuto, 1)).Succeeded);

        var prompt = await fixture.PostAsync(session, "hello", expectedRevision: 2);

        Assert.Equal(2, prompt.AcceptedPolicy!.Revision);
        Assert.Equal(WeaverAutonomyMode.FullAuto, prompt.AcceptedPolicy.Policy.AutonomyMode);
    }

    [Fact]
    public async Task Insertion_refuses_a_revision_that_moved_after_authorization_without_inserting_or_claiming()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        Assert.True((await fixture.ChangeAsync(session, WeaverAutonomyMode.Manual, 1)).Succeeded);

        var refused = await Assert.ThrowsAsync<WeaverSessionPolicyChangedException>(() => fixture.PostAsync(session, "hello", expectedRevision: 1));

        Assert.Equal(1, refused.ExpectedRevision);
        Assert.Equal(2, refused.CurrentRevision);
        Assert.Equal("weaver.message.policy_changed", WeaverSessionPolicyChangedException.Code);
        Assert.Empty(await fixture.Sessions.ListMessagesAsync(session.Id));
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
        Assert.Equal(2, (await fixture.PostAsync(session, "hello", expectedRevision: 2)).AcceptedPolicy!.Revision);
    }

    [Fact]
    public async Task Terminalizing_a_never_started_prompt_releases_only_its_own_claim()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var prompt = await fixture.PostAsync(session, "hello");
        var foreground = (await fixture.Owner.AcquireAsync(session.Id, WeaverSessionClaimKind.Foreground))!;

        await fixture.Sessions.SetMessageStatusAsync(session.Id, prompt.Id, WeaverMessageStatus.Cancelled);

        Assert.Equal(WeaverSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        fixture.Owner.Release(foreground);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
    }

    [Fact]
    public async Task Releasing_an_old_token_never_releases_a_successor()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var owner = fixture.Owner;
        var old = (await owner.AcquireAsync(session.Id, WeaverSessionClaimKind.Foreground))!;
        owner.Release(old);
        var successor = (await owner.AcquireAsync(session.Id, WeaverSessionClaimKind.Foreground))!;
        owner.Release(old);
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        owner.Release(successor);

        var oldPause = (await owner.EnsurePauseAsync(session.Id))!;
        Assert.Same(oldPause, await owner.EnsurePauseAsync(session.Id));
        owner.Release(oldPause);
        var nextPause = (await owner.EnsurePauseAsync(session.Id))!;
        Assert.NotSame(oldPause, nextPause);
        owner.Release(oldPause);
        Assert.Same(nextPause, owner.FindPause(session.Id));
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        owner.Release(nextPause);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
    }

    // ---- Foreground registration handoffs ----

    [Fact]
    public async Task Foreground_claim_is_held_across_a_held_registration_and_a_held_unregistration()
    {
        var registry = new GatedTurnRegistry(new WeaverTurnRegistry()) { HoldRegister = true, HoldUnregister = true };
        using var fixture = new Fixture(services => services.Replace(ServiceDescriptor.Singleton<IWeaverTurnRegistry>(registry)));
        var session = await fixture.CreateAsync();

        var stream = Task.Run(() => Collect(fixture.Streaming.StreamAsync(session.Id)));
        await registry.RegisterEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        registry.RegisterRelease.SetResult();

        await registry.UnregisterEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        registry.UnregisterRelease.SetResult();

        var events = await stream.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal("weaver.message.no_pending", events[^1].Error?.Code);
        // A forwarding registry replacement keeps the update supported: the orchestrator holds claims around it.
        var changed = await fixture.ChangeAsync(session, WeaverAutonomyMode.Manual, 1);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, changed.Outcome);
        Assert.Equal(2, changed.State!.Revision);
    }

    [Fact]
    public async Task The_default_registry_busy_refusal_releases_the_new_foreground_claim()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var turns = fixture.Scope.ServiceProvider.GetRequiredService<IWeaverTurnRegistry>();
        turns.Register(session.Id, "external-turn");
        try
        {
            var refused = Assert.Single(await Collect(fixture.Streaming.StreamAsync(session.Id)));
            Assert.Equal("weaver.turn.busy", refused.Error?.Code);
            Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
        }
        finally { turns.Unregister(session.Id, "external-turn"); }
    }

    [Theory]
    [InlineData("write-then-throw")]
    [InlineData("busy-shaped-replacement")]
    public async Task An_unknown_registration_failure_leaves_the_foreground_claim_unresolved(string failure)
    {
        var registry = new GatedTurnRegistry(new WeaverTurnRegistry()) { Failure = failure };
        using var fixture = new Fixture(services => services.Replace(ServiceDescriptor.Singleton<IWeaverTurnRegistry>(registry)));
        var session = await fixture.CreateAsync();

        if (failure == "write-then-throw")
            await Assert.ThrowsAsync<WeaverContributionException>(() => Collect(fixture.Streaming.StreamAsync(session.Id)));
        else
            Assert.Equal("weaver.turn.busy", Assert.Single(await Collect(fixture.Streaming.StreamAsync(session.Id))).Error?.Code);

        // The owner cannot rule out a write, so it does not fabricate an idle state.
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
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
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        tool.Release.SetResult();
        var executed = await dispatch.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(WeaverToolDispatchOutcome.Executed, executed.Outcome);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));

        // Duplicate replay returns the retained receipt and releases its preparation claim.
        Assert.Equal(WeaverToolDispatchOutcome.Executed, (await fixture.Invoker.DispatchAsync(Invocation(session, "turn-1"), session.Policy)).Outcome);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));

        // Cancellation during preparation releases it as well.
        tool.Arm();
        using var cancellation = new CancellationTokenSource();
        var cancelled = Task.Run(() => fixture.Invoker.DispatchAsync(Invocation(session, "turn-2"), session.Policy, cancellation.Token));
        await tool.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
        Assert.Equal(1, tool.Inner.Invocations);
    }

    [Fact]
    public async Task Preparation_uses_the_claim_snapshot_as_its_approval_basis_and_stamps_its_revision()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), FullAuto);
        var policy = fixture.Services.GetRequiredService<IWeaverSessionPolicyService>();
        var session = fixture.Session;
        var changed = await policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, WeaverAutonomyMode.Manual, 1));
        Assert.Equal(2, changed.State!.Revision);

        // The caller still passes the earlier FullAuto snapshot; it cannot loosen the claimed Manual basis.
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), session.Policy);

        Assert.Equal(WeaverToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
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
        Assert.Equal(WeaverActionProposalStatus.AwaitingApproval, proposal.Status);
        Assert.Equal(1, proposal.PolicyRevision);
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, await ProbeAsync(fixture));

        if (settle == "deny")
            Assert.Equal(WeaverActionProposalStatus.Denied, (await fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "No")).Value?.Status);
        else
        {
            await fixture.ApproveAsync(proposal);
            Assert.Equal(WeaverSessionPolicyOutcome.Busy, await ProbeAsync(fixture));
            Assert.True((await fixture.ExecuteAsync(proposal)).Value!.Executed);
        }

        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    [Fact]
    public async Task A_server_scope_uncertain_outcome_releases_its_proposal_claim()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool { NoopWithoutReceipt = true });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);

        Assert.Equal("uncertain", (await fixture.ExecuteAsync(proposal)).Value?.Status);

        Assert.Equal(WeaverActionProposalStatus.Uncertain, (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    [Fact]
    public async Task A_client_proposal_retains_its_claim_while_awaiting_the_client_and_while_client_scope_uncertain()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        Assert.Equal("awaiting-client", (await fixture.ExecuteAsync(proposal)).Value?.Status);
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, await ProbeAsync(fixture));

        Assert.Equal(WeaverActionProposalStatus.Uncertain, (await fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "Cancel")).Value?.Status);
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, await ProbeAsync(fixture));

        // A late admitted receipt settles it; the acknowledgement does not depend on the current mode.
        Assert.True((await fixture.AcknowledgeAsync(proposal)).Value!.Executed);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
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
        var proposal = Assert.IsType<WeaverActionProposal>(Assert.Single(first, x => x.Kind == WeaverStreamEventKind.ProposalCreated).Payload);
        Assert.NotNull(fixture.Owner.FindPause(session.Id));
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));

        Assert.True((await fixture.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.True((await fixture.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value!.Executed);
        // The proposal settled; the pause still covers the originating turn until its continuation.
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));

        var resumed = await Collect(fixture.Streaming.StreamAsync(session.Id));

        Assert.Equal(WeaverStreamEventKind.Completed, resumed[^1].Kind);
        Assert.Null(fixture.Owner.FindPause(session.Id));
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
        Assert.Equal(1, tool.Invocations);
    }

    [Fact]
    public async Task An_orphaned_pre_dispatch_proposal_stops_counting_as_busy_after_its_deadline()
    {
        var sink = new CancellingAuditSink();
        var tool = new ApplyChangeTool();
        using var fixture = new Fixture(services => services.Replace(ServiceDescriptor.Singleton<IWeaverAuditSink>(sp =>
        {
            sink.Inner = sp.GetRequiredService<InMemoryWeaverAuditStore>();
            return sink;
        })), tool);
        var session = await fixture.CreateAsync();
        await fixture.PostAsync(session, "mutate the resource");
        using var cancellation = new CancellationTokenSource();
        sink.CancelAfterProposal = cancellation;

        // The turn is cancelled after proposal insertion and before the pause is saved.
        var events = await Collect(fixture.Streaming.StreamAsync(session.Id, cancellation.Token));

        Assert.Equal(WeaverStreamEventKind.TurnCancelled, events[^1].Kind);
        Assert.Null(await fixture.Scope.ServiceProvider.GetRequiredService<IWeaverTurnStateStore>().FindAsync(session.Id));
        Assert.Null(fixture.Owner.FindPause(session.Id));
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));

        fixture.Clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
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
                    services.Replace(ServiceDescriptor.Singleton<IWeaverSessionService>(sp => new ForwardingSessions(
                        new InMemoryWeaverSessionService(sp.GetRequiredService<IWeaverAuditSink>(), sp.GetRequiredService<TimeProvider>()))));
                    break;
                case "orchestrator":
                    services.Replace(ServiceDescriptor.Scoped<IWeaverStreamingService>(sp => new ForwardingStreaming(
                        ActivatorUtilities.CreateInstance<DefaultWeaverTurnOrchestrator>(sp))));
                    break;
                case "invoker":
                    services.Replace(ServiceDescriptor.Scoped<IWeaverToolInvoker>(sp => new ForwardingInvoker(
                        ActivatorUtilities.CreateInstance<DefaultWeaverToolInvoker>(sp))));
                    break;
                case "proposals":
                    services.Replace(ServiceDescriptor.Scoped<IWeaverProposalService>(sp => new ForwardingProposals(
                        ActivatorUtilities.CreateInstance<InMemoryWeaverProposalService>(sp))));
                    break;
            }
        });
        var session = await fixture.CreateAsync();

        Assert.Equal(WeaverSessionPolicyOutcome.Unsupported, (await fixture.Policy.GetAsync(session.Id, session.ActorId, session.TenantId)).Outcome);
        var refused = await fixture.ChangeAsync(session, WeaverAutonomyMode.FullAuto, 1);

        Assert.Equal(WeaverSessionPolicyOutcome.Unsupported, refused.Outcome);
        Assert.Null(refused.State);
        var stored = (await fixture.Sessions.FindAsync(session.Id))!;
        Assert.Equal(WeaverAutonomyMode.AutoReadOnly, stored.Policy.AutonomyMode);
        Assert.Equal(1, stored.PolicyRevision);
    }

    [Fact]
    public void The_policy_service_is_registered_once_as_a_scoped_default()
    {
        var services = new ServiceCollection();
        services.AddWeaver();
        services.AddWeaver();

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IWeaverSessionPolicyService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public async Task The_default_composition_resolves_a_supported_policy_service_per_scope()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        using var other = fixture.Root.CreateScope();

        Assert.IsType<DefaultWeaverSessionPolicyService>(fixture.Policy);
        Assert.NotSame(fixture.Policy, other.ServiceProvider.GetRequiredService<IWeaverSessionPolicyService>());
        var readback = await other.ServiceProvider.GetRequiredService<IWeaverSessionPolicyService>().GetAsync(session.Id, session.ActorId, session.TenantId);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, readback.Outcome);
        Assert.Equal(1, readback.State!.Revision);
        Assert.Equal(1, session.PolicyRevision);
    }

    // ---- Helpers ----

    private static WeaverToolInvocation Invocation(WeaverSession session, string turnId)
        => WeaverTestToolHelpers.Invocation(DeterministicWeaverProvider.MutatingToolName, session.Id) with { TurnId = turnId };

    private static async Task<WeaverSessionPolicyOutcome> ProbeAsync(ProposalFixture fixture)
        => await Fixture.ProbeAsync(fixture.Services.GetRequiredService<IWeaverSessionPolicyService>(), fixture.Session);

    private static async Task<List<WeaverStreamEvent>> Collect(IAsyncEnumerable<WeaverStreamEvent> events)
    {
        var result = new List<WeaverStreamEvent>();
        await foreach (var item in events)
            result.Add(item);
        return result;
    }

    private static TaskCompletionSource Barrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Fixture : IDisposable
    {
        public Fixture(Action<IServiceCollection>? configure = null, params IWeaverTool[] tools)
        {
            Root = WeaverTestServices.Create(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
                services.AddSingleton<IWeaverProvider, DeterministicWeaverProvider>();
                foreach (var tool in tools) services.AddSingleton(tool);
                configure?.Invoke(services);
            });
            Scope = Root.CreateScope();
        }

        public ProposalTestClock Clock { get; } = new();
        public ServiceProvider Root { get; }
        public IServiceScope Scope { get; }
        public IWeaverSessionService Sessions => Scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        public IWeaverStreamingService Streaming => Scope.ServiceProvider.GetRequiredService<IWeaverStreamingService>();
        public IWeaverSessionPolicyService Policy => Scope.ServiceProvider.GetRequiredService<IWeaverSessionPolicyService>();
        public IWeaverProposalService Proposals => Scope.ServiceProvider.GetRequiredService<IWeaverProposalService>();
        public IWeaverToolInvoker Invoker => Scope.ServiceProvider.GetRequiredService<IWeaverToolInvoker>();
        public InMemoryWeaverSessionService Owner => (InMemoryWeaverSessionService)Sessions;

        public Task<WeaverSession> CreateAsync(WeaverPolicy? policy = null) => Sessions.CreateAsync(new(
            "tenant-1", "actor-1", "conversation-1", DeterministicWeaverProvider.Id, "explain", "Policy session", policy ?? WeaverPolicy.Default,
            new Dictionary<string, string>()));

        public Task<WeaverMessage> PostAsync(WeaverSession session, string content, long? expectedRevision = null)
            => Sessions.AddMessageAsync(session.Id, new(WeaverRole.User, content, WeaverMessageStatus.Pending, null, [], [])
            { ExpectedPolicyRevision = expectedRevision });

        public Task<WeaverSessionPolicyResult> ChangeAsync(WeaverSession session, WeaverAutonomyMode mode, long expectedRevision)
            => Policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, mode, expectedRevision));

        public Task<WeaverSessionPolicyOutcome> ProbeAsync(WeaverSession session) => ProbeAsync(Policy, session);

        /// <summary>Requests the current mode at the current revision: busy reports Busy, idle acknowledges unchanged.</summary>
        public static async Task<WeaverSessionPolicyOutcome> ProbeAsync(IWeaverSessionPolicyService policy, WeaverSession session)
        {
            var current = await policy.GetAsync(session.Id, session.ActorId, session.TenantId);
            Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, current.Outcome);
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
    private sealed class GatedTurnRegistry(WeaverTurnRegistry inner) : IWeaverTurnRegistry
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
                throw new WeaverContributionException("A replacement registry refused registration.");
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
    private sealed class GatedApplyChangeTool : IWeaverTool
    {
        public ApplyChangeTool Inner { get; } = new();
        public TaskCompletionSource Entered { get; private set; } = Barrier();
        public TaskCompletionSource Release { get; private set; } = Barrier();
        public WeaverToolDescriptor Descriptor => Inner.Descriptor;

        public void Arm()
        {
            Entered = Barrier();
            Release = Barrier();
        }

        public async Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await Inner.PrepareAsync(invocation, cancellationToken);
        }

        public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
            => Inner.ExecuteAsync(invocation, cancellationToken);
    }

    /// <summary>Cancels the foreground turn right after its proposal is inserted, before the pause can be saved.</summary>
    private sealed class CancellingAuditSink : IWeaverAuditSink
    {
        public IWeaverAuditSink Inner { get; set; } = null!;
        public CancellationTokenSource? CancelAfterProposal { get; set; }

        public async Task EmitAsync(WeaverAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await Inner.EmitAsync(auditEvent, cancellationToken);
            if (CancelAfterProposal is { } cancellation && auditEvent.Kind == WeaverAuditEventKind.ToolInvoked
                && auditEvent.Metadata.GetValueOrDefault("outcome") == "proposal-created")
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
        }
    }

    internal sealed class ForwardingSessions(IWeaverSessionService inner) : IWeaverSessionService
    {
        public Task<WeaverSession> CreateAsync(WeaverSessionCreateRequest request, CancellationToken cancellationToken = default) => inner.CreateAsync(request, cancellationToken);
        public Task<WeaverSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default) => inner.FindAsync(sessionId, cancellationToken);
        public Task<WeaverMessage> AddMessageAsync(string sessionId, WeaverMessageCreateRequest request, CancellationToken cancellationToken = default) => inner.AddMessageAsync(sessionId, request, cancellationToken);
        public Task<IReadOnlyCollection<WeaverMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default) => inner.ListMessagesAsync(sessionId, cancellationToken);
        public Task<WeaverMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default) => inner.FindMessageAsync(sessionId, messageId, cancellationToken);
        public Task<WeaverMessage?> FindLatestMessageAsync(string sessionId, WeaverRole? role = null, CancellationToken cancellationToken = default) => inner.FindLatestMessageAsync(sessionId, role, cancellationToken);
        public Task SetMessageStatusAsync(string sessionId, string messageId, WeaverMessageStatus status, CancellationToken cancellationToken = default) => inner.SetMessageStatusAsync(sessionId, messageId, status, cancellationToken);
        public Task AddContextAsync(string sessionId, IReadOnlyCollection<WeaverContextAttachment> attachments, CancellationToken cancellationToken = default) => inner.AddContextAsync(sessionId, attachments, cancellationToken);
        public Task<IReadOnlyCollection<WeaverContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default) => inner.ListContextAsync(sessionId, cancellationToken);
    }

    internal sealed class ForwardingStreaming(IWeaverStreamingService inner) : IWeaverStreamingService
    {
        public IAsyncEnumerable<WeaverStreamEvent> StreamAsync(string sessionId, CancellationToken cancellationToken = default) => inner.StreamAsync(sessionId, cancellationToken);
    }

    internal sealed class ForwardingInvoker(IWeaverToolInvoker inner) : IWeaverToolInvoker
    {
        public Task<WeaverToolDispatch> DispatchAsync(WeaverToolInvocation invocation, WeaverPolicy policy, CancellationToken cancellationToken = default)
            => inner.DispatchAsync(invocation, policy, cancellationToken);
    }

    internal sealed class ForwardingProposals(IWeaverProposalService inner) : IWeaverProposalService
    {
        public Task<WeaverActionProposal> AddAsync(WeaverActionProposal proposal, CancellationToken cancellationToken = default) => inner.AddAsync(proposal, cancellationToken);
        public Task<WeaverActionProposal?> FindAsync(string proposalId, CancellationToken cancellationToken = default) => inner.FindAsync(proposalId, cancellationToken);
        public Task<WeaverResult<WeaverActionProposal>> ApproveAsync(string proposalId, string actorId, string? expectedRevision = null, string? comment = null, CancellationToken cancellationToken = default)
            => inner.ApproveAsync(proposalId, actorId, expectedRevision, comment, cancellationToken);
        public Task<WeaverResult<WeaverActionProposal>> DenyAsync(string proposalId, string actorId, string? reason, CancellationToken cancellationToken = default)
            => inner.DenyAsync(proposalId, actorId, reason, cancellationToken);
        public Task<WeaverResult<WeaverProposalExecutionResult>> ExecuteAsync(string proposalId, string actorId, string? expectedRevision = null, CancellationToken cancellationToken = default)
            => inner.ExecuteAsync(proposalId, actorId, expectedRevision, cancellationToken);
        public Task<WeaverResult<WeaverProposalExecutionResult>> AcknowledgeAsync(string sessionId, string invocationId, string actorId, WeaverClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
            => inner.AcknowledgeAsync(sessionId, invocationId, actorId, acknowledgement, cancellationToken);
        public Task<WeaverToolResult?> FindResultAsync(string proposalId, CancellationToken cancellationToken = default) => inner.FindResultAsync(proposalId, cancellationToken);
    }
}
