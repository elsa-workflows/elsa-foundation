using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Atomically redrives an eligible detached workflow dispatch without changing its logical identity.</summary>
public interface IWorkflowDispatchRedriveStore
{
    ValueTask<WorkflowDispatchRedriveResult> RedriveAsync(
        WorkflowDispatchRedriveRequest request,
        CancellationToken cancellationToken = default);
}
