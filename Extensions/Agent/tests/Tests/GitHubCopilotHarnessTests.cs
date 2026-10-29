using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Agent.GitHubCopilot.Options;
using Elsa.Agent.GitHubCopilot.Services;
using Elsa.Agent.Workflows.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Elsa.Agent.Services;

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
        var request = new AgentHarnessTurnRequest("session-1", "actor-1", [new(AgentRole.User, "do it")], [], [new EchoTool()], AgentPolicy.Default)
        { TenantId = "tenant-1", TurnId = "turn-1", Client = new("client-1", "generation-1") };

        var events = new List<AgentStreamEvent>();
        await foreach (var item in provider.RunTurnAsync(request))
            events.Add(item);

        // Our tool was handed to the Copilot session as a config tool and auto-invoked by the (fake) harness.
        Assert.Single(factory.Session.RegisteredTools, tool => tool.Name == DeterministicAgentProvider.ReadOnlyToolName);
        Assert.Contains("echo:hi", factory.Session.LastToolResult);
        Assert.Contains("test.echo", factory.Session.LastToolResult);
        var contributed = Assert.Single(events, item => item.Kind == AgentStreamEventKind.ContributedResult);
        Assert.Equal("test.echo", Assert.IsType<AgentToolResult>(contributed.Payload).Payload?.TypeId);
        Assert.Equal("tenant-1", factory.LastInvocation?.TenantId);
        Assert.Equal("turn-1", factory.LastInvocation?.TurnId);
        Assert.Equal(request.Client, factory.LastInvocation?.Client);

        // The SDK's tool-execution events are mapped to our timeline vocabulary.
        var started = Assert.Single(events, x => x.Kind == AgentStreamEventKind.ToolCallStarted);
        Assert.Equal(DeterministicAgentProvider.ReadOnlyToolName, ((AgentToolCallRequest)started.Payload!).ToolName);
        var completed = Assert.Single(events, x => x.Kind == AgentStreamEventKind.ToolCallCompleted);
        Assert.True(((AgentToolCallOutcome)completed.Payload!).Succeeded);
        Assert.Contains(events, x => x.Kind == AgentStreamEventKind.MessageDelta && x.Content == "done");
    }

    [Fact]
    public async Task Harness_runs_the_workflow_read_graph_tool_and_feeds_the_graph_back()
    {
        var provider = BuildProvider(out var factory, new WorkflowReadGraphTool());
        var graph = WorkflowAgentTests.Snapshot();
        var request = new AgentHarnessTurnRequest("session-1", "actor-1", [new(AgentRole.User, "read the graph")], [graph], [new WorkflowReadGraphTool()], AgentPolicy.Default);

        var events = new List<AgentStreamEvent>();
        await foreach (var item in provider.RunTurnAsync(request))
            events.Add(item);

        // The real read-only workflow tool ran and its serialized graph (real node ids) was fed back to the SDK.
        Assert.Contains("node-1", factory.Session.LastToolResult);
        Assert.Contains("workflows.graph", factory.Session.LastToolResult);
        var started = Assert.Single(events, x => x.Kind == AgentStreamEventKind.ToolCallStarted);
        Assert.Equal(WorkflowReadGraphTool.ToolName, ((AgentToolCallRequest)started.Payload!).ToolName);
        Assert.Contains(events, x => x.Kind == AgentStreamEventKind.ToolCallCompleted);
    }

    [Fact]
    public async Task Harness_surfaces_a_proposal_when_an_auto_invoked_tool_is_mutating()
    {
        var provider = BuildProvider(out var factory, new ApplyChangeTool());
        var request = new AgentHarnessTurnRequest("session-1", "actor-1", [new(AgentRole.User, "apply a change")], [], [new ApplyChangeTool()], AgentPolicy.Default);

        var events = new List<AgentStreamEvent>();
        await foreach (var item in provider.RunTurnAsync(request))
            events.Add(item);

        // Under the default (review) policy a mutating tool becomes a proposal instead of running inline.
        Assert.Contains(events, x => x.Kind == AgentStreamEventKind.ProposalCreated);
        Assert.Contains("awaiting human approval", factory.Session.LastToolResult);
    }

    [Fact]
    public async Task Receipt_continuation_preserves_typed_result_without_replaying_request_or_advertising_mutation()
    {
        var provider = BuildProvider(out var factory, new EchoTool(), new ApplyChangeTool());
        var result = new AgentToolResult("prior-call", "prior-tool", true, "Owning module observed the change.",
            new("independent.result", "7", JsonSerializer.SerializeToElement(new { actualValue = 42 })));
        var request = new AgentHarnessTurnRequest("session-1", "actor-1", [new(AgentRole.User, "ORIGINAL_MUTATION_PROMPT_DO_NOT_REPLAY")], [],
            [new EchoTool(), new ApplyChangeTool()], AgentPolicy.Default)
        { IsReceiptContinuation = true, PendingToolResults = [result], TurnId = "turn-2", TenantId = "tenant-1" };
        await foreach (var _ in provider.RunTurnAsync(request)) { }
        Assert.DoesNotContain("ORIGINAL_MUTATION_PROMPT_DO_NOT_REPLAY", factory.Session.LastPrompt);
        Assert.Contains("independent.result", factory.Session.LastPrompt);
        Assert.Contains("42", factory.Session.LastPrompt);
        Assert.Empty(factory.Session.RegisteredTools);
    }

    private static GitHubCopilotAgentProvider BuildProvider(out FakeCopilotClientFactory factory, params IAgentTool[] tools)
    {
        factory = new FakeCopilotClientFactory();
        var options = new GitHubCopilotAgentOptions { Enabled = true, GitHubToken = "test-token" };
        IAgentTool[] registered = tools.Length == 0 ? [new EchoTool()] : tools;
        var capturedFactory = factory;
        var invoker = new AgentTestToolInvoker(async (invocation, policy) =>
        {
            capturedFactory.LastInvocation = invocation;
            var tool = registered.Single(item => item.Descriptor.Name == invocation.ToolName);
            var preparation = await tool.PrepareAsync(invocation);
            if (!preparation.Succeeded) return AgentToolDispatch.Denied(preparation.Error!);
            var prepared = AgentTestToolHelpers.Prepared(invocation, tool.Descriptor, preparation.Value!);
            if (tool.Descriptor.Mutability == AgentToolMutability.Mutating && policy.RequiresApprovalForMutations)
            {
                var now = DateTimeOffset.UtcNow;
                return AgentToolDispatch.Proposed(new("proposal-1", invocation.SessionId, null, tool.Descriptor.Name, "tool-invocation",
                    tool.Descriptor.DisplayName, tool.Descriptor.Description, tool.Descriptor.Risk, prepared.Target?.Revision,
                    [], [], [], null, [], prepared.Target?.ResourceType, prepared.Target?.ResourceId, true,
                    AgentActionProposalStatus.AwaitingApproval, null, null, now, now) { PreparedInvocation = prepared });
            }
            var result = await tool.ExecuteAsync(prepared);
            return result.Succeeded ? AgentToolDispatch.Executed(result.Value!) : AgentToolDispatch.Denied(result.Error!);
        });
        return new GitHubCopilotAgentProvider(factory, Options.Create(options), invoker, NullLogger<GitHubCopilotAgentProvider>.Instance);
    }

    private sealed class FakeCopilotClientFactory : IGitHubCopilotClientFactory
    {
        public FakeCopilotSession Session { get; } = new();
        public AgentToolInvocation? LastInvocation { get; set; }

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
        public string? LastPrompt { get; private set; }

        public void Configure(IReadOnlyCollection<AIFunction>? tools) => _tools = tools ?? [];

        // Simulates the Copilot harness auto-invoking a config tool and emitting its tool-execution lifecycle.
        public async IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastPrompt = prompt;
            yield return new(GitHubCopilotStreamEventKind.Started);

            var tool = _tools.FirstOrDefault();
            if (tool is not null)
            {
                yield return new(GitHubCopilotStreamEventKind.ToolExecutionStarted, ToolCallId: "call-1", ToolName: tool.Name);
                var result = await tool.InvokeAsync(new AIFunctionArguments(tool.Name == WorkflowReadGraphTool.ToolName ? new Dictionary<string, object?>() : new Dictionary<string, object?> { ["text"] = "hi" }), cancellationToken);
                LastToolResult = JsonSerializer.Serialize(result);
                yield return new(GitHubCopilotStreamEventKind.ToolExecutionCompleted, ToolCallId: "call-1", Success: true);
            }

            yield return new(GitHubCopilotStreamEventKind.MessageDelta, "done");
            yield return new(GitHubCopilotStreamEventKind.Completed);
        }

        public Task AbortAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
