using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// The kind-agnostic surface both pipeline contexts share: the originating work item and the mutable per-dispatch
/// workspace. A context-aware handler (<see cref="IRuntimePipelineWorkHandler"/>) receives this in the <c>Invoke</c>
/// slot without depending on the workflow-vs-activity context type.
/// </summary>
public interface IRuntimePipelineContext
{
    RuntimeSchedulerWorkItem WorkItem { get; }
    RuntimePipelineWorkspace Workspace { get; }
}
