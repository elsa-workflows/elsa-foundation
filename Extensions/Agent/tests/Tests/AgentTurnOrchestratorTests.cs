using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Extensions;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;

namespace Elsa.Agent.Tests;

public sealed class AgentTurnOrchestratorTests
{
    [Fact]
    public async Task Duplicate_provider_argument_names_are_refused_without_invoking_the_tool_or_breaking_the_stream()
    {
        using var root = Build(new LoopingToolProvider("""{"text":"first","text":"second"}"""), [new EchoTool()], maxSteps: 1);

        var events = await RunAsync(root, LoopingToolProvider.Id, "read state");

        var outcome = Assert.IsType<AgentToolCallOutcome>(Assert.Single(events, x => x.Kind == AgentStreamEventKind.ToolCallCompleted).Payload);
        Assert.False(outcome.Succeeded);
        Assert.Contains("unique property names", outcome.Summary);
        Assert.Equal(AgentStreamEventKind.Completed, events[^1].Kind);
        using var scope = root.CreateScope();
        Assert.DoesNotContain(await scope.ServiceProvider.GetRequiredService<IAgentAuditReader>().ListAsync(), x => x.Kind == AgentAuditEventKind.ToolInvoked);
    }

    [Fact]
    public async Task Default_session_alias_is_unavailable_without_creating_or_bypassing_a_foreground_lease()
    {
        using var root = Build(new DeterministicAgentProvider(), [new EchoTool()]);
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var streaming = scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        var turns = scope.ServiceProvider.GetRequiredService<IAgentTurnRegistry>();
        var session = await CreateSessionAsync(sessions, DeterministicAgentProvider.Id);
        await PostAsync(sessions, session.Id, "read state");
        var alias = SwapCase(session.Id);
        Assert.NotEqual(session.Id, alias);
        var refused = Assert.Single(await Collect(streaming.StreamAsync(alias)));
        Assert.Equal("agent.session.unavailable", refused.Error?.Code);
        Assert.Equal(404, refused.Error?.StatusCode);
        Assert.False(turns.IsActive(session.Id));
        Assert.False(turns.IsActive(alias));
        await using var first = streaming.StreamAsync(session.Id).GetAsyncEnumerator();
        Assert.True(await first.MoveNextAsync());
        Assert.Equal(AgentStreamEventKind.Started, first.Current.Kind);
        Assert.Equal("agent.session.unavailable", Assert.Single(await Collect(streaming.StreamAsync(alias))).Error?.Code);
        Assert.True(turns.IsActive(session.Id));
        Assert.False(turns.IsActive(alias));

        var second = await Collect(streaming.StreamAsync(session.Id));
        Assert.Equal("agent.turn.busy", Assert.Single(second).Error?.Code);
        var canonical = new List<AgentStreamEvent> { first.Current };
        while (await first.MoveNextAsync()) canonical.Add(first.Current);
        Assert.Equal(AgentStreamEventKind.Completed, canonical[^1].Kind);
        Assert.False(turns.IsActive(session.Id));
    }

