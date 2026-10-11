using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Contracts;

/// <summary>
/// Features a harness/provider supports, so the host can render and degrade per capability instead of assuming a
/// uniform feature set across providers.
/// </summary>
[Flags]
public enum AgentHarnessCapabilities
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
public sealed record AgentHarnessTurnRequest(
    string SessionId,
    string ActorId,
    IReadOnlyList<AgentTurnMessage> History,
    IReadOnlyCollection<AgentContextAttachment> Context,
    IReadOnlyCollection<IAgentTool> Tools,
    AgentPolicy Policy)
{
    public AgentTurnMessage? LatestUserMessage => History.LastOrDefault(x => x.Role == AgentRole.User);
}

/// <summary>
/// A provider that owns its own multi-step agent loop (e.g. the GitHub Copilot SDK harness). When a registered
/// <see cref="IAgentProvider"/> also implements this, the orchestrator delegates the whole turn to it — running tools,
/// reasoning, and planning inside the vendor harness — rather than driving the host step loop. Providers that do not
/// implement it use the host orchestrator over <see cref="IAgentProvider.ContinueTurnAsync"/>.
/// </summary>
public interface IAgentHarness
{
    AgentHarnessCapabilities Capabilities { get; }

    IAsyncEnumerable<AgentStreamEvent> RunTurnAsync(AgentHarnessTurnRequest request, CancellationToken cancellationToken = default);
}
