using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Services;

/// <summary>Freezes the engine's preparation identity without altering a module's observed outcome.</summary>
internal static class WeaverResultProvenance
{
    public static WeaverToolResult Stamp(WeaverToolResult result, WeaverPreparedToolInvocation preparation)
    {
        var access = new WeaverToolResultAccess(preparation.ModuleId, preparation.ToolContractVersion, preparation.Target)
        { DisclosureSourceId = preparation.DisclosureSourceId, InvocationId = preparation.InvocationId };
        return result.Access == access ? result : result with { Access = access };
    }
}
