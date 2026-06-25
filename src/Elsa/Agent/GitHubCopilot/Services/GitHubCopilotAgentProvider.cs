using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.GitHubCopilot.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Agent.GitHubCopilot.Services;

public sealed class GitHubCopilotAgentProvider(
    IGitHubCopilotClientFactory clientFactory,
    IOptions<GitHubCopilotAgentOptions> options,
    ILogger<GitHubCopilotAgentProvider> logger) : IAgentProvider
{
    public const string Id = "github-copilot";

    private const string MutationPolicyMessage = "GitHub Copilot SDK tool approval is not exposed directly. Elsa-owned proposal approval is required for workflow, file, package, runtime, and external-service mutations.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    public async IAsyncEnumerable<AgentStreamEvent> SendMessageAsync(AgentProviderMessage message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
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

    private GitHubCopilotSessionRequest BuildSessionRequest(string sessionId)
    {
        var value = options.Value;
        return new(
            sessionId,
            NormalizeModel(value.Model),
            value.ReasoningEffort,
            value.Streaming,
            value.SystemMessage,
            value.AvailableTools.ToList(),
            value.ExcludedTools.ToList());
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
            GitHubCopilotStreamEventKind.MessageDelta => new(NewId(), AgentStreamEventKind.MessageDelta, item.Content, null, null, DateTimeOffset.UtcNow, AgentResultKind.Message),
            GitHubCopilotStreamEventKind.Completed => new(NewId(), AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow),
            GitHubCopilotStreamEventKind.Error => Error(item.ErrorCode ?? "agent.provider.github_copilot.sdk_error", NormalizeMessage(item.ErrorMessage), 502),
            _ => Error("agent.provider.github_copilot.unknown_event", "GitHub Copilot SDK returned an unknown stream event.", 502)
        };

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
}
