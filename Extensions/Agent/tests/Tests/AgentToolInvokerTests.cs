using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Extensions;
using Elsa.Agent.Services;
using Elsa.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

public sealed class AgentToolInvokerTests
{
    [Fact]
    public async Task Read_only_tool_executes_inline_with_its_typed_result()
    {
        using var fixture = await ProposalFixture.CreateAsync(new EchoTool());
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.Executed, dispatch.Outcome);
        Assert.Equal("echo:hi", dispatch.Result?.Content);
        Assert.Equal("test.echo", dispatch.Result?.Payload?.TypeId);
        Assert.Null(dispatch.Result?.Receipt);
        var audited = Assert.Single(await fixture.Audit.ListAsync(fixture.Session.Id), x => x.Kind == AgentAuditEventKind.ToolInvoked);
        Assert.Equal("executed", audited.Metadata["outcome"]);
        Assert.Equal(fixture.Tool.Descriptor.Name, audited.Metadata["toolName"]);
    }

    [Theory]
    [InlineData(AgentAutonomyMode.Manual)]
    [InlineData(AgentAutonomyMode.AutoReadOnly)]
    public async Task Mutating_tool_prepares_an_exact_target_before_review(AgentAutonomyMode autonomy)
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), AgentPolicy.Default with { AutonomyMode = autonomy });
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
        Assert.Equal(AgentActionProposalStatus.AwaitingApproval, dispatch.Proposal?.Status);
        Assert.Equal("test.resource", dispatch.Proposal?.ResourceTarget?.ResourceType);
        Assert.Equal("revision-1", dispatch.Proposal?.BaseRevision);
        Assert.Equal("new", dispatch.Proposal!.PreparedInvocation!.Arguments.GetProperty("value").GetString());
        Assert.Equal(1, fixture.Mutation!.PrepareCalls);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Fact]
    public async Task Malformed_module_preparation_never_creates_a_review_or_effect()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool { Malformed = true });
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal("test.malformed", dispatch.Error?.Code);
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
        Assert.DoesNotContain(await fixture.Audit.ListAsync(fixture.Session.Id), x => x.Kind == AgentAuditEventKind.ProposalCreated);
    }

    [Fact]
    public async Task Server_mutation_under_full_auto_uses_the_same_observed_receipt_path()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto });
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.Executed, dispatch.Outcome);
        Assert.Equal("new", fixture.Mutation!.Value);
        Assert.Equal(fixture.Mutation.Revision, dispatch.Result?.Receipt?.After.Revision);
    }

    [Fact]
    public async Task Client_mutation_requires_explicit_review_even_in_full_auto()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool(), AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto });
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
        Assert.True(dispatch.Proposal!.RequiresApproval);
        Assert.Equal(AgentActionProposalStatus.AwaitingApproval, dispatch.Proposal.Status);
        Assert.Equal(0, fixture.Client!.ExecuteCalls);
    }

    [Fact]
    public async Task A_client_tool_without_its_completion_counterpart_is_refused_before_preparation_or_review()
    {
        var tool = new ProposalClientToolWithoutCompletion(new ProposalClientTool());
        using var fixture = await ProposalFixture.CreateAsync(tool);

        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);

        Assert.Equal(AgentToolDispatchOutcome.Denied, dispatch.Outcome);
        Assert.Equal("agent.tool.client_unavailable", dispatch.Error?.Code);
        Assert.Null(dispatch.Proposal);
        Assert.Equal(0, tool.PrepareCalls);
        Assert.Equal(0, tool.ExecuteCalls);
        Assert.DoesNotContain(await fixture.Audit.ListAsync(fixture.Session.Id), entry => entry.Kind == AgentAuditEventKind.ProposalCreated);
    }

    [Fact]
    public async Task Repeated_mutating_call_returns_its_receipt_without_repreparing_the_changed_resource()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto });
        var invocation = fixture.Invocation();
        var first = await fixture.Invoker.DispatchAsync(invocation, fixture.Session.Policy);
        var repeated = await fixture.Invoker.DispatchAsync(invocation, fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.Executed, repeated.Outcome);
        Assert.Same(first.Result, repeated.Result);
        Assert.Equal(1, fixture.Mutation!.ExecuteCalls);
        Assert.Equal(1, fixture.Mutation.PrepareCalls);
        var conflicting = await fixture.Invoker.DispatchAsync(invocation with { Arguments = new Dictionary<string, object?> { ["value"] = "other" } }, fixture.Session.Policy);
        Assert.Equal("agent.tool.duplicate_conflict", conflicting.Error?.Code);
        Assert.Equal(1, fixture.Mutation.ExecuteCalls);
    }

    [Fact]
    public async Task Receipt_continuation_cannot_authorize_a_second_mutation()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto });
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation() with { AllowMutations = false }, fixture.Session.Policy);
        Assert.Equal("agent.tool.continuation_read_only", dispatch.Error?.Code);
        Assert.Equal(0, fixture.Mutation!.PrepareCalls);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Fact]
    public async Task Additional_distinct_mutator_in_the_bounded_turn_is_explicitly_refused()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var first = await fixture.Invoker.DispatchAsync(fixture.Invocation("first"), fixture.Session.Policy);
        var second = await fixture.Invoker.DispatchAsync(fixture.Invocation("second"), fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, first.Outcome);
        Assert.Equal("agent.tool.turn_mutation_limit", second.Error?.Code);
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
        var proposals = (await fixture.Audit.ListAsync(fixture.Session.Id)).Where(x => x.Kind == AgentAuditEventKind.ProposalCreated);
        Assert.Single(proposals);
    }

    [Fact]
    public async Task A_module_snapshot_binds_client_execution_without_a_global_first_module_binding()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool());
        var invocation = fixture.Invocation() with { Client = null };
        var snapshotBinding = invocation.Context.Single().Client;
        fixture.Client!.UseSnapshotBinding = true;
        var dispatch = await fixture.Invoker.DispatchAsync(invocation, fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
        Assert.Equal(snapshotBinding, dispatch.Proposal?.PreparedInvocation?.Client);
    }

    [Fact]
    public async Task Wrong_actor_or_tenant_never_reaches_module_preparation()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var invocation = fixture.Invocation();
        Assert.Equal("agent.tool.forbidden", (await fixture.Invoker.DispatchAsync(invocation with { ActorId = "other" }, fixture.Session.Policy)).Error?.Code);
        Assert.Equal("agent.tool.forbidden", (await fixture.Invoker.DispatchAsync(invocation with { TenantId = "other" }, fixture.Session.Policy)).Error?.Code);
        Assert.Equal(0, fixture.Mutation!.PrepareCalls);
    }

    [Fact]
    public async Task Current_denial_blocks_effects_and_review_creation()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        fixture.Authority.Allowed = false;
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal("agent.tool.forbidden", dispatch.Error?.Code);
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
        Assert.DoesNotContain(await fixture.Audit.ListAsync(fixture.Session.Id), x => x.Kind == AgentAuditEventKind.ProposalCreated);
    }

    [Fact]
    public async Task Unknown_tool_is_explicitly_denied()
    {
        using var fixture = await ProposalFixture.CreateAsync(new EchoTool());
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation() with { ToolName = "absent" }, fixture.Session.Policy);
        Assert.Equal("agent.tool.not_found", dispatch.Error?.Code);
    }

    [Fact]
    public async Task Read_exception_is_a_refusal_without_exposing_infrastructure_details()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ThrowingTool());
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal("agent.tool.invocation_failed", dispatch.Error?.Code);
        Assert.DoesNotContain("tool exploded", dispatch.Error?.Message);
    }

    [Fact]
    public async Task Generic_defaults_fail_closed_without_a_current_authorizer()
    {
        var authorizer = new UnavailableAgentToolAuthorizer();
        var result = await authorizer.AuthorizeAsync(new EchoTool().Descriptor, "actor-1", "tenant-1", null);
        Assert.False(result.Allowed);
        Assert.Equal("agent.authority.unavailable", Assert.Single(result.Violations).Code);
    }

    // ---- #3440 T018: preparation-to-proposal, approved dispatch, client ACK and receipt continuation handoffs ----
    // Real default invoker, proposal service and session owner; barriers sit only in module/audit seams.

    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(30);
    private static readonly AgentPolicy FullAuto = AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto };

    [Theory]
    [InlineData("call-gate-during-preparation")]
    [InlineData("call-gate-and-proposal-semaphore-during-automatic-dispatch")]
    [InlineData("proposal-semaphore-during-client-acknowledgement")]
    public async Task Policy_reads_and_updates_complete_while_a_call_or_proposal_semaphore_is_held(string hold)
    {
        var mutation = new ProposalMutationTool { Started = hold.Contains("dispatch") ? HandoffBarrier() : null, Release = hold.Contains("dispatch") ? HandoffBarrier() : null };
        var client = new ProposalClientTool { CompleteStarted = HandoffBarrier(), CompleteRelease = HandoffBarrier() };
        var prepare = new PrepareGatedTool(mutation);
        IAgentTool tool = hold switch { "call-gate-during-preparation" => prepare, "proposal-semaphore-during-client-acknowledgement" => client, _ => mutation };
        using var fixture = await ProposalFixture.CreateAsync(tool, hold.Contains("dispatch") ? FullAuto : null);
        var policy = fixture.Services.GetRequiredService<IAgentSessionPolicyService>();
        var session = fixture.Session;
        Task<AgentToolDispatch>? held = null;
        Task<AgentToolDispatch>? duplicate = null;
        Task<AgentResult<AgentProposalExecutionResult>>? heldAcknowledgement = null;
        Task<AgentResult<AgentProposalExecutionResult>>? duplicateAcknowledgement = null;
        AgentActionProposal? proposal = null;
        switch (hold)
        {
            case "call-gate-during-preparation":
                held = Task.Run(() => fixture.Invoker.DispatchAsync(fixture.Invocation(), session.Policy));
                await prepare.Entered.Task.WaitAsync(HandoffTimeout);
                duplicate = Task.Run(() => fixture.Invoker.DispatchAsync(fixture.Invocation(), session.Policy));
                break;
            case "call-gate-and-proposal-semaphore-during-automatic-dispatch":
                held = Task.Run(() => fixture.Invoker.DispatchAsync(fixture.Invocation(), session.Policy));
                await mutation.Started!.Task.WaitAsync(HandoffTimeout);
                duplicate = Task.Run(() => fixture.Invoker.DispatchAsync(fixture.Invocation(), session.Policy));
                break;
            default:
                proposal = await fixture.ProposeAsync();
                await fixture.ApproveAsync(proposal);
                Assert.Equal("awaiting-client", (await fixture.ExecuteAsync(proposal)).Value?.Status);
                heldAcknowledgement = Task.Run(() => fixture.AcknowledgeAsync(proposal));
                await client.CompleteStarted!.Task.WaitAsync(HandoffTimeout);
                duplicateAcknowledgement = Task.Run(() => fixture.AcknowledgeAsync(proposal));
                break;
        }

        // No session monitor spans the semaphore wait: the owner answers immediately, and the work owns the policy.
        var readback = await policy.GetAsync(session.Id, session.ActorId, session.TenantId).WaitAsync(HandoffTimeout);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, readback.Outcome);
        var update = await policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, AgentAutonomyMode.Manual, readback.State!.Revision)).WaitAsync(HandoffTimeout);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, update.Outcome);
        Assert.False((duplicate ?? (Task)duplicateAcknowledgement!).IsCompleted);

        prepare.Release.TrySetResult();
        mutation.Release?.TrySetResult();
        client.CompleteRelease!.TrySetResult();
        if (held is not null)
        {
            var first = await held.WaitAsync(HandoffTimeout);
            var repeated = await duplicate!.WaitAsync(HandoffTimeout);
            Assert.Equal(first.Outcome, repeated.Outcome);
            // The waiting duplicate reused the one prepared operation instead of preparing another.
            Assert.Single(fixture.Services.GetRequiredService<InMemoryAgentProposalStore>().Proposals.Values);
            Assert.Equal(1, mutation.PrepareCalls);
            if (first.Outcome == AgentToolDispatchOutcome.Executed)
            {
                Assert.Equal(first.Result!.Receipt!.InvocationId, repeated.Result!.Receipt!.InvocationId);
                Assert.Equal(1, mutation.DispatchCalls);
                Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(policy, session));
            }
            else
            {
                Assert.Equal(first.Proposal!.Id, repeated.Proposal!.Id);
                Assert.Equal(AgentSessionPolicyOutcome.Busy, await ProbeAsync(policy, session));
                Assert.Equal(AgentActionProposalStatus.Denied, (await fixture.Proposals.DenyAsync(first.Proposal.Id, ProposalFixture.Actor, "No")).Value?.Status);
                Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(policy, session));
                Assert.Equal(0, mutation.DispatchCalls);
            }
        }
        else
        {
            var accepted = await heldAcknowledgement!.WaitAsync(HandoffTimeout);
            Assert.True(accepted.Value!.Executed);
            Assert.Same(accepted.Value, (await duplicateAcknowledgement!.WaitAsync(HandoffTimeout)).Value);
            Assert.Equal(1, client.CompleteCalls);
            Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(policy, session));
        }
    }

    [Fact]
    public async Task The_preparation_claim_hands_off_to_the_proposal_claim_without_an_idle_gap()
    {
        var sink = new HookedAuditSink();
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), null, services =>
            services.Replace(ServiceDescriptor.Singleton<IAgentAuditSink>(sp => sink.Bind(sp.GetRequiredService<InMemoryAgentAuditStore>()))));
        var policy = fixture.Services.GetRequiredService<IAgentSessionPolicyService>();
        var created = sink.HoldNext(AgentAuditEventKind.ProposalCreated);

        var dispatch = Task.Run(() => fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy));
        await created.Entered.Task.WaitAsync(HandoffTimeout);
        // Both the preparation claim and the inserted proposal's claim exist at this point.
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await ProbeAsync(policy, fixture.Session));
        created.Release.SetResult();
        var proposal = (await dispatch.WaitAsync(HandoffTimeout)).Proposal!;

        // The preparation claim is released; the proposal claim alone still covers the reviewed work.
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await ProbeAsync(policy, fixture.Session));
        Assert.Equal(AgentActionProposalStatus.Denied, (await fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "No")).Value?.Status);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(policy, fixture.Session));
    }

    [Fact]
    public async Task An_uncertain_direct_dispatch_is_retained_and_never_authorizes_a_replay_or_another_mutation()
    {
        var tool = new ProposalMutationTool { Started = HandoffBarrier(), Release = HandoffBarrier() };
        using var fixture = await ProposalFixture.CreateAsync(tool, FullAuto);
        var policy = fixture.Services.GetRequiredService<IAgentSessionPolicyService>();
        using var cancellation = new CancellationTokenSource();

        var dispatch = Task.Run(() => fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy, cancellation.Token));
        await tool.Started!.Task.WaitAsync(HandoffTimeout);
        cancellation.Cancel();
        var interrupted = await dispatch.WaitAsync(HandoffTimeout);

        Assert.False(interrupted.Result!.Succeeded);
        Assert.Null(interrupted.Result.Receipt);
        var proposal = Assert.Single(fixture.Services.GetRequiredService<InMemoryAgentProposalStore>().Proposals.Values);
        Assert.Equal(AgentActionProposalStatus.Uncertain, proposal.Status);
        // A server-scope uncertainty no longer depends on policy; every claim of this dispatch is released.
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(policy, fixture.Session));

        var repeated = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(interrupted.Result.Access!.InvocationId, repeated.Result!.Access!.InvocationId);
        Assert.False(repeated.Result.Succeeded);
        Assert.Equal("uncertain", (await fixture.ExecuteAsync(proposal)).Value?.Status);
        Assert.Equal(1, tool.PrepareCalls);
        // Another call in the same turn cannot acquire a second mutation to resolve the uncertainty.
        Assert.Equal("agent.tool.turn_mutation_limit", (await fixture.Invoker.DispatchAsync(fixture.Invocation("call-2"), fixture.Session.Policy)).Error?.Code);
        Assert.Equal(1, tool.DispatchCalls);
        Assert.Single(fixture.Services.GetRequiredService<InMemoryAgentProposalStore>().Proposals.Values);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(policy, fixture.Session));
    }

    [Fact]
    public async Task An_accepted_receipt_survives_a_failure_after_the_effect_and_the_same_call_recovers_it_without_dispatch()
    {
        var sink = new HookedAuditSink();
        var tool = new ProposalMutationTool();
        using var fixture = await ProposalFixture.CreateAsync(tool, FullAuto, services =>
            services.Replace(ServiceDescriptor.Singleton<IAgentAuditSink>(sp => sink.Bind(sp.GetRequiredService<InMemoryAgentAuditStore>()))));
        var policy = fixture.Services.GetRequiredService<IAgentSessionPolicyService>();
        sink.OnNext(AgentAuditEventKind.ToolInvoked, (_, _) => throw new InvalidOperationException("The audit sink failed after the effect."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy));

        // The failure cannot leak the preparation claim, and the settled receipt holds no proposal claim.
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(policy, fixture.Session));
        var proposal = Assert.Single(fixture.Services.GetRequiredService<InMemoryAgentProposalStore>().Proposals.Values);
        Assert.Equal(AgentActionProposalStatus.Executed, proposal.Status);
        var recovered = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.Executed, recovered.Outcome);
        Assert.Equal(proposal.PreparedInvocation!.InvocationId, recovered.Result!.Receipt!.InvocationId);
        Assert.Same((await fixture.Proposals.FindResultAsync(proposal.Id))!.Receipt, recovered.Result.Receipt);
        Assert.Equal(1, tool.DispatchCalls);
        Assert.Equal(1, tool.PrepareCalls);
        Assert.Equal("new", tool.Value);
    }

    [Fact]
    public async Task An_unresolved_client_acknowledgement_keeps_the_pause_and_a_late_receipt_continues_the_originating_turn_once()
    {
        var tool = new ProposalClientTool();
        tool.Descriptor = tool.Descriptor with { Name = DeterministicAgentProvider.MutatingToolName };
        var target = new AgentResourceTarget("test.module", "test.editor", "editor-1", "revision-1");
        var binding = new AgentClientBinding("client-1", "generation-1");
        var snapshot = new AgentContextAttachment("client-snapshot", "test", "editor-1", "Editor", "test.editor", AgentContextSensitivity.Internal,
            "selection", "Bound editor", null, new Dictionary<string, string>())
        { ModuleId = "test.module", Origin = AgentContextOrigin.ClientSnapshot, Client = binding, Target = target };
        var source = new AgentTestContextSource(AgentTestContextSource.TestModuleSourceId, "test.module", [snapshot], [tool]);
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddFoundationAgentAbstractions();
        services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer, AllowTestAgentToolAuthorizer>());
        services.AddSingleton<IAgentContextProvider>(source);
        services.AddSingleton<IAgentTool>(tool);
        services.AddSingleton<IAgentProvider, DeterministicAgentProvider>();
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var streaming = scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        var proposals = scope.ServiceProvider.GetRequiredService<IAgentProposalService>();
        var policy = scope.ServiceProvider.GetRequiredService<IAgentSessionPolicyService>();
        var owner = (InMemoryAgentSessionService)sessions;
        var session = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", DeterministicAgentProvider.Id, "explain", null, FullAuto, new Dictionary<string, string>()));
        var collected = await scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(session.Policy,
            new(session.Id, source.ScopeKind, new Dictionary<string, string>()) { CallerAttachments = [snapshot] });
        Assert.True(collected.Succeeded, collected.Error?.Message);
        var context = collected.Value!.Attachments;
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "mutate the resource", AgentMessageStatus.Pending, null,
            context.Select(item => item.Id).ToArray(), context) { Client = binding });
        var first = await Collect(streaming.StreamAsync(session.Id));
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        Assert.True((await proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.Equal("awaiting-client", (await proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value?.Status);
        var pause = owner.FindPause(session.Id);
        Assert.NotNull(pause);

        // The client has not acknowledged. A reconnect cannot continue, replay or release the retained ownership.
        var early = await Collect(streaming.StreamAsync(session.Id));
        Assert.Equal("agent.turn.awaiting_review", early[^1].Error?.Code);
        Assert.Same(pause, owner.FindPause(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await ProbeAsync(policy, session));
        Assert.Equal(AgentActionProposalStatus.AwaitingClient, (await proposals.FindAsync(proposal.Id))!.Status);

        var prepared = proposal.PreparedInvocation!;
        var acknowledgement = new AgentClientAcknowledgement(prepared.Client!, prepared.ApprovalDigest, AgentClientOutcome.Applied, "The client observed the edit.",
            target with { Revision = "revision-2" }, new("test.client-change", "1", JsonSerializer.SerializeToElement(new { observed = true, value = "new", revision = "revision-2" })));
        var accepted = await proposals.AcknowledgeAsync(session.Id, prepared.InvocationId, session.ActorId, acknowledgement);
        Assert.True(accepted.Value!.Executed);
        // The receipt settled the proposal; the pause still covers the originating turn until it consumes the receipt.
        Assert.Same(pause, owner.FindPause(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await ProbeAsync(policy, session));

        var resumed = await Collect(streaming.StreamAsync(session.Id));
        Assert.Equal(AgentStreamEventKind.Completed, resumed[^1].Kind);
        Assert.Contains(resumed, x => x.Kind == AgentStreamEventKind.MessageDelta && x.Content!.Contains(accepted.Value.ToolResult!.Content, StringComparison.Ordinal));
        var recorded = Assert.Single(await sessions.ListMessagesAsync(session.Id), x => x.Role == AgentRole.Tool);
        Assert.Equal(prepared.InvocationId, Assert.Single(recorded.ToolResults).Receipt!.InvocationId);
        Assert.Null(owner.FindPause(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(policy, session));
        // A duplicate acknowledgement returns the retained receipt; no continuation is admitted again.
        Assert.Same(accepted.Value, (await proposals.AcknowledgeAsync(session.Id, prepared.InvocationId, session.ActorId, acknowledgement)).Value);
        Assert.Equal("agent.message.no_pending", (await Collect(streaming.StreamAsync(session.Id)))[^1].Error?.Code);
        Assert.Equal(1, tool.CompleteCalls);
        Assert.Equal(0, tool.ExecuteCalls);
    }

    private static async Task<AgentSessionPolicyOutcome> ProbeAsync(IAgentSessionPolicyService policy, AgentSession session)
    {
        var current = await policy.GetAsync(session.Id, session.ActorId, session.TenantId);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, current.Outcome);
        return (await policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, current.State!.Policy.AutonomyMode, current.State.Revision))).Outcome;
    }

    private static async Task<List<AgentStreamEvent>> Collect(IAsyncEnumerable<AgentStreamEvent> events)
    {
        var result = new List<AgentStreamEvent>();
        await foreach (var item in events)
            result.Add(item);
        return result;
    }

    private static TaskCompletionSource HandoffBarrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Forwards to a real module; its next preparation is held inside the call gate.</summary>
    private sealed class PrepareGatedTool(IAgentTool inner) : IAgentTool
    {
        private int _held;
        public TaskCompletionSource Entered { get; } = HandoffBarrier();
        public TaskCompletionSource Release { get; } = HandoffBarrier();
        public AgentToolDescriptor Descriptor => inner.Descriptor;

        public async Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _held, 1) == 0)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await inner.PrepareAsync(invocation, cancellationToken);
        }

        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
            => inner.ExecuteAsync(invocation, cancellationToken);
    }
}
