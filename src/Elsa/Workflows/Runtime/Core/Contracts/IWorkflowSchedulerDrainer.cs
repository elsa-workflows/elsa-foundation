using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Drains queued scheduler work for one workflow execution.
/// </summary>
public interface IWorkflowSchedulerDrainer
{
    ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default);
}
