using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// The kind-agnostic surface both pipeline contexts share: the originating work item and the mutable per-dispatch
/// workspace. Lets the terminal handler reach the running dispatch's workspace (via <see cref="IRuntimePipelineContextAccessor"/>)
/// without depending on the workflow-vs-activity context type.
/// </summary>
public interface IRuntimePipelineContext
{
    RuntimeSchedulerWorkItem WorkItem { get; }
    RuntimePipelineWorkspace Workspace { get; }
}
