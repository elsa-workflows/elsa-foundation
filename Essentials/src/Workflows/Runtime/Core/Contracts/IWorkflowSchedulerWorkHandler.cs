using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Handles one drained scheduler work item.
/// </summary>
public interface IWorkflowSchedulerWorkHandler
{
    string Name { get; }
    bool CanHandle(RuntimeSchedulerWorkItem workItem);
    ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default);
}
