using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Extensions;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
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
        var attachment = new AgentContextAttachment("client-snapshot", "test", "resource-1", "Local resource", "test.snapshot", AgentContextSensitivity.Internal,
            "selection", "Local resource snapshot", new { revision = "revision-1" }, new Dictionary<string, string>())
        { ModuleId = tool.Descriptor.ModuleId, Target = tool.Target, Client = tool.Binding, Origin = AgentContextOrigin.ClientSnapshot };
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "mutate the resource", AgentMessageStatus.Pending, null, [attachment.Id], [attachment]) { Client = tool.Binding });
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
        public AgentResourceTarget Target { get; } = new("test.expiry", "test.local-resource", "resource-1", "revision-1");
        public AgentClientBinding Binding { get; } = new("client-1", "generation-1");
        public int ServerExecutions { get; private set; }
        public AgentToolDescriptor Descriptor { get; } = new(DeterministicAgentProvider.MutatingToolName, "Edit local resource", "Reviewed local change.",
            AgentToolMutability.Mutating, AgentRisk.ReviewRequired, "{}", [], [])
        { ModuleId = "test.expiry", ExecutionLocation = AgentExecutionLocation.ClientWorkingState, ResultType = "test.local-change", ResultSchemaVersion = "1" };
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
