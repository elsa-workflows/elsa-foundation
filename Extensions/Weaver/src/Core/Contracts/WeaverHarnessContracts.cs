using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Core.Contracts;

/// <summary>
/// Features a harness/provider supports, so the host can render and degrade per capability instead of assuming a
/// uniform feature set across providers.
/// </summary>
[Flags]
public enum WeaverHarnessCapabilities
{
    None = 0,
    Tools = 1 << 0,
    Reasoning = 1 << 1,
    Usage = 1 << 2,
    Plan = 1 << 3,
    SubAgents = 1 << 4,
    Mcp = 1 << 5,
    Permissions = 1 << 6
}

/// <summary>The full-turn request handed to a loop-owning harness.</summary>
public sealed record WeaverHarnessTurnRequest(
    string SessionId,
    string ActorId,
    IReadOnlyList<WeaverTurnMessage> History,
    IReadOnlyCollection<WeaverContextAttachment> Context,
    IReadOnlyCollection<IWeaverTool> Tools,
    WeaverPolicy Policy)
{
    public WeaverTurnMessage? LatestUserMessage => History.LastOrDefault(x => x.Role == WeaverRole.User);
    public string TurnId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public WeaverClientBinding? Client { get; init; }
    public IReadOnlyList<WeaverToolResult> PendingToolResults { get; init; } = [];
    public bool IsReceiptContinuation { get; init; }
    /// <summary>Server-owned dispatch boundary for recording and admitting results before the harness sends them to its provider.</summary>
    public IWeaverToolInvoker? ToolInvoker { get; init; }
}

/// <summary>
/// A provider that owns its own multi-step agent loop (e.g. the GitHub Copilot SDK harness). When a registered
/// <see cref="IWeaverProvider"/> also implements this, the orchestrator delegates the whole turn to it — running tools,
/// reasoning, and planning inside the vendor harness — rather than driving the host step loop. Providers that do not
/// implement it use the host orchestrator over <see cref="IWeaverProvider.ContinueTurnAsync"/>.
/// </summary>
public interface IWeaverHarness
{
    WeaverHarnessCapabilities Capabilities { get; }

    IAsyncEnumerable<WeaverStreamEvent> RunTurnAsync(WeaverHarnessTurnRequest request, CancellationToken cancellationToken = default);
}
