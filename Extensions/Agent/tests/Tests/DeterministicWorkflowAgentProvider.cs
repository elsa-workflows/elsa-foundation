using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Tests;

/// <summary>A second provider identity for registry-conflict tests; it advertises no authoring engine.</summary>
internal sealed class DeterministicWorkflowAgentProvider : IAgentProvider
{
    public const string Id = "deterministic-workflow-authoring";
    public string ProviderId => Id;
    public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentProviderSession(session.Id, Id, new Dictionary<string, string>()));
    public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new("fixture-message", AgentStreamEventKind.MessageDelta, "Deterministic fixture response.", null, null, DateTimeOffset.UtcNow, AgentResultKind.Message);
    }
    public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentToolApprovalResult(false, "Test fixture has no approval authority."));
    public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentProviderDiagnostics(Id, true, "Test fixture", AgentProviderKind.ProviderSdkBinding,
            [AgentProviderOperation.Chat, AgentProviderOperation.Streaming], AgentProviderRiskProfile.ReadOnly, new Dictionary<string, string>()));
}
