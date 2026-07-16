using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Models;

/// <summary>Exact runtime-owned executable and source identities pinned into a parent executable node.</summary>
public sealed record DispatchWorkflowPin(
    WorkflowExecutableIdentity Executable,
    WorkflowExecutableSourceProvenance Source);
