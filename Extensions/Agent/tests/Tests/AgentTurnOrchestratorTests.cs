using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Extensions;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

public sealed class AgentTurnOrchestratorTests
{
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

        await proposals.ApproveAsync(proposal.Id, "actor-1");
        await proposals.ExecuteAsync(proposal.Id, "actor-1");

        var second = await Collect(streaming.StreamAsync(session.Id));

        var delta = second.Last(x => x.Kind == AgentStreamEventKind.MessageDelta);
        Assert.Contains("approved and applied", delta.Content);
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
        Assert.Contains("did not complete", delta.Content);
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
                turns.Cancel(((AgentTurnStarted)streamEvent.Payload!).TurnId);
        }

        Assert.Contains(collected, x => x.Kind == AgentStreamEventKind.TurnCancelled);
        Assert.DoesNotContain(collected, x => x.Kind == AgentStreamEventKind.Completed);
    }

    [Fact]
    public async Task Follow_up_messages_are_steered_into_the_running_turn_between_steps()
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
                await PostAsync(sessions, session.Id, "second request");
            }
        }

        Assert.Contains(collected, x => x.Kind == AgentStreamEventKind.Progress && x.Content!.Contains("second request"));
        var finalDelta = collected.Last(x => x.Kind == AgentStreamEventKind.MessageDelta);
        Assert.Contains("first request", finalDelta.Content);
        Assert.Contains("second request", finalDelta.Content);
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
        var services = new ServiceCollection();
        if (maxSteps is not null)
            services.Configure<AgentTurnOptions>(o => o.MaxSteps = maxSteps.Value);
        services.AddFoundationAgentAbstractions();
        services.AddSingleton(provider);
        foreach (var tool in tools)
            services.AddSingleton(tool);
        return services.BuildServiceProvider();
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

    private sealed class LoopingToolProvider : IAgentProvider
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
                new AgentToolCallRequest(toolCallId, DeterministicAgentProvider.ReadOnlyToolName, """{"text":"x"}""", AgentRisk.ReadOnly, false));
        }

        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentToolApprovalResult(false, string.Empty));

        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderDiagnostics(ProviderId, true, "ok", AgentProviderKind.ProviderSdkBinding, [AgentProviderOperation.Chat], AgentProviderRiskProfile.ReadOnly, new Dictionary<string, string>()));
    }
}
