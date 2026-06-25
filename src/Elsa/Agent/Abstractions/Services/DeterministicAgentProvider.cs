using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Services;

public sealed class DeterministicAgentProvider : IAgentProvider
{
    public const string Id = "deterministic-test";

    public string ProviderId => Id;

    public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>
        {
            ["adapter"] = "deterministic",
            ["status"] = "available"
        }));

    public async IAsyncEnumerable<AgentStreamEvent> SendMessageAsync(AgentProviderMessage message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var messageId = Guid.NewGuid().ToString("N");
        yield return new AgentStreamEvent(messageId, AgentStreamEventKind.Started, null, null, null, DateTimeOffset.UtcNow);
        yield return new AgentStreamEvent(messageId, AgentStreamEventKind.MessageDelta, "Deterministic agent provider response.", null, null, DateTimeOffset.UtcNow);
        yield return new AgentStreamEvent(messageId, AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
    }

    public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentToolApprovalResult(request.Approved, request.Approved ? "Tool approval accepted by deterministic provider." : "Tool approval denied by deterministic provider."));

    public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentProviderDiagnostics(
            ProviderId,
            IsAvailable: true,
            Status: "Deterministic provider is available for backend contract validation.",
            AgentProviderKind.ProviderSdkBinding,
            [AgentProviderOperation.Chat, AgentProviderOperation.Streaming, AgentProviderOperation.ToolApproval],
            AgentProviderRiskProfile.ReadOnly,
            new Dictionary<string, string> { ["adapter"] = "deterministic" }));
}
