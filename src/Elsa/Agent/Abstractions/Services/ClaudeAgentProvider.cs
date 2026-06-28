using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Services;

/// <summary>
/// Interface-complete placeholder for a model-backed (e.g. Claude via Microsoft.Extensions.AI) harness, proving the
/// pluggable seam: it sits behind the same <see cref="IAgentHarness"/> as the GitHub Copilot provider but advertises
/// only <see cref="AgentHarnessCapabilities.Tools"/> (no plan mode / sub-agents / MCP). Wiring it for real means
/// driving an Anthropic <c>IChatClient</c> with <c>FunctionInvokingChatClient</c> over the shared
/// <see cref="AgentToolAIFunctions"/> tool currency; until configured, it reports unavailable rather than guessing.
/// </summary>
public sealed class ClaudeAgentProvider : IAgentProvider, IAgentHarness
{
    public const string Id = "claude";

    public string ProviderId => Id;

    // A model provider's loop comes from MEAI's FunctionInvokingChatClient — it does not natively offer the Copilot
    // harness's plan mode, sub-agents, MCP, or permission UX, so it advertises tools only and degrades gracefully.
    public AgentHarnessCapabilities Capabilities => AgentHarnessCapabilities.Tools;

    public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>
        {
            ["adapter"] = "claude",
            ["status"] = "not-configured"
        }));

    public async IAsyncEnumerable<AgentStreamEvent> RunTurnAsync(AgentHarnessTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new AgentStreamEvent(
            Guid.NewGuid().ToString("N"),
            AgentStreamEventKind.Error,
            null,
            null,
            new("agent.provider.claude.not_configured", "The Claude provider is a stub. Configure an Anthropic IChatClient to enable model-backed, function-calling turns.", 501),
            DateTimeOffset.UtcNow,
            AgentResultKind.Error);
    }

    // Not used while this provider is harness-driven; required by IAgentProvider.
    public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new AgentStreamEvent(Guid.NewGuid().ToString("N"), AgentStreamEventKind.Error, null, null, new("agent.provider.claude.not_configured", "The Claude provider is not configured.", 501), DateTimeOffset.UtcNow, AgentResultKind.Error);
    }

    public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentToolApprovalResult(false, "The Claude provider stub does not execute tool approvals."));

    public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentProviderDiagnostics(
            ProviderId,
            IsAvailable: false,
            Status: "Claude provider stub: configure an Anthropic IChatClient to enable.",
            AgentProviderKind.ProviderSdkBinding,
            [AgentProviderOperation.Chat, AgentProviderOperation.Streaming],
            AgentProviderRiskProfile.ReviewRequired,
            new Dictionary<string, string> { ["adapter"] = "claude", ["status"] = "not-configured" }));
}
