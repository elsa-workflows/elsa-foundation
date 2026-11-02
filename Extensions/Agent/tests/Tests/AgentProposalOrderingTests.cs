using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Elsa.Agent.Tests.AgentProposalTests;

namespace Elsa.Agent.Tests;

/// <summary>
/// T014: controlled orderings of competing proposal transitions on the real default owners. Barriers sit
/// only in the audit sink, the host authorizer and the test module; none re-implements the proposal or
/// session owner. Each losing transition has an explicit correlated disposition.
/// </summary>
public sealed class AgentProposalOrderingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly AgentPolicy FullAuto = AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto };

    // ---- Approve vs deny ----

    [Fact]
    public async Task Approval_held_then_denial_serializes_and_the_pre_dispatch_denial_closes_execution()
    {
        var (fixture, sink) = await CreateAsync(new ProposalMutationTool());
        using var _ = fixture;
        var proposal = await fixture.ProposeAsync();
        var held = sink.HoldNext(AgentAuditEventKind.ProposalApproved);

        var approve = fixture.ApproveAsync(proposal);
        await held.Entered.Task.WaitAsync(Timeout);
        var deny = fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "Changed my mind");
        Assert.False(deny.IsCompleted);
        held.Release.SetResult();

        Assert.Equal(AgentActionProposalStatus.Approved, (await approve).Value?.Status);
        Assert.Equal(AgentActionProposalStatus.Denied, (await deny).Value?.Status);
        var execute = await fixture.ExecuteAsync(proposal);
        Assert.Equal("agent.proposal.closed", execute.Error?.Code);
        Assert.Equal(409, execute.Error?.StatusCode);
        Assert.Equal(0, fixture.Mutation!.DispatchCalls);
        AssertLifecycle(await fixture.Proposals.FindResultAsync(proposal.Id), proposal, "denied");
        await AssertDecisionsAsync(fixture, proposal, approved: 1, denied: 1);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    [Fact]
    public async Task Denial_held_then_approval_refuses_the_late_approval_explicitly()
    {
        var (fixture, sink) = await CreateAsync(new ProposalMutationTool());
        using var _ = fixture;
        var proposal = await fixture.ProposeAsync();
        var held = sink.HoldNext(AgentAuditEventKind.ProposalDenied);

        var deny = fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "No");
        await held.Entered.Task.WaitAsync(Timeout);
        var approve = fixture.ApproveAsync(proposal);
        Assert.False(approve.IsCompleted);
        held.Release.SetResult();

        Assert.Equal(AgentActionProposalStatus.Denied, (await deny).Value?.Status);
        var late = await approve;
        Assert.Equal("agent.proposal.invalid_state", late.Error?.Code);
        Assert.Equal(409, late.Error?.StatusCode);
        Assert.Equal("agent.proposal.closed", (await fixture.ExecuteAsync(proposal)).Error?.Code);
        Assert.Equal(0, fixture.Mutation!.DispatchCalls);
        AssertLifecycle(await fixture.Proposals.FindResultAsync(proposal.Id), proposal, "denied");
        await AssertDecisionsAsync(fixture, proposal, approved: 0, denied: 1);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    [Fact]
    public async Task Duplicate_concurrent_approvals_record_one_decision()
    {
        var (fixture, sink) = await CreateAsync(new ProposalMutationTool());
        using var _ = fixture;
        var proposal = await fixture.ProposeAsync();
        var held = sink.HoldNext(AgentAuditEventKind.ProposalApproved);

        var first = fixture.ApproveAsync(proposal);
        await held.Entered.Task.WaitAsync(Timeout);
        var duplicate = fixture.ApproveAsync(proposal);
        Assert.False(duplicate.IsCompleted);
        held.Release.SetResult();

        var original = (await first).Value!;
        var repeated = (await duplicate).Value!;
        Assert.Equal(AgentActionProposalStatus.Approved, repeated.Status);
        Assert.Equal(original.ApprovedAt, repeated.ApprovedAt);
        Assert.Equal(original.ApprovedBy, repeated.ApprovedBy);
        await AssertDecisionsAsync(fixture, proposal, approved: 1, denied: 0);
        // The policy stays owned until the reviewed operation settles.
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await ProbeAsync(fixture));
    }

    // ---- Execute vs deny ----

    [Fact]
    public async Task Execution_in_flight_then_denial_refuses_the_late_denial_and_keeps_the_single_receipt()
    {
        var started = Barrier();
        var release = Barrier();
        var (fixture, _) = await CreateAsync(new ProposalMutationTool { Started = started, Release = release });
        using var __ = fixture;
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);

        var execute = fixture.ExecuteAsync(proposal);
        await started.Task.WaitAsync(Timeout);
        var deny = fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "Too late");
        Assert.False(deny.IsCompleted);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await ProbeAsync(fixture));
        release.SetResult();

        var applied = await execute;
        Assert.True(applied.Value?.Executed);
        var late = await deny;
        Assert.Equal("agent.proposal.closed", late.Error?.Code);
        Assert.Equal(409, late.Error?.StatusCode);
        Assert.Equal(1, fixture.Mutation!.ExecuteCalls);
        Assert.Equal(AgentActionProposalStatus.Executed, (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.Same(applied.Value!.ToolResult, await fixture.Proposals.FindResultAsync(proposal.Id));
        AssertReceipt(applied.Value.ToolResult, proposal);
        Assert.Same(applied.Value, (await fixture.ExecuteAsync(proposal)).Value);
        Assert.Equal(1, fixture.Mutation.DispatchCalls);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    [Fact]
    public async Task Denial_held_then_execution_refuses_the_late_execution_with_zero_effect()
    {
        var (fixture, sink) = await CreateAsync(new ProposalMutationTool());
        using var _ = fixture;
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var held = sink.HoldNext(AgentAuditEventKind.ProposalDenied);

        var deny = fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "Stop");
        await held.Entered.Task.WaitAsync(Timeout);
        var execute = fixture.ExecuteAsync(proposal);
        Assert.False(execute.IsCompleted);
        held.Release.SetResult();

        Assert.Equal(AgentActionProposalStatus.Denied, (await deny).Value?.Status);
        Assert.Equal("agent.proposal.closed", (await execute).Error?.Code);
        Assert.Equal(0, fixture.Mutation!.DispatchCalls);
        Assert.Equal("initial", fixture.Mutation.Value);
        AssertLifecycle(await fixture.Proposals.FindResultAsync(proposal.Id), proposal, "denied");
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    // ---- Execute vs cancel ----

    [Fact]
    public async Task Cancellation_during_the_pre_dispatch_authority_recheck_is_cancelled_with_zero_effect()
    {
        var (fixture, _) = await CreateAsync(new ProposalMutationTool());
        using var __ = fixture;
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var entered = Barrier();
        fixture.Authority.HoldNextDecision(async token =>
        {
            entered.TrySetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
        });
        using var cancellation = new CancellationTokenSource();

        var execute = fixture.Proposals.ExecuteAsync(proposal.Id, ProposalFixture.Actor, proposal.BaseRevision, cancellation.Token);
        await entered.Task.WaitAsync(Timeout);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await ProbeAsync(fixture));
        cancellation.Cancel();
        var cancelled = await execute;

        // The owning module was never dispatched, so this is a known zero-effect cancellation, not uncertainty.
        Assert.False(cancelled.Succeeded);
        Assert.Equal("agent.proposal.cancelled", cancelled.Error?.Code);
        Assert.Equal(409, cancelled.Error?.StatusCode);
        Assert.Equal(0, fixture.Mutation!.DispatchCalls);
        Assert.Equal("initial", fixture.Mutation.Value);
        Assert.Equal(AgentActionProposalStatus.Cancelled, (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
        AssertLifecycle(await fixture.Proposals.FindResultAsync(proposal.Id), proposal, "refused");
        // A repeated execution returns the retained correlated cancellation; it never redispatches.
        Assert.Equal("agent.proposal.cancelled", (await fixture.ExecuteAsync(proposal)).Error?.Code);
        Assert.Equal(0, fixture.Mutation.DispatchCalls);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    [Fact]
    public async Task A_module_returning_the_cancellation_code_after_dispatch_is_an_ordinary_refusal()
    {
        // Review round 1: the public failure code alone must not classify a post-dispatch module outcome as
        // the engine's zero-effect "cancelled before dispatch".
        var tool = new ProposalMutationTool { RefuseAfterDispatchWith = DefaultAgentActionProposalExecutor.CancelledBeforeDispatchCode };
        var (fixture, _) = await CreateAsync(tool);
        using var __ = fixture;
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);

        var refused = await fixture.ExecuteAsync(proposal);

        Assert.False(refused.Succeeded);
        Assert.Equal(1, tool.DispatchCalls);
        Assert.Equal(AgentActionProposalStatus.Failed, (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
        var lifecycle = await fixture.Proposals.FindResultAsync(proposal.Id);
        AssertLifecycle(lifecycle, proposal, "refused");
        Assert.Equal("The owning executor refused application before reporting an effect.", lifecycle!.Content);
        Assert.DoesNotContain("cancelled before dispatch", lifecycle.Content, StringComparison.OrdinalIgnoreCase);
        // A refusal settles: the claim is released and the retained refusal is never redispatched.
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
        await fixture.ExecuteAsync(proposal);
        Assert.Equal(1, tool.DispatchCalls);
    }

    [Fact]
    public async Task Cancellation_after_module_dispatch_is_uncertain_and_never_replayed()
    {
        var started = Barrier();
        var release = Barrier();
        var (fixture, _) = await CreateAsync(new ProposalMutationTool { Started = started, Release = release });
        using var __ = fixture;
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        using var cancellation = new CancellationTokenSource();

        var execute = fixture.Proposals.ExecuteAsync(proposal.Id, ProposalFixture.Actor, proposal.BaseRevision, cancellation.Token);
        await started.Task.WaitAsync(Timeout);
        cancellation.Cancel();
        var interrupted = await execute;

        Assert.True(interrupted.Succeeded);
        Assert.Equal("uncertain", interrupted.Value?.Status);
        Assert.False(interrupted.Value!.Executed);
        AssertLifecycle(interrupted.Value.ToolResult, proposal, "uncertain");
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.Same(interrupted.Value, (await fixture.ExecuteAsync(proposal)).Value);
        Assert.Equal("agent.proposal.closed", (await fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "Late")).Error?.Code);
        Assert.Equal(1, fixture.Mutation!.DispatchCalls);
        // A server-scope uncertainty no longer depends on policy, so its claim is released.
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    // ---- Client acknowledgement ----

    [Fact]
    public async Task Duplicate_concurrent_acknowledgements_admit_one_receipt_and_refuse_a_conflict()
    {
        var started = Barrier();
        var release = Barrier();
        var (fixture, _) = await CreateAsync(new ProposalClientTool { CompleteStarted = started, CompleteRelease = release });
        using var __ = fixture;
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        Assert.Equal("awaiting-client", (await fixture.ExecuteAsync(proposal)).Value?.Status);
        var acknowledgement = fixture.Acknowledgement(proposal);

        var first = fixture.AcknowledgeAsync(proposal, acknowledgement);
        await started.Task.WaitAsync(Timeout);
        var duplicate = fixture.AcknowledgeAsync(proposal, acknowledgement);
        var conflicting = fixture.AcknowledgeAsync(proposal, acknowledgement with { Summary = "A different report" });
        Assert.False(duplicate.IsCompleted);
        Assert.False(conflicting.IsCompleted);
        release.SetResult();

        var accepted = await first;
        Assert.True(accepted.Value?.Executed);
        Assert.Same(accepted.Value, (await duplicate).Value);
        Assert.Equal("agent.invocation.acknowledgement_conflict", (await conflicting).Error?.Code);
        Assert.Equal(1, fixture.Client!.CompleteCalls);
        Assert.Equal(0, fixture.Client.ExecuteCalls);
        Assert.Same(accepted.Value!.ToolResult, await fixture.Proposals.FindResultAsync(proposal.Id));
        AssertReceipt(accepted.Value.ToolResult, proposal);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    [Fact]
    public async Task Acknowledgement_held_then_denial_refuses_the_late_denial_without_replacing_the_receipt()
    {
        var started = Barrier();
        var release = Barrier();
        var (fixture, _) = await CreateAsync(new ProposalClientTool { CompleteStarted = started, CompleteRelease = release });
        using var __ = fixture;
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        await fixture.ExecuteAsync(proposal);

        var acknowledge = fixture.AcknowledgeAsync(proposal);
        await started.Task.WaitAsync(Timeout);
        var deny = fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "Cancel");
        Assert.False(deny.IsCompleted);
        release.SetResult();

        var accepted = await acknowledge;
        Assert.True(accepted.Value?.Executed);
        Assert.Equal("agent.proposal.closed", (await deny).Error?.Code);
        Assert.Equal(AgentActionProposalStatus.Executed, (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.Same(accepted.Value!.ToolResult, await fixture.Proposals.FindResultAsync(proposal.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    [Fact]
    public async Task A_late_acknowledgement_after_denied_dispatch_reconciles_once_and_conflicts_cannot_replace_it()
    {
        var (fixture, _) = await CreateAsync(new ProposalClientTool());
        using var __ = fixture;
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var dispatched = await fixture.ExecuteAsync(proposal);
        Assert.Equal(proposal.PreparedInvocation!.InvocationId, dispatched.Value!.ClientInvocation!.InvocationId);

        var denied = await fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "Stop");
        Assert.Equal(AgentActionProposalStatus.Uncertain, denied.Value?.Status);
        AssertLifecycle(await fixture.Proposals.FindResultAsync(proposal.Id), proposal, "uncertain");
        // Client-scope uncertainty keeps the policy owned while a truthful receipt may still arrive.
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await ProbeAsync(fixture));
        var retry = await fixture.ExecuteAsync(proposal);
        Assert.Equal("uncertain", retry.Value?.Status);
        Assert.Null(retry.Value!.ClientInvocation);

        var reconciled = await fixture.AcknowledgeAsync(proposal);
        var conflicting = await fixture.AcknowledgeAsync(proposal, fixture.Acknowledgement(proposal) with { Outcome = AgentClientOutcome.Refused });
        var duplicateDenial = await fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "Again");

        Assert.True(reconciled.Value?.Executed);
        AssertReceipt(reconciled.Value!.ToolResult, proposal);
        Assert.Equal("agent.invocation.acknowledgement_conflict", conflicting.Error?.Code);
        Assert.Equal("agent.proposal.closed", duplicateDenial.Error?.Code);
        var executed = await fixture.ExecuteAsync(proposal);
        Assert.Same(reconciled.Value, executed.Value);
        Assert.Null(executed.Value!.ClientInvocation);
        Assert.Same(reconciled.Value.ToolResult, await fixture.Proposals.FindResultAsync(proposal.Id));
        Assert.Equal(1, fixture.Client!.CompleteCalls);
        Assert.Equal(0, fixture.Client.ExecuteCalls);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await ProbeAsync(fixture));
    }

    // ---- Originating-turn orderings ----

    [Fact]
    public async Task A_denial_winning_over_a_late_approval_returns_its_refusal_to_the_originating_turn()
    {
        var sink = new HookedAuditSink();
        var tool = new ApplyChangeTool();
        using var turn = new TurnFixture(sink, null, tool);
        var session = await turn.CreateAsync();
        await turn.PostAsync(session, "mutate the resource");
        var first = await Collect(turn.Streaming.StreamAsync(session.Id));
        var turnId = Assert.IsType<AgentTurnStarted>(Assert.Single(first, x => x.Kind == AgentStreamEventKind.Started).Payload).TurnId;
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        Assert.Equal(turnId, proposal.PreparedInvocation!.TurnId);
        var held = sink.HoldNext(AgentAuditEventKind.ProposalDenied);

        var deny = turn.Proposals.DenyAsync(proposal.Id, session.ActorId, "No");
        await held.Entered.Task.WaitAsync(Timeout);
        var approve = turn.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        held.Release.SetResult();
        Assert.Equal(AgentActionProposalStatus.Denied, (await deny).Value?.Status);
        Assert.Equal("agent.proposal.invalid_state", (await approve).Error?.Code);
        Assert.Equal("agent.proposal.closed", (await turn.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Error?.Code);
        // The pause still owns the policy until its continuation consumes the outcome.
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await turn.ProbeAsync(session));

        var resumed = await Collect(turn.Streaming.StreamAsync(session.Id));

        Assert.Equal(turnId, Assert.IsType<AgentTurnStarted>(Assert.Single(resumed, x => x.Kind == AgentStreamEventKind.Started).Payload).TurnId);
        Assert.Contains(resumed, x => x.Kind == AgentStreamEventKind.MessageDelta && x.Content!.Contains("denied", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(AgentStreamEventKind.Completed, resumed[^1].Kind);
        var recorded = await RecordedResultAsync(turn, session, proposal);
        AssertLifecycle(recorded, proposal, "denied");
        Assert.Equal(proposal.PreparedInvocation.InvocationId, recorded.Access!.InvocationId);
        Assert.Equal(0, tool.Invocations);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await turn.ProbeAsync(session));
    }

    [Fact]
    public async Task Turn_cancellation_before_automatic_dispatch_cancels_the_operation_with_zero_effect()
    {
        var sink = new HookedAuditSink();
        var authority = new GatedAuthorizer();
        var tool = new ApplyChangeTool();
        using var turn = new TurnFixture(sink, authority, tool);
        var session = await turn.CreateAsync(FullAuto);
        await turn.PostAsync(session, "mutate the resource");
        var entered = Barrier();
        // Hold the execution-time authority recheck of the inserted automatic operation.
        sink.OnNext(AgentAuditEventKind.ProposalCreated, (_, _) =>
        {
            authority.HoldNext(async token =>
            {
                entered.TrySetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            });
            return Task.CompletedTask;
        });
        using var cancellation = new CancellationTokenSource();

        var streaming = Task.Run(() => Collect(turn.Streaming.StreamAsync(session.Id, cancellation.Token)));
        await entered.Task.WaitAsync(Timeout);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await turn.ProbeAsync(session));
        cancellation.Cancel();
        var events = await streaming.WaitAsync(Timeout);

        Assert.Equal(AgentStreamEventKind.TurnCancelled, events[^1].Kind);
        Assert.DoesNotContain(events, x => x.Kind == AgentStreamEventKind.Completed);
        Assert.Equal(0, tool.Invocations);
        var proposal = Assert.Single(turn.ProposalStore.Proposals.Values);
        Assert.Equal(AgentActionProposalStatus.Cancelled, proposal.Status);
        AssertLifecycle(await turn.Proposals.FindResultAsync(proposal.Id), proposal, "refused");
        // No uncertain or applied observation was invented for the transcript.
        Assert.DoesNotContain(await turn.Sessions.ListMessagesAsync(session.Id), x => x.Role == AgentRole.Tool);
        Assert.Equal(AgentMessageStatus.Cancelled, Assert.Single(await turn.Sessions.ListMessagesAsync(session.Id), x => x.Role == AgentRole.User).Status);
        Assert.Equal("agent.proposal.cancelled", (await turn.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Error?.Code);
        Assert.Equal(0, tool.Invocations);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await turn.ProbeAsync(session));
    }

    [Fact]
    public async Task Turn_cancellation_after_automatic_dispatch_is_uncertain_once_and_never_replayed()
    {
        var tool = new HeldApplyChangeTool();
        using var turn = new TurnFixture(new HookedAuditSink(), null, tool);
        var session = await turn.CreateAsync(FullAuto);
        await turn.PostAsync(session, "mutate the resource");
        using var cancellation = new CancellationTokenSource();

        var streaming = Task.Run(() => Collect(turn.Streaming.StreamAsync(session.Id, cancellation.Token)));
        await tool.Applied.Task.WaitAsync(Timeout);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await turn.ProbeAsync(session));
        cancellation.Cancel();
        var events = await streaming.WaitAsync(Timeout);

        Assert.Equal(AgentStreamEventKind.TurnCancelled, events[^1].Kind);
        Assert.Equal(1, tool.Inner.Invocations);
        var proposal = Assert.Single(turn.ProposalStore.Proposals.Values);
        Assert.Equal(AgentActionProposalStatus.Uncertain, proposal.Status);
        var retained = await turn.Proposals.FindResultAsync(proposal.Id);
        AssertLifecycle(retained, proposal, "uncertain");
        // The uncertainty is recorded for the originating call; it is never relabelled as applied.
        var recorded = await RecordedResultAsync(turn, session, proposal);
        AssertLifecycle(recorded, proposal, "uncertain");
        var repeated = await turn.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        Assert.Equal("uncertain", repeated.Value?.Status);
        Assert.Same(retained, repeated.Value!.ToolResult);
        Assert.Equal(1, tool.Inner.Invocations);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await turn.ProbeAsync(session));
    }

    // ---- Helpers ----

    private static async Task<(ProposalFixture Fixture, HookedAuditSink Sink)> CreateAsync(IAgentTool tool, AgentPolicy? policy = null)
    {
        var sink = new HookedAuditSink();
        var fixture = await ProposalFixture.CreateAsync(tool, policy, services =>
            services.Replace(ServiceDescriptor.Singleton<IAgentAuditSink>(sp => sink.Bind(sp.GetRequiredService<InMemoryAgentAuditStore>()))));
        return (fixture, sink);
    }

    private static TaskCompletionSource Barrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void AssertReceipt(AgentToolResult? result, AgentActionProposal proposal)
    {
        var prepared = proposal.PreparedInvocation!;
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.Equal(prepared.ToolCallId, result.ToolCallId);
        Assert.Equal(prepared.InvocationId, result.Receipt!.InvocationId);
        Assert.Equal(prepared.InvocationId, result.Access!.InvocationId);
        Assert.Equal(prepared.Target, result.Receipt.Before);
    }

    private static async Task AssertDecisionsAsync(ProposalFixture fixture, AgentActionProposal proposal, int approved, int denied)
    {
        var audit = (await fixture.Audit.ListAsync(fixture.Session.Id)).Where(x => x.Metadata.GetValueOrDefault("proposalId") == proposal.Id).ToList();
        Assert.Equal(approved, audit.Count(x => x.Kind == AgentAuditEventKind.ProposalApproved));
        Assert.Equal(denied, audit.Count(x => x.Kind == AgentAuditEventKind.ProposalDenied));
        Assert.All(audit, x => Assert.Equal(proposal.PreparedInvocation!.InvocationId, x.Metadata["invocationId"]));
    }

    private static async Task<AgentToolResult> RecordedResultAsync(TurnFixture turn, AgentSession session, AgentActionProposal proposal)
    {
        var message = Assert.Single(await turn.Sessions.ListMessagesAsync(session.Id), x => x.Role == AgentRole.Tool);
        var result = Assert.Single(message.ToolResults);
        Assert.Equal(proposal.PreparedInvocation!.ToolCallId, result.ToolCallId);
        return result;
    }

    private static async Task<List<AgentStreamEvent>> Collect(IAsyncEnumerable<AgentStreamEvent> events)
    {
        var result = new List<AgentStreamEvent>();
        await foreach (var item in events)
            result.Add(item);
        return result;
    }

    private sealed class TurnFixture : IDisposable
    {
        private readonly ServiceProvider _root;
        private readonly IServiceScope _scope;

        public TurnFixture(HookedAuditSink sink, IAgentToolAuthorizer? authority, params IAgentTool[] tools)
        {
            _root = AgentTestServices.Create(services =>
            {
                services.AddSingleton<IAgentProvider, DeterministicAgentProvider>();
                services.Replace(ServiceDescriptor.Singleton<IAgentAuditSink>(sp => sink.Bind(sp.GetRequiredService<InMemoryAgentAuditStore>())));
                if (authority is not null) services.Replace(ServiceDescriptor.Singleton(typeof(IAgentToolAuthorizer), authority));
                foreach (var tool in tools) services.AddSingleton(tool);
            });
            _scope = _root.CreateScope();
        }

        public IAgentSessionService Sessions => _scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        public IAgentStreamingService Streaming => _scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        public IAgentProposalService Proposals => _scope.ServiceProvider.GetRequiredService<IAgentProposalService>();
        public InMemoryAgentProposalStore ProposalStore => _scope.ServiceProvider.GetRequiredService<InMemoryAgentProposalStore>();
        private IAgentSessionPolicyService Policy => _scope.ServiceProvider.GetRequiredService<IAgentSessionPolicyService>();

        public Task<AgentSession> CreateAsync(AgentPolicy? policy = null) => Sessions.CreateAsync(new(
            "tenant-1", "actor-1", "conversation-1", DeterministicAgentProvider.Id, "explain", "Ordering session", policy ?? AgentPolicy.Default,
            new Dictionary<string, string>()));

        public Task<AgentMessage> PostAsync(AgentSession session, string content)
            => Sessions.AddMessageAsync(session.Id, new(AgentRole.User, content, AgentMessageStatus.Pending, null, [], []));

        public async Task<AgentSessionPolicyOutcome> ProbeAsync(AgentSession session)
        {
            var current = await Policy.GetAsync(session.Id, session.ActorId, session.TenantId);
            Assert.Equal(AgentSessionPolicyOutcome.Succeeded, current.Outcome);
            return (await Policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, current.State!.Policy.AutonomyMode, current.State.Revision))).Outcome;
        }

        public void Dispose()
        {
            _scope.Dispose();
            _root.Dispose();
        }
    }

    /// <summary>Allows current authority; a one-shot barrier can hold the next decision.</summary>
    private sealed class GatedAuthorizer : IAgentToolAuthorizer
    {
        private Func<CancellationToken, Task>? _next;
        public void HoldNext(Func<CancellationToken, Task> barrier) => Volatile.Write(ref _next, barrier);

        public async Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId,
            AgentResourceTarget? target, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _next, null) is { } barrier) await barrier(cancellationToken);
            return new(true, []);
        }
    }

    /// <summary>The real apply-change module whose effect is applied before its confirmation is interrupted.</summary>
    private sealed class HeldApplyChangeTool : IAgentTool
    {
        public ApplyChangeTool Inner { get; } = new();
        public TaskCompletionSource Applied { get; } = Barrier();
        public AgentToolDescriptor Descriptor => Inner.Descriptor;

        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
            => Inner.PrepareAsync(invocation, cancellationToken);

        public async Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            var applied = await Inner.ExecuteAsync(invocation, cancellationToken);
            Applied.TrySetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            return applied;
        }
    }
}