    [Fact]
    public async Task Default_session_alias_cannot_consume_a_canonical_pause_or_its_observed_result()
    {
        using var root = Build(new DeterministicAgentProvider(), [new ApplyChangeTool()]);
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var streaming = scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        var proposals = scope.ServiceProvider.GetRequiredService<IAgentProposalService>();
        var states = scope.ServiceProvider.GetRequiredService<IAgentTurnStateStore>();
        var session = await CreateSessionAsync(sessions, DeterministicAgentProvider.Id);
        await PostAsync(sessions, session.Id, "mutate the resource");
        var alias = SwapCase(session.Id);
        Assert.NotEqual(session.Id, alias);
        Assert.Equal("agent.session.unavailable", Assert.Single(await Collect(streaming.StreamAsync(alias))).Error?.Code);
        Assert.Null(states.Find(session.Id));
        var first = await Collect(streaming.StreamAsync(session.Id));
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        var pause = Assert.IsType<AgentPausedTurn>(states.Find(session.Id));
        Assert.Null(states.Find(alias));
        Assert.Equal("agent.session.unavailable", Assert.Single(await Collect(streaming.StreamAsync(alias))).Error?.Code);
        Assert.Same(pause, states.Find(session.Id));
        Assert.True((await proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        var result = await proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        Assert.True(result.Succeeded);
        Assert.Equal("agent.session.unavailable", Assert.Single(await Collect(streaming.StreamAsync(alias))).Error?.Code);
        Assert.Same(pause, states.Find(session.Id));

        var resumed = await Collect(streaming.StreamAsync(session.Id));
        Assert.Contains(resumed, x => x.Kind == AgentStreamEventKind.MessageDelta && x.Content!.Contains(result.Value!.ToolResult!.Content));
        Assert.Equal(AgentStreamEventKind.Completed, resumed[^1].Kind);
        Assert.Null(states.Find(session.Id));
    }

    [Fact]
    public async Task Pre_dispatch_expiry_drains_the_pause_as_a_refusal_without_inventing_a_receipt()
    {
        var clock = new TurnTestClock();
        var tool = new ApplyChangeTool();
        using var root = AgentTestServices.Create(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            services.AddSingleton<IAgentProvider, DeterministicAgentProvider>();
            services.AddSingleton<IAgentTool>(tool);
        });
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var streaming = scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        var proposals = scope.ServiceProvider.GetRequiredService<IAgentProposalService>();
        var states = scope.ServiceProvider.GetRequiredService<IAgentTurnStateStore>();
        var session = await CreateSessionAsync(sessions, DeterministicAgentProvider.Id);
        await PostAsync(sessions, session.Id, "mutate the resource");
        var first = await Collect(streaming.StreamAsync(session.Id));
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        Assert.True((await proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        clock.Advance(TimeSpan.FromMinutes(6));

        var refused = await proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);

        Assert.Equal("agent.proposal.expired", refused.Error?.Code);
        Assert.Equal(409, refused.Error?.StatusCode);
        Assert.Equal(AgentActionProposalStatus.Expired, (await proposals.FindAsync(proposal.Id))!.Status);
        Assert.Null(await proposals.FindResultAsync(proposal.Id));
        var resumed = await Collect(streaming.StreamAsync(session.Id));
        Assert.Contains(resumed, x => x.Kind == AgentStreamEventKind.MessageDelta && x.Content!.Contains("Expired", StringComparison.Ordinal));
        Assert.DoesNotContain(resumed, x => x.Kind == AgentStreamEventKind.ToolCallRequested);
        Assert.Equal(AgentStreamEventKind.Completed, resumed[^1].Kind);
        Assert.Null(states.Find(session.Id));
        Assert.Equal(0, tool.Invocations);
        await PostAsync(sessions, session.Id, "a new explicit request");
    }

    [Fact]
    public async Task Reconnect_after_an_expired_client_dispatch_reports_uncertainty_and_releases_the_pause()
    {
        var clock = new TurnTestClock();
        var tool = new PendingClientTool();
        var attachment = new AgentContextAttachment("client-snapshot", "test", "resource-1", "Local resource", "test.snapshot", AgentContextSensitivity.Internal,
            "selection", "Local resource snapshot", new { revision = "revision-1" }, new Dictionary<string, string>())
        { ModuleId = tool.Descriptor.ModuleId, Target = tool.Target, Client = tool.Binding, Origin = AgentContextOrigin.ClientSnapshot };
        var source = new AgentTestContextSource(PendingClientTool.ContextSourceId, tool.Descriptor.ModuleId, [attachment], [tool]);
        using var root = AgentTestServices.Create(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            services.AddSingleton<IAgentProvider, DeterministicAgentProvider>();
            services.AddSingleton<IAgentTool>(tool);
            services.AddSingleton<IAgentContextProvider>(source);
        });
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var streaming = scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        var proposals = scope.ServiceProvider.GetRequiredService<IAgentProposalService>();
        var states = scope.ServiceProvider.GetRequiredService<IAgentTurnStateStore>();
        var session = await CreateSessionAsync(sessions, DeterministicAgentProvider.Id);
        var collected = await scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(session.Policy,
            new(session.Id, source.ScopeKind, new Dictionary<string, string>()) { CallerAttachments = [attachment] });
        Assert.True(collected.Succeeded, collected.Error?.Message);
        var context = collected.Value!.Attachments;
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "mutate the resource", AgentMessageStatus.Pending, null,
            context.Select(item => item.Id).ToArray(), context) { Client = tool.Binding });
        var first = await Collect(streaming.StreamAsync(session.Id));
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        Assert.True((await proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        var dispatch = await proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        Assert.Equal("awaiting-client", dispatch.Value?.Status);
        Assert.False(dispatch.Value!.Executed);
        clock.Advance(TimeSpan.FromMinutes(6));

        var resumed = await Collect(streaming.StreamAsync(session.Id));
        Assert.Contains(resumed, x => x.Kind == AgentStreamEventKind.MessageDelta && x.Content!.Contains("uncertain", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resumed, x => x.Error?.Code == "agent.turn.awaiting_review");
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await proposals.FindAsync(proposal.Id))!.Status);
        Assert.Null(states.Find(session.Id));
        Assert.Equal(0, tool.ServerExecutions);
        await PostAsync(sessions, session.Id, "a new explicit request");
    }

