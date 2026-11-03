using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Contracts;

/// <summary>Replacement contract: current server-owned actor/tenant, permission and host-policy admission.</summary>
/// <remarks>Implementations must re-evaluate current authority; caller metadata is not a permission grant.</remarks>
public interface IAgentToolAuthorizer
{
    Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId,
        AgentResourceTarget? target, CancellationToken cancellationToken = default);
}

/// <summary>Optional completion contract on the same installed tool that prepared a browser operation.</summary>
public interface IAgentClientTool
{
    Task<AgentResult<AgentToolResult>> CompleteAsync(AgentPreparedToolInvocation invocation,
        AgentClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default);
}
