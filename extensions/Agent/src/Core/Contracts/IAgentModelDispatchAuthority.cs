using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Contracts;

/// <summary>
/// Per-turn authority supplied by the runtime. A qualified provider calls it immediately before
/// every physical model request, below SDK retry/redirect behavior. A refusal permits no send.
/// </summary>
public interface IAgentModelDispatchAuthority
{
    /// <summary>Persists a conservative request/output reservation under the current journal fence.</summary>
    ValueTask BeforeSendAsync(AgentModelDispatchObservation observation, CancellationToken cancellationToken);
}
