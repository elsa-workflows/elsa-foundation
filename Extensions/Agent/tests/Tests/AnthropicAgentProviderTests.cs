using System.Runtime.CompilerServices;
using System.Text.Json;
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
    public async Task Tool_result_protocol_preserves_independent_module_type_version_and_evidence()
    {
        var client = new ScriptedChatClient(Update(new TextContent("Done.")));
        var provider = BuildProvider(client);
        var result = new AgentToolResult("call_1", "independent_tool", true, "Observed result.",
            new("independent.result", "7", JsonSerializer.SerializeToElement(new { actualValue = 42 })));
        var context = new AgentTurnContext("s1", "s1", [new(AgentRole.Tool, result.Content, result.ToolCallId, result.ToolName)], [result], [], [], 1, 4);
        await Collect(provider.ContinueTurnAsync(context));
        var toolMessage = Assert.Single(client.CapturedMessages!, message => message.Role == ChatRole.Tool);
        var protocolResult = Assert.Single(toolMessage.Contents.OfType<FunctionResultContent>());
        var json = Assert.IsType<string>(protocolResult.Result);
        Assert.Contains("independent.result", json);
        Assert.Contains("\"schemaVersion\":\"7\"", json);
        Assert.Contains("42", json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Context_envelope_identity_and_provenance_reach_the_provider(bool changeOrigin)
    {
        var attachment = IndependentContext();
        var changed = changeOrigin
            ? attachment with { Origin = AgentContextOrigin.AuthoritativeRead }
            : attachment with { Target = attachment.Target! with { ResourceId = "other-resource", Revision = "other-revision" } };

        var originalPrompt = await ContextPrompt(attachment);
        var changedPrompt = await ContextPrompt(changed);

        // Domain content is identical: the envelope alone must preserve the selected resource and its provenance.
        Assert.NotEqual(originalPrompt, changedPrompt);
        Assert.Contains("independent.module", originalPrompt);
        Assert.Contains("\"schemaVersion\":\"7\"", originalPrompt);
        Assert.Contains("envelope-only-resource", originalPrompt);
        Assert.Contains("envelope-only-revision", originalPrompt);
        Assert.Contains("ClientSnapshot", originalPrompt);
        Assert.Contains(changeOrigin ? "AuthoritativeRead" : "other-resource", changedPrompt);
        Assert.DoesNotContain("private-client-binding", originalPrompt);
        Assert.DoesNotContain("private-authority-scope", originalPrompt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Context_metadata_preserves_the_sensitive_content_gate(bool includeSensitive)
    {
        var client = new ScriptedChatClient();
        var provider = BuildProvider(client, includeSensitiveContextContent: includeSensitive);
        var sensitive = IndependentContext() with { Sensitivity = AgentContextSensitivity.Sensitive, Content = new { value = "SENSITIVE_CONTENT_ONLY" } };
        var secret = IndependentContext() with { Id = "secret", Sensitivity = AgentContextSensitivity.Secret, Content = new { value = "SECRET_CONTENT_ONLY" } };

        await Collect(provider.ContinueTurnAsync(AgentTurnContext.ForMessage("s1", "Inspect", [sensitive, secret])));

        var prompt = string.Join("\n", client.CapturedMessages!.Select(message => message.Text));
        Assert.Contains("envelope-only-resource", prompt);
        Assert.Equal(includeSensitive, prompt.Contains("SENSITIVE_CONTENT_ONLY", StringComparison.Ordinal));
        Assert.DoesNotContain("SECRET_CONTENT_ONLY", prompt);
        Assert.DoesNotContain("private-client-binding", prompt);
        Assert.DoesNotContain("private-authority-scope", prompt);
    }

    [Fact]
    public async Task Context_metadata_keeps_resource_data_out_of_installed_guidance()
    {
        var client = new ScriptedChatClient();
        var provider = BuildProvider(client);
        var resource = IndependentContext() with { Content = new { instructions = "UNTRUSTED_RESOURCE_INSTRUCTION" } };
        var guidance = IndependentContext() with { Id = "guidance", Target = null, Origin = AgentContextOrigin.InstalledGuidance, Content = new { instructions = "INSTALLED_MODULE_GUIDANCE" } };

        await Collect(provider.ContinueTurnAsync(AgentTurnContext.ForMessage("s1", "Inspect", [resource, guidance])));

        var system = Assert.Single(client.CapturedMessages!, message => message.Role == ChatRole.System).Text;
        var resourceMessage = Assert.Single(client.CapturedMessages!, message => message.Role == ChatRole.User && message.Text?.StartsWith("Resource context data.", StringComparison.Ordinal) == true).Text;
        Assert.Contains("INSTALLED_MODULE_GUIDANCE", system);
        Assert.Contains("InstalledGuidance", system);
        Assert.DoesNotContain("UNTRUSTED_RESOURCE_INSTRUCTION", system);
        Assert.DoesNotContain("envelope-only-resource", system);
        Assert.Contains("UNTRUSTED_RESOURCE_INSTRUCTION", resourceMessage);
        Assert.Contains("ClientSnapshot", resourceMessage);
        Assert.DoesNotContain("INSTALLED_MODULE_GUIDANCE", resourceMessage);
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

    private static AnthropicAgentProvider BuildProvider(IChatClient client, string? apiKey = "sk-test", bool includeSensitiveContextContent = false)
    {
        var options = Options.Create(new AnthropicAgentOptions { Enabled = true, ApiKey = apiKey, Model = "claude-test", IncludeSensitiveContextContent = includeSensitiveContextContent });
        return new AnthropicAgentProvider(new StubChatClientFactory(client), options, NullLogger<AnthropicAgentProvider>.Instance);
    }

    private static AgentContextAttachment IndependentContext() => new("context", "independent.context", null, "Resource", "independent.context",
        AgentContextSensitivity.Internal, "selection", "Bounded resource data.", new { value = "same-domain-content" }, new Dictionary<string, string>())
    {
        ModuleId = "independent.module", SchemaVersion = "7", Origin = AgentContextOrigin.ClientSnapshot,
        Target = new("independent.module", "independent.resource", "envelope-only-resource", "envelope-only-revision"),
        Client = new("private-client-binding", "private-generation"), AgentScopes = ["private-authority-scope"]
    };

    private static async Task<string> ContextPrompt(AgentContextAttachment attachment)
    {
        var client = new ScriptedChatClient();
        await Collect(BuildProvider(client).ContinueTurnAsync(AgentTurnContext.ForMessage("s1", "Inspect", [attachment])));
        return string.Join("\n", client.CapturedMessages!.Select(message => message.Text));
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
