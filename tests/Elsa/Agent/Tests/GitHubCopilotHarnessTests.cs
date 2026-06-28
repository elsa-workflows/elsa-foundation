using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Agent.GitHubCopilot.Options;
using Elsa.Agent.GitHubCopilot.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Agent.Tests;

public sealed class GitHubCopilotHarnessTests
{
    [Fact]
    public void Provider_is_a_loop_owning_harness_with_rich_capabilities()
    {
        var provider = BuildProvider(out _);

        Assert.IsAssignableFrom<IAgentHarness>(provider);
        Assert.True(provider.Capabilities.HasFlag(AgentHarnessCapabilities.Tools));
        Assert.True(provider.Capabilities.HasFlag(AgentHarnessCapabilities.Plan));
        Assert.True(provider.Capabilities.HasFlag(AgentHarnessCapabilities.Mcp));
    }

    [Fact]
    public async Task Harness_registers_tools_runs_them_and_maps_tool_execution_events()
    {
        var provider = BuildProvider(out var factory);
        var request = new AgentHarnessTurnRequest("session-1", "actor-1", [new(AgentRole.User, "do it")], [], [new EchoTool()], AgentPolicy.Default);

        var events = new List<AgentStreamEvent>();
        await foreach (var item in provider.RunTurnAsync(request))
            events.Add(item);

        // Our tool was handed to the Copilot session as a config tool and auto-invoked by the (fake) harness.
        Assert.Single(factory.Session.RegisteredTools, tool => tool.Name == DeterministicAgentProvider.ReadOnlyToolName);
        Assert.Contains("echo:hi", factory.Session.LastToolResult);

        // The SDK's tool-execution events are mapped to our timeline vocabulary.
        var started = Assert.Single(events, x => x.Kind == AgentStreamEventKind.ToolCallStarted);
        Assert.Equal(DeterministicAgentProvider.ReadOnlyToolName, ((AgentToolCallRequest)started.Payload!).ToolName);
        var completed = Assert.Single(events, x => x.Kind == AgentStreamEventKind.ToolCallCompleted);
        Assert.True(((AgentToolCallOutcome)completed.Payload!).Succeeded);
        Assert.Contains(events, x => x.Kind == AgentStreamEventKind.MessageDelta && x.Content == "done");
    }

    private static GitHubCopilotAgentProvider BuildProvider(out FakeCopilotClientFactory factory)
    {
        factory = new FakeCopilotClientFactory();
        var options = new GitHubCopilotAgentOptions { Enabled = true, GitHubToken = "test-token" };
        var audit = new InMemoryAgentAuditStore();
        var proposals = new InMemoryAgentProposalService(new NoopAgentActionProposalExecutor(), audit);
        var invoker = new DefaultAgentToolInvoker(new DefaultAgentToolRegistry([new EchoTool()]), proposals, audit);
        return new GitHubCopilotAgentProvider(factory, Options.Create(options), invoker, NullLogger<GitHubCopilotAgentProvider>.Instance);
    }

    private sealed class FakeCopilotClientFactory : IGitHubCopilotClientFactory
    {
        public FakeCopilotSession Session { get; } = new();

        public IGitHubCopilotClient Create(GitHubCopilotClientRequest request) => new FakeCopilotClient(Session);
    }

    private sealed class FakeCopilotClient(FakeCopilotSession session) : IGitHubCopilotClient
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<string>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<string>>([]);

        public Task<IGitHubCopilotSession> CreateSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
        {
            session.Configure(request.CustomTools);
            return Task.FromResult<IGitHubCopilotSession>(session);
        }

        public Task<IGitHubCopilotSession> ResumeSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
        {
            session.Configure(request.CustomTools);
            return Task.FromResult<IGitHubCopilotSession>(session);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeCopilotSession : IGitHubCopilotSession
    {
        private IReadOnlyCollection<AIFunction> _tools = [];

        public string SessionId => "session-1";
        public string? WorkspacePath => null;
        public IReadOnlyCollection<AIFunction> RegisteredTools => _tools;
        public string? LastToolResult { get; private set; }

        public void Configure(IReadOnlyCollection<AIFunction>? tools) => _tools = tools ?? [];

        // Simulates the Copilot harness auto-invoking a config tool and emitting its tool-execution lifecycle.
        public async IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new(GitHubCopilotStreamEventKind.Started);

            var tool = _tools.FirstOrDefault();
            if (tool is not null)
            {
                yield return new(GitHubCopilotStreamEventKind.ToolExecutionStarted, ToolCallId: "call-1", ToolName: tool.Name);
                var result = await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["text"] = "hi" }), cancellationToken);
                LastToolResult = result?.ToString();
                yield return new(GitHubCopilotStreamEventKind.ToolExecutionCompleted, ToolCallId: "call-1", Success: true);
            }

            yield return new(GitHubCopilotStreamEventKind.MessageDelta, "done");
            yield return new(GitHubCopilotStreamEventKind.Completed);
        }

        public Task AbortAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