    [Fact]
    public async Task Read_only_tool_runs_inline_and_result_feeds_back_into_a_final_answer()
    {
        using var sp = Build(new DeterministicAgentProvider(), [new EchoTool()]);

        var events = await RunAsync(sp, DeterministicAgentProvider.Id, "use tool to read state");

        Assert.Contains(events, x => x.Kind == AgentStreamEventKind.Started && x.Payload is AgentTurnStarted);
        Assert.Contains(events, x => x.Kind == AgentStreamEventKind.ToolCallRequested);
        var completed = Assert.Single(events, x => x.Kind == AgentStreamEventKind.ToolCallCompleted);
        Assert.True(((AgentToolCallOutcome)completed.Payload!).Succeeded);
        var finalDelta = events.Last(x => x.Kind == AgentStreamEventKind.MessageDelta);
        Assert.Contains("echo:hello", finalDelta.Content);
        Assert.Equal(AgentStreamEventKind.Completed, events[^1].Kind);
    }

    [Fact]
    public async Task Multi_step_plan_runs_two_tool_calls_before_completing()
    {
        using var sp = Build(new DeterministicAgentProvider(), [new EchoTool()]);

        var events = await RunAsync(sp, DeterministicAgentProvider.Id, "run a multi-step plan");

        Assert.Equal(2, events.Count(x => x.Kind == AgentStreamEventKind.ToolCallRequested));
        Assert.Equal(2, events.Count(x => x.Kind == AgentStreamEventKind.ToolCallCompleted));
        Assert.Equal(AgentStreamEventKind.Completed, events[^1].Kind);
    }

    [Fact]
    public async Task Mutating_tool_pauses_for_approval_then_resumes_after_execution()
    {
        using var sp = Build(new DeterministicAgentProvider(), [new ApplyChangeTool()]);
        using var scope = sp.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var streaming = scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        var proposals = scope.ServiceProvider.GetRequiredService<IAgentProposalService>();
        var session = await CreateSessionAsync(sessions, DeterministicAgentProvider.Id);
        await PostAsync(sessions, session.Id, "mutate the workflow");

        var first = await Collect(streaming.StreamAsync(session.Id));

        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        Assert.Contains(first, x => x.Kind == AgentStreamEventKind.ToolApprovalRequested);
        Assert.DoesNotContain(first, x => x.Kind == AgentStreamEventKind.Completed);

        Assert.True((await proposals.ApproveAsync(proposal.Id, "actor-1", proposal.BaseRevision)).Succeeded);
        var applied = await proposals.ExecuteAsync(proposal.Id, "actor-1", proposal.BaseRevision);
        Assert.True(applied.Succeeded);
        Assert.NotNull(applied.Value!.ToolResult!.Receipt);

        var second = await Collect(streaming.StreamAsync(session.Id));

        var delta = second.Last(x => x.Kind == AgentStreamEventKind.MessageDelta);
        Assert.Contains(applied.Value.ToolResult.Content, delta.Content);
        Assert.Equal(AgentStreamEventKind.Completed, second[^1].Kind);
    }

    [Fact]
    public async Task Denied_proposal_resumes_the_turn_with_a_failed_tool_result()
    {
        using var sp = Build(new DeterministicAgentProvider(), [new ApplyChangeTool()]);
        using var scope = sp.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var streaming = scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        var proposals = scope.ServiceProvider.GetRequiredService<IAgentProposalService>();
        var session = await CreateSessionAsync(sessions, DeterministicAgentProvider.Id);
        await PostAsync(sessions, session.Id, "mutate the workflow");

        var first = await Collect(streaming.StreamAsync(session.Id));
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload);

        await proposals.DenyAsync(proposal.Id, "actor-1", "not now");

        var second = await Collect(streaming.StreamAsync(session.Id));

