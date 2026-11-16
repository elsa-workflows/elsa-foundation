using System.Runtime.CompilerServices;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Tests;

/// <summary>A second provider identity for registry-conflict tests; it advertises no authoring engine.</summary>
internal sealed class DeterministicWorkflowWeaverProvider : IWeaverProvider
{
    public const string Id = "deterministic-workflow-authoring";
    public string ProviderId => Id;
    public Task<WeaverProviderSession> CreateSessionAsync(WeaverSession session, CancellationToken cancellationToken = default)
        => Task.FromResult(new WeaverProviderSession(session.Id, Id, new Dictionary<string, string>()));
    public async IAsyncEnumerable<WeaverStreamEvent> ContinueTurnAsync(WeaverTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new("fixture-message", WeaverStreamEventKind.MessageDelta, "Deterministic fixture response.", null, null, DateTimeOffset.UtcNow, WeaverResultKind.Message);
    }
    public Task<WeaverToolApprovalResult> ApproveToolAsync(WeaverProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new WeaverToolApprovalResult(false, "Test fixture has no approval authority."));
    public Task<WeaverProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new WeaverProviderDiagnostics(Id, true, "Test fixture", WeaverProviderKind.ProviderSdkBinding,
            [WeaverProviderOperation.Chat, WeaverProviderOperation.Streaming], WeaverProviderRiskProfile.ReadOnly, new Dictionary<string, string>()));
}
