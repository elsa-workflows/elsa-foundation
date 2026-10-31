using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Services;

/// <summary>Freezes the engine's preparation identity without altering a module's observed outcome.</summary>
internal static class AgentResultProvenance
{
    public static AgentToolResult Stamp(AgentToolResult result, AgentPreparedToolInvocation preparation)
    {
        var access = new AgentToolResultAccess(preparation.ModuleId, preparation.ToolContractVersion, preparation.Target)
        { DisclosureSourceId = preparation.DisclosureSourceId, InvocationId = preparation.InvocationId };
        return result.Access == access ? result : result with { Access = access };
    }
}
