using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Agent.GitHubCopilot.Options;
using Elsa.Agent.Workflows.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Agent.GitHubCopilot.Services;

public sealed class GitHubCopilotAgentProvider(
    IGitHubCopilotClientFactory clientFactory,
    IOptions<GitHubCopilotAgentOptions> options,
    IAgentToolInvoker toolInvoker,
    ILogger<GitHubCopilotAgentProvider> logger) : IAgentProvider, IAgentHarness
{
    public const string Id = "github-copilot";

    private const string MutationPolicyMessage = "GitHub Copilot SDK tool approval is not exposed directly. Elsa-owned proposal approval is required for workflow, file, package, runtime, and external-service mutations.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public string ProviderId => Id;

    public async Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
    {
        var readiness = GetReadiness();
        if (!readiness.Available)
            return new AgentProviderSession(session.Id, ProviderId, BuildMetadata(readiness, []));

        await using var client = clientFactory.Create(BuildClientRequest(readiness));
        await client.StartAsync(cancellationToken);
        await using var providerSession = await client.CreateSessionAsync(BuildSessionRequest(session.Id), cancellationToken);

        var metadata = BuildMetadata(readiness, []);
        metadata["sessionId"] = providerSession.SessionId;
        metadata["workspace"] = string.IsNullOrWhiteSpace(providerSession.WorkspacePath) ? "sdk-managed" : "configured";
        return new AgentProviderSession(session.Id, ProviderId, metadata);
    }

    public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var message = new AgentProviderMessage(context.SessionId, context.LatestUserMessage?.Content ?? string.Empty, context.Context);
        var readiness = GetReadiness();
        if (!readiness.Available)
        {
            yield return Error("agent.provider.github_copilot.unavailable", readiness.Status, 503);
            yield break;
        }

        await using var client = clientFactory.Create(BuildClientRequest(readiness));
        IGitHubCopilotSession? session = null;
        AgentStreamEvent? setupError = null;

        try
        {
            await client.StartAsync(cancellationToken);
            session = await client.ResumeSessionAsync(BuildSessionRequest(message.SessionId), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to resume GitHub Copilot session {SessionId}; creating a new session.", message.SessionId);
            try
            {
                session = await client.CreateSessionAsync(BuildSessionRequest(message.SessionId), cancellationToken);
            }
            catch (Exception createEx) when (createEx is not OperationCanceledException)
            {
                setupError = Error("agent.provider.github_copilot.session_failed", NormalizeMessage(createEx), 503);
            }
        }

        if (setupError is not null)
        {
            yield return setupError;
            yield break;
        }

        if (session is null)
        {
            yield return Error("agent.provider.github_copilot.session_failed", "GitHub Copilot session could not be created.", 503);
            yield break;
        }

        await using (session)
        {
            await foreach (var item in session.SendAsync(BuildPrompt(message), cancellationToken))
                yield return MapStreamEvent(item);
        }
    }

    // GitHub Copilot owns its own multi-step agent loop (tools, reasoning, plan, MCP, permissions). When the
    // orchestrator sees this capability it delegates the whole turn here instead of driving the host step loop.
    public AgentHarnessCapabilities Capabilities =>
        AgentHarnessCapabilities.Tools
        | AgentHarnessCapabilities.Reasoning
        | AgentHarnessCapabilities.Usage
        | AgentHarnessCapabilities.Plan
        | AgentHarnessCapabilities.Mcp
        | AgentHarnessCapabilities.Permissions;

    public async IAsyncEnumerable<AgentStreamEvent> RunTurnAsync(AgentHarnessTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var readiness = GetReadiness();
        if (!readiness.Available)
        {
            yield return Error("agent.provider.github_copilot.unavailable", readiness.Status, 503);
            yield break;
        }

        var message = new AgentProviderMessage(request.SessionId, request.LatestUserMessage?.Content ?? string.Empty, request.Context);

        // Side-band channel so proposals raised when a mutating tool is auto-invoked surface in the live stream
        // alongside the SDK's own tool-execution events.
        var output = Channel.CreateUnbounded<AgentStreamEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var toolContext = new AgentToolAIFunctionContext(
            request.SessionId,
            request.ActorId,
            request.Context,
            request.Policy,
            toolInvoker,
            proposal => output.Writer.TryWrite(new AgentStreamEvent(NewId(), AgentStreamEventKind.ProposalCreated, null, proposal.Id, null, DateTimeOffset.UtcNow, AgentResultKind.Proposal, proposal)));
        var aiFunctions = AgentToolAIFunctions.CreateAll(request.Tools, toolContext);

        await using var client = clientFactory.Create(BuildClientRequest(readiness));
        IGitHubCopilotSession? session = null;
        AgentStreamEvent? setupError = null;

        try
        {
            await client.StartAsync(cancellationToken);
            session = await client.ResumeSessionAsync(BuildSessionRequest(request.SessionId, aiFunctions), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to resume GitHub Copilot session {SessionId}; creating a new session.", request.SessionId);
            try
            {
                session = await client.CreateSessionAsync(BuildSessionRequest(request.SessionId, aiFunctions), cancellationToken);
            }
            catch (Exception createEx) when (createEx is not OperationCanceledException)
            {
                setupError = Error("agent.provider.github_copilot.session_failed", NormalizeMessage(createEx), 503);
            }
        }

        if (setupError is not null)
        {
            yield return setupError;
            yield break;
        }

        if (session is null)
        {
            yield return Error("agent.provider.github_copilot.session_failed", "GitHub Copilot session could not be created.", 503);
            yield break;
        }

        await using (session)
        {
            var toolNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var pump = PumpTurnAsync(session, BuildPrompt(message), output.Writer, toolNames, cancellationToken);

            await foreach (var item in output.Reader.ReadAllAsync(cancellationToken))
                yield return item;

            await pump;
        }
    }

    private async Task PumpTurnAsync(IGitHubCopilotSession session, string prompt, ChannelWriter<AgentStreamEvent> writer, Dictionary<string, string> toolNames, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var raw in session.SendAsync(prompt, cancellationToken))
            {
                var mapped = MapHarnessEvent(raw, toolNames);
                if (mapped is not null)
                    writer.TryWrite(mapped);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation surfaces to the reader via the linked token; nothing more to emit.
        }
        catch (Exception ex)
        {
            writer.TryWrite(Error("agent.provider.github_copilot.turn_failed", NormalizeMessage(ex), 502));
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static AgentStreamEvent? MapHarnessEvent(GitHubCopilotStreamEvent raw, Dictionary<string, string> toolNames)
    {
        switch (raw.Kind)
        {
            case GitHubCopilotStreamEventKind.MessageDelta:
                return new(NewId(), AgentStreamEventKind.MessageDelta, raw.Content, null, null, DateTimeOffset.UtcNow, AgentResultKind.Message);
            case GitHubCopilotStreamEventKind.ToolExecutionStarted:
            {
                var name = raw.ToolName ?? "tool";
                if (!string.IsNullOrEmpty(raw.ToolCallId))
                    toolNames[raw.ToolCallId] = name;
                return new(NewId(), AgentStreamEventKind.ToolCallStarted, null, raw.ToolCallId, null, DateTimeOffset.UtcNow, null, new AgentToolCallRequest(raw.ToolCallId ?? NewId(), name, string.Empty, AgentRisk.ReadOnly, false));
            }
            case GitHubCopilotStreamEventKind.ToolExecutionCompleted:
            {
                var name = raw.ToolCallId is not null && toolNames.TryGetValue(raw.ToolCallId, out var resolved) ? resolved : raw.ToolName ?? "tool";
                var succeeded = raw.Success ?? string.IsNullOrEmpty(raw.ErrorMessage);
                return new(NewId(), AgentStreamEventKind.ToolCallCompleted, null, raw.ToolCallId, null, DateTimeOffset.UtcNow, null, new AgentToolCallOutcome(raw.ToolCallId ?? string.Empty, name, succeeded, succeeded ? "Tool completed." : raw.ErrorMessage ?? "Tool failed."));
            }
            case GitHubCopilotStreamEventKind.Error:
                return Error(raw.ErrorCode ?? "agent.provider.github_copilot.sdk_error", raw.ErrorMessage ?? "GitHub Copilot stream failed.", 502);
            default:
                return null;
        }
    }

    public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentToolApprovalResult(false, MutationPolicyMessage));

    public async Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var readiness = GetReadiness();
        var models = new List<string>();

        if (readiness.Available)
        {
            try
            {
                await using var client = clientFactory.Create(BuildClientRequest(readiness));
                await client.StartAsync(cancellationToken);
                await client.PingAsync(cancellationToken);
                models.AddRange(await client.ListModelsAsync(cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "GitHub Copilot diagnostics failed.");
                readiness = readiness with
                {
                    Available = false,
                    Code = "sdk-unavailable",
                    Status = $"GitHub Copilot SDK call failed: {NormalizeMessage(ex)}"
                };
            }
        }

        return new AgentProviderDiagnostics(
            ProviderId,
            readiness.Available,
            readiness.Status,
            AgentProviderKind.ProviderSdkBinding,
            [AgentProviderOperation.Chat, AgentProviderOperation.Streaming],
            AgentProviderRiskProfile.ReviewRequired,
            BuildMetadata(readiness, models));
    }

    private GitHubCopilotSessionRequest BuildSessionRequest(string sessionId, IReadOnlyCollection<Microsoft.Extensions.AI.AIFunction>? customTools = null)
    {
        var value = options.Value;
        return new(
            sessionId,
            NormalizeModel(value.Model),
            value.ReasoningEffort,
            value.Streaming,
            value.SystemMessage,
            value.AvailableTools.ToList(),
            value.ExcludedTools.ToList(),
            customTools);
    }

    private GitHubCopilotClientRequest BuildClientRequest(ProviderReadiness readiness)
    {
        var value = options.Value;
        return new(
            readiness.GitHubToken,
            value.UseLoggedInUser,
            value.RuntimeUrl,
            value.RuntimeConnectionToken,
            value.BaseDirectory,
            value.WorkingDirectory);
    }

    private ProviderReadiness GetReadiness()
    {
        var value = options.Value;
        if (!value.Enabled)
            return new(false, "disabled", "GitHub Copilot provider is disabled by configuration.", null, "none");

        if (!string.IsNullOrWhiteSpace(value.RuntimeUrl))
        {
            if (!IsValidRuntimeUrl(value.RuntimeUrl))
                return new(false, "misconfigured", "GitHub Copilot runtime URL is invalid. Use a port, host:port, or http(s)://host:port.", null, "invalid-runtime-url");
        }

        var token = ResolveToken(value, out var authMode);
        if (token is null && !value.UseLoggedInUser && string.IsNullOrWhiteSpace(value.RuntimeUrl))
            return new(false, "missing-auth", "GitHub Copilot provider is enabled, but no backend-owned GitHub token, environment token, logged-in user, or external runtime URL is configured.", null, "none");

        return new(true, "configured", "GitHub Copilot provider is configured.", token, authMode);
    }

    private static string? ResolveToken(GitHubCopilotAgentOptions value, out string authMode)
    {
        if (!string.IsNullOrWhiteSpace(value.GitHubToken))
        {
            authMode = "configured-token";
            return value.GitHubToken;
        }

        if (!string.IsNullOrWhiteSpace(value.GitHubTokenEnvironmentVariable))
        {
            var token = Environment.GetEnvironmentVariable(value.GitHubTokenEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(token))
            {
                authMode = $"environment:{value.GitHubTokenEnvironmentVariable}";
                return token;
            }
        }

        if (value.UseLoggedInUser)
        {
            authMode = "logged-in-user";
            return null;
        }

        if (!string.IsNullOrWhiteSpace(value.RuntimeUrl))
        {
            authMode = "external-runtime";
            return null;
        }

        authMode = "none";
        return null;
    }

    private static string? NormalizeModel(string? model)
        => string.IsNullOrWhiteSpace(model) || string.Equals(model, "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : model;

    private static bool IsValidRuntimeUrl(string runtimeUrl)
    {
        if (int.TryParse(runtimeUrl, out var port))
            return IsValidPort(port);

        if (Uri.TryCreate(runtimeUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return (uri.Scheme is "http" or "https") && !string.IsNullOrWhiteSpace(uri.Host) && IsValidPort(uri.Port);

        if (runtimeUrl.Contains("://", StringComparison.Ordinal))
            return false;

        var separator = runtimeUrl.LastIndexOf(':');
        if (separator <= 0 || separator == runtimeUrl.Length - 1)
            return false;

        var host = runtimeUrl[..separator];
        var portText = runtimeUrl[(separator + 1)..];
        return !string.IsNullOrWhiteSpace(host) && int.TryParse(portText, out port) && IsValidPort(port);
    }

    private static bool IsValidPort(int port) => port is > 0 and <= 65535;

    private string BuildPrompt(AgentProviderMessage message)
    {
        if (message.Context.Count == 0)
            return message.Content;

        var builder = new StringBuilder();
        builder.AppendLine("Elsa context attachments:");
        foreach (var attachment in message.Context)
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

        builder.AppendLine();
        if (message.Context.Any(x => string.Equals(x.ContentType, "workflow.definition", StringComparison.OrdinalIgnoreCase)))
        {
            builder.AppendLine("Workflow authoring response contract:");
            builder.AppendLine("Return ordinary explanations as text. For workflow authoring results, return one JSON object with resultKind set to message, clarification, workflowGraphOperationBatch, or error.");
            builder.AppendLine("Use workflowGraphOperationBatch payloads that match the Elsa workflow graph operation batch contract and schema version.");
            builder.AppendLine();
        }

        builder.AppendLine("User request:");
        builder.Append(message.Content);
        return builder.ToString();
    }

    private bool CanIncludeContent(AgentContextAttachment attachment)
        => attachment.Sensitivity <= AgentContextSensitivity.Internal
           || (options.Value.IncludeSensitiveContextContent && attachment.Sensitivity <= AgentContextSensitivity.Sensitive);

    private AgentStreamEvent MapStreamEvent(GitHubCopilotStreamEvent item)
        => item.Kind switch
        {
            GitHubCopilotStreamEventKind.Started => new(NewId(), AgentStreamEventKind.Started, null, null, null, DateTimeOffset.UtcNow),
            GitHubCopilotStreamEventKind.MessageDelta => TryMapStructuredResult(item.Content, out var result) ? result : new(NewId(), AgentStreamEventKind.MessageDelta, item.Content, null, null, DateTimeOffset.UtcNow, AgentResultKind.Message),
            GitHubCopilotStreamEventKind.Completed => new(NewId(), AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow),
            GitHubCopilotStreamEventKind.Error => Error(item.ErrorCode ?? "agent.provider.github_copilot.sdk_error", NormalizeMessage(item.ErrorMessage), 502),
            _ => Error("agent.provider.github_copilot.unknown_event", "GitHub Copilot SDK returned an unknown stream event.", 502)
        };

    private bool TryMapStructuredResult(string? content, out AgentStreamEvent result)
    {
        result = null!;
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var json = ExtractJsonObject(content);
        if (json is null)
            return false;

        CopilotWorkflowAuthoringResult? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CopilotWorkflowAuthoringResult>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.ResultKind))
            return false;

        result = envelope.ResultKind switch
        {
            "message" => new(NewId(), AgentStreamEventKind.MessageDelta, envelope.Message, null, null, DateTimeOffset.UtcNow, AgentResultKind.Message),
            "clarification" when envelope.Clarification is not null => new(NewId(), AgentStreamEventKind.ClarificationRequested, envelope.Clarification.Question, null, null, DateTimeOffset.UtcNow, AgentResultKind.Clarification, envelope.Clarification),
            "workflowGraphOperationBatch" when envelope.WorkflowGraphOperationBatch is not null => new(NewId(), AgentStreamEventKind.WorkflowGraphOperationBatchCreated, envelope.Message ?? "Prepared one workflow graph operation batch.", null, null, DateTimeOffset.UtcNow, AgentResultKind.WorkflowGraphOperationBatch, envelope.WorkflowGraphOperationBatch),
            "error" => Error(envelope.ErrorCode ?? "agent.provider.github_copilot.result_error", NormalizeMessage(envelope.ErrorMessage), 502),
            _ => Error("agent.provider.github_copilot.unsupported_result", "GitHub Copilot returned an unsupported workflow authoring result.", 502)
        };
        return true;
    }

    private static string? ExtractJsonObject(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            var fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd >= 0 && fenceEnd > firstLineEnd)
                trimmed = trimmed[(firstLineEnd + 1)..fenceEnd].Trim();
        }

        return trimmed.StartsWith('{') && trimmed.EndsWith('}') ? trimmed : null;
    }

    private static Dictionary<string, string> BuildMetadata(ProviderReadiness readiness, IReadOnlyCollection<string> models)
    {
        var metadata = new Dictionary<string, string>
        {
            ["providerKind"] = AgentProviderKind.ProviderSdkBinding.ToString(),
            ["statusCode"] = readiness.Code,
            ["authMode"] = readiness.AuthMode,
            ["sdkPackage"] = "GitHub.Copilot.SDK",
            ["sdkVersion"] = typeof(GitHub.Copilot.CopilotClient).Assembly.GetName().Version?.ToString() ?? "unknown",
            ["toolMutationPolicy"] = "elsa-owned-proposals",
            ["toolApproval"] = "sdk-permission-requests-denied"
        };

        if (models.Count > 0)
            metadata["models"] = string.Join(",", models.Take(20));

        return metadata;
    }

    private static AgentStreamEvent Error(string code, string message, int statusCode)
        => new(NewId(), AgentStreamEventKind.Error, null, null, new(code, message, statusCode), DateTimeOffset.UtcNow, AgentResultKind.Error);

    private string NormalizeMessage(Exception ex)
        => NormalizeMessage(string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message);

    private string NormalizeMessage(string? message)
    {
        var normalized = string.IsNullOrWhiteSpace(message) ? "GitHub Copilot SDK call failed." : message;
        var value = options.Value;
        if (!string.IsNullOrWhiteSpace(value.GitHubToken))
            normalized = normalized.Replace(value.GitHubToken, "[redacted]", StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(value.RuntimeConnectionToken))
            normalized = normalized.Replace(value.RuntimeConnectionToken, "[redacted]", StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(value.GitHubTokenEnvironmentVariable))
        {
            var environmentToken = Environment.GetEnvironmentVariable(value.GitHubTokenEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(environmentToken))
                normalized = normalized.Replace(environmentToken, "[redacted]", StringComparison.Ordinal);
        }

        return normalized;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private sealed record ProviderReadiness(
        bool Available,
        string Code,
        string Status,
        string? GitHubToken,
        string AuthMode);

    private sealed record CopilotWorkflowAuthoringResult(
        string? ResultKind,
        string? Message,
        WorkflowClarificationResult? Clarification,
        WorkflowGraphOperationBatch? WorkflowGraphOperationBatch,
        string? ErrorCode,
        string? ErrorMessage);
}
