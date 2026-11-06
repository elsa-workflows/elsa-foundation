using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Contracts;

/// <summary>Scoped replacement contract: a bounded observation of installed Agent registrations and current admission hints.</summary>
/// <remarks>
/// Replace the default registration; multiple or non-scoped registrations are rejected. The supplied policy belongs
/// to the caller's actual scope. A sessionless API uses the host default and labels that scope explicitly. Neither
/// presence nor a targetless authority hint grants context disclosure, resource permission or tool execution.
/// </remarks>
public interface IAgentSupportSource
{
    Task<AgentSupportView> ReadAsync(string actorId, string tenantId, AgentPolicy policy, CancellationToken cancellationToken = default);
}
