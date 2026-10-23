using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Atomically linearizes child admission against parent cancellation.</summary>
public interface IWorkflowDispatchAdmissionStore
{
    ValueTask<WorkflowDispatchAdmissionResult> TryAdmitAsync(
        string dispatchId,
        DateTimeOffset admittedAt,
        CancellationToken cancellationToken = default);
}
