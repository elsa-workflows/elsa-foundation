using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Elsa.Agent.Anthropic.Options;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Agent.Anthropic.Services;

/// <summary>
/// A real, model-backed agent provider driven by the host orchestrator (NOT an <c>IAgentHarness</c>). It maps the
/// orchestrator's accumulated <see cref="AgentTurnContext"/> to Microsoft.Extensions.AI <see cref="ChatMessage"/>s,
/// advertises the registry tools as declaration-only functions, and streams one step: assistant text becomes
/// <see cref="AgentStreamEventKind.MessageDelta"/> events and model tool calls become
/// <see cref="AgentStreamEventKind.ToolCallRequested"/> events. The orchestrator owns the loop, executes the tools
/// through the policy-aware invoker (so mutations are gated by autonomy mode), and calls back with their results.
/// </summary>
public sealed class AnthropicAgentProvider(
    IAnthropicChatClientFactory chatClientFactory,
    IOptions<AnthropicAgentOptions> options,
    ILogger<AnthropicAgentProvider> logger) : IAgentProvider
{
    public const string Id = "anthropic";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ProviderId => Id;

    public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
    {
        var readiness = GetReadiness();
        return Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>
        {
            ["adapter"] = "anthropic",
            ["model"] = options.Value.Model,
            ["status"] = readiness.Available ? "configured" : readiness.Code
        }));
    }

    public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var readiness = GetReadiness();
        if (!readiness.Available)
        {
            yield return Error("agent.provider.anthropic.unavailable", readiness.Status, 503);
            yield break;
        }

        var messages = BuildMessages(context);
        var chatOptions = BuildChatOptions(context);

        using var chatClient = chatClientFactory.Create(readiness.ApiKey!, options.Value.Model);
        await using var enumerator = chatClient.GetStreamingResponseAsync(messages, chatOptions, cancellationToken).GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            ChatResponseUpdate update;
            AgentStreamEvent? failure = null;
            try
            {
                if (!await enumerator.MoveNextAsync())
                    break;
                update = enumerator.Current;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Never hand the raw exception to the sink: its rendering (message, inner chain, stack)
                // can embed the API key. Log the redacted rendering instead (issue #414 item 7).
                logger.LogDebug("Anthropic streaming turn failed for session {SessionId}: {Error}", context.SessionId, Normalize(ex.ToString()));
                failure = Error("agent.provider.anthropic.turn_failed", Normalize(ex), 502);
                update = null!;
            }

            if (failure is not null)
            {
                yield return failure;
                yield break;
            }

            foreach (var streamEvent in MapUpdate(update, context))
                yield return streamEvent;
        }
    }

    // Not used by this provider: tool approval is owned by the orchestrator's policy-aware invoker, not the model SDK.
    public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentToolApprovalResult(false, "The Anthropic provider does not approve tools directly; the orchestrator gates mutations by autonomy mode."));

    public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var readiness = GetReadiness();
        return Task.FromResult(new AgentProviderDiagnostics(
            ProviderId,
            readiness.Available,
            readiness.Status,
            AgentProviderKind.ProviderSdkBinding,
            [AgentProviderOperation.Chat, AgentProviderOperation.Streaming],
            AgentProviderRiskProfile.ReviewRequired,
            new Dictionary<string, string>
            {
                ["adapter"] = "anthropic",
                ["model"] = options.Value.Model,
                ["statusCode"] = readiness.Code,
                ["authMode"] = readiness.AuthMode,
                ["sdkPackage"] = "Anthropic"
            }));
    }

    private IEnumerable<AgentStreamEvent> MapUpdate(ChatResponseUpdate update, AgentTurnContext context)
    {
        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    yield return new(NewId(), AgentStreamEventKind.MessageDelta, text.Text, null, null, DateTimeOffset.UtcNow, AgentResultKind.Message);
                    break;
                case FunctionCallContent call:
                    yield return MapToolCall(call, context);
                    break;
            }
        }
    }

    // The orchestrator owns dispatch and policy: Risk/RequiresApproval are advisory hints from the advertised
    // descriptor (mutating tools surface as requiring approval), mirroring the other providers.
    private AgentStreamEvent MapToolCall(FunctionCallContent call, AgentTurnContext context)
    {
        var arguments = call.Arguments is { Count: > 0 } args ? JsonSerializer.Serialize(args, JsonOptions) : "{}";
        var descriptor = context.AvailableTools.FirstOrDefault(x => string.Equals(x.Name, call.Name, StringComparison.Ordinal));
        var risk = descriptor?.Risk ?? AgentRisk.ReviewRequired;
        var requiresApproval = descriptor?.Mutability == AgentToolMutability.Mutating;
        var callId = string.IsNullOrWhiteSpace(call.CallId) ? NewId() : call.CallId;
        return new(NewId(), AgentStreamEventKind.ToolCallRequested, null, callId, null, DateTimeOffset.UtcNow, null,
            new AgentToolCallRequest(callId, call.Name, arguments, risk, requiresApproval));
    }

    private ChatOptions BuildChatOptions(AgentTurnContext context)
    {
        var chatOptions = new ChatOptions { MaxOutputTokens = options.Value.MaxOutputTokens };
        if (context.AvailableTools.Count > 0)
            chatOptions.Tools = AgentToolAIFunctions.CreateDeclarations(context.AvailableTools).Cast<AITool>().ToList();
        return chatOptions;
    }

    // Reconstructs the structurally-correct Anthropic transcript from the orchestrator's history. The orchestrator
    // appends each executed tool's result to history as a Tool message (carrying its call id), so history is the
    // authoritative record — PendingToolResults is redundant here. Because history stores assistant turns as plain
    // text (the tool_use blocks are not persisted), we synthesize the assistant function-call blocks that each
    // tool-result run answers, preserving the API's required tool_use/tool_result pairing.
    private List<ChatMessage> BuildMessages(AgentTurnContext context)
    {
        var messages = new List<ChatMessage>();

        var system = BuildSystemPrompt(context);
        if (!string.IsNullOrWhiteSpace(system))
            messages.Add(new ChatMessage(ChatRole.System, system));

        var history = context.History;
        for (var i = 0; i < history.Count; i++)
        {
            var message = history[i];
            switch (message.Role)
            {
                case AgentRole.User:
                    messages.Add(new ChatMessage(ChatRole.User, message.Content));
                    break;
                case AgentRole.System:
                    messages.Add(new ChatMessage(ChatRole.System, message.Content));
                    break;
                case AgentRole.Assistant:
                {
                    var toolRun = CollectToolRun(history, i + 1);
                    if (toolRun.Count > 0)
                    {
                        messages.Add(AssistantWithToolCalls(message.Content, toolRun));
                        messages.Add(ToolResults(toolRun));
                        i += toolRun.Count;
                    }
                    else
                    {
                        messages.Add(new ChatMessage(ChatRole.Assistant, message.Content));
                    }

                    break;
                }
                case AgentRole.Tool:
                {
                    // The model emitted tool calls with no accompanying text: an assistant turn of only tool_use.
                    var toolRun = CollectToolRun(history, i);
                    messages.Add(AssistantWithToolCalls(null, toolRun));
                    messages.Add(ToolResults(toolRun));
                    i += toolRun.Count - 1;
                    break;
                }
            }
        }

        return messages;
    }

    private static List<AgentTurnMessage> CollectToolRun(IReadOnlyList<AgentTurnMessage> history, int start)
    {
        var run = new List<AgentTurnMessage>();
        for (var i = start; i < history.Count && history[i].Role == AgentRole.Tool; i++)
            run.Add(history[i]);
        return run;
    }

    private static ChatMessage AssistantWithToolCalls(string? text, IReadOnlyList<AgentTurnMessage> toolRun)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrWhiteSpace(text))
            contents.Add(new TextContent(text));
        foreach (var tool in toolRun)
            contents.Add(new FunctionCallContent(tool.ToolCallId ?? NewId(), tool.ToolName ?? "tool", arguments: null));
        return new ChatMessage(ChatRole.Assistant, contents);
    }

    private static ChatMessage ToolResults(IReadOnlyList<AgentTurnMessage> toolRun)
    {
        var contents = new List<AIContent>();
        foreach (var tool in toolRun)
            contents.Add(new FunctionResultContent(tool.ToolCallId ?? string.Empty, tool.Content));
        return new ChatMessage(ChatRole.Tool, contents);
    }

    private string? BuildSystemPrompt(AgentTurnContext context)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(options.Value.SystemMessage))
            builder.AppendLine(options.Value.SystemMessage);

        if (context.Context.Count > 0)
        {
            builder.AppendLine("Elsa context attachments:");
            foreach (var attachment in context.Context)
            {
                builder.Append("- ").Append(attachment.Label)
                    .Append(" [").Append(attachment.Source).Append(']')
                    .Append(" sensitivity=").Append(attachment.Sensitivity)
                    .AppendLine();

                if (!string.IsNullOrWhiteSpace(attachment.Summary))
                    builder.Append("  Summary: ").AppendLine(attachment.Summary);

                if (CanIncludeContent(attachment) && attachment.Content is not null)
                    builder.Append("  Content: ").AppendLine(JsonSerializer.Serialize(attachment.Content, JsonOptions));
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private bool CanIncludeContent(AgentContextAttachment attachment)
        => AgentProviderPrimitives.CanIncludeContent(attachment, options.Value.IncludeSensitiveContextContent);

    private AnthropicReadiness GetReadiness()
    {
        var value = options.Value;
        if (!value.Enabled)
            return new(false, "disabled", "Anthropic provider is disabled by configuration.", null, "none");

        var apiKey = ResolveApiKey(value, out var authMode);
        if (string.IsNullOrWhiteSpace(apiKey))
            return new(false, "missing-auth", "Anthropic provider is enabled, but no API key is configured directly or via the configured environment variable.", null, "none");

        return new(true, "configured", $"Anthropic provider is configured for model '{value.Model}'.", apiKey, authMode);
    }

    private static string? ResolveApiKey(AnthropicAgentOptions value, out string authMode)
    {
        if (!string.IsNullOrWhiteSpace(value.ApiKey))
        {
            authMode = "configured-key";
            return value.ApiKey;
        }

        if (!string.IsNullOrWhiteSpace(value.ApiKeyEnvironmentVariable))
        {
            var token = Environment.GetEnvironmentVariable(value.ApiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(token))
            {
                authMode = $"environment:{value.ApiKeyEnvironmentVariable}";
                return token;
            }
        }

        authMode = "none";
        return null;
    }

    private string Normalize(Exception ex)
        => Normalize(string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message);

    // Redacts the one secret this provider holds — the resolved API key (configured or env-var). The
    // secret set is a single element; the shared helper's fallback is the message itself, so a blank
    // message returns blank exactly as before.
    private string Normalize(string message)
        => AgentLogRedaction.Redact(message, [ResolveApiKey(options.Value, out _)], message);

    private static AgentStreamEvent Error(string code, string message, int statusCode)
        => AgentProviderPrimitives.Error(code, message, statusCode);

    private static string NewId() => AgentProviderPrimitives.NewId();

    private sealed record AnthropicReadiness(bool Available, string Code, string Status, string? ApiKey, string AuthMode);
}
