using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Core.Contracts;

/// <summary>Replacement contract: current server-owned actor/tenant, permission and host-policy admission.</summary>
/// <remarks>Implementations must re-evaluate current authority; caller metadata is not a permission grant.</remarks>
public interface IWeaverToolAuthorizer
{
    Task<WeaverPolicyDecision> AuthorizeAsync(WeaverToolDescriptor tool, string actorId, string tenantId,
        WeaverResourceTarget? target, CancellationToken cancellationToken = default);
}

/// <summary>Optional completion contract on the same installed tool that prepared a browser operation.</summary>
public interface IWeaverClientTool
{
    Task<WeaverResult<WeaverToolResult>> CompleteAsync(WeaverPreparedToolInvocation invocation,
        WeaverClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default);
}
