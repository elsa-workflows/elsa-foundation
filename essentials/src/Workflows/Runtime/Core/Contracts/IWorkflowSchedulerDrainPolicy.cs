using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Decides whether recorded scheduler work should trigger a scheduler drain.
/// </summary>
public interface IWorkflowSchedulerDrainPolicy
{
    RuntimeSchedulerDrainRequest? CreateDrainRequest(
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeSchedulerWorkItem workItem);
}
