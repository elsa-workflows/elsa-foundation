using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// The addressing shared by every scheduler command payload that targets one activity execution: the executable
/// it was scheduled against, the target's node and activity execution, and the reason recorded on its checkpoints.
/// Workflow-level payloads such as <see cref="RuntimeCheckpointCommandPayload"/> address no single activity and do
/// not implement it.
/// </summary>
public interface IActivityCommandPayload
{
    WorkflowExecutableIdentity PinnedExecutable { get; }
    string ExecutableNodeId { get; }
    string ActivityExecutionId { get; }
    string Reason { get; }
}
