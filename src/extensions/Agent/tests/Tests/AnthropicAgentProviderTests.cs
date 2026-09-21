using System.Runtime.CompilerServices;
using Elsa.Agent.Anthropic;
using Elsa.Agent.Anthropic.Options;
using Elsa.Agent.Anthropic.Services;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Agent.Tests;

public sealed class AnthropicAgentProviderTests
{
    private static readonly AgentToolDescriptor MutatingTool = new ApplyChangeTool().Descriptor;

    [Fact]
    public async Task Feature_registers_a_single_unavailable_provider_when_unconfigured()
    {
        var services = new ServiceCollection();
        new AnthropicAgentFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var agentProvider = Assert.Single(provider.GetServices<IAgentProvider>());
        Assert.IsType<AnthropicAgentProvider>(agentProvider);

        var diagnostics = await agentProvider.GetDiagnosticsAsync();
        Assert.Equal(AgentProviderKind.ProviderSdkBinding, diagnostics.Kind);
        Assert.False(diagnostics.IsAvailable);
        Assert.Contains(AgentProviderOperation.Streaming, diagnostics.SupportedOperations);
    }

    [Fact]
    public async Task Maps_text_to_message_deltas_and_function_calls_to_tool_requests()
    {
        var client = new ScriptedChatClient(
            Update(new TextContent("Hello ")),
            Update(new TextContent("world")),
            Update(new FunctionCallContent("call_1", MutatingTool.Name, new Dictionary<string, object?> { ["change"] = "add-node" })));
        var provider = BuildProvider(client);
        var context = new AgentTurnContext("s1", "s1", [new AgentTurnMessage(AgentRole.User, "Edit the workflow.")], [], [MutatingTool], [], 0, 4);

        var events = await Collect(provider.ContinueTurnAsync(context));

        var deltas = events.Where(e => e.Kind == AgentStreamEventKind.MessageDelta).Select(e => e.Content).ToList();
        Assert.Equal(["Hello ", "world"], deltas);

        var toolEvent = Assert.Single(events, e => e.Kind == AgentStreamEventKind.ToolCallRequested);
        var request = Assert.IsType<AgentToolCallRequest>(toolEvent.Payload);
        Assert.Equal("call_1", request.ToolCallId);
        Assert.Equal(MutatingTool.Name, request.ToolName);
        Assert.Contains("add-node", request.Arguments);
        // The descriptor is mutating, so the request advertises that approval is needed (orchestrator gates it).
        Assert.True(request.RequiresApproval);
        Assert.Equal(AgentRisk.ReviewRequired, request.Risk);

        // Tools are advertised as declaration-only functions so the model emits calls rather than auto-running them.
        var tool = Assert.Single(client.CapturedOptions!.Tools!);
        Assert.Equal(MutatingTool.Name, Assert.IsAssignableFrom<AIFunction>(tool).Name);
    }

    [Fact]
    public async Task Reconstructs_tool_use_and_tool_result_pairing_from_history()
    {
        var client = new ScriptedChatClient(Update(new TextContent("Done.")));
        var provider = BuildProvider(client);
        // History as the orchestrator accumulates it: user, assistant text, then the executed tool's result.
        var history = new List<AgentTurnMessage>
        {
            new(AgentRole.User, "Edit the workflow."),
            new(AgentRole.Assistant, "Calling a tool."),
            new(AgentRole.Tool, "applied", "call_1", MutatingTool.Name)
        };
        var context = new AgentTurnContext("s1", "s1", history, [], [MutatingTool], [], 1, 4);

        await Collect(provider.ContinueTurnAsync(context));

        var messages = client.CapturedMessages!;
        var assistant = Assert.Single(messages, m => m.Role == ChatRole.Assistant);
        var call = Assert.Single(assistant.Contents.OfType<FunctionCallContent>());
        Assert.Equal("call_1", call.CallId);
        Assert.Equal(MutatingTool.Name, call.Name);

        var toolMessage = Assert.Single(messages, m => m.Role == ChatRole.Tool);
        var result = Assert.Single(toolMessage.Contents.OfType<FunctionResultContent>());
        // The tool_result must reference the same call id as the synthesized tool_use (Anthropic pairing rule).
        Assert.Equal("call_1", result.CallId);
    }

    [Fact]
    public async Task Yields_an_error_when_no_api_key_is_configured()
    {
        var provider = BuildProvider(new ScriptedChatClient(), apiKey: null);
        var context = AgentTurnContext.ForMessage("s1", "Hi", []);

        var events = await Collect(provider.ContinueTurnAsync(context));

        var error = Assert.Single(events);
        Assert.Equal(AgentStreamEventKind.Error, error.Kind);
        Assert.Equal("agent.provider.anthropic.unavailable", error.Error!.Code);
    }

    private static AnthropicAgentProvider BuildProvider(IChatClient client, string? apiKey = "sk-test")
    {
        var options = Options.Create(new AnthropicAgentOptions { Enabled = true, ApiKey = apiKey, Model = "claude-test" });
        return new AnthropicAgentProvider(new StubChatClientFactory(client), options, NullLogger<AnthropicAgentProvider>.Instance);
    }

    private static ChatResponseUpdate Update(AIContent content) => new() { Contents = [content] };

    private static async Task<List<AgentStreamEvent>> Collect(IAsyncEnumerable<AgentStreamEvent> events)
    {
        var list = new List<AgentStreamEvent>();
        await foreach (var item in events)
            list.Add(item);
        return list;
    }

    private sealed class StubChatClientFactory(IChatClient client) : IAnthropicChatClientFactory
    {
        public IChatClient Create(string apiKey, string model) => client;
    }

    private sealed class ScriptedChatClient(params ChatResponseUpdate[] updates) : IChatClient
    {
        public IReadOnlyList<ChatMessage>? CapturedMessages { get; private set; }
        public ChatOptions? CapturedOptions { get; private set; }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CapturedMessages = messages.ToList();
            CapturedOptions = options;
            foreach (var update in updates)
            {
                await Task.Yield();
                yield return update;
            }
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The provider drives streaming only.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
