using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Workflows.Runtime.Services;

/// <summary>The Runtime-side workflow capabilities: explaining one exact, immutable Executable.</summary>
internal sealed class WorkflowRuntimeAgentCapabilityProvider : IAgentCapabilityProvider
{
    public ValueTask<IReadOnlyCollection<AgentCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyCollection<AgentCapability>>([
            new("workflow.executable-explain", WorkflowExecutableContext.ModuleId, "Explain workflow Executable",
                "Explains one exact, immutable workflow Executable at its Artifact Hash: its compiled graph and the Source References that point at it. Executables are never changed.",
                AgentCapabilityKind.Answer, AgentRisk.ReadOnly, ["/workflows"], ["workflows/runtime:read"], [WorkflowExecutableContext.ExecutableKind]) { Modes = ["explain"] }
        ]);
}
