using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Workflows.Runtime.Services;

/// <summary>The Runtime-side workflow capabilities: explaining one exact, immutable Executable, and one exact Run read-only.</summary>
internal sealed class WorkflowRuntimeWeaverCapabilityProvider : IWeaverCapabilityProvider
{
    public ValueTask<IReadOnlyCollection<WeaverCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyCollection<WeaverCapability>>([
            new("workflow.executable-explain", WorkflowExecutableContext.ModuleId, "Explain workflow Executable",
                "Explains one exact, immutable workflow Executable at its Artifact Hash: its compiled graph and the Source References that point at it. Executables are never changed.",
                WeaverCapabilityKind.Answer, WeaverRisk.ReadOnly, ["/workflows"], ["workflows/runtime:read"], [WorkflowExecutableContext.ExecutableKind]) { Modes = ["explain"] },
            new("workflow.run-explain", WorkflowRunContext.ModuleId, "Explain workflow Run",
                "Explains one exact workflow Run read-only: why it faulted, where it waits and what a selected activity execution recorded, from its recorded incidents, activity executions and value evidence. Withheld values stay withheld, unavailable data is reported by reason, and a Run is never retried, cancelled, deleted or dispatched.",
                WeaverCapabilityKind.Answer, WeaverRisk.ReadOnly, ["/workflows"], ["workflows/runtime:read"], [WorkflowRunContext.RunKind]) { Modes = ["explain"] }
        ]);
}
