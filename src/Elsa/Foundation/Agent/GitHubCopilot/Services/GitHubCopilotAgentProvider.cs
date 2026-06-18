using System.Runtime.CompilerServices;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;

namespace Elsa.Foundation.Agent.GitHubCopilot.Services;

public sealed class GitHubCopilotAgentProvider : IAgentProvider
{
    public const string Id = "github-copilot";

    public string ProviderId => Id;

    public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>
        {
            ["adapter"] = "stub",
            ["status"] = "sdk-not-bound"
        }));

    public async IAsyncEnumerable<AgentStreamEvent> SendMessageAsync(AgentProviderMessage message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new AgentStreamEvent(
            Guid.NewGuid().ToString("N"),
            AgentStreamEventKind.Error,
            null,
            null,
            new AgentError("agent.provider.github_copilot.sdk_unavailable", "GitHub Copilot SDK adapter is not configured in this backend.", 503),
            DateTimeOffset.UtcNow);
    }

    public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentToolApprovalResult(false, "GitHub Copilot SDK tool approval adapter is not configured."));

    public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentProviderDiagnostics(
            ProviderId,
            IsAvailable: false,
            Status: "GitHub Copilot SDK adapter seam is registered, but no SDK binding is configured.",
            new Dictionary<string, string> { ["adapter"] = "stub", ["sdk"] = "unbound" }));
}