        var delta = second.Last(x => x.Kind == AgentStreamEventKind.MessageDelta);
        Assert.Contains("denied", delta.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AgentStreamEventKind.Completed, second[^1].Kind);
    }

    [Fact]
    public async Task Cancelling_a_turn_emits_turn_cancelled_and_no_completion()
    {
        using var sp = Build(new DeterministicAgentProvider(), [new EchoTool()]);
        using var scope = sp.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var streaming = scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        var turns = scope.ServiceProvider.GetRequiredService<IAgentTurnRegistry>();
        var session = await CreateSessionAsync(sessions, DeterministicAgentProvider.Id);
        await PostAsync(sessions, session.Id, "use tool to read state");

        var collected = new List<AgentStreamEvent>();
        await foreach (var streamEvent in streaming.StreamAsync(session.Id))
        {
            collected.Add(streamEvent);
            if (streamEvent.Kind == AgentStreamEventKind.Started)
                turns.Cancel(session.Id, ((AgentTurnStarted)streamEvent.Payload!).TurnId);
        }

        Assert.Contains(collected, x => x.Kind == AgentStreamEventKind.TurnCancelled);
        Assert.DoesNotContain(collected, x => x.Kind == AgentStreamEventKind.Completed);
    }

    [Fact]
    public async Task A_busy_foreground_turn_refuses_another_prompt_without_losing_it_silently()
    {
        using var sp = Build(new SteeringProbeProvider(), [new EchoTool()]);
        using var scope = sp.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var streaming = scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        var session = await CreateSessionAsync(sessions, SteeringProbeProvider.Id);
        await PostAsync(sessions, session.Id, "first request");

        var collected = new List<AgentStreamEvent>();
        var injected = false;
        await foreach (var streamEvent in streaming.StreamAsync(session.Id))
        {
            collected.Add(streamEvent);
            // After the first step's tool finishes, queue a follow-up before the next step runs.
            if (!injected && streamEvent.Kind == AgentStreamEventKind.ToolCallCompleted)
            {
                injected = true;
                await Assert.ThrowsAsync<AgentContributionException>(() => PostAsync(sessions, session.Id, "second request"));
            }
        }

        Assert.DoesNotContain(await sessions.ListMessagesAsync(session.Id), x => x.Content == "second request");
        var finalDelta = collected.Last(x => x.Kind == AgentStreamEventKind.MessageDelta);
        Assert.Contains("first request", finalDelta.Content);
        Assert.DoesNotContain("second request", finalDelta.Content);
    }

    [Fact]
    public async Task Loop_owning_harness_provider_is_delegated_and_its_events_forwarded()
    {
        using var sp = Build(new DeterministicHarnessProvider(), []);

        var events = await RunAsync(sp, DeterministicHarnessProvider.Id, "anything");

        Assert.Contains(events, x => x.Kind == AgentStreamEventKind.Started && x.Payload is AgentTurnStarted);
        var delta = events.Last(x => x.Kind == AgentStreamEventKind.MessageDelta);
        Assert.Equal("harness answer", delta.Content);
        Assert.Equal(AgentStreamEventKind.Completed, events[^1].Kind);
        Assert.DoesNotContain(events, x => x.Kind == AgentStreamEventKind.StepStarted);
    }

    [Fact]
    public async Task Turn_stops_at_the_step_budget()
    {
        using var sp = Build(new LoopingToolProvider(), [new EchoTool()], maxSteps: 2);

        var events = await RunAsync(sp, LoopingToolProvider.Id, "loop forever");

        Assert.Equal(2, events.Count(x => x.Kind == AgentStreamEventKind.StepStarted));
        Assert.Contains(events, x => x.Kind == AgentStreamEventKind.MessageDelta && x.Content!.Contains("step limit"));
        Assert.Equal(AgentStreamEventKind.Completed, events[^1].Kind);
    }

    // ---- #3440 T017: lookup-to-registration, terminal-to-unregister and verified-pause handoffs ----
    // Real default orchestrator/owner; only the participating registry and pause store are forwarding replacements.

    [Fact]
    public async Task A_policy_committed_between_session_lookup_and_foreground_acquisition_governs_the_turn_instead_of_the_lookup()
    {
        var tool = new ApplyChangeTool();
        using var fixture = new HandoffFixture(tool);
        var session = await fixture.CreateAsync();
        var held = fixture.States.HoldNextFind();

        // The stream has already read the session (AutoReadOnly) and holds before acquiring its foreground claim.
        var stream = Task.Run(() => Collect(fixture.Streaming.StreamAsync(session.Id)));
        await held.Entered.Task.WaitAsync(HandoffTimeout);
        var changed = await fixture.ChangeAsync(session, AgentAutonomyMode.FullAuto, 1);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, changed.Outcome);
        Assert.Equal(2, changed.State!.Revision);
        var prompt = await fixture.Sessions.AddMessageAsync(session.Id, new(AgentRole.User, "mutate the resource", AgentMessageStatus.Pending, null, [], [])
        { ExpectedPolicyRevision = 2 });
        held.Release.SetResult();
        var events = await stream.WaitAsync(HandoffTimeout);

        // The acquired claim's FullAuto snapshot replaced the stale lookup, which would have required review.
        Assert.Equal(AgentStreamEventKind.Completed, events[^1].Kind);
        Assert.DoesNotContain(events, x => x.Kind == AgentStreamEventKind.ProposalCreated);
        Assert.Equal(1, tool.Invocations);
        var proposal = Assert.Single(fixture.ProposalStore.Proposals.Values);
        Assert.False(proposal.RequiresApproval);
        Assert.Equal(2, proposal.PolicyRevision);
        Assert.Equal(AgentActionProposalStatus.Executed, proposal.Status);
        Assert.Equal(2, prompt.AcceptedPolicy!.Revision);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
    }

    [Fact]
    public async Task A_completed_turn_stays_busy_after_its_terminal_event_and_status_until_actual_unregister()
    {
        using var fixture = new HandoffFixture();
        var session = await fixture.CreateAsync();
        await PostAsync(fixture.Sessions, session.Id, "hello");
        var unregister = fixture.Turns.HoldNextUnregister(afterInner: false);
        await using var turn = fixture.Streaming.StreamAsync(session.Id).GetAsyncEnumerator();

        while (await turn.MoveNextAsync() && turn.Current.Kind != AgentStreamEventKind.Completed) { }
        Assert.Equal(AgentStreamEventKind.Completed, turn.Current.Kind);
        // The terminal event is displayed; the turn has not released anything yet.
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));

        var cleanup = Task.Run(async () => await turn.MoveNextAsync());
        await unregister.Entered.Task.WaitAsync(HandoffTimeout);
        // The source is terminal and its prompt claim released, but the foreground claim spans actual Unregister.
        Assert.Equal(AgentMessageStatus.Completed, Assert.Single(await fixture.Sessions.ListMessagesAsync(session.Id), x => x.Role == AgentRole.User).Status);
        Assert.True(fixture.Turns.IsActive(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await fixture.ChangeAsync(session, AgentAutonomyMode.FullAuto, 1)).Outcome);
        unregister.Release.SetResult();
        Assert.False(await cleanup.WaitAsync(HandoffTimeout));

        Assert.False(fixture.Turns.IsActive(session.Id));
        Assert.Equal(2, (await fixture.ChangeAsync(session, AgentAutonomyMode.FullAuto, 1)).State!.Revision);
    }

    [Fact]
    public async Task A_verified_pause_is_claimed_before_foreground_release_and_its_continuation_releases_only_its_own_claims()
    {
        var tool = new ApplyChangeTool();
        using var fixture = new HandoffFixture(tool);
        var session = await fixture.CreateAsync();
        await PostAsync(fixture.Sessions, session.Id, "mutate the resource");
        var pausedUnregister = fixture.Turns.HoldNextUnregister(afterInner: true);

        var paused = Task.Run(() => Collect(fixture.Streaming.StreamAsync(session.Id)));
        await pausedUnregister.Entered.Task.WaitAsync(HandoffTimeout);
        // The lease is gone and foreground is still claimed; the pause claim already covers the turn.
        Assert.False(fixture.Turns.IsActive(session.Id));
        var pause = fixture.Owner.FindPause(session.Id);
        Assert.NotNull(pause);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        pausedUnregister.Release.SetResult();
        var first = await paused.WaitAsync(HandoffTimeout);
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        Assert.Same(pause, fixture.Owner.FindPause(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));

        Assert.True((await fixture.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.True((await fixture.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value!.Executed);
        Assert.Same(pause, fixture.Owner.FindPause(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));

        var continuationUnregister = fixture.Turns.HoldNextUnregister(afterInner: false);
        var continuation = Task.Run(() => Collect(fixture.Streaming.StreamAsync(session.Id)));
        await continuationUnregister.Entered.Task.WaitAsync(HandoffTimeout);
        // The continuation removed and released exactly the pause it observed, while its own foreground claim remains.
        Assert.Null(fixture.States.Find(session.Id));
        Assert.Null(fixture.Owner.FindPause(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        continuationUnregister.Release.SetResult();
        Assert.Equal(AgentStreamEventKind.Completed, (await continuation.WaitAsync(HandoffTimeout))[^1].Kind);

        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
        Assert.Equal(1, tool.Invocations);
    }

    [Fact]
    public async Task An_old_turn_cleanup_releases_only_its_own_foreground_claim_and_never_a_successor()
    {
        var provider = new HeldDeterministicProvider();
        using var fixture = new HandoffFixture(provider: provider);
        var session = await fixture.CreateAsync();
        await PostAsync(fixture.Sessions, session.Id, "first");
        var oldUnregister = fixture.Turns.HoldNextUnregister(afterInner: true);

        var old = Task.Run(() => Collect(fixture.Streaming.StreamAsync(session.Id)));
        await oldUnregister.Entered.Task.WaitAsync(HandoffTimeout);
        // The old lease is free but its foreground claim is not yet released. A successor turn runs to its own
        // terminal state and holds in Unregister, so only the two foreground claims remain.
        await PostAsync(fixture.Sessions, session.Id, "second");
        var successorUnregister = fixture.Turns.HoldNextUnregister(afterInner: false);
        using var successorScope = fixture.Root.CreateScope();
        var successor = Task.Run(() => Collect(successorScope.ServiceProvider.GetRequiredService<IAgentStreamingService>().StreamAsync(session.Id)));
        await successorUnregister.Entered.Task.WaitAsync(HandoffTimeout);
        Assert.All(await fixture.Sessions.ListMessagesAsync(session.Id), x => Assert.Equal(AgentMessageStatus.Completed, x.Status));
        oldUnregister.Release.SetResult();
        Assert.Equal(AgentStreamEventKind.Completed, (await old.WaitAsync(HandoffTimeout))[^1].Kind);

        // The old turn's cleanup released exactly its own token; the successor's claim still owns the policy.
        Assert.True(fixture.Turns.IsActive(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, await fixture.ProbeAsync(session));
        successorUnregister.Release.SetResult();
        Assert.Equal(AgentStreamEventKind.Completed, (await successor.WaitAsync(HandoffTimeout))[^1].Kind);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, await fixture.ProbeAsync(session));
        Assert.Equal(2, provider.Turns);
    }

    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(30);

    private static TaskCompletionSource HandoffBarrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class HandoffFixture : IDisposable
    {
        private readonly IServiceScope _scope;

        public HandoffFixture(IAgentTool? tool = null, IAgentProvider? provider = null)
        {
            Root = AgentTestServices.Create(services =>
            {
                services.AddSingleton(provider ?? new DeterministicAgentProvider());
                if (tool is not null) services.AddSingleton(tool);
                services.Replace(ServiceDescriptor.Singleton<IAgentTurnRegistry>(Turns));
                services.Replace(ServiceDescriptor.Singleton<IAgentTurnStateStore>(States));
            });
            _scope = Root.CreateScope();
        }

        public ServiceProvider Root { get; }
        public BarrierTurnRegistry Turns { get; } = new(new AgentTurnRegistry());
        public BarrierTurnStateStore States { get; } = new(new InMemoryAgentTurnStateStore());
        public IAgentSessionService Sessions => _scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        public IAgentStreamingService Streaming => _scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        public IAgentProposalService Proposals => _scope.ServiceProvider.GetRequiredService<IAgentProposalService>();
        public InMemoryAgentProposalStore ProposalStore => _scope.ServiceProvider.GetRequiredService<InMemoryAgentProposalStore>();
        internal InMemoryAgentSessionService Owner => (InMemoryAgentSessionService)Sessions;
        private IAgentSessionPolicyService Policy => _scope.ServiceProvider.GetRequiredService<IAgentSessionPolicyService>();

        public Task<AgentSession> CreateAsync() => CreateSessionAsync(Sessions, DeterministicAgentProvider.Id);

        public Task<AgentSessionPolicyResult> ChangeAsync(AgentSession session, AgentAutonomyMode mode, long expectedRevision)
            => Policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, mode, expectedRevision));

        /// <summary>Requests the current mode at the current revision: Busy while owned, otherwise an unchanged acknowledgement.</summary>
        public async Task<AgentSessionPolicyOutcome> ProbeAsync(AgentSession session)
        {
            var current = await Policy.GetAsync(session.Id, session.ActorId, session.TenantId);
            Assert.Equal(AgentSessionPolicyOutcome.Succeeded, current.Outcome);
            var probe = await ChangeAsync(session, current.State!.Policy.AutonomyMode, current.State.Revision);
            Assert.Equal(current.State.Revision, probe.State!.Revision);
            return probe.Outcome;
        }

        public void Dispose()
        {
            _scope.Dispose();
            Root.Dispose();
        }
    }

    /// <summary>A forwarding (participating) registry replacement whose next Unregister can be held before or after the real one.</summary>
    internal sealed class BarrierTurnRegistry(AgentTurnRegistry inner) : IAgentTurnRegistry
    {
        private (TaskCompletionSource Entered, TaskCompletionSource Release, bool AfterInner)? _next;

        public (TaskCompletionSource Entered, TaskCompletionSource Release) HoldNextUnregister(bool afterInner)
        {
            var hold = (HandoffBarrier(), HandoffBarrier(), afterInner);
            lock (inner) _next = hold;
            return (hold.Item1, hold.Item2);
        }

        public CancellationToken Register(string sessionId, string turnId) => inner.Register(sessionId, turnId);
        public bool Cancel(string sessionId, string turnId) => inner.Cancel(sessionId, turnId);
        public bool IsActive(string sessionId) => inner.IsActive(sessionId);

        public void Unregister(string sessionId, string turnId)
        {
            (TaskCompletionSource Entered, TaskCompletionSource Release, bool AfterInner)? hold;
            lock (inner)
            {
                hold = _next;
                _next = null;
            }
            if (hold is { AfterInner: true }) inner.Unregister(sessionId, turnId);
            if (hold is { } held)
            {
                held.Entered.TrySetResult();
                Assert.True(held.Release.Task.Wait(HandoffTimeout));
            }
            if (hold is not { AfterInner: true }) inner.Unregister(sessionId, turnId);
        }
    }

    /// <summary>A forwarding (participating) pause store replacement whose next Find can be held.</summary>
    internal sealed class BarrierTurnStateStore(InMemoryAgentTurnStateStore inner) : IAgentTurnStateStore
    {
        private (TaskCompletionSource Entered, TaskCompletionSource Release)? _next;

        public (TaskCompletionSource Entered, TaskCompletionSource Release) HoldNextFind()
        {
            var hold = (HandoffBarrier(), HandoffBarrier());
            lock (inner) _next = hold;
            return hold;
        }

        public void Save(AgentPausedTurn state) => inner.Save(state);
        public void Remove(string sessionId) => inner.Remove(sessionId);

        public AgentPausedTurn? Find(string sessionId)
        {
            (TaskCompletionSource Entered, TaskCompletionSource Release)? hold;
            lock (inner)
            {
                hold = _next;
                _next = null;
            }
            if (hold is { } held)
            {
                held.Entered.TrySetResult();
                Assert.True(held.Release.Task.Wait(HandoffTimeout));
            }
            return inner.Find(sessionId);
        }
    }

    /// <summary>The deterministic host-step provider; its next turn can be held inside the provider.</summary>
    private sealed class HeldDeterministicProvider : IAgentProvider
    {
        private readonly DeterministicAgentProvider _inner = new();
        private (TaskCompletionSource Entered, TaskCompletionSource Release)? _next;
        private int _turns;

        public string ProviderId => _inner.ProviderId;
        public int Turns => Volatile.Read(ref _turns);

        public (TaskCompletionSource Entered, TaskCompletionSource Release) HoldNext()
        {
            var hold = (HandoffBarrier(), HandoffBarrier());
            lock (_inner) _next = hold;
            return hold;
        }

        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default) => _inner.CreateSessionAsync(session, cancellationToken);

        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _turns);
            (TaskCompletionSource Entered, TaskCompletionSource Release)? hold;
            lock (_inner)
            {
                hold = _next;
                _next = null;
            }
            if (hold is { } held)
            {
                held.Entered.TrySetResult();
                await held.Release.Task.WaitAsync(cancellationToken);
            }
            await foreach (var item in _inner.ContinueTurnAsync(context, cancellationToken))
                yield return item;
        }

        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => _inner.ApproveToolAsync(request, cancellationToken);
        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => _inner.GetDiagnosticsAsync(cancellationToken);
    }

    private static ServiceProvider Build(IAgentProvider provider, IEnumerable<IAgentTool> tools, int? maxSteps = null)
    {
        return AgentTestServices.Create(services =>
        {
            if (maxSteps is not null) services.Configure<AgentTurnOptions>(o => o.MaxSteps = maxSteps.Value);
            services.AddSingleton(provider);
            foreach (var tool in tools) services.AddSingleton(tool);
        });
    }

    private static async Task<List<AgentStreamEvent>> RunAsync(ServiceProvider sp, string providerId, string prompt)
    {
        using var scope = sp.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var streaming = scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        var session = await CreateSessionAsync(sessions, providerId);
        await PostAsync(sessions, session.Id, prompt);
        return await Collect(streaming.StreamAsync(session.Id));
    }

    private static Task<AgentSession> CreateSessionAsync(IAgentSessionService sessions, string providerId)
        => sessions.CreateAsync(new(
            "tenant-1",
            "actor-1",
            "conversation-1",
            providerId,
            "explain",
            "Test session",
            AgentPolicy.Default,
            new Dictionary<string, string>()));

    private static Task PostAsync(IAgentSessionService sessions, string sessionId, string content)
        => sessions.AddMessageAsync(sessionId, new(AgentRole.User, content, AgentMessageStatus.Pending, null, [], []));

    private static async Task<List<AgentStreamEvent>> Collect(IAsyncEnumerable<AgentStreamEvent> events)
    {
        var result = new List<AgentStreamEvent>();
        await foreach (var item in events)
            result.Add(item);
        return result;
    }

    private static string SwapCase(string value) => new(value.Select(x => char.IsUpper(x) ? char.ToLowerInvariant(x) : char.ToUpperInvariant(x)).ToArray());

    private sealed class TurnTestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class PendingClientTool : IAgentTool, IAgentClientTool
    {
        public const string ContextSourceId = "test.expiry.disclosure";
        public AgentResourceTarget Target { get; } = new("test.expiry", "test.local-resource", "resource-1", "revision-1");
        public AgentClientBinding Binding { get; } = new("client-1", "generation-1");
        public int ServerExecutions { get; private set; }
        public AgentToolDescriptor Descriptor { get; } = new(DeterministicAgentProvider.MutatingToolName, "Edit local resource", "Reviewed local change.",
            AgentToolMutability.Mutating, AgentRisk.ReviewRequired, "{}", [], [])
        { ModuleId = "test.expiry", DisclosureSourceId = ContextSourceId, ExecutionLocation = AgentExecutionLocation.ClientWorkingState,
            ResultType = "test.local-change", ResultSchemaVersion = "1" };
        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolPreparation>.Success(new(Target, JsonSerializer.SerializeToElement(invocation.Arguments), AgentRisk.ReviewRequired,
                new("test.local-change", "1", JsonSerializer.SerializeToElement(new { summary = "Edit local resource" })), true) { Client = Binding }));
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            ServerExecutions++;
            return Task.FromResult(AgentResult<AgentToolResult>.Failure("test.client_only", "This tool must not execute on the server."));
        }
        public Task<AgentResult<AgentToolResult>> CompleteAsync(AgentPreparedToolInvocation invocation, AgentClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolResult>.Failure("test.no_receipt", "No client receipt was supplied in this scenario."));
    }

    private sealed class SteeringProbeProvider : IAgentProvider
    {
        public const string Id = "steering-probe-test";

        public string ProviderId => Id;

        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));

        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            // Step 0 calls a read-only tool to force a second step; later steps report what user messages they saw.
            if (context.StepIndex == 0)
            {
                yield return new AgentStreamEvent(
                    Guid.NewGuid().ToString("N"),
                    AgentStreamEventKind.ToolCallRequested,
                    null,
                    "probe-call",
                    null,
                    DateTimeOffset.UtcNow,
                    null,
                    new AgentToolCallRequest("probe-call", DeterministicAgentProvider.ReadOnlyToolName, """{"text":"probe"}""", AgentRisk.ReadOnly, false));
                yield break;
            }

            var userMessages = string.Join(" | ", context.History.Where(x => x.Role == AgentRole.User).Select(x => x.Content));
            yield return new AgentStreamEvent(Guid.NewGuid().ToString("N"), AgentStreamEventKind.MessageDelta, $"Saw: {userMessages}", null, null, DateTimeOffset.UtcNow, AgentResultKind.Message);
        }

        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentToolApprovalResult(false, string.Empty));

        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderDiagnostics(ProviderId, true, "ok", AgentProviderKind.ProviderSdkBinding, [AgentProviderOperation.Chat], AgentProviderRiskProfile.ReadOnly, new Dictionary<string, string>()));
    }

    private sealed class DeterministicHarnessProvider : IAgentProvider, IAgentHarness
    {
        public const string Id = "harness-test";

        public string ProviderId => Id;

        public AgentHarnessCapabilities Capabilities => AgentHarnessCapabilities.Tools | AgentHarnessCapabilities.Plan;

        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));

        // The harness owns the whole turn: it emits content directly (the orchestrator must not run the host loop).
        public async IAsyncEnumerable<AgentStreamEvent> RunTurnAsync(AgentHarnessTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new AgentStreamEvent(Guid.NewGuid().ToString("N"), AgentStreamEventKind.MessageDelta, "harness answer", null, null, DateTimeOffset.UtcNow, AgentResultKind.Message);
        }

        // Must never be reached for a harness provider; emits a sentinel to make accidental host-loop use visible.
        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new AgentStreamEvent(Guid.NewGuid().ToString("N"), AgentStreamEventKind.MessageDelta, "host-loop should not run", null, null, DateTimeOffset.UtcNow, AgentResultKind.Message);
        }

        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentToolApprovalResult(false, string.Empty));

        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderDiagnostics(ProviderId, true, "ok", AgentProviderKind.ProviderSdkBinding, [AgentProviderOperation.Chat], AgentProviderRiskProfile.ReadOnly, new Dictionary<string, string>()));
    }

    private sealed class LoopingToolProvider(string argumentsJson = """{"text":"x"}""") : IAgentProvider
    {
        public const string Id = "looping-test";

        public string ProviderId => Id;

        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));

        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var toolCallId = $"call-{context.StepIndex}";
            yield return new AgentStreamEvent(
                Guid.NewGuid().ToString("N"),
                AgentStreamEventKind.ToolCallRequested,
                null,
                toolCallId,
                null,
                DateTimeOffset.UtcNow,
                null,
                new AgentToolCallRequest(toolCallId, DeterministicAgentProvider.ReadOnlyToolName, argumentsJson, AgentRisk.ReadOnly, false));
        }

        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentToolApprovalResult(false, string.Empty));

        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderDiagnostics(ProviderId, true, "ok", AgentProviderKind.ProviderSdkBinding, [AgentProviderOperation.Chat], AgentProviderRiskProfile.ReadOnly, new Dictionary<string, string>()));
    }
}
